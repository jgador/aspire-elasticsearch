// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Aspire.Dashboard.Otlp.Storage.Elasticsearch;

/// <summary>
/// Represents a log entry read back from Elasticsearch for display in the Historical Logs page.
/// </summary>
public sealed class HistoricalLogEntry
{
    public DateTime Timestamp { get; set; }
    public string Message { get; set; } = string.Empty;
    public string LogLevel { get; set; } = string.Empty;
    public string? ServiceName { get; set; }
    public string? ServiceInstanceId { get; set; }
    public string? TraceId { get; set; }
    public string? SpanId { get; set; }
    public string? LoggerName { get; set; }
    public string? OriginalFormat { get; set; }
    public string? ErrorType { get; set; }
    public string? ErrorMessage { get; set; }
    public string? ErrorStackTrace { get; set; }
    public Dictionary<string, string>? Labels { get; set; }
}
