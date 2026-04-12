// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Net;
using Elastic.Clients.Elasticsearch;
using Elastic.Transport;
using Microsoft.Extensions.Options;
using HttpMethod = Elastic.Transport.HttpMethod;

namespace Aspire.Dashboard.Otlp.Storage.Elasticsearch;

/// <summary>
/// Ensures the Elasticsearch component templates, ILM policy, index template, and data stream are configured on startup.
/// </summary>
internal sealed class ElasticsearchDataStreamSetup
{
    private const string AssetsRelativePath = "Otlp/Storage/Elasticsearch/Assets";
    private const string SettingsComponentTemplateAssetPath = "component-template/aspire-settings.json";
    private const string MappingComponentTemplateAssetPath = "component-template/aspire-mappings.json";
    private const string IlmPolicyAssetPath = "ilm-policy/aspire-default-7d.json";
    private const string IndexTemplateAssetPath = "index-template/aspire-logs.json";

    private const string DataStreamNameToken = "__ELASTICSEARCH_DATA_STREAM_NAME__";
    private const string IlmPolicyNameToken = "__ELASTICSEARCH_ILM_POLICY_NAME__";
    private const string SettingsComponentTemplateNameToken = "__ELASTICSEARCH_SETTINGS_COMPONENT_TEMPLATE_NAME__";
    private const string MappingComponentTemplateNameToken = "__ELASTICSEARCH_MAPPING_COMPONENT_TEMPLATE_NAME__";

    private readonly ElasticsearchClient _client;
    private readonly IHostEnvironment _environment;
    private readonly ElasticsearchOptions _options;
    private readonly ILogger<ElasticsearchDataStreamSetup> _logger;
    private readonly SemaphoreSlim _setupLock = new(1, 1);

    public ElasticsearchDataStreamSetup(
        ElasticsearchClient client,
        IHostEnvironment environment,
        IOptions<ElasticsearchOptions> options,
        ILogger<ElasticsearchDataStreamSetup> logger)
    {
        _client = client;
        _environment = environment;
        _options = options.Value;
        _logger = logger;
    }

    public async Task EnsureDataStreamAsync(CancellationToken cancellationToken)
    {
        var assetNames = CreateAssetNames();

        await _setupLock.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            _logger.LogInformation(
                "Ensuring Elasticsearch assets for data stream '{DataStreamName}' from '{AssetRootPath}'.",
                _options.DataStreamName,
                ResolveAssetRootPath());

            await EnsureIlmPolicyAsync(assetNames, cancellationToken).ConfigureAwait(false);
            await EnsureComponentTemplateAsync(
                assetNames.SettingsComponentTemplateName,
                SettingsComponentTemplateAssetPath,
                assetNames,
                cancellationToken).ConfigureAwait(false);
            await EnsureComponentTemplateAsync(
                assetNames.MappingComponentTemplateName,
                MappingComponentTemplateAssetPath,
                assetNames,
                cancellationToken).ConfigureAwait(false);
            await EnsureIndexTemplateAsync(assetNames, cancellationToken).ConfigureAwait(false);
            await EnsureConcreteDataStreamAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _setupLock.Release();
        }
    }

    private async Task EnsureIlmPolicyAsync(ElasticsearchAssetNames assetNames, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Ensuring Elasticsearch ILM policy '{IlmPolicyName}'.", assetNames.IlmPolicyName);

        var body = await LoadAssetAsync(IlmPolicyAssetPath, assetNames, cancellationToken).ConfigureAwait(false);
        var response = await _client.Transport.RequestAsync<StringResponse>(
            HttpMethod.PUT,
            $"/_ilm/policy/{Uri.EscapeDataString(assetNames.IlmPolicyName)}",
            PostData.String(body),
            cancellationToken).ConfigureAwait(false);

        EnsureSuccessfulResponse(response, $"Failed to create ILM policy '{assetNames.IlmPolicyName}'");

        _logger.LogInformation("Elasticsearch ILM policy '{IlmPolicyName}' is ready.", assetNames.IlmPolicyName);
    }

    private async Task EnsureComponentTemplateAsync(
        string componentTemplateName,
        string assetRelativePath,
        ElasticsearchAssetNames assetNames,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Ensuring Elasticsearch component template '{ComponentTemplateName}' for data stream '{DataStreamName}'.",
            componentTemplateName,
            _options.DataStreamName);

        var body = await LoadAssetAsync(assetRelativePath, assetNames, cancellationToken).ConfigureAwait(false);
        var response = await _client.Transport.RequestAsync<StringResponse>(
            HttpMethod.PUT,
            $"/_component_template/{Uri.EscapeDataString(componentTemplateName)}",
            PostData.String(body),
            cancellationToken).ConfigureAwait(false);

        EnsureSuccessfulResponse(response, $"Failed to create component template '{componentTemplateName}'");

        _logger.LogInformation("Elasticsearch component template '{ComponentTemplateName}' is ready.", componentTemplateName);
    }

    private async Task EnsureIndexTemplateAsync(ElasticsearchAssetNames assetNames, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Ensuring Elasticsearch index template '{TemplateName}' for data stream '{DataStreamName}'.",
            assetNames.IndexTemplateName,
            _options.DataStreamName);

        var body = await LoadAssetAsync(IndexTemplateAssetPath, assetNames, cancellationToken).ConfigureAwait(false);
        var response = await _client.Transport.RequestAsync<StringResponse>(
            HttpMethod.PUT,
            $"/_index_template/{Uri.EscapeDataString(assetNames.IndexTemplateName)}",
            PostData.String(body),
            cancellationToken).ConfigureAwait(false);

        EnsureSuccessfulResponse(response, $"Failed to create index template '{assetNames.IndexTemplateName}'");

        _logger.LogInformation("Elasticsearch index template '{TemplateName}' is ready.", assetNames.IndexTemplateName);
    }

    private async Task EnsureConcreteDataStreamAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Ensuring Elasticsearch data stream '{DataStreamName}'.", _options.DataStreamName);

        var escapedDataStreamName = Uri.EscapeDataString(_options.DataStreamName);
        var getDataStreamResponse = await _client.Transport.RequestAsync<StringResponse>(
            HttpMethod.GET,
            $"/_data_stream/{escapedDataStreamName}",
            cancellationToken).ConfigureAwait(false);

        if (HasSuccessfulStatusCode(getDataStreamResponse))
        {
            _logger.LogInformation("Elasticsearch data stream '{DataStreamName}' is ready.", _options.DataStreamName);
            return;
        }

        if (getDataStreamResponse.ApiCallDetails?.HttpStatusCode is not (int)HttpStatusCode.NotFound)
        {
            ThrowUnexpectedResponse(getDataStreamResponse, $"Failed to query data stream '{_options.DataStreamName}'");
        }

        var createDataStreamResponse = await _client.Transport.RequestAsync<StringResponse>(
            HttpMethod.PUT,
            $"/_data_stream/{escapedDataStreamName}",
            cancellationToken).ConfigureAwait(false);

        EnsureSuccessfulResponse(createDataStreamResponse, $"Failed to create data stream '{_options.DataStreamName}'");

        _logger.LogInformation("Elasticsearch data stream '{DataStreamName}' is ready.", _options.DataStreamName);
    }

    private async Task<string> LoadAssetAsync(
        string assetRelativePath,
        ElasticsearchAssetNames assetNames,
        CancellationToken cancellationToken)
    {
        var assetPath = ResolveAssetPath(assetRelativePath);
        var contents = await File.ReadAllTextAsync(assetPath, cancellationToken).ConfigureAwait(false);

        return contents
            .Replace(DataStreamNameToken, assetNames.DataStreamName, StringComparison.Ordinal)
            .Replace(IlmPolicyNameToken, assetNames.IlmPolicyName, StringComparison.Ordinal)
            .Replace(SettingsComponentTemplateNameToken, assetNames.SettingsComponentTemplateName, StringComparison.Ordinal)
            .Replace(MappingComponentTemplateNameToken, assetNames.MappingComponentTemplateName, StringComparison.Ordinal);
    }

    private string ResolveAssetPath(string assetRelativePath)
    {
        var normalizedRelativePath = assetRelativePath.Replace('/', Path.DirectorySeparatorChar);

        foreach (var assetRootPath in EnumerateAssetRootPaths())
        {
            var assetPath = Path.Combine(assetRootPath, normalizedRelativePath);
            if (File.Exists(assetPath))
            {
                return assetPath;
            }
        }

        throw new FileNotFoundException(
            $"Could not find Elasticsearch asset '{assetRelativePath}'. Expected it under '{AssetsRelativePath}' " +
            $"relative to '{_environment.ContentRootPath}' or '{AppContext.BaseDirectory}'.");
    }

    private string ResolveAssetRootPath()
    {
        foreach (var assetRootPath in EnumerateAssetRootPaths())
        {
            if (Directory.Exists(assetRootPath))
            {
                return assetRootPath;
            }
        }

        return Path.Combine(_environment.ContentRootPath, AssetsRelativePath.Replace('/', Path.DirectorySeparatorChar));
    }

    private IEnumerable<string> EnumerateAssetRootPaths()
    {
        yield return Path.Combine(_environment.ContentRootPath, AssetsRelativePath.Replace('/', Path.DirectorySeparatorChar));
        yield return Path.Combine(AppContext.BaseDirectory, AssetsRelativePath.Replace('/', Path.DirectorySeparatorChar));
    }

    private static bool HasSuccessfulStatusCode(StringResponse response)
    {
        return response.ApiCallDetails?.HttpStatusCode is >= 200 and < 300;
    }

    private void EnsureSuccessfulResponse(StringResponse response, string failureMessage)
    {
        if (HasSuccessfulStatusCode(response))
        {
            return;
        }

        ThrowUnexpectedResponse(response, failureMessage);
    }

    private void ThrowUnexpectedResponse(StringResponse response, string failureMessage)
    {
        var statusCode = response.ApiCallDetails?.HttpStatusCode;
        var responseBody = string.IsNullOrWhiteSpace(response.Body) ? "<empty>" : response.Body;

        _logger.LogError("{FailureMessage}. Status code: {StatusCode}. Response: {ResponseBody}", failureMessage, statusCode, responseBody);

        throw new InvalidOperationException($"{failureMessage}. Status code: {statusCode}. Response: {responseBody}");
    }

    private ElasticsearchAssetNames CreateAssetNames()
    {
        return new ElasticsearchAssetNames(
            _options.DataStreamName,
            $"{_options.DataStreamName}-7d",
            $"{_options.DataStreamName}-settings",
            $"{_options.DataStreamName}-mappings",
            $"{_options.DataStreamName}-template");
    }

    private readonly record struct ElasticsearchAssetNames(
        string DataStreamName,
        string IlmPolicyName,
        string SettingsComponentTemplateName,
        string MappingComponentTemplateName,
        string IndexTemplateName);
}
