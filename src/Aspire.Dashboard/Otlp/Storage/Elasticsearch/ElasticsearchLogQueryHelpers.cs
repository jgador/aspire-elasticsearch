// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Globalization;
using System.Text;
using Aspire.Dashboard.Model.Otlp;
using Aspire.Dashboard.Otlp.Model;
using Aspire.Dashboard.Utils;
using Elastic.Clients.Elasticsearch;
using Elastic.Clients.Elasticsearch.Core.Search;
using Elastic.Clients.Elasticsearch.QueryDsl;

namespace Aspire.Dashboard.Otlp.Storage.Elasticsearch;

internal static class ElasticsearchLogQueryHelpers
{
    private const string KeywordSuffix = ".keyword";
    private const string QueryStringSpecialCharacters = "+-=><!(){}[]^\"~*?:\\/&| ";

    private static readonly LogLevel[] s_severityOrder =
    [
        LogLevel.Trace,
        LogLevel.Debug,
        LogLevel.Information,
        LogLevel.Warning,
        LogLevel.Error,
        LogLevel.Critical
    ];

    public const string TimestampFieldName = "@timestamp";
    public const string MessageFieldName = "message";
    public const string LogLevelFieldName = "log.level";
    public const string CategoryFieldName = "log.logger";
    public const string OriginalFormatFieldName = "log.original_format";
    public const string ServiceNameFieldName = "service.name";
    public const string ServiceInstanceIdFieldName = "service.instance.id";
    public const string TraceIdFieldName = "trace.id";
    public const string SpanIdFieldName = "span.id";
    public const string ParentIdFieldName = "parent.id";
    public const string EventNameFieldName = "event.name";
    public const string ErrorTypeFieldName = "error.type";
    public const string ErrorMessageFieldName = "error.message";
    public const string ErrorStackTraceFieldName = "error.stack_trace";
    public const string LabelsFieldName = "labels";

    public static bool TryCreateSearchRequest(string dataStreamName, GetLogsContext context, out SearchRequest searchRequest)
    {
        if (!TryCreateQuery(context.ResourceKey, context.Filters, out var query))
        {
            searchRequest = default!;
            return false;
        }

        searchRequest = CreateSearchRequest(dataStreamName, context.StartIndex, context.Count, query);
        return true;
    }

    public static SearchRequest CreateSearchRequest(string dataStreamName, int startIndex, int count, Query? query)
    {
        var searchRequest = new SearchRequest(dataStreamName)
        {
            From = startIndex,
            Size = count,
            TrackTotalHits = new TrackHits(true),
            Sort =
            [
                new SortOptions
                {
                    Field = new FieldSort
                    {
                        Field = TimestampFieldName,
                        Order = SortOrder.Asc
                    }
                }
            ]
        };

        if (query is { } q)
        {
            searchRequest.Query = q;
        }

        return searchRequest;
    }

    public static bool TryCreateQuery(ResourceKey? resourceKey, IEnumerable<TelemetryFilter>? filters, out Query? query)
    {
        var queries = new List<Query>();

        AddResourceKeyFilter(resourceKey, queries);

        if (filters is not null)
        {
            foreach (var filter in filters.GetEnabledFilters())
            {
                if (filter is not FieldTelemetryFilter fieldFilter || !TryTranslateFilter(fieldFilter, out var filterQuery))
                {
                    query = default;
                    return false;
                }

                queries.Add(filterQuery);
            }
        }

        query = queries.Count switch
        {
            0 => null,
            1 => queries[0],
            _ => new BoolQuery
            {
                Filter = queries
            }
        };

        return true;
    }

    public static bool TryResolveAggregationField(string field, out string aggregationField)
    {
        if (TryResolveStringField(field, out var resolvedField))
        {
            aggregationField = resolvedField.AggregationField;
            return true;
        }

        aggregationField = default!;
        return false;
    }

    public static bool TryTranslateFilter(FieldTelemetryFilter filter, out Query query)
    {
        switch (filter.Field)
        {
            case nameof(OtlpLogEntry.TimeStamp):
                return TryCreateTimestampQuery(filter, out query);
            case nameof(OtlpLogEntry.Severity):
                return TryCreateSeverityQuery(filter, out query);
            default:
                if (TryResolveStringField(filter.Field, out var resolvedField))
                {
                    return TryCreateStringQuery(resolvedField, filter, out query);
                }

                query = default!;
                return false;
        }
    }

    private static void AddResourceKeyFilter(ResourceKey? resourceKey, List<Query> queries)
    {
        if (resourceKey is not { } key)
        {
            return;
        }

        queries.Add(new TermQuery
        {
            Field = ServiceNameFieldName,
            Value = key.Name
        });

        if (!string.IsNullOrEmpty(key.InstanceId))
        {
            queries.Add(new TermQuery
            {
                Field = ServiceInstanceIdFieldName,
                Value = key.InstanceId
            });
        }
    }

    private static bool TryResolveStringField(string field, out ResolvedField resolvedField)
    {
        switch (field)
        {
            case nameof(OtlpLogEntry.Message):
            case KnownStructuredLogFields.MessageField:
                resolvedField = CreateTextField(MessageFieldName);
                return true;
            case KnownStructuredLogFields.CategoryField:
                resolvedField = CreateKeywordField(CategoryFieldName);
                return true;
            case nameof(OtlpLogEntry.OriginalFormat):
            case KnownStructuredLogFields.OriginalFormatField:
                resolvedField = CreateKeywordField(OriginalFormatFieldName);
                return true;
            case nameof(OtlpLogEntry.EventName):
            case KnownStructuredLogFields.EventNameField:
                resolvedField = CreateKeywordField(EventNameFieldName);
                return true;
            case nameof(OtlpLogEntry.ParentId):
            case KnownStructuredLogFields.ParentIdField:
                resolvedField = CreateKeywordField(ParentIdFieldName);
                return true;
            case nameof(OtlpLogEntry.TraceId):
            case KnownStructuredLogFields.TraceIdField:
                resolvedField = CreateKeywordField(TraceIdFieldName);
                return true;
            case nameof(OtlpLogEntry.SpanId):
            case KnownStructuredLogFields.SpanIdField:
                resolvedField = CreateKeywordField(SpanIdFieldName);
                return true;
            case KnownStructuredLogFields.LevelField:
                resolvedField = CreateKeywordField(LogLevelFieldName);
                return true;
            case KnownResourceFields.ServiceNameField:
                resolvedField = CreateKeywordField(ServiceNameFieldName);
                return true;
            case KnownResourceFields.ServiceInstanceIdField:
                resolvedField = CreateKeywordField(ServiceInstanceIdFieldName);
                return true;
            case OtlpLogEntry.ExceptionTypeField:
                resolvedField = CreateKeywordField(ErrorTypeFieldName);
                return true;
            case OtlpLogEntry.ExceptionMessageField:
                resolvedField = CreateTextField(ErrorMessageFieldName);
                return true;
            case OtlpLogEntry.ExceptionStackTraceField:
                resolvedField = CreateTextField(ErrorStackTraceFieldName);
                return true;
            default:
                resolvedField = CreateDynamicLabelField(field);
                return true;
        }
    }

    private static bool TryCreateStringQuery(ResolvedField resolvedField, FieldTelemetryFilter filter, out Query query)
    {
        switch (filter.Condition)
        {
            case FilterCondition.Equals:
                query = CreateEqualsQuery(resolvedField.ExactField, filter.Value);
                return true;
            case FilterCondition.NotEqual:
                query = CreateNotQuery(CreateEqualsQuery(resolvedField.ExactField, filter.Value));
                return true;
            case FilterCondition.Contains:
                query = CreateContainsQuery(resolvedField, filter.Value);
                return true;
            case FilterCondition.NotContains:
                query = CreateNotQuery(CreateContainsQuery(resolvedField, filter.Value));
                return true;
            default:
                query = default!;
                return false;
        }
    }

    private static bool TryCreateTimestampQuery(FieldTelemetryFilter filter, out Query query)
    {
        if (!DateTime.TryParse(
            filter.Value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind,
            out var timestamp))
        {
            query = default!;
            return false;
        }

        var rangeQuery = new DateRangeQuery
        {
            Field = TimestampFieldName
        };
        var utcTimestamp = timestamp.ToUniversalTime();

        switch (filter.Condition)
        {
            case FilterCondition.Equals:
                rangeQuery.Gte = utcTimestamp;
                rangeQuery.Lte = utcTimestamp;
                break;
            case FilterCondition.GreaterThan:
                rangeQuery.Gt = utcTimestamp;
                break;
            case FilterCondition.LessThan:
                rangeQuery.Lt = utcTimestamp;
                break;
            case FilterCondition.GreaterThanOrEqual:
                rangeQuery.Gte = utcTimestamp;
                break;
            case FilterCondition.LessThanOrEqual:
                rangeQuery.Lte = utcTimestamp;
                break;
            case FilterCondition.NotEqual:
                query = CreateNotQuery(new DateRangeQuery
                {
                    Field = TimestampFieldName,
                    Gte = utcTimestamp,
                    Lte = utcTimestamp
                });
                return true;
            default:
                query = default!;
                return false;
        }

        query = rangeQuery;
        return true;
    }

    private static bool TryCreateSeverityQuery(FieldTelemetryFilter filter, out Query query)
    {
        if (!Enum.TryParse<LogLevel>(filter.Value, ignoreCase: true, out var severity))
        {
            query = default!;
            return false;
        }

        switch (filter.Condition)
        {
            case FilterCondition.Equals:
                query = new TermQuery
                {
                    Field = LogLevelFieldName,
                    Value = severity.ToString()
                };
                return true;
            case FilterCondition.NotEqual:
                query = CreateNotQuery(new TermQuery
                {
                    Field = LogLevelFieldName,
                    Value = severity.ToString()
                });
                return true;
            case FilterCondition.GreaterThanOrEqual:
                {
                    var allowedLevels = s_severityOrder.Where(level => (int)level >= (int)severity).ToList();
                    if (allowedLevels.Count == 0)
                    {
                        query = default!;
                        return false;
                    }

                    query = allowedLevels.Count == 1
                        ? CreateEqualsQuery(LogLevelFieldName, allowedLevels[0].ToString())
                        : new BoolQuery
                        {
                            MinimumShouldMatch = 1,
                            Should = allowedLevels.Select(level => CreateEqualsQuery(LogLevelFieldName, level.ToString())).ToList()
                        };

                    return true;
                }
            default:
                query = default!;
                return false;
        }
    }

    private static Query CreateEqualsQuery(string fieldName, string value)
    {
        return new TermQuery
        {
            Field = fieldName,
            Value = value
        };
    }

    private static Query CreateContainsQuery(ResolvedField resolvedField, string value)
    {
        return resolvedField.ContainsQueryKind switch
        {
            ContainsQueryKind.Wildcard => CreateWildcardContainsQuery(resolvedField.ContainsField, value),
            ContainsQueryKind.QueryString => CreateQueryStringContainsQuery(resolvedField.ContainsField, value),
            _ => throw new InvalidOperationException($"Unsupported contains query kind '{resolvedField.ContainsQueryKind}'.")
        };
    }

    private static Query CreateWildcardContainsQuery(string fieldName, string value)
    {
        return new WildcardQuery
        {
            Field = fieldName,
            CaseInsensitive = true,
            Value = $"*{EscapeWildcardValue(value)}*"
        };
    }

    private static Query CreateQueryStringContainsQuery(string fieldName, string value)
    {
        return new QueryStringQuery
        {
            DefaultField = new Field(fieldName),
            AllowLeadingWildcard = true,
            AnalyzeWildcard = true,
            Query = $"*{EscapeQueryStringValue(value)}*"
        };
    }

    private static Query CreateNotQuery(Query innerQuery)
    {
        return new BoolQuery
        {
            MustNot = [innerQuery]
        };
    }

    private static string EscapeWildcardValue(string value)
    {
        var builder = new StringBuilder(value.Length);

        foreach (var character in value)
        {
            if (character is '\\' or '*' or '?')
            {
                builder.Append('\\');
            }

            builder.Append(character);
        }

        return builder.ToString();
    }

    private static string EscapeQueryStringValue(string value)
    {
        var builder = new StringBuilder(value.Length);

        for (var i = 0; i < value.Length; i++)
        {
            var character = value[i];

            if (character is '&' or '|' && i + 1 < value.Length && value[i + 1] == character)
            {
                builder.Append('\\');
                builder.Append(character);
                builder.Append('\\');
                builder.Append(character);
                i++;
                continue;
            }

            if (QueryStringSpecialCharacters.Contains(character))
            {
                builder.Append('\\');
            }

            builder.Append(character);
        }

        return builder.ToString();
    }

    private static ResolvedField CreateKeywordField(string fieldName) => new(fieldName, fieldName, fieldName, ContainsQueryKind.Wildcard);

    private static ResolvedField CreateTextField(string fieldName)
    {
        var keywordFieldName = fieldName + KeywordSuffix;
        return new ResolvedField(keywordFieldName, keywordFieldName, keywordFieldName, ContainsQueryKind.Wildcard);
    }

    private static ResolvedField CreateDynamicLabelField(string fieldName)
    {
        var labelFieldName = $"{LabelsFieldName}.{fieldName}";
        return new ResolvedField(labelFieldName, labelFieldName, labelFieldName, ContainsQueryKind.QueryString);
    }

    private readonly record struct ResolvedField(
        string ExactField,
        string ContainsField,
        string AggregationField,
        ContainsQueryKind ContainsQueryKind);

    private enum ContainsQueryKind
    {
        Wildcard,
        QueryString
    }
}
