// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Xunit;

namespace Aspire.Dashboard.Tests.Elasticsearch;

public class ElasticsearchDataStreamSetupTests
{
    [Fact]
    public async Task EnsureDataStreamAsync_CreatesConfiguredDataStreamWhenMissing()
    {
        var invoker = ElasticsearchTestHelpers.CreateInvoker(
            ElasticsearchTestHelpers.CreateAcknowledgedResponseJson(),
            ElasticsearchTestHelpers.CreateAcknowledgedResponseJson(),
            ElasticsearchTestHelpers.CreateAcknowledgedResponseJson(),
            ElasticsearchTestHelpers.CreateAcknowledgedResponseJson());
        invoker.EnqueueResponse(ElasticsearchTestHelpers.CreateIndexNotFoundResponseJson("aspire-logs"), statusCode: 404);
        invoker.EnqueueResponse(ElasticsearchTestHelpers.CreateAcknowledgedResponseJson());
        var setup = ElasticsearchTestHelpers.CreateDataStreamSetup(invoker);

        await setup.EnsureDataStreamAsync(CancellationToken.None);

        Assert.Collection(invoker.Requests,
            request =>
            {
                var normalizedBody = NormalizeJsonForContains(request.Body);

                Assert.Equal("PUT", request.Method);
                Assert.Equal("/_ilm/policy/aspire-logs-7d", request.PathAndQuery);
                Assert.Contains("\"max_age\":\"7d\"", normalizedBody);
            },
            request =>
            {
                var normalizedBody = NormalizeJsonForContains(request.Body);

                Assert.Equal("PUT", request.Method);
                Assert.Equal("/_component_template/aspire-logs-settings", request.PathAndQuery);
                Assert.Contains("\"index.lifecycle.name\":\"aspire-logs-7d\"", normalizedBody);
            },
            request =>
            {
                var normalizedBody = NormalizeJsonForContains(request.Body);

                Assert.Equal("PUT", request.Method);
                Assert.Equal("/_component_template/aspire-logs-mappings", request.PathAndQuery);
                Assert.Contains("\"@timestamp\"", normalizedBody);
                Assert.Contains("\"service.name\"", normalizedBody);
                Assert.Contains("\"labels\":{\"type\":\"flattened\"}", normalizedBody);
            },
            request =>
            {
                var normalizedBody = NormalizeJsonForContains(request.Body);

                Assert.Equal("PUT", request.Method);
                Assert.Equal("/_index_template/aspire-logs-template", request.PathAndQuery);
                Assert.Contains("\"index_patterns\":[\"aspire-logs*\"]", normalizedBody);
                Assert.Contains("\"composed_of\":[\"aspire-logs-settings\",\"aspire-logs-mappings\"]", normalizedBody);
                Assert.Contains("\"data_stream\":{}", normalizedBody);
            },
            request =>
            {
                Assert.Equal("GET", request.Method);
                Assert.Equal("/_data_stream/aspire-logs", request.PathAndQuery);
                Assert.Equal(string.Empty, request.Body);
            },
            request =>
            {
                Assert.Equal("PUT", request.Method);
                Assert.Equal("/_data_stream/aspire-logs", request.PathAndQuery);
                Assert.Equal(string.Empty, request.Body);
            });
    }

    [Fact]
    public async Task EnsureDataStreamAsync_DoesNotCreateConfiguredDataStreamWhenItAlreadyExists()
    {
        var invoker = ElasticsearchTestHelpers.CreateInvoker(
            ElasticsearchTestHelpers.CreateAcknowledgedResponseJson(),
            ElasticsearchTestHelpers.CreateAcknowledgedResponseJson(),
            ElasticsearchTestHelpers.CreateAcknowledgedResponseJson(),
            ElasticsearchTestHelpers.CreateAcknowledgedResponseJson(),
            ElasticsearchTestHelpers.CreateGetDataStreamResponseJson("aspire-logs"));
        var setup = ElasticsearchTestHelpers.CreateDataStreamSetup(invoker);

        await setup.EnsureDataStreamAsync(CancellationToken.None);

        Assert.Collection(invoker.Requests,
            request =>
            {
                Assert.Equal("PUT", request.Method);
                Assert.Equal("/_ilm/policy/aspire-logs-7d", request.PathAndQuery);
            },
            request =>
            {
                Assert.Equal("PUT", request.Method);
                Assert.Equal("/_component_template/aspire-logs-settings", request.PathAndQuery);
            },
            request =>
            {
                Assert.Equal("PUT", request.Method);
                Assert.Equal("/_component_template/aspire-logs-mappings", request.PathAndQuery);
            },
            request =>
            {
                Assert.Equal("PUT", request.Method);
                Assert.Equal("/_index_template/aspire-logs-template", request.PathAndQuery);
            },
            request =>
            {
                Assert.Equal("GET", request.Method);
                Assert.Equal("/_data_stream/aspire-logs", request.PathAndQuery);
            });
    }

    private static string NormalizeJsonForContains(string value)
    {
        return value
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .Replace("\r", string.Empty, StringComparison.Ordinal)
            .Replace("\n", string.Empty, StringComparison.Ordinal);
    }
}
