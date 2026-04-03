// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Elastic.Clients.Elasticsearch;
using Elastic.Clients.Elasticsearch.Mapping;
using Microsoft.Extensions.Options;

namespace Aspire.Dashboard.Otlp.Storage.Elasticsearch;

/// <summary>
/// Ensures the Elasticsearch index template and data stream are configured on startup.
/// </summary>
internal sealed class ElasticsearchDataStreamSetup
{
    private readonly ElasticsearchClient _client;
    private readonly ElasticsearchOptions _options;
    private readonly ILogger<ElasticsearchDataStreamSetup> _logger;

    public ElasticsearchDataStreamSetup(
        ElasticsearchClient client,
        IOptions<ElasticsearchOptions> options,
        ILogger<ElasticsearchDataStreamSetup> logger)
    {
        _client = client;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>
    /// Creates or updates the index template for the Aspire logs data stream.
    /// The template enables data stream mode and defines field mappings.
    /// </summary>
    public async Task EnsureDataStreamAsync(CancellationToken cancellationToken)
    {
        var templateName = $"{_options.DataStreamName}-template";

        _logger.LogInformation("Ensuring Elasticsearch index template '{TemplateName}' for data stream '{DataStreamName}'.",
            templateName, _options.DataStreamName);

        var putTemplateResponse = await _client.Indices.PutIndexTemplateAsync(templateName, descriptor => descriptor
            .IndexPatterns($"{_options.DataStreamName}*")
            .DataStream()
            .Priority(500)
            .Template(template => template
                .Settings(settings => settings
                    .NumberOfShards(1)
                    .NumberOfReplicas(0)
                )
                .Mappings(mappings => mappings
                    .Properties(props => props
                        .Date("@timestamp", new DateProperty())
                        .Keyword("log.level", new KeywordProperty())
                        .IntegerNumber("log.severity_number", new IntegerNumberProperty())
                        .Text("log.logger", new TextProperty())
                        .Text("log.original_format", new TextProperty())
                        .IntegerNumber("log.flags", new IntegerNumberProperty())
                        .Text("message", new TextProperty())
                        .Keyword("trace.id", new KeywordProperty())
                        .Keyword("span.id", new KeywordProperty())
                        .Keyword("parent.id", new KeywordProperty())
                        .Keyword("service.name", new KeywordProperty())
                        .Keyword("service.instance.id", new KeywordProperty())
                        .Keyword("event.name", new KeywordProperty())
                        .Keyword("error.type", new KeywordProperty())
                        .Text("error.message", new TextProperty())
                        .Text("error.stack_trace", new TextProperty())
                        .Object("labels", new ObjectProperty())
                    )
                )
            ), cancellationToken).ConfigureAwait(false);

        if (!putTemplateResponse.IsValidResponse)
        {
            _logger.LogError("Failed to create index template '{TemplateName}': {Error}",
                templateName, putTemplateResponse.DebugInformation);
            throw new InvalidOperationException($"Failed to create Elasticsearch index template: {putTemplateResponse.DebugInformation}");
        }

        _logger.LogInformation("Elasticsearch index template '{TemplateName}' is ready.", templateName);
    }
}
