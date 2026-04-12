// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Aspire.Dashboard.Configuration;
using Aspire.Dashboard.Otlp.Model;
using Elastic.Clients.Elasticsearch;
using Microsoft.Extensions.Options;

namespace Aspire.Dashboard.Otlp.Storage.Elasticsearch;

internal sealed class ElasticsearchLogReader
{
    private readonly ElasticsearchClient _client;
    private readonly ElasticsearchOptions _options;
    private readonly ILogger<ElasticsearchLogReader> _logger;
    private readonly OtlpContext _otlpContext;

    public ElasticsearchLogReader(
        ElasticsearchClient client,
        IOptions<ElasticsearchOptions> options,
        IOptions<DashboardOptions> dashboardOptions,
        ILogger<ElasticsearchLogReader> logger)
    {
        _client = client;
        _options = options.Value;
        _logger = logger;
        _otlpContext = new OtlpContext
        {
            Logger = logger,
            Options = dashboardOptions.Value.TelemetryLimits
        };
    }

    public async Task<PagedResult<OtlpLogEntry>?> TryGetLogsAsync(GetLogsContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!ElasticsearchLogQueryHelpers.TryCreateSearchRequest(_options.DataStreamName, context, out var searchRequest))
        {
            return null;
        }

        var response = await _client.SearchAsync<ElasticsearchLogDocument>(searchRequest, cancellationToken).ConfigureAwait(false);
        if (!response.IsValidResponse)
        {
            _logger.LogWarning("Failed to query logs from Elasticsearch data stream '{DataStreamName}': {Error}",
                _options.DataStreamName, response.DebugInformation);
            return null;
        }

        var mapper = CreateMapper();
        var items = response.Documents.Select(mapper.ToLogEntry).ToList();

        return new PagedResult<OtlpLogEntry>
        {
            TotalItemCount = GetTotalItemCount(response.Total, context.StartIndex, context.Count, items.Count),
            Items = items,
            IsFull = false
        };
    }

    internal static int GetTotalItemCount(long totalItemCount, int startIndex, int requestedCount, int returnedCount)
    {
        if (totalItemCount <= int.MaxValue)
        {
            return (int)totalItemCount;
        }

        // The dashboard grid only accepts int counts. When Elasticsearch reports more than that,
        // return a bounded lower estimate that still lets paging continue from the current window.
        var lowerBound = (long)startIndex + returnedCount;

        if (requestedCount > 0 && returnedCount == requestedCount)
        {
            lowerBound++;
        }
        else
        {
            lowerBound = Math.Max(lowerBound, (long)startIndex + 1);
        }

        return lowerBound >= int.MaxValue
            ? int.MaxValue
            : (int)lowerBound;
    }

    private ElasticsearchLogMapper CreateMapper() => new(_otlpContext);
}
