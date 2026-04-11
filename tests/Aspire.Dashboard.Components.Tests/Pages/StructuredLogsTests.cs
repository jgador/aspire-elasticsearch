// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Aspire.Dashboard.Components.Pages;
using Aspire.Dashboard.Components.Resize;
using Aspire.Dashboard.Components.Tests.Shared;
using Aspire.Dashboard.Configuration;
using Aspire.Dashboard.Extensions;
using Aspire.Dashboard.Model;
using Aspire.Dashboard.Model.Otlp;
using Aspire.Dashboard.Otlp.Model;
using Aspire.Dashboard.Otlp.Storage;
using Aspire.Dashboard.Otlp.Storage.Elasticsearch;
using Aspire.Dashboard.Utils;
using Bunit;
using Elastic.Clients.Elasticsearch;
using Elastic.Transport;
using Google.Protobuf.Collections;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OpenTelemetry.Proto.Logs.V1;
using Xunit;
using static Aspire.Tests.Shared.Telemetry.TelemetryTestHelpers;

namespace Aspire.Dashboard.Components.Tests.Pages;

[UseCulture("en-US")]
public partial class StructuredLogsTests : DashboardTestContext
{
    [Fact]
    public void Render_ResourceInstanceHasDashes_AppKeyResolvedCorrectly()
    {
        // Arrange
        SetupStructureLogsServices();

        var telemetryRepository = Services.GetRequiredService<TelemetryRepository>();
        telemetryRepository.AddLogs(new AddContext(), new RepeatedField<ResourceLogs>
        {
            new ResourceLogs
            {
                Resource = CreateResource(name: "TestApp", instanceId: "abc-def"),
                ScopeLogs =
                {
                    new ScopeLogs
                    {
                        Scope = CreateScope(name: "test-scope"),
                        LogRecords =
                        {
                            CreateLogRecord()
                        }
                    }
                }
            }
        });

        var navigationManager = Services.GetRequiredService<NavigationManager>();
        var uri = navigationManager.ToAbsoluteUri(DashboardUrls.StructuredLogsUrl(resource: "TestApp"));
        navigationManager.NavigateTo(uri.OriginalString);

        var viewport = new ViewportInformation(IsDesktop: true, IsUltraLowHeight: false, IsUltraLowWidth: false);

        var dimensionManager = Services.GetRequiredService<DimensionManager>();
        dimensionManager.InvokeOnViewportInformationChanged(viewport);

        // Act
        var cut = RenderComponent<StructuredLogs>(builder =>
        {
            builder.Add(p => p.ResourceName, "TestApp");
            builder.Add(p => p.ViewportInformation, viewport);
        });

        // Assert
        var viewModel = Services.GetRequiredService<StructuredLogsViewModel>();

        Assert.NotNull(viewModel.ResourceKey);
        Assert.Equal("TestApp", viewModel.ResourceKey.Value.Name);
        Assert.Equal("abc-def", viewModel.ResourceKey.Value.InstanceId);
    }

    [Fact]
    public void Render_ElasticsearchEnabled_ClearsUnreadErrorLogs()
    {
        SetupStructureLogsServices(elasticsearchLogsService: CreateElasticsearchLogsService());

        var viewModel = Services.GetRequiredService<StructuredLogsViewModel>();
        Assert.True(viewModel.HasElasticsearchLogsService);

        var telemetryRepository = Services.GetRequiredService<TelemetryRepository>();
        telemetryRepository.AddLogs(new AddContext(), new RepeatedField<ResourceLogs>
        {
            new ResourceLogs
            {
                Resource = CreateResource(),
                ScopeLogs =
                {
                    new ScopeLogs
                    {
                        Scope = CreateScope(name: "test-scope"),
                        LogRecords =
                        {
                            CreateLogRecord(severity: SeverityNumber.Error)
                        }
                    }
                }
            }
        });

        Assert.NotEmpty(telemetryRepository.GetResourceUnviewedErrorLogsCount());

        var viewport = new ViewportInformation(IsDesktop: true, IsUltraLowHeight: false, IsUltraLowWidth: false);
        var dimensionManager = Services.GetRequiredService<DimensionManager>();
        dimensionManager.InvokeOnViewportInformationChanged(viewport);

        var cut = RenderComponent<StructuredLogs>(builder =>
        {
            builder.Add(p => p.ViewportInformation, viewport);
        });

        cut.WaitForAssertion(() => Assert.Empty(telemetryRepository.GetResourceUnviewedErrorLogsCount()));
    }

    [Fact]
    public void Render_TraceIdAndSpanId_FilterAdded()
    {
        // Arrange
        SetupStructureLogsServices();

        var navigationManager = Services.GetRequiredService<NavigationManager>();
        var uri = navigationManager.ToAbsoluteUri(DashboardUrls.StructuredLogsUrl(traceId: "123", spanId: "456"));
        navigationManager.NavigateTo(uri.OriginalString);

        var viewport = new ViewportInformation(IsDesktop: true, IsUltraLowHeight: false, IsUltraLowWidth: false);

        var dimensionManager = Services.GetRequiredService<DimensionManager>();
        dimensionManager.InvokeOnViewportInformationChanged(viewport);

        // Act
        var cut = RenderComponent<StructuredLogs>(builder =>
        {
            builder.Add(p => p.ViewportInformation, viewport);
        });

        // Assert
        var viewModel = Services.GetRequiredService<StructuredLogsViewModel>();

        Assert.Collection(viewModel.Filters,
            f =>
            {
                Assert.Equal(KnownStructuredLogFields.TraceIdField, f.Field);
                Assert.Equal("123", f.Value);
            },
            f =>
            {
                Assert.Equal(KnownStructuredLogFields.SpanIdField, f.Field);
                Assert.Equal("456", f.Value);
            });
    }

    [Fact]
    public void Render_DuplicateFilters_SingleFilterAdded()
    {
        // Arrange
        SetupStructureLogsServices();

        var filter = new FieldTelemetryFilter { Field = "TestField", Condition = FilterCondition.Contains, Value = "TestValue" };
        var serializedFilter = TelemetryFilterFormatter.SerializeFiltersToString([filter, filter]);

        var navigationManager = Services.GetRequiredService<NavigationManager>();
        var uri = navigationManager.ToAbsoluteUri(DashboardUrls.StructuredLogsUrl(filters: serializedFilter));
        navigationManager.NavigateTo(uri.OriginalString);

        var viewport = new ViewportInformation(IsDesktop: true, IsUltraLowHeight: false, IsUltraLowWidth: false);

        var dimensionManager = Services.GetRequiredService<DimensionManager>();
        dimensionManager.InvokeOnViewportInformationChanged(viewport);

        // Act
        var cut = RenderComponent<StructuredLogs>(builder =>
        {
            builder.Add(p => p.ViewportInformation, viewport);
        });

        // Assert
        var viewModel = Services.GetRequiredService<StructuredLogsViewModel>();

        Assert.Collection(viewModel.Filters,
            f =>
            {
                Assert.Equal(filter.Field, f.Field);
                Assert.Equal(filter.Condition, f.Condition);
                Assert.Equal(filter.Value, f.Value);
            });
    }

    [Fact]
    public void Render_FiltersWithSpecialCharacters_SuccessfullyParsed()
    {
        // Arrange
        SetupStructureLogsServices();

        var filter1 = new FieldTelemetryFilter { Field = "Test:Field", Condition = FilterCondition.Contains, Value = "Test Value" };
        var filter2 = new FieldTelemetryFilter { Field = "Test!@#", Condition = FilterCondition.Contains, Value = "http://localhost#fragment?hi=true" };
        var filter3 = new FieldTelemetryFilter { Field = "\u2764\uFE0F", Condition = FilterCondition.Contains, Value = "\u4F60" };
        var serializedFilter = TelemetryFilterFormatter.SerializeFiltersToString([filter1, filter2, filter3]);

        var navigationManager = Services.GetRequiredService<NavigationManager>();
        var uri = navigationManager.ToAbsoluteUri(DashboardUrls.StructuredLogsUrl(filters: serializedFilter));
        navigationManager.NavigateTo(uri.OriginalString);

        var viewport = new ViewportInformation(IsDesktop: true, IsUltraLowHeight: false, IsUltraLowWidth: false);

        var dimensionManager = Services.GetRequiredService<DimensionManager>();
        dimensionManager.InvokeOnViewportInformationChanged(viewport);

        // Act
        var cut = RenderComponent<StructuredLogs>(builder =>
        {
            builder.Add(p => p.ViewportInformation, viewport);
        });

        // Assert
        var viewModel = Services.GetRequiredService<StructuredLogsViewModel>();

        Assert.Collection(viewModel.Filters,
            f =>
            {
                Assert.Equal(filter1.Field, f.Field);
                Assert.Equal(filter1.Condition, f.Condition);
                Assert.Equal(filter1.Value, f.Value);
            },
            f =>
            {
                Assert.Equal(filter2.Field, f.Field);
                Assert.Equal(filter2.Condition, f.Condition);
                Assert.Equal(filter2.Value, f.Value);
            },
            f =>
            {
                Assert.Equal(filter3.Field, f.Field);
                Assert.Equal(filter3.Condition, f.Condition);
                Assert.Equal(filter3.Value, f.Value);
            });
    }

    [Fact]
    public void Render_NoDuration_DefaultsToLastFifteenMinutes()
    {
        SetupStructureLogsServices();

        var viewport = new ViewportInformation(IsDesktop: true, IsUltraLowHeight: false, IsUltraLowWidth: false);
        var dimensionManager = Services.GetRequiredService<DimensionManager>();
        dimensionManager.InvokeOnViewportInformationChanged(viewport);

        var cut = RenderComponent<StructuredLogs>(builder =>
        {
            builder.Add(p => p.ViewportInformation, viewport);
        });

        var viewModel = Services.GetRequiredService<StructuredLogsViewModel>();

        Assert.Equal(15, cut.Instance.PageViewModel.SelectedDuration.Id);
        Assert.Equal(StructuredLogsViewModel.DefaultDuration, viewModel.Duration);
        Assert.Equal("/structuredlogs?duration=15", cut.Instance.GetUrlFromSerializableViewModel(cut.Instance.ConvertViewModelToSerializable()));
    }

    [Fact]
    public void Render_CustomDurationInQuery_CustomTimeRangeSelected()
    {
        SetupStructureLogsServices();

        var navigationManager = Services.GetRequiredService<NavigationManager>();
        var uri = navigationManager.ToAbsoluteUri(DashboardUrls.StructuredLogsUrl(duration: 90));
        navigationManager.NavigateTo(uri.OriginalString);

        var viewport = new ViewportInformation(IsDesktop: true, IsUltraLowHeight: false, IsUltraLowWidth: false);
        var dimensionManager = Services.GetRequiredService<DimensionManager>();
        dimensionManager.InvokeOnViewportInformationChanged(viewport);

        var cut = RenderComponent<StructuredLogs>(builder =>
        {
            builder.Add(p => p.ViewportInformation, viewport);
        });

        var viewModel = Services.GetRequiredService<StructuredLogsViewModel>();

        Assert.Null(cut.Instance.PageViewModel.SelectedDuration.Id);
        Assert.Equal(90, cut.Instance.PageViewModel.CustomDurationValue);
        Assert.Equal(StructuredLogs.StructuredLogsTimeUnit.Minutes, cut.Instance.PageViewModel.SelectedCustomDurationUnit.Id);
        Assert.Equal(TimeSpan.FromMinutes(90), viewModel.Duration);
        Assert.Equal("/structuredlogs?duration=90", cut.Instance.GetUrlFromSerializableViewModel(cut.Instance.ConvertViewModelToSerializable()));
    }

    [Fact]
    public void GetFilters_DurationConfigured_AddsTimestampCutoffFilter()
    {
        SetupStructureLogsServices();

        var timeProvider = (TestTimeProvider)Services.GetRequiredService<BrowserTimeProvider>();
        timeProvider.UtcNow = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);

        var viewModel = Services.GetRequiredService<StructuredLogsViewModel>();
        viewModel.Duration = TimeSpan.FromMinutes(30);

        var timestampFilter = Assert.Single(viewModel.GetFilters().OfType<FieldTelemetryFilter>(), f => f.Field == nameof(OtlpLogEntry.TimeStamp));

        Assert.Equal(FilterCondition.GreaterThanOrEqual, timestampFilter.Condition);
        Assert.Equal(
            timeProvider.UtcNow.UtcDateTime.Subtract(TimeSpan.FromMinutes(30)),
            DateTime.Parse(timestampFilter.Value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind));
    }

    [Fact]
    public async Task HandleSelectedDurationChanged_ClearsSelectedLogEntry()
    {
        SetupStructureLogsServices();

        var telemetryRepository = Services.GetRequiredService<TelemetryRepository>();
        telemetryRepository.AddLogs(new AddContext(), new RepeatedField<ResourceLogs>
        {
            new ResourceLogs
            {
                Resource = CreateResource(name: "TestApp"),
                ScopeLogs =
                {
                    new ScopeLogs
                    {
                        Scope = CreateScope(name: "test-scope"),
                        LogRecords =
                        {
                            CreateLogRecord()
                        }
                    }
                }
            }
        });

        var viewport = new ViewportInformation(IsDesktop: true, IsUltraLowHeight: false, IsUltraLowWidth: false);
        var dimensionManager = Services.GetRequiredService<DimensionManager>();
        dimensionManager.InvokeOnViewportInformationChanged(viewport);

        var cut = RenderComponent<StructuredLogs>(builder =>
        {
            builder.Add(p => p.ViewportInformation, viewport);
        });

        var logEntry = telemetryRepository.GetLogs(new GetLogsContext
        {
            ResourceKey = null,
            StartIndex = 0,
            Count = 1,
            Filters = []
        }).Items.Single();

        cut.Instance.SelectedLogEntry = new StructureLogsDetailsViewModel
        {
            LogEntry = logEntry
        };
        cut.Instance.PageViewModel.SelectedDuration = new SelectViewModel<int?> { Id = 30, Name = "Last 30 minutes" };

        await cut.InvokeAsync(cut.Instance.HandleSelectedDurationChangedAsync);

        var navigationManager = Services.GetRequiredService<NavigationManager>();
        var viewModel = Services.GetRequiredService<StructuredLogsViewModel>();

        Assert.Null(cut.Instance.SelectedLogEntry);
        Assert.Equal(TimeSpan.FromMinutes(30), viewModel.Duration);
        Assert.Equal("/structuredlogs?duration=30", new Uri(navigationManager.Uri).PathAndQuery);
    }

    private void SetupStructureLogsServices(ElasticsearchLogsService? elasticsearchLogsService = null)
    {
        FluentUISetupHelpers.SetupFluentDivider(this);
        FluentUISetupHelpers.SetupFluentInputLabel(this);
        FluentUISetupHelpers.SetupFluentDataGrid(this);
        FluentUISetupHelpers.SetupFluentList(this);
        FluentUISetupHelpers.SetupFluentSearch(this);
        FluentUISetupHelpers.SetupFluentKeyCode(this);
        FluentUISetupHelpers.SetupFluentMenu(this);
        FluentUISetupHelpers.SetupFluentToolbar(this);
        FluentUISetupHelpers.SetupFluentAnchoredRegion(this);
        FluentUISetupHelpers.SetupFluentTextField(this);

        JSInterop.SetupVoid("initializeContinuousScroll");
        JSInterop.SetupVoid("resetContinuousScrollPosition");

        FluentUISetupHelpers.AddCommonDashboardServices(this);
        Services.AddSingleton<ILogger<StructuredLogs>>(NullLogger<StructuredLogs>.Instance);
        if (elasticsearchLogsService is not null)
        {
            Services.AddSingleton(elasticsearchLogsService);
        }
        Services.AddSingleton(sp => new StructuredLogsViewModel(
            sp.GetRequiredService<TelemetryRepository>(),
            sp.GetRequiredService<BrowserTimeProvider>(),
            sp));
    }

    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static ElasticsearchLogsService CreateElasticsearchLogsService()
    {
        var resourcesResponse = CreateSearchResponseJson(
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
                                service_name = "TestService",
                                service_instance_id = "TestId"
                            },
                            doc_count = 1
                        }
                    }
                }
            });

        var logsResponse = CreateSearchResponseJson(
            documents:
            [
                CreateDocument(
                    message: "from es",
                    logLevel: "Error",
                    severityNumber: (int)SeverityNumber.Error,
                    serviceName: "TestService",
                    serviceInstanceId: "TestId")
            ],
            totalItemCount: 1);

        var invoker = new TestRequestInvoker((_, body) =>
            body.Contains("\"composite\"", StringComparison.Ordinal)
                ? new TestResponse(resourcesResponse)
                : new TestResponse(logsResponse));

        var client = new ElasticsearchClient(new ElasticsearchClientSettings(
            new SingleNodePool(new Uri("http://localhost:9200")),
            invoker));

        var dashboardOptions = Options.Create(new DashboardOptions());
        var elasticsearchOptions = Options.Create(new ElasticsearchOptions
        {
            Enabled = true,
            Endpoint = "http://localhost:9200",
            DataStreamName = "aspire-logs"
        });

        var reader = new ElasticsearchLogReader(
            client,
            elasticsearchOptions,
            dashboardOptions,
            NullLogger<ElasticsearchLogReader>.Instance);

        return new ElasticsearchLogsService(
            client,
            reader,
            elasticsearchOptions,
            dashboardOptions,
            NullLogger<ElasticsearchLogsService>.Instance);
    }

    private static string CreateSearchResponseJson(
        IEnumerable<ElasticsearchLogDocument>? documents = null,
        long totalItemCount = 0,
        object? aggregations = null)
    {
        var response = new
        {
            took = 1,
            timed_out = false,
            _shards = new
            {
                total = 1,
                successful = 1,
                skipped = 0,
                failed = 0
            },
            hits = new
            {
                total = new
                {
                    value = totalItemCount,
                    relation = "eq"
                },
                hits = (documents ?? [])
                    .Select((document, index) => new
                    {
                        _index = "aspire-logs",
                        _id = $"doc-{index}",
                        _source = document
                    })
                    .ToArray()
            },
            aggregations
        };

        return JsonSerializer.Serialize(response, s_jsonOptions);
    }

    private static ElasticsearchLogDocument CreateDocument(
        string message,
        string logLevel,
        int severityNumber,
        string serviceName,
        string serviceInstanceId)
    {
        return new ElasticsearchLogDocument
        {
            Timestamp = new DateTime(2024, 6, 15, 10, 30, 0, DateTimeKind.Utc),
            Message = message,
            LogLevel = logLevel,
            SeverityNumber = severityNumber,
            LoggerName = "Orders.Logger",
            TraceId = "0123456789abcdef0123456789abcdef",
            SpanId = "0123456789abcdef",
            ServiceName = serviceName,
            ServiceInstanceId = serviceInstanceId
        };
    }

    private sealed class TestRequestInvoker(Func<string, string, TestResponse> getResponse) : IRequestInvoker, IDisposable
    {
        private static readonly Dictionary<string, IEnumerable<string>> s_responseHeaders = new(StringComparer.OrdinalIgnoreCase)
        {
            ["X-Elastic-Product"] = ["Elasticsearch"]
        };

        private readonly InMemoryRequestInvoker _inner = new();

        public ResponseFactory ResponseFactory => _inner.ResponseFactory;

        public TResponse Request<TResponse>(Endpoint endpoint, BoundConfiguration boundConfiguration, PostData? postData)
            where TResponse : TransportResponse, new()
        {
            var response = GetResponse(endpoint, boundConfiguration, postData);
            var responseBytes = Encoding.UTF8.GetBytes(response.Body);

            return ResponseFactory.Create<TResponse>(
                endpoint,
                boundConfiguration,
                postData,
                ex: null,
                statusCode: response.StatusCode,
                headers: s_responseHeaders,
                responseStream: new MemoryStream(responseBytes),
                contentType: response.ContentType,
                contentLength: responseBytes.Length,
                threadPoolStats: null,
                tcpStats: null);
        }

        public Task<TResponse> RequestAsync<TResponse>(Endpoint endpoint, BoundConfiguration boundConfiguration, PostData? postData, CancellationToken cancellationToken)
            where TResponse : TransportResponse, new()
        {
            var response = GetResponse(endpoint, boundConfiguration, postData);
            var responseBytes = Encoding.UTF8.GetBytes(response.Body);

            return ResponseFactory.CreateAsync<TResponse>(
                endpoint,
                boundConfiguration,
                postData,
                ex: null,
                statusCode: response.StatusCode,
                headers: s_responseHeaders,
                responseStream: new MemoryStream(responseBytes),
                contentType: response.ContentType,
                contentLength: responseBytes.Length,
                threadPoolStats: null,
                tcpStats: null,
                cancellationToken);
        }

        private TestResponse GetResponse(Endpoint endpoint, BoundConfiguration boundConfiguration, PostData? postData)
        {
            if (postData is null)
            {
                return getResponse(endpoint.PathAndQuery, string.Empty);
            }

            using var stream = new MemoryStream();
            postData.Write(stream, boundConfiguration.ConnectionSettings, disableDirectStreaming: false);

            return getResponse(endpoint.PathAndQuery, Encoding.UTF8.GetString(stream.ToArray()));
        }

        public void Dispose()
        {
        }
    }

    private readonly record struct TestResponse(string Body, int StatusCode = 200, string ContentType = "application/json");
}
