// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Aspire.Dashboard.Otlp.Storage.Elasticsearch;
using Xunit;

namespace Aspire.Dashboard.Tests.Elasticsearch;

public class ElasticsearchOptionsTests
{
    [Fact]
    public void Defaults_AreCorrect()
    {
        var options = new ElasticsearchOptions();

        Assert.False(options.Enabled);
        Assert.Null(options.Endpoint);
        Assert.Equal("aspire-logs", options.DataStreamName);
        Assert.Equal(100, options.BatchSize);
        Assert.Equal(5, options.FlushIntervalSeconds);
        Assert.Null(options.ApiKey);
        Assert.Null(options.Username);
        Assert.Null(options.Password);
    }

    [Fact]
    public void Properties_CanBeSet()
    {
        var options = new ElasticsearchOptions
        {
            Enabled = true,
            Endpoint = "https://es.example.com:9200",
            DataStreamName = "custom-logs",
            BatchSize = 500,
            FlushIntervalSeconds = 10,
            ApiKey = "test-api-key",
            Username = "elastic",
            Password = "changeme"
        };

        Assert.True(options.Enabled);
        Assert.Equal("https://es.example.com:9200", options.Endpoint);
        Assert.Equal("custom-logs", options.DataStreamName);
        Assert.Equal(500, options.BatchSize);
        Assert.Equal(10, options.FlushIntervalSeconds);
        Assert.Equal("test-api-key", options.ApiKey);
        Assert.Equal("elastic", options.Username);
        Assert.Equal("changeme", options.Password);
    }
}
