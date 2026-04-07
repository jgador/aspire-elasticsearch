// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Elastic.Clients.Elasticsearch;
using Elastic.Transport;
using Microsoft.Extensions.Options;

namespace Aspire.Dashboard.Otlp.Storage.Elasticsearch;

/// <summary>
/// Extension methods for registering Elasticsearch log persistence and read services.
/// </summary>
internal static class ElasticsearchServiceExtensions
{
    private const string ConfigSectionPath = "Dashboard:Elasticsearch";

    /// <summary>
    /// Registers Elasticsearch log persistence and read services if enabled in configuration.
    /// </summary>
    public static IServiceCollection AddElasticsearchLogPersistence(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var section = configuration.GetSection(ConfigSectionPath);
        var enabled = section.GetValue<bool>(nameof(ElasticsearchOptions.Enabled));

        if (!enabled)
        {
            return services;
        }

        services.Configure<ElasticsearchOptions>(section);

        services.AddSingleton(sp =>
        {
            var options = sp.GetRequiredService<IOptions<ElasticsearchOptions>>().Value;
            return CreateElasticsearchClient(options);
        });

        services.AddSingleton<ElasticsearchDataStreamSetup>();
        services.AddSingleton<ElasticsearchLogReader>();
        services.AddSingleton<ElasticsearchLogsService>();
        services.AddHostedService<ElasticsearchLogPersistenceService>();

        return services;
    }

    private static ElasticsearchClient CreateElasticsearchClient(ElasticsearchOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.Endpoint))
        {
            throw new InvalidOperationException(
                "Elasticsearch endpoint must be configured when Elasticsearch log persistence is enabled. " +
                $"Set the '{ConfigSectionPath}:{nameof(ElasticsearchOptions.Endpoint)}' configuration value.");
        }

        var settings = new ElasticsearchClientSettings(new Uri(options.Endpoint));

        if (!string.IsNullOrWhiteSpace(options.ApiKey))
        {
            settings = settings.Authentication(new ApiKey(options.ApiKey));
        }
        else if (!string.IsNullOrWhiteSpace(options.Username) && !string.IsNullOrWhiteSpace(options.Password))
        {
            settings = settings.Authentication(new BasicAuthentication(options.Username, options.Password));
        }

        return new ElasticsearchClient(settings);
    }
}
