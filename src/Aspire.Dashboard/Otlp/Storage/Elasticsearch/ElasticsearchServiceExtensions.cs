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
    /// <summary>
    /// Registers Elasticsearch log persistence and read services if enabled in configuration.
    /// </summary>
    public static IServiceCollection AddElasticsearchLogPersistence(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var section = configuration.GetSection(ElasticsearchConfigNames.SectionPath);

        if (!ElasticsearchConfigNames.IsEnabled(configuration))
        {
            return services;
        }

        services.Configure<ElasticsearchOptions>(section);
        services.PostConfigure<ElasticsearchOptions>(options =>
        {
            ApplyAliasedConfiguration(configuration, options);
            NormalizePositiveValues(options);
        });

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
                $"Set the '{ElasticsearchConfigNames.EndpointKey}' configuration value " +
                $"or the '{ElasticsearchConfigNames.EndpointEnvVarName}' environment variable.");
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

    private static void ApplyAliasedConfiguration(IConfiguration configuration, ElasticsearchOptions options)
    {
        if (GetBoolValue(configuration, ElasticsearchConfigNames.EnabledEnvVarName) is { } enabled)
        {
            options.Enabled = enabled;
        }

        if (configuration[ElasticsearchConfigNames.EndpointEnvVarName] is { Length: > 0 } endpoint)
        {
            options.Endpoint = endpoint;
        }

        if (configuration[ElasticsearchConfigNames.DataStreamNameEnvVarName] is { Length: > 0 } dataStreamName)
        {
            options.DataStreamName = dataStreamName;
        }

        if (GetIntValue(configuration, ElasticsearchConfigNames.BatchSizeEnvVarName) is { } batchSize && batchSize > 0)
        {
            options.BatchSize = batchSize;
        }

        if (GetIntValue(configuration, ElasticsearchConfigNames.FlushIntervalSecondsEnvVarName) is { } flushIntervalSeconds && flushIntervalSeconds > 0)
        {
            options.FlushIntervalSeconds = flushIntervalSeconds;
        }

        if (configuration[ElasticsearchConfigNames.ApiKeyEnvVarName] is { Length: > 0 } apiKey)
        {
            options.ApiKey = apiKey;
        }

        if (configuration[ElasticsearchConfigNames.UsernameEnvVarName] is { Length: > 0 } username)
        {
            options.Username = username;
        }

        if (configuration[ElasticsearchConfigNames.PasswordEnvVarName] is { Length: > 0 } password)
        {
            options.Password = password;
        }
    }

    private static void NormalizePositiveValues(ElasticsearchOptions options)
    {
        var defaultOptions = new ElasticsearchOptions();

        if (options.BatchSize <= 0)
        {
            options.BatchSize = defaultOptions.BatchSize;
        }

        if (options.FlushIntervalSeconds <= 0)
        {
            options.FlushIntervalSeconds = defaultOptions.FlushIntervalSeconds;
        }
    }

    private static bool? GetBoolValue(IConfiguration configuration, string key)
    {
        return configuration.GetValue<bool?>(key);
    }

    private static int? GetIntValue(IConfiguration configuration, string key)
    {
        return configuration.GetValue<int?>(key);
    }
}
