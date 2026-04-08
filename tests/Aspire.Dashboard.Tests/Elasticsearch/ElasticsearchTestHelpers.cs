// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Aspire.Dashboard.Configuration;
using Aspire.Dashboard.Otlp.Storage.Elasticsearch;
using Elastic.Clients.Elasticsearch;
using Elastic.Transport;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Aspire.Dashboard.Tests.Elasticsearch;

internal static class ElasticsearchTestHelpers
{
    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static ElasticsearchLogReader CreateReader(
        RecordingInMemoryRequestInvoker invoker,
        ElasticsearchOptions? options = null)
    {
        var client = CreateClient(invoker);
        return new ElasticsearchLogReader(
            client,
            Options.Create(options ?? CreateOptions()),
            Options.Create(new DashboardOptions()),
            NullLogger<ElasticsearchLogReader>.Instance);
    }

    public static ElasticsearchLogsService CreateLogsService(
        RecordingInMemoryRequestInvoker invoker,
        ElasticsearchOptions? options = null)
    {
        var client = CreateClient(invoker);
        var reader = new ElasticsearchLogReader(
            client,
            Options.Create(options ?? CreateOptions()),
            Options.Create(new DashboardOptions()),
            NullLogger<ElasticsearchLogReader>.Instance);

        return new ElasticsearchLogsService(
            client,
            reader,
            Options.Create(options ?? CreateOptions()),
            Options.Create(new DashboardOptions()),
            NullLogger<ElasticsearchLogsService>.Instance);
    }

    public static ElasticsearchDataStreamSetup CreateDataStreamSetup(
        RecordingInMemoryRequestInvoker invoker,
        ElasticsearchOptions? options = null)
    {
        var client = CreateClient(invoker);
        return new ElasticsearchDataStreamSetup(
            client,
            CreateHostEnvironment(),
            Options.Create(options ?? CreateOptions()),
            NullLogger<ElasticsearchDataStreamSetup>.Instance);
    }

    public static RecordingInMemoryRequestInvoker CreateInvoker(params string[] responses)
    {
        var invoker = new RecordingInMemoryRequestInvoker();
        foreach (var response in responses)
        {
            invoker.EnqueueResponse(response);
        }

        return invoker;
    }

    public static string CreateSearchResponseJson(
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

    public static string CreateAcknowledgedResponseJson()
    {
        return JsonSerializer.Serialize(new
        {
            acknowledged = true
        }, s_jsonOptions);
    }

    public static string CreateIndexNotFoundResponseJson(string indexName)
    {
        return JsonSerializer.Serialize(new
        {
            error = new
            {
                root_cause = new[]
                {
                    new
                    {
                        type = "index_not_found_exception",
                        reason = $"no such index [{indexName}]",
                        resource = new
                        {
                            type = "index_or_alias",
                            id = indexName
                        },
                        index_uuid = "_na_",
                        index = indexName
                    }
                },
                type = "index_not_found_exception",
                reason = $"no such index [{indexName}]",
                resource = new
                {
                    type = "index_or_alias",
                    id = indexName
                },
                index_uuid = "_na_",
                index = indexName
            },
            status = 404
        }, s_jsonOptions);
    }

    public static string CreateGetDataStreamResponseJson(string dataStreamName)
    {
        return JsonSerializer.Serialize(new
        {
            data_streams = new[]
            {
                new
                {
                    name = dataStreamName,
                    timestamp_field = new
                    {
                        name = "@timestamp"
                    },
                    indices = new[]
                    {
                        new
                        {
                            index_name = $".ds-{dataStreamName}-2026.04.07-000001",
                            index_uuid = "test-index-uuid",
                            prefer_ilm = true,
                            managed_by = "Index Lifecycle Management"
                        }
                    },
                    generation = 1,
                    status = "GREEN",
                    template = $"{dataStreamName}-template",
                    hidden = false,
                    system = false,
                    allow_custom_routing = false,
                    replicated = false,
                    rollover_on_write = false
                }
            }
        }, s_jsonOptions);
    }

    public static ElasticsearchLogDocument CreateDocument(
        string? serviceName = "orders-api",
        string? serviceInstanceId = "instance-1",
        string? message = "Order created",
        string? logLevel = "Information",
        int severityNumber = 9,
        string? loggerName = "Orders.Logger",
        string? traceId = "0123456789abcdef0123456789abcdef",
        string? spanId = "0123456789abcdef")
    {
        return new ElasticsearchLogDocument
        {
            Timestamp = new DateTime(2024, 6, 15, 10, 30, 0, DateTimeKind.Utc),
            Message = message ?? string.Empty,
            LogLevel = logLevel ?? string.Empty,
            SeverityNumber = severityNumber,
            LoggerName = loggerName,
            TraceId = traceId,
            SpanId = spanId,
            ServiceName = serviceName,
            ServiceInstanceId = serviceInstanceId
        };
    }

    private static ElasticsearchClient CreateClient(RecordingInMemoryRequestInvoker invoker)
    {
        return new ElasticsearchClient(new ElasticsearchClientSettings(
            new SingleNodePool(new Uri("http://localhost:9200")),
            invoker));
    }

    private static ElasticsearchOptions CreateOptions()
    {
        return new ElasticsearchOptions
        {
            Enabled = true,
            Endpoint = "http://localhost:9200",
            DataStreamName = "aspire-logs"
        };
    }

    private static IHostEnvironment CreateHostEnvironment()
    {
        return new TestHostEnvironment
        {
            ApplicationName = "Aspire.Dashboard.Tests",
            ContentRootPath = ResolveDashboardContentRootPath(),
            EnvironmentName = Environments.Development
        };
    }

    private static string ResolveDashboardContentRootPath()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "src", "Aspire.Dashboard");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Unable to locate the Aspire.Dashboard project directory for Elasticsearch asset tests.");
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = string.Empty;

        public string ApplicationName { get; set; } = string.Empty;

        public string ContentRootPath { get; set; } = string.Empty;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}

internal sealed class RecordingInMemoryRequestInvoker : IRequestInvoker, IDisposable
{
    private static readonly Dictionary<string, IEnumerable<string>> s_responseHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        ["X-Elastic-Product"] = ["Elasticsearch"]
    };

    private readonly InMemoryRequestInvoker _inner = new();
    private readonly Queue<TestResponse> _responses = new();

    public List<RecordedRequest> Requests { get; } = [];

    public ResponseFactory ResponseFactory => _inner.ResponseFactory;

    public void EnqueueResponse(string responseBody, int statusCode = 200, string contentType = "application/json")
    {
        _responses.Enqueue(new TestResponse(responseBody, statusCode, contentType));
    }

    public TResponse Request<TResponse>(Endpoint endpoint, BoundConfiguration boundConfiguration, PostData? postData)
        where TResponse : TransportResponse, new()
    {
        Requests.Add(CaptureRequest(endpoint, boundConfiguration, postData));
        var response = DequeueResponse();
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
        Requests.Add(CaptureRequest(endpoint, boundConfiguration, postData));
        var response = DequeueResponse();
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

    private static RecordedRequest CaptureRequest(Endpoint endpoint, BoundConfiguration boundConfiguration, PostData? postData)
    {
        if (postData is null)
        {
            return new RecordedRequest(endpoint.Method.ToString(), endpoint.PathAndQuery, string.Empty);
        }

        using var stream = new MemoryStream();
        postData.Write(stream, boundConfiguration.ConnectionSettings, disableDirectStreaming: false);

        return new RecordedRequest(
            endpoint.Method.ToString(),
            endpoint.PathAndQuery,
            Encoding.UTF8.GetString(stream.ToArray()));
    }

    private TestResponse DequeueResponse()
    {
        if (_responses.Count == 0)
        {
            throw new InvalidOperationException("No Elasticsearch test response was queued.");
        }

        return _responses.Dequeue();
    }

    public void Dispose()
    {
    }

    public readonly record struct RecordedRequest(string Method, string PathAndQuery, string Body);

    private readonly record struct TestResponse(string Body, int StatusCode, string ContentType);
}
