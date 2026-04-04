// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Aspire.Dashboard.Otlp.Storage.Elasticsearch;

/// <summary>
/// Result of a historical logs query from Elasticsearch.
/// </summary>
public sealed class HistoricalLogsQueryResult
{
    public required List<HistoricalLogEntry> Items { get; init; }
    public required long TotalCount { get; init; }

    public static HistoricalLogsQueryResult Empty { get; } = new()
    {
        Items = [],
        TotalCount = 0
    };
}
