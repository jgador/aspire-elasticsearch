// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Aspire.Dashboard.Otlp.Storage.Elasticsearch;

/// <summary>
/// Well-known configuration keys and environment variable names for Elasticsearch log persistence.
/// </summary>
internal static class ElasticsearchConfigNames
{
    public const string SectionPath = "Dashboard:Elasticsearch";

    public const string EnabledKey = SectionPath + ":" + nameof(ElasticsearchOptions.Enabled);
    public const string EndpointKey = SectionPath + ":" + nameof(ElasticsearchOptions.Endpoint);
    public const string DataStreamNameKey = SectionPath + ":" + nameof(ElasticsearchOptions.DataStreamName);
    public const string BatchSizeKey = SectionPath + ":" + nameof(ElasticsearchOptions.BatchSize);
    public const string FlushIntervalSecondsKey = SectionPath + ":" + nameof(ElasticsearchOptions.FlushIntervalSeconds);
    public const string ApiKeyKey = SectionPath + ":" + nameof(ElasticsearchOptions.ApiKey);
    public const string UsernameKey = SectionPath + ":" + nameof(ElasticsearchOptions.Username);
    public const string PasswordKey = SectionPath + ":" + nameof(ElasticsearchOptions.Password);

    public const string EnabledEnvVarName = "ASPIRE_DASHBOARD_ELASTICSEARCH_ENABLED";
    public const string EndpointEnvVarName = "ASPIRE_DASHBOARD_ELASTICSEARCH_ENDPOINT";
    public const string DataStreamNameEnvVarName = "ASPIRE_DASHBOARD_ELASTICSEARCH_DATASTREAMNAME";
    public const string BatchSizeEnvVarName = "ASPIRE_DASHBOARD_ELASTICSEARCH_BATCHSIZE";
    public const string FlushIntervalSecondsEnvVarName = "ASPIRE_DASHBOARD_ELASTICSEARCH_FLUSHINTERVALSECONDS";
    public const string ApiKeyEnvVarName = "ASPIRE_DASHBOARD_ELASTICSEARCH_APIKEY";
    public const string UsernameEnvVarName = "ASPIRE_DASHBOARD_ELASTICSEARCH_USERNAME";
    public const string PasswordEnvVarName = "ASPIRE_DASHBOARD_ELASTICSEARCH_PASSWORD";

    public static bool IsEnabled(IConfiguration configuration)
    {
        return configuration.GetValue<bool?>(EnabledEnvVarName)
            ?? configuration.GetValue<bool?>(EnabledKey)
            ?? false;
    }
}
