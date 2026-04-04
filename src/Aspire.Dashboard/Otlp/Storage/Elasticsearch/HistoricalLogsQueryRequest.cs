// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Aspire.Dashboard.Otlp.Storage.Elasticsearch;

/// <summary>
/// Parameters for querying historical logs from Elasticsearch.
/// </summary>
internal sealed class HistoricalLogsQueryRequest
{
    public DateTime? StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    public string? ServiceName { get; set; }
    public string? LogLevel { get; set; }
    public string? SearchText { get; set; }
    public int PageIndex { get; set; }
    public int PageSize { get; set; } = 50;
}
