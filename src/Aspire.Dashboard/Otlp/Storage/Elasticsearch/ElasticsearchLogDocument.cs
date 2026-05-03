// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text.Json.Serialization;

namespace Aspire.Dashboard.Otlp.Storage.Elasticsearch;

/// <summary>
/// Represents a log entry document for indexing into an Elasticsearch data stream.
/// Field names follow the Elastic Common Schema (ECS) where applicable.
/// </summary>
public sealed class ElasticsearchLogDocument
{
    /// <summary>
    /// Gets or sets the timestamp of the log entry. Required field for data streams.
    /// </summary>
    [JsonPropertyName("@timestamp")]
    public DateTime Timestamp { get; set; }

    /// <summary>
    /// Gets or sets the log message body.
    /// </summary>
    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the log severity level (e.g., Information, Warning, Error).
    /// </summary>
    [JsonPropertyName("log.level")]
    public string LogLevel { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the numeric severity value from the OpenTelemetry specification.
    /// </summary>
    [JsonPropertyName("log.severity_number")]
    public int SeverityNumber { get; set; }

    /// <summary>
    /// Gets or sets the logger name / log category (scope name).
    /// </summary>
    [JsonPropertyName("log.logger")]
    public string? LoggerName { get; set; }

    /// <summary>
    /// Gets or sets the original message format string before parameter substitution.
    /// </summary>
    [JsonPropertyName("log.original_format")]
    public string? OriginalFormat { get; set; }

    /// <summary>
    /// Gets or sets the distributed trace ID associated with this log entry.
    /// </summary>
    [JsonPropertyName("trace.id")]
    public string? TraceId { get; set; }

    /// <summary>
    /// Gets or sets the span ID associated with this log entry.
    /// </summary>
    [JsonPropertyName("span.id")]
    public string? SpanId { get; set; }

    /// <summary>
    /// Gets or sets the parent span ID.
    /// </summary>
    [JsonPropertyName("parent.id")]
    public string? ParentId { get; set; }

    /// <summary>
    /// Gets or sets the name of the service that produced this log entry.
    /// </summary>
    [JsonPropertyName("service.name")]
    public string? ServiceName { get; set; }

    /// <summary>
    /// Gets or sets the instance ID of the service that produced this log entry.
    /// </summary>
    [JsonPropertyName("service.instance.id")]
    public string? ServiceInstanceId { get; set; }

    /// <summary>
    /// Gets or sets the version of the service that produced this log entry.
    /// </summary>
    [JsonPropertyName("service.version")]
    public string? ServiceVersion { get; set; }

    /// <summary>
    /// Gets or sets the event name, if this log entry represents a named event.
    /// </summary>
    [JsonPropertyName("event.name")]
    public string? EventName { get; set; }

    /// <summary>
    /// Gets or sets the OpenTelemetry log record flags.
    /// </summary>
    [JsonPropertyName("log.flags")]
    public uint Flags { get; set; }

    /// <summary>
    /// Gets or sets the exception type, if this log entry contains exception information.
    /// </summary>
    [JsonPropertyName("error.type")]
    public string? ErrorType { get; set; }

    /// <summary>
    /// Gets or sets the exception message, if this log entry contains exception information.
    /// </summary>
    [JsonPropertyName("error.message")]
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Gets or sets the exception stack trace, if this log entry contains exception information.
    /// </summary>
    [JsonPropertyName("error.stack_trace")]
    public string? ErrorStackTrace { get; set; }

    /// <summary>
    /// Gets or sets additional attributes from the log entry as key-value pairs.
    /// </summary>
    [JsonPropertyName("labels")]
    public Dictionary<string, string>? Labels { get; set; }
}
