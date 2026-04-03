// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Aspire.Dashboard.Otlp.Storage.Elasticsearch;

/// <summary>
/// Configuration options for Elasticsearch log persistence.
/// </summary>
public sealed class ElasticsearchOptions
{
    /// <summary>
    /// Gets or sets whether Elasticsearch log persistence is enabled. Defaults to <c>false</c>.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Gets or sets the Elasticsearch endpoint URL (e.g., <c>http://localhost:9200</c>).
    /// </summary>
    public string? Endpoint { get; set; }

    /// <summary>
    /// Gets or sets the name of the Elasticsearch data stream to write logs to. Defaults to <c>aspire-logs</c>.
    /// </summary>
    public string DataStreamName { get; set; } = "aspire-logs";

    /// <summary>
    /// Gets or sets the maximum number of log entries to accumulate before flushing a batch to Elasticsearch.
    /// Defaults to <c>100</c>.
    /// </summary>
    public int BatchSize { get; set; } = 100;

    /// <summary>
    /// Gets or sets the maximum interval in seconds between flushes to Elasticsearch,
    /// even if the batch is not full. Defaults to <c>5</c>.
    /// </summary>
    public int FlushIntervalSeconds { get; set; } = 5;

    /// <summary>
    /// Gets or sets an optional API key for Elasticsearch authentication.
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// Gets or sets an optional username for basic authentication with Elasticsearch.
    /// </summary>
    public string? Username { get; set; }

    /// <summary>
    /// Gets or sets an optional password for basic authentication with Elasticsearch.
    /// </summary>
    public string? Password { get; set; }
}
