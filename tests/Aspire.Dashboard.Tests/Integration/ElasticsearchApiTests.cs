// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Net;
using Microsoft.AspNetCore.InternalTesting;
using Xunit;

namespace Aspire.Dashboard.Tests.Integration;

public class ElasticsearchApiTests
{
    private readonly ITestOutputHelper _testOutputHelper;

    public ElasticsearchApiTests(ITestOutputHelper testOutputHelper)
    {
        _testOutputHelper = testOutputHelper;
    }

    [Fact]
    public async Task PostSetup_WhenElasticsearchIsDisabled_ReturnsNotFound()
    {
        await using var app = IntegrationTestHelpers.CreateDashboardWebApplication(_testOutputHelper);
        await app.StartAsync().DefaultTimeout();

        using var httpClient = IntegrationTestHelpers.CreateHttpClient($"http://{app.FrontendSingleEndPointAccessor().EndPoint}");

        var response = await httpClient.PostAsync("/api/elasticsearch/setup", content: null).DefaultTimeout();

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task PostSetup_WhenElasticsearchSetupFails_ReturnsServiceUnavailable()
    {
        await using var app = IntegrationTestHelpers.CreateDashboardWebApplication(_testOutputHelper, config =>
        {
            config["Dashboard:Elasticsearch:Enabled"] = "true";
            config["Dashboard:Elasticsearch:Endpoint"] = "http://127.0.0.1:1";
        });
        await app.StartAsync().DefaultTimeout();

        using var httpClient = IntegrationTestHelpers.CreateHttpClient($"http://{app.FrontendSingleEndPointAccessor().EndPoint}");

        var response = await httpClient.PostAsync("/api/elasticsearch/setup", content: null).DefaultTimeout();
        var responseBody = await response.Content.ReadAsStringAsync().DefaultTimeout();

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.DoesNotContain("Could not find Elasticsearch asset", responseBody, StringComparison.Ordinal);
    }
}
