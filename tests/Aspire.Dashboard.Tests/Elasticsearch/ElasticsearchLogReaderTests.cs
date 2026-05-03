// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Aspire.Dashboard.Model.Otlp;
using Aspire.Dashboard.Otlp.Model;
using Aspire.Dashboard.Otlp.Storage;
using Aspire.Dashboard.Otlp.Storage.Elasticsearch;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Aspire.Dashboard.Tests.Elasticsearch;

public class ElasticsearchLogReaderTests
{
    [Theory]
    [InlineData(42L, 0, 10, 10, 42)]
    [InlineData((long)int.MaxValue + 100, 0, 50, 50, 51)]
    [InlineData((long)int.MaxValue + 100, 10, 0, 0, 11)]
    [InlineData((long)int.MaxValue + 100, int.MaxValue - 1, 100, 1, int.MaxValue)]
    public void GetTotalItemCount_ReturnsBoundedCount(long totalItemCount, int startIndex, int requestedCount, int returnedCount, int expected)
    {
        var result = ElasticsearchLogReader.GetTotalItemCount(totalItemCount, startIndex, requestedCount, returnedCount);

        Assert.Equal(expected, result);
    }

    [Fact]
    public async Task TryGetLogsAsync_ReturnsMappedLogs()
    {
        var invoker = ElasticsearchTestHelpers.CreateInvoker(
            ElasticsearchTestHelpers.CreateSearchResponseJson(
                documents:
                [
                    ElasticsearchTestHelpers.CreateDocument(
                        serviceName: "checkout",
                        serviceInstanceId: "instance-a",
                        message: "Checkout failed",
                        logLevel: "Error",
                        severityNumber: 17,
                        loggerName: "Checkout.Logger",
                        traceId: "0123456789abcdef0123456789abcdef",
                        spanId: "0123456789abcdef",
                        serviceVersion: "1.2.3")
                ],
                totalItemCount: 1));

        var reader = ElasticsearchTestHelpers.CreateReader(invoker);

        var result = await reader.TryGetLogsAsync(new GetLogsContext
        {
            ResourceKey = null,
            StartIndex = 0,
            Count = 50,
            Filters = []
        }, CancellationToken.None);

        Assert.NotNull(result);
        var log = Assert.Single(result.Items);
        Assert.Equal("Checkout failed", log.Message);
        Assert.Equal(LogLevel.Error, log.Severity);
        Assert.Equal("Checkout.Logger", log.Scope.Name);
        Assert.Equal("checkout", log.ResourceView.Resource.ResourceName);
        Assert.Equal("instance-a", log.ResourceView.ResourceKey.InstanceId);
        Assert.Contains(log.ResourceView.Properties, p => p.Key == "service.version" && p.Value == "1.2.3");
    }

    [Fact]
    public async Task TryGetLogsAsync_WritesDescendingTimestampSort()
    {
        var invoker = ElasticsearchTestHelpers.CreateInvoker(
            ElasticsearchTestHelpers.CreateSearchResponseJson(totalItemCount: 0));

        var reader = ElasticsearchTestHelpers.CreateReader(invoker);

        await reader.TryGetLogsAsync(new GetLogsContext
        {
            ResourceKey = null,
            StartIndex = 0,
            Count = 10,
            Filters = []
        }, CancellationToken.None);

        var request = Assert.Single(invoker.Requests);
        Assert.Contains("\"@timestamp\"", request.Body);
        Assert.Contains("\"order\":\"desc\"", request.Body);
    }

    [Fact]
    public async Task TryGetLogsAsync_MessageContainsFilter_WritesKeywordWildcardQuery()
    {
        var invoker = ElasticsearchTestHelpers.CreateInvoker(
            ElasticsearchTestHelpers.CreateSearchResponseJson(totalItemCount: 0));

        var reader = ElasticsearchTestHelpers.CreateReader(invoker);

        await reader.TryGetLogsAsync(new GetLogsContext
        {
            ResourceKey = null,
            StartIndex = 0,
            Count = 10,
            Filters =
            [
                new FieldTelemetryFilter
                {
                    Field = nameof(OtlpLogEntry.Message),
                    Condition = FilterCondition.Contains,
                    Value = "Order failed"
                }
            ]
        }, CancellationToken.None);

        var request = Assert.Single(invoker.Requests);
        Assert.Contains("\"message.keyword\"", request.Body);
        Assert.Contains("*Order failed*", request.Body);
    }

    [Fact]
    public async Task TryGetLogsAsync_CustomLabelEqualsFilter_WritesFlattenedLabelField()
    {
        var invoker = ElasticsearchTestHelpers.CreateInvoker(
            ElasticsearchTestHelpers.CreateSearchResponseJson(totalItemCount: 0));

        var reader = ElasticsearchTestHelpers.CreateReader(invoker);

        await reader.TryGetLogsAsync(new GetLogsContext
        {
            ResourceKey = null,
            StartIndex = 0,
            Count = 10,
            Filters =
            [
                new FieldTelemetryFilter
                {
                    Field = "OrderId",
                    Condition = FilterCondition.Equals,
                    Value = "1234"
                }
            ]
        }, CancellationToken.None);

        var request = Assert.Single(invoker.Requests);
        Assert.Contains("\"labels.OrderId\"", request.Body);
        Assert.Contains("\"1234\"", request.Body);
    }

    [Fact]
    public async Task TryGetLogsAsync_ServiceVersionEqualsFilter_WritesServiceVersionField()
    {
        var invoker = ElasticsearchTestHelpers.CreateInvoker(
            ElasticsearchTestHelpers.CreateSearchResponseJson(totalItemCount: 0));

        var reader = ElasticsearchTestHelpers.CreateReader(invoker);

        await reader.TryGetLogsAsync(new GetLogsContext
        {
            ResourceKey = null,
            StartIndex = 0,
            Count = 10,
            Filters =
            [
                new FieldTelemetryFilter
                {
                    Field = "service.version",
                    Condition = FilterCondition.Equals,
                    Value = "1.2.3"
                }
            ]
        }, CancellationToken.None);

        var request = Assert.Single(invoker.Requests);
        Assert.Contains("\"service.version\"", request.Body);
        Assert.Contains("\"1.2.3\"", request.Body);
        Assert.DoesNotContain("\"labels.service.version\"", request.Body);
    }

    [Fact]
    public async Task TryGetLogsAsync_CustomLabelContainsFilter_WritesFlattenedLabelQueryString()
    {
        var invoker = ElasticsearchTestHelpers.CreateInvoker(
            ElasticsearchTestHelpers.CreateSearchResponseJson(totalItemCount: 0));

        var reader = ElasticsearchTestHelpers.CreateReader(invoker);

        await reader.TryGetLogsAsync(new GetLogsContext
        {
            ResourceKey = null,
            StartIndex = 0,
            Count = 10,
            Filters =
            [
                new FieldTelemetryFilter
                {
                    Field = "OrderId",
                    Condition = FilterCondition.Contains,
                    Value = "12 34"
                }
            ]
        }, CancellationToken.None);

        var request = Assert.Single(invoker.Requests);
        Assert.Contains("\"query_string\"", request.Body);
        Assert.Contains("\"default_field\":\"labels.OrderId\"", request.Body);
        Assert.Contains("\"query\":\"*12\\\\ 34*\"", request.Body);
    }

    [Fact]
    public async Task TryGetLogsAsync_UnsupportedTimestampContainsFilter_ReturnsNull()
    {
        var invoker = ElasticsearchTestHelpers.CreateInvoker(
            ElasticsearchTestHelpers.CreateSearchResponseJson(totalItemCount: 0));

        var reader = ElasticsearchTestHelpers.CreateReader(invoker);

        var result = await reader.TryGetLogsAsync(new GetLogsContext
        {
            ResourceKey = null,
            StartIndex = 0,
            Count = 10,
            Filters =
            [
                new FieldTelemetryFilter
                {
                    Field = nameof(OtlpLogEntry.TimeStamp),
                    Condition = FilterCondition.Contains,
                    Value = "2024-06-15T10:30:00.0000000Z"
                }
            ]
        }, CancellationToken.None);

        Assert.Null(result);
        Assert.Empty(invoker.Requests);
    }
}
