// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Elastic.Clients.Elasticsearch;
using Elastic.Clients.Elasticsearch.QueryDsl;
using Microsoft.Extensions.Options;

namespace Aspire.Dashboard.Otlp.Storage.Elasticsearch;

/// <summary>
/// Queries the Elasticsearch data stream for historical log entries.
/// </summary>
internal sealed class ElasticsearchLogQueryService
{
    private readonly ElasticsearchClient _client;
    private readonly ElasticsearchOptions _options;
    private readonly ILogger<ElasticsearchLogQueryService> _logger;

    public ElasticsearchLogQueryService(
        ElasticsearchClient client,
        IOptions<ElasticsearchOptions> options,
        ILogger<ElasticsearchLogQueryService> logger)
    {
        _client = client;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>
    /// Queries historical logs from Elasticsearch with filtering and pagination.
    /// </summary>
    public async Task<HistoricalLogsQueryResult> QueryLogsAsync(
        HistoricalLogsQueryRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var mustClauses = new List<Action<QueryDescriptor<ElasticsearchLogDocument>>>();

            if (request.StartTime.HasValue || request.EndTime.HasValue)
            {
                mustClauses.Add(q => q.Range(r => r.Date(dr =>
                {
                    dr.Field("@timestamp");
                    if (request.StartTime.HasValue)
                    {
                        dr.Gte(request.StartTime.Value);
                    }
                    if (request.EndTime.HasValue)
                    {
                        dr.Lte(request.EndTime.Value);
                    }
                })));
            }

            if (!string.IsNullOrWhiteSpace(request.ServiceName))
            {
                mustClauses.Add(q => q.Term(t => t.Field("service.name").Value(request.ServiceName)));
            }

            if (!string.IsNullOrWhiteSpace(request.LogLevel))
            {
                mustClauses.Add(q => q.Term(t => t.Field("log.level").Value(request.LogLevel)));
            }

            if (!string.IsNullOrWhiteSpace(request.SearchText))
            {
                mustClauses.Add(q => q.Match(m => m.Field("message").Query(request.SearchText)));
            }

            var from = request.PageIndex * request.PageSize;

            var response = await _client.SearchAsync<ElasticsearchLogDocument>(s => s
                .Indices(_options.DataStreamName)
                .From(from)
                .Size(request.PageSize)
                .Sort(sort => sort.Field("@timestamp", f => f.Order(SortOrder.Desc)))
                .Query(q =>
                {
                    if (mustClauses.Count > 0)
                    {
                        q.Bool(b => b.Must(mustClauses.ToArray()));
                    }
                    else
                    {
                        q.MatchAll(new MatchAllQuery());
                    }
                }),
                cancellationToken).ConfigureAwait(false);

            if (!response.IsValidResponse)
            {
                if (IsIndexNotFoundException(response))
                {
                    // Data stream hasn't been created yet — no logs have been indexed.
                    return HistoricalLogsQueryResult.Empty;
                }

                _logger.LogWarning("Elasticsearch query failed: {Error}", response.DebugInformation);
                return HistoricalLogsQueryResult.Empty;
            }

            var items = response.Documents.Select(MapToEntry).ToList();

            return new HistoricalLogsQueryResult
            {
                Items = items,
                TotalCount = response.Total
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error querying historical logs from Elasticsearch.");
            return HistoricalLogsQueryResult.Empty;
        }
    }

    /// <summary>
    /// Gets the distinct service names from the data stream for populating the resource filter.
    /// </summary>
    public async Task<List<string>> GetServiceNamesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _client.SearchAsync<ElasticsearchLogDocument>(s => s
                .Indices(_options.DataStreamName)
                .Size(0)
                .Aggregations(agg => agg
                    .Add("service_names", a => a.Terms(t => t.Field("service.name").Size(1000)))),
                cancellationToken).ConfigureAwait(false);

            if (!response.IsValidResponse)
            {
                if (IsIndexNotFoundException(response))
                {
                    return [];
                }

                _logger.LogWarning("Elasticsearch aggregation query failed: {Error}", response.DebugInformation);
                return [];
            }

            var termsAgg = response.Aggregations?.GetStringTerms("service_names");
            if (termsAgg is null)
            {
                return [];
            }

            return termsAgg.Buckets.Select(b => b.Key.Value?.ToString() ?? string.Empty).Where(s => s.Length > 0).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error querying service names from Elasticsearch.");
            return [];
        }
    }

    private static HistoricalLogEntry MapToEntry(ElasticsearchLogDocument doc)
    {
        return new HistoricalLogEntry
        {
            Timestamp = doc.Timestamp,
            Message = doc.Message,
            LogLevel = doc.LogLevel,
            ServiceName = doc.ServiceName,
            ServiceInstanceId = doc.ServiceInstanceId,
            TraceId = doc.TraceId,
            SpanId = doc.SpanId,
            LoggerName = doc.LoggerName,
            OriginalFormat = doc.OriginalFormat,
            ErrorType = doc.ErrorType,
            ErrorMessage = doc.ErrorMessage,
            ErrorStackTrace = doc.ErrorStackTrace,
            Labels = doc.Labels
        };
    }

    private static bool IsIndexNotFoundException<T>(SearchResponse<T> response)
    {
        return response.ElasticsearchServerError?.Status == 404;
    }
}
