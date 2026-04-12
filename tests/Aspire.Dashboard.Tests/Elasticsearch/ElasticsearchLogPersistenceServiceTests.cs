// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Aspire.Dashboard.Otlp.Model;
using Aspire.Dashboard.Otlp.Storage.Elasticsearch;
using Google.Protobuf.Collections;
using Microsoft.AspNetCore.InternalTesting;
using OpenTelemetry.Proto.Logs.V1;
using Xunit;
using static Aspire.Tests.Shared.Telemetry.TelemetryTestHelpers;

namespace Aspire.Dashboard.Tests.Elasticsearch;

public class ElasticsearchLogPersistenceServiceTests
{
    private static readonly DateTime s_testTime = new(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task ExecuteAsync_FlushesBufferedLogsWhenFlushIntervalElapsesWithoutNewLogs()
    {
        var repository = CreateRepository();
        repository.AddLogs(new AddContext(), new RepeatedField<ResourceLogs>
        {
            new ResourceLogs
            {
                Resource = CreateResource(name: "service1", instanceId: "inst1"),
                ScopeLogs =
                {
                    new ScopeLogs
                    {
                        Scope = CreateScope("TestLogger"),
                        LogRecords =
                        {
                            CreateLogRecord(time: s_testTime, message: "delayed log", severity: SeverityNumber.Info)
                        }
                    }
                }
            }
        });

        var options = new ElasticsearchOptions
        {
            Enabled = true,
            Endpoint = "http://localhost:9200",
            DataStreamName = "aspire-logs",
            BatchSize = 10,
            FlushIntervalSeconds = 1
        };

        var invoker = ElasticsearchTestHelpers.CreateInvoker(
            ElasticsearchTestHelpers.CreateAcknowledgedResponseJson(),
            ElasticsearchTestHelpers.CreateAcknowledgedResponseJson(),
            ElasticsearchTestHelpers.CreateAcknowledgedResponseJson(),
            ElasticsearchTestHelpers.CreateAcknowledgedResponseJson(),
            ElasticsearchTestHelpers.CreateGetDataStreamResponseJson(options.DataStreamName),
            ElasticsearchTestHelpers.CreateBulkResponseJson());

        using var service = ElasticsearchTestHelpers.CreatePersistenceService(repository, invoker, options);
        await service.StartAsync(CancellationToken.None).DefaultTimeout();

        try
        {
            await AsyncTestHelpers.AssertIsTrueRetryAsync(
                () => invoker.RequestCount >= 6,
                "Expected the flush interval to trigger a bulk write without waiting for another log.");
        }
        finally
        {
            await service.StopAsync(CancellationToken.None).DefaultTimeout();
        }

        Assert.Collection(invoker.GetRequestsSnapshot(),
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
            },
            request =>
            {
                Assert.EndsWith("_bulk", request.PathAndQuery, StringComparison.Ordinal);
                Assert.Contains("\"message\":\"delayed log\"", request.Body);
                Assert.Contains("\"service.name\":\"service1\"", request.Body);
            });
    }
}
