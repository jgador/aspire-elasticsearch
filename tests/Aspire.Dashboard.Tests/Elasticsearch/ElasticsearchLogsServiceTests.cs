// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Aspire.Dashboard.Model.Otlp;
using Aspire.Dashboard.Otlp.Model;
using Xunit;

namespace Aspire.Dashboard.Tests.Elasticsearch;

public class ElasticsearchLogsServiceTests
{
    [Fact]
    public async Task TryGetResourcesAsync_ReturnsSortedResourcesFromCompositeAggregation()
    {
        var invoker = ElasticsearchTestHelpers.CreateInvoker(
            ElasticsearchTestHelpers.CreateSearchResponseJson(
                aggregations: new Dictionary<string, object?>
                {
                    ["composite#resources"] = new
                    {
                        after_key = new
                        {
                            service_name = "checkout",
                            service_instance_id = "instance-b"
                        },
                        buckets = new object[]
                        {
                            new
                            {
                                key = new
                                {
                                    service_name = "checkout",
                                    service_instance_id = "instance-b"
                                },
                                doc_count = 5
                            },
                            new
                            {
                                key = new
                                {
                                    service_name = "frontend",
                                    service_instance_id = (string?)null
                                },
                                doc_count = 2
                            }
                        }
                    }
                }),
            ElasticsearchTestHelpers.CreateSearchResponseJson(
                aggregations: new Dictionary<string, object?>
                {
                    ["composite#resources"] = new
                    {
                        buckets = new object[]
                        {
                            new
                            {
                                key = new
                                {
                                    service_name = "checkout",
                                    service_instance_id = "instance-a"
                                },
                                doc_count = 3
                            }
                        }
                    }
                }));

        var service = ElasticsearchTestHelpers.CreateLogsService(invoker);

        var resources = await service.TryGetResourcesAsync(CancellationToken.None);

        Assert.NotNull(resources);
        Assert.Collection(resources,
            resource =>
            {
                Assert.Equal("checkout", resource.ResourceName);
                Assert.Equal("instance-a", resource.InstanceId);
                Assert.True(resource.HasLogs);
            },
            resource =>
            {
                Assert.Equal("checkout", resource.ResourceName);
                Assert.Equal("instance-b", resource.InstanceId);
                Assert.True(resource.HasLogs);
            },
            resource =>
            {
                Assert.Equal("frontend", resource.ResourceName);
                Assert.Null(resource.InstanceId);
                Assert.True(resource.HasLogs);
            });
    }

    [Fact]
    public async Task TryGetLogPropertyKeysAsync_ReturnsLabelAndExceptionKeys()
    {
        var invoker = ElasticsearchTestHelpers.CreateInvoker(
            ElasticsearchTestHelpers.CreateSearchResponseJson(
                aggregations: new Dictionary<string, object?>
                {
                    ["sterms#property_keys"] = new
                    {
                        doc_count_error_upper_bound = 0,
                        sum_other_doc_count = 0,
                        buckets = new object[]
                        {
                            new { key = "OrderId", doc_count = 3 },
                            new { key = "CustomerId", doc_count = 1 }
                        }
                    },
                    ["filter#exception_type_exists"] = new
                    {
                        doc_count = 1
                    },
                    ["filter#exception_message_exists"] = new
                    {
                        doc_count = 0
                    },
                    ["filter#exception_stacktrace_exists"] = new
                    {
                        doc_count = 2
                    }
                }));

        var service = ElasticsearchTestHelpers.CreateLogsService(invoker);

        var propertyKeys = await service.TryGetLogPropertyKeysAsync(resourceKey: null, CancellationToken.None);

        Assert.Equal(
        [
            "CustomerId",
            OtlpLogEntry.ExceptionStackTraceField,
            OtlpLogEntry.ExceptionTypeField,
            "OrderId"
        ], propertyKeys);
    }

    [Fact]
    public async Task TryGetLogsFieldValuesAsync_ReturnsCountsFromTermsAggregation()
    {
        var invoker = ElasticsearchTestHelpers.CreateInvoker(
            ElasticsearchTestHelpers.CreateSearchResponseJson(
                aggregations: new Dictionary<string, object?>
                {
                    ["sterms#field_values"] = new
                    {
                        doc_count_error_upper_bound = 0,
                        sum_other_doc_count = 0,
                        buckets = new object[]
                        {
                            new { key = "Checkout.Logger", doc_count = 4 },
                            new { key = "Frontend.Logger", doc_count = 2 }
                        }
                    }
                }));

        var service = ElasticsearchTestHelpers.CreateLogsService(invoker);

        var values = await service.TryGetLogsFieldValuesAsync(resourceKey: null, KnownStructuredLogFields.CategoryField, CancellationToken.None);

        Assert.NotNull(values);
        Assert.Equal(4, values["Checkout.Logger"]);
        Assert.Equal(2, values["Frontend.Logger"]);

        var request = Assert.Single(invoker.Requests);
        Assert.Contains("\"log.logger\"", request.Body);
    }

    [Fact]
    public async Task TryGetLogsForTraceAsync_UsesTraceFilter()
    {
        const string traceId = "0123456789abcdef0123456789abcdef";

        var invoker = ElasticsearchTestHelpers.CreateInvoker(
            ElasticsearchTestHelpers.CreateSearchResponseJson(
                documents:
                [
                    ElasticsearchTestHelpers.CreateDocument(traceId: traceId, message: "trace log 1"),
                    ElasticsearchTestHelpers.CreateDocument(traceId: traceId, message: "trace log 2")
                ],
                totalItemCount: 2));

        var service = ElasticsearchTestHelpers.CreateLogsService(invoker);

        var logs = await service.TryGetLogsForTraceAsync(traceId, CancellationToken.None);

        Assert.NotNull(logs);
        Assert.Equal(2, logs.Count);
        Assert.All(logs, log => Assert.Equal(traceId, log.TraceId));

        var request = Assert.Single(invoker.Requests);
        Assert.Contains("\"trace.id\"", request.Body);
        Assert.Contains(traceId, request.Body);
    }
}
