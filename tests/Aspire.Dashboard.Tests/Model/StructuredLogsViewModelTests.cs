// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Aspire.Dashboard.Model;
using Aspire.Dashboard.Model.Otlp;
using Aspire.Dashboard.Otlp.Model;
using Aspire.Dashboard.Otlp.Storage;
using Aspire.Dashboard.Otlp.Storage.Elasticsearch;
using Aspire.Dashboard.Tests.Elasticsearch;
using Google.Protobuf.Collections;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Proto.Logs.V1;
using Xunit;
using static Aspire.Tests.Shared.Telemetry.TelemetryTestHelpers;

namespace Aspire.Dashboard.Tests.Model;

public sealed class StructuredLogsViewModelTests
{
    private static readonly DateTime s_logTimestamp = new(2024, 6, 15, 10, 30, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset s_currentTime = new(2024, 6, 15, 11, 0, 0, TimeSpan.Zero);

    [Fact]
    public void GetLogs_UsesElasticsearchWhenAvailable()
    {
        var repository = CreateRepository();
        AddRepositoryLog(repository, message: "from repo");

        var invoker = ElasticsearchTestHelpers.CreateInvoker(
            ElasticsearchTestHelpers.CreateSearchResponseJson(
                documents:
                [
                    ElasticsearchTestHelpers.CreateDocument(message: "from es")
                ],
                totalItemCount: 1));

        var service = ElasticsearchTestHelpers.CreateLogsService(invoker);
        var viewModel = CreateViewModel(repository, elasticsearchLogsService: service);

        var logs = viewModel.GetLogs();

        var logEntry = Assert.Single(logs.Items);
        Assert.Equal("from es", logEntry.Message);
        Assert.Equal(1, logs.TotalItemCount);
        Assert.True(viewModel.HasElasticsearchLogsService);
        Assert.Single(invoker.Requests);
    }

    [Fact]
    public void GetLogs_UsesTelemetryRepositoryWhenElasticsearchServiceIsUnavailable()
    {
        var repository = CreateRepository();
        AddRepositoryLog(repository, message: "from repo");

        var viewModel = CreateViewModel(repository);

        var logs = viewModel.GetLogs();

        var logEntry = Assert.Single(logs.Items);
        Assert.Equal("from repo", logEntry.Message);
        Assert.Equal(1, logs.TotalItemCount);
        Assert.False(viewModel.HasElasticsearchLogsService);
    }

    [Fact]
    public void GetLogs_ReturnsNewestLogsFirst()
    {
        var repository = CreateRepository();
        AddRepositoryLog(repository, message: "oldest", timestamp: s_logTimestamp);
        AddRepositoryLog(repository, message: "newest", timestamp: s_logTimestamp.AddMinutes(1));

        var viewModel = CreateViewModel(repository);

        var logs = viewModel.GetLogs();

        Assert.Collection(logs.Items,
            l => Assert.Equal("newest", l.Message),
            l => Assert.Equal("oldest", l.Message));
    }

    [Fact]
    public void GetResourcesAndMetadata_UseElasticsearchWhenAvailable()
    {
        var repository = CreateRepository();
        AddRepositoryLog(repository, message: "from repo", resourceName: "repo-service");

        var invoker = ElasticsearchTestHelpers.CreateInvoker(
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
                                    service_name = "es-service",
                                    service_instance_id = "es-instance"
                                },
                                doc_count = 2
                            }
                        }
                    }
                }),
            ElasticsearchTestHelpers.CreateSearchResponseJson(
                aggregations: new Dictionary<string, object?>
                {
                    ["sterms#property_keys"] = new
                    {
                        doc_count_error_upper_bound = 0,
                        sum_other_doc_count = 0,
                        buckets = new object[]
                        {
                            new { key = "OrderId", doc_count = 3 }
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
                        doc_count = 0
                    }
                }),
            ElasticsearchTestHelpers.CreateSearchResponseJson(
                aggregations: new Dictionary<string, object?>
                {
                    ["sterms#field_values"] = new
                    {
                        doc_count_error_upper_bound = 0,
                        sum_other_doc_count = 0,
                        buckets = new object[]
                        {
                            new { key = "Checkout.Logger", doc_count = 4 }
                        }
                    }
                }));

        var service = ElasticsearchTestHelpers.CreateLogsService(invoker);
        var viewModel = CreateViewModel(repository, elasticsearchLogsService: service);

        var resources = viewModel.GetResources();
        var propertyKeys = viewModel.GetLogPropertyKeys();
        var fieldValues = viewModel.GetLogsFieldValues(KnownStructuredLogFields.CategoryField);

        var resource = Assert.Single(resources);
        Assert.Equal("es-service", resource.ResourceName);
        Assert.Equal("es-instance", resource.InstanceId);
        Assert.Equal(new[] { OtlpLogEntry.ExceptionTypeField, "OrderId" }, propertyKeys);
        Assert.Equal(4, fieldValues["Checkout.Logger"]);
        Assert.Equal(3, invoker.Requests.Count);
    }

    [Fact]
    public void GetLogsFieldValues_PassesSelectedResourceKeyToElasticsearch()
    {
        var repository = CreateRepository();
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
                            new { key = "Checkout.Logger", doc_count = 4 }
                        }
                    }
                }));

        var service = ElasticsearchTestHelpers.CreateLogsService(invoker);
        var viewModel = CreateViewModel(repository, elasticsearchLogsService: service);
        viewModel.ResourceKey = new ResourceKey("checkout", "instance-a");

        var fieldValues = viewModel.GetLogsFieldValues(KnownStructuredLogFields.CategoryField);

        Assert.Equal(4, fieldValues["Checkout.Logger"]);

        var request = Assert.Single(invoker.Requests);
        Assert.Contains("\"service.name\"", request.Body);
        Assert.Contains("\"checkout\"", request.Body);
        Assert.Contains("\"service.instance.id\"", request.Body);
        Assert.Contains("\"instance-a\"", request.Body);
    }

    [Fact]
    public void GetErrorLogs_UsesElasticsearchSeverityAndDurationFilters()
    {
        var repository = CreateRepository();

        var invoker = ElasticsearchTestHelpers.CreateInvoker(
            ElasticsearchTestHelpers.CreateSearchResponseJson(
                documents:
                [
                    ElasticsearchTestHelpers.CreateDocument(
                        message: "from es error",
                        logLevel: "Error",
                        severityNumber: (int)SeverityNumber.Error)
                ],
                totalItemCount: 1));

        var service = ElasticsearchTestHelpers.CreateLogsService(invoker);
        var viewModel = CreateViewModel(repository, elasticsearchLogsService: service, duration: TimeSpan.FromMinutes(30));

        var errorLogs = viewModel.GetErrorLogs(count: 10);

        var logEntry = Assert.Single(errorLogs.Items);
        Assert.Equal(LogLevel.Error, logEntry.Severity);

        var request = Assert.Single(invoker.Requests);
        Assert.Contains("\"@timestamp\"", request.Body);
        Assert.Contains("2024-06-15T10:30:00", request.Body);
        Assert.Contains("\"log.level\"", request.Body);
        Assert.Contains("\"Error\"", request.Body);
        Assert.Contains("\"Critical\"", request.Body);
    }

    private static StructuredLogsViewModel CreateViewModel(
        TelemetryRepository repository,
        ElasticsearchLogsService? elasticsearchLogsService = null,
        TimeSpan? duration = null)
    {
        var timeProvider = new TestTimeProvider
        {
            UtcNow = s_currentTime
        };

        var viewModel = elasticsearchLogsService is null
            ? new StructuredLogsViewModel(repository, timeProvider)
            : new StructuredLogsViewModel(repository, timeProvider, elasticsearchLogsService);

        viewModel.StartIndex = 0;
        viewModel.Count = 10;
        viewModel.Duration = duration ?? TimeSpan.FromDays(1);

        return viewModel;
    }

    private static void AddRepositoryLog(TelemetryRepository repository, string message, string resourceName = "repo-service", DateTime? timestamp = null)
    {
        var addContext = new AddContext();
        repository.AddLogs(addContext, new RepeatedField<ResourceLogs>
        {
            new ResourceLogs
            {
                Resource = CreateResource(name: resourceName, instanceId: "repo-instance"),
                ScopeLogs =
                {
                    new ScopeLogs
                    {
                        Scope = CreateScope(name: "Repo.Logger"),
                        LogRecords =
                        {
                            CreateLogRecord(time: timestamp ?? s_logTimestamp, message: message)
                        }
                    }
                }
            }
        });

        Assert.Equal(0, addContext.FailureCount);
    }

    private sealed class TestTimeProvider : BrowserTimeProvider
    {
        public TestTimeProvider()
            : base(Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance)
        {
        }

        public DateTimeOffset UtcNow { get; set; }

        public override DateTimeOffset GetUtcNow() => UtcNow;
    }
}
