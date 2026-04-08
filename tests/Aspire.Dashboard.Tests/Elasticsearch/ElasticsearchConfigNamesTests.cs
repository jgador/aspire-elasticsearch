// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Aspire.Dashboard.Otlp.Storage.Elasticsearch;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Xunit;

namespace Aspire.Dashboard.Tests.Elasticsearch;

public class ElasticsearchConfigNamesTests
{
    [Fact]
    public void ConfigKeys_AreExpected()
    {
        Assert.Equal("Dashboard:Elasticsearch", ElasticsearchConfigNames.SectionPath);
        Assert.Equal("Dashboard:Elasticsearch:Enabled", ElasticsearchConfigNames.EnabledKey);
        Assert.Equal("Dashboard:Elasticsearch:Endpoint", ElasticsearchConfigNames.EndpointKey);
        Assert.Equal("Dashboard:Elasticsearch:DataStreamName", ElasticsearchConfigNames.DataStreamNameKey);
        Assert.Equal("Dashboard:Elasticsearch:BatchSize", ElasticsearchConfigNames.BatchSizeKey);
        Assert.Equal("Dashboard:Elasticsearch:FlushIntervalSeconds", ElasticsearchConfigNames.FlushIntervalSecondsKey);
        Assert.Equal("Dashboard:Elasticsearch:ApiKey", ElasticsearchConfigNames.ApiKeyKey);
        Assert.Equal("Dashboard:Elasticsearch:Username", ElasticsearchConfigNames.UsernameKey);
        Assert.Equal("Dashboard:Elasticsearch:Password", ElasticsearchConfigNames.PasswordKey);
    }

    [Fact]
    public void EnvironmentVariableNames_AreExpected()
    {
        Assert.Equal("ASPIRE_DASHBOARD_ELASTICSEARCH_ENABLED", ElasticsearchConfigNames.EnabledEnvVarName);
        Assert.Equal("ASPIRE_DASHBOARD_ELASTICSEARCH_ENDPOINT", ElasticsearchConfigNames.EndpointEnvVarName);
        Assert.Equal("ASPIRE_DASHBOARD_ELASTICSEARCH_DATASTREAMNAME", ElasticsearchConfigNames.DataStreamNameEnvVarName);
        Assert.Equal("ASPIRE_DASHBOARD_ELASTICSEARCH_BATCHSIZE", ElasticsearchConfigNames.BatchSizeEnvVarName);
        Assert.Equal("ASPIRE_DASHBOARD_ELASTICSEARCH_FLUSHINTERVALSECONDS", ElasticsearchConfigNames.FlushIntervalSecondsEnvVarName);
        Assert.Equal("ASPIRE_DASHBOARD_ELASTICSEARCH_APIKEY", ElasticsearchConfigNames.ApiKeyEnvVarName);
        Assert.Equal("ASPIRE_DASHBOARD_ELASTICSEARCH_USERNAME", ElasticsearchConfigNames.UsernameEnvVarName);
        Assert.Equal("ASPIRE_DASHBOARD_ELASTICSEARCH_PASSWORD", ElasticsearchConfigNames.PasswordEnvVarName);
    }

    [Fact]
    public void AddElasticsearchLogPersistence_AppliesAliasedConfiguration()
    {
        var configuration = new ConfigurationManager()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [ElasticsearchConfigNames.EnabledEnvVarName] = "true",
                [ElasticsearchConfigNames.EndpointEnvVarName] = "http://localhost:9200",
                [ElasticsearchConfigNames.DataStreamNameEnvVarName] = "custom-logs",
                [ElasticsearchConfigNames.BatchSizeEnvVarName] = "250",
                [ElasticsearchConfigNames.FlushIntervalSecondsEnvVarName] = "15",
                [ElasticsearchConfigNames.ApiKeyEnvVarName] = "test-api-key",
                [ElasticsearchConfigNames.UsernameEnvVarName] = "elastic",
                [ElasticsearchConfigNames.PasswordEnvVarName] = "changeme"
            })
            .Build();

        var services = new ServiceCollection();

        services.AddElasticsearchLogPersistence(configuration);

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<ElasticsearchOptions>>().Value;

        Assert.True(options.Enabled);
        Assert.Equal("http://localhost:9200", options.Endpoint);
        Assert.Equal("custom-logs", options.DataStreamName);
        Assert.Equal(250, options.BatchSize);
        Assert.Equal(15, options.FlushIntervalSeconds);
        Assert.Equal("test-api-key", options.ApiKey);
        Assert.Equal("elastic", options.Username);
        Assert.Equal("changeme", options.Password);
        Assert.Contains(
            services,
            descriptor => descriptor.ServiceType == typeof(IHostedService)
                && descriptor.ImplementationType == typeof(ElasticsearchLogPersistenceService));
    }
}
