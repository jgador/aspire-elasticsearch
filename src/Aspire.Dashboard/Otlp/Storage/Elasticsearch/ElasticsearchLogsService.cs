// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Aspire.Dashboard.Configuration;
using Aspire.Dashboard.Model.Otlp;
using Aspire.Dashboard.Otlp.Model;
using Elastic.Clients.Elasticsearch;
using Elastic.Clients.Elasticsearch.Aggregations;
using Elastic.Clients.Elasticsearch.QueryDsl;
using Microsoft.Extensions.Options;

namespace Aspire.Dashboard.Otlp.Storage.Elasticsearch;

internal sealed class ElasticsearchLogsService
{
    private const int ResourcePageSize = 1000;
    private const int LogPageSize = 1000;
    private const int MetadataBucketLimit = 1000;

    private const string ResourcesAggregationName = "resources";
    private const string PropertyKeysAggregationName = "property_keys";
    private const string FieldValuesAggregationName = "field_values";
    private const string ExceptionTypeExistsAggregationName = "exception_type_exists";
    private const string ExceptionMessageExistsAggregationName = "exception_message_exists";
    private const string ExceptionStackTraceExistsAggregationName = "exception_stacktrace_exists";

    private const string ResourceNameKey = "service_name";
    private const string ResourceInstanceIdKey = "service_instance_id";

    private readonly ElasticsearchClient _client;
    private readonly ElasticsearchLogReader _logReader;
    private readonly ElasticsearchOptions _options;
    private readonly ILogger<ElasticsearchLogsService> _logger;
    private readonly OtlpContext _otlpContext;

    public ElasticsearchLogsService(
        ElasticsearchClient client,
        ElasticsearchLogReader logReader,
        IOptions<ElasticsearchOptions> options,
        IOptions<DashboardOptions> dashboardOptions,
        ILogger<ElasticsearchLogsService> logger)
    {
        _client = client;
        _logReader = logReader;
        _options = options.Value;
        _logger = logger;
        _otlpContext = new OtlpContext
        {
            Logger = logger,
            Options = dashboardOptions.Value.TelemetryLimits
        };
    }

    public Task<PagedResult<OtlpLogEntry>?> TryGetLogsAsync(GetLogsContext context, CancellationToken cancellationToken)
    {
        return _logReader.TryGetLogsAsync(context, cancellationToken);
    }

    public async Task<List<OtlpResource>?> TryGetResourcesAsync(CancellationToken cancellationToken)
    {
        var resources = new List<OtlpResource>();
        Dictionary<Field, FieldValue>? afterKey = null;

        while (true)
        {
            var searchRequest = CreateResourcesSearchRequest(afterKey);
            var response = await _client.SearchAsync<ElasticsearchLogDocument>(searchRequest, cancellationToken).ConfigureAwait(false);
            if (!response.IsValidResponse)
            {
                _logger.LogWarning("Failed to query resources from Elasticsearch data stream '{DataStreamName}': {Error}",
                    _options.DataStreamName, response.DebugInformation);
                return null;
            }

            if (response.Aggregations is null ||
                !response.Aggregations.TryGetAggregate<CompositeAggregate>(ResourcesAggregationName, out var compositeAggregate) ||
                compositeAggregate is null)
            {
                break;
            }

            foreach (var bucket in compositeAggregate.Buckets)
            {
                if (!TryGetStringKey(bucket.Key, ResourceNameKey, out var resourceName) || string.IsNullOrWhiteSpace(resourceName))
                {
                    continue;
                }

                var instanceId = TryGetStringKey(bucket.Key, ResourceInstanceIdKey, out var resolvedInstanceId)
                    ? resolvedInstanceId
                    : null;

                var resource = new OtlpResource(resourceName, instanceId, uninstrumentedPeer: false, _otlpContext)
                {
                    HasLogs = true
                };

                resources.Add(resource);
            }

            if (compositeAggregate.AfterKey is not { Count: > 0 })
            {
                break;
            }

            afterKey = compositeAggregate.AfterKey.ToDictionary(kvp => new Field(kvp.Key), kvp => kvp.Value);
        }

        return resources
            .DistinctBy(resource => resource.ResourceKey)
            .OrderBy(resource => resource.ResourceKey)
            .ToList();
    }

    public Task<List<OtlpLogEntry>?> TryGetLogsForTraceAsync(string traceId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(traceId);

        return TryGetAllLogsAsync(
            resourceKey: null,
            filters:
            [
                new FieldTelemetryFilter
                {
                    Field = KnownStructuredLogFields.TraceIdField,
                    Condition = FilterCondition.Equals,
                    Value = traceId
                }
            ],
            cancellationToken);
    }

    public Task<List<OtlpLogEntry>?> TryGetLogsForSpanAsync(string traceId, string spanId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(traceId);
        ArgumentException.ThrowIfNullOrEmpty(spanId);

        return TryGetAllLogsAsync(
            resourceKey: null,
            filters:
            [
                new FieldTelemetryFilter
                {
                    Field = KnownStructuredLogFields.TraceIdField,
                    Condition = FilterCondition.Equals,
                    Value = traceId
                },
                new FieldTelemetryFilter
                {
                    Field = KnownStructuredLogFields.SpanIdField,
                    Condition = FilterCondition.Equals,
                    Value = spanId
                }
            ],
            cancellationToken);
    }

    public async Task<List<string>?> TryGetLogPropertyKeysAsync(ResourceKey? resourceKey, CancellationToken cancellationToken)
    {
        if (!ElasticsearchLogQueryHelpers.TryCreateQuery(resourceKey, filters: null, out var query))
        {
            return null;
        }

        var request = new SearchRequest(_options.DataStreamName)
        {
            Size = 0,
            Query = query,
            Aggregations = new Dictionary<string, Aggregation>
            {
                [PropertyKeysAggregationName] = new()
                {
                    Terms = new TermsAggregation
                    {
                        Script = new Script
                        {
                            Source = """
                                if (params._source.containsKey('labels') && params._source.labels != null) {
                                    return params._source.labels.keySet();
                                }

                                return [];
                                """
                        },
                        Size = MetadataBucketLimit
                    }
                },
                [ExceptionTypeExistsAggregationName] = CreateExistsAggregation(ElasticsearchLogQueryHelpers.ErrorTypeFieldName),
                [ExceptionMessageExistsAggregationName] = CreateExistsAggregation(ElasticsearchLogQueryHelpers.ErrorMessageFieldName),
                [ExceptionStackTraceExistsAggregationName] = CreateExistsAggregation(ElasticsearchLogQueryHelpers.ErrorStackTraceFieldName)
            }
        };

        var response = await _client.SearchAsync<ElasticsearchLogDocument>(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsValidResponse)
        {
            _logger.LogWarning("Failed to query log property keys from Elasticsearch data stream '{DataStreamName}': {Error}",
                _options.DataStreamName, response.DebugInformation);
            return null;
        }

        var propertyKeys = new HashSet<string>(StringComparers.OtlpAttribute);

        if (response.Aggregations is { } aggregations)
        {
            if (aggregations.TryGetAggregate<StringTermsAggregate>(PropertyKeysAggregationName, out var propertyKeysAggregate) &&
                propertyKeysAggregate is not null)
            {
                foreach (var bucket in propertyKeysAggregate.Buckets)
                {
                    if (bucket.Key.TryGetString(out var key) && !string.IsNullOrWhiteSpace(key))
                    {
                        propertyKeys.Add(key);
                    }
                }
            }

            AddExceptionPropertyKeyIfPresent(aggregations, ExceptionTypeExistsAggregationName, OtlpLogEntry.ExceptionTypeField, propertyKeys);
            AddExceptionPropertyKeyIfPresent(aggregations, ExceptionMessageExistsAggregationName, OtlpLogEntry.ExceptionMessageField, propertyKeys);
            AddExceptionPropertyKeyIfPresent(aggregations, ExceptionStackTraceExistsAggregationName, OtlpLogEntry.ExceptionStackTraceField, propertyKeys);
        }

        return propertyKeys.OrderBy(key => key).ToList();
    }

    public async Task<Dictionary<string, int>?> TryGetLogsFieldValuesAsync(ResourceKey? resourceKey, string field, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(field);

        if (!ElasticsearchLogQueryHelpers.TryResolveAggregationField(field, out var aggregationField))
        {
            return null;
        }

        if (!ElasticsearchLogQueryHelpers.TryCreateQuery(resourceKey, filters: null, out var query))
        {
            return null;
        }

        var request = new SearchRequest(_options.DataStreamName)
        {
            Size = 0,
            Query = query,
            Aggregations = new Dictionary<string, Aggregation>
            {
                [FieldValuesAggregationName] = new()
                {
                    Terms = new TermsAggregation
                    {
                        Field = aggregationField,
                        Size = MetadataBucketLimit
                    }
                }
            }
        };

        var response = await _client.SearchAsync<ElasticsearchLogDocument>(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsValidResponse)
        {
            _logger.LogWarning("Failed to query log field values from Elasticsearch data stream '{DataStreamName}': {Error}",
                _options.DataStreamName, response.DebugInformation);
            return null;
        }

        var values = new Dictionary<string, int>(StringComparers.OtlpAttribute);

        if (response.Aggregations is { } aggregations &&
            aggregations.TryGetAggregate<StringTermsAggregate>(FieldValuesAggregationName, out var fieldValuesAggregate) &&
            fieldValuesAggregate is not null)
        {
            foreach (var bucket in fieldValuesAggregate.Buckets)
            {
                if (!bucket.Key.TryGetString(out var value) || string.IsNullOrWhiteSpace(value))
                {
                    continue;
                }

                values[value] = bucket.DocCount >= int.MaxValue
                    ? int.MaxValue
                    : (int)bucket.DocCount;
            }
        }

        return values;
    }

    private async Task<List<OtlpLogEntry>?> TryGetAllLogsAsync(ResourceKey? resourceKey, List<TelemetryFilter> filters, CancellationToken cancellationToken)
    {
        var logs = new List<OtlpLogEntry>();
        var startIndex = 0;

        while (true)
        {
            var page = await _logReader.TryGetLogsAsync(new GetLogsContext
            {
                ResourceKey = resourceKey,
                StartIndex = startIndex,
                Count = LogPageSize,
                Filters = filters
            }, cancellationToken).ConfigureAwait(false);

            if (page is null)
            {
                return null;
            }

            if (page.Items.Count == 0)
            {
                break;
            }

            logs.AddRange(page.Items);

            if (page.Items.Count < LogPageSize || logs.Count >= page.TotalItemCount)
            {
                break;
            }

            startIndex += page.Items.Count;
        }

        return logs;
    }

    private SearchRequest CreateResourcesSearchRequest(Dictionary<Field, FieldValue>? afterKey)
    {
        var compositeAggregation = new CompositeAggregation
        {
            Size = ResourcePageSize,
            Sources =
            [
                new KeyValuePair<string, CompositeAggregationSource>(ResourceNameKey, new CompositeAggregationSource
                {
                    Terms = new CompositeTermsAggregation
                    {
                        Field = ElasticsearchLogQueryHelpers.ServiceNameFieldName
                    }
                }),
                new KeyValuePair<string, CompositeAggregationSource>(ResourceInstanceIdKey, new CompositeAggregationSource
                {
                    Terms = new CompositeTermsAggregation
                    {
                        Field = ElasticsearchLogQueryHelpers.ServiceInstanceIdFieldName,
                        MissingBucket = true
                    }
                })
            ]
        };

        if (afterKey is { Count: > 0 })
        {
            compositeAggregation.After = afterKey;
        }

        return new SearchRequest(_options.DataStreamName)
        {
            Size = 0,
            Aggregations = new Dictionary<string, Aggregation>
            {
                [ResourcesAggregationName] = new()
                {
                    Composite = compositeAggregation
                }
            }
        };
    }

    private static Aggregation CreateExistsAggregation(string fieldName)
    {
        return new Aggregation
        {
            Filter = new ExistsQuery
            {
                Field = fieldName
            }
        };
    }

    private static void AddExceptionPropertyKeyIfPresent(
        AggregateDictionary aggregations,
        string aggregationName,
        string propertyKey,
        HashSet<string> propertyKeys)
    {
        if (aggregations.TryGetAggregate<FilterAggregate>(aggregationName, out var filterAggregate) &&
            filterAggregate is { DocCount: > 0 })
        {
            propertyKeys.Add(propertyKey);
        }
    }

    private static bool TryGetStringKey(IReadOnlyDictionary<string, FieldValue> keyValues, string keyName, out string value)
    {
        if (keyValues.TryGetValue(keyName, out var fieldValue) &&
            fieldValue.TryGetString(out var resolvedValue) &&
            resolvedValue is not null)
        {
            value = resolvedValue;
            return true;
        }

        value = default!;
        return false;
    }
}
