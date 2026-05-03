// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Aspire.Dashboard.Configuration;
using Aspire.Dashboard.Otlp.Model;
using Aspire.Dashboard.Otlp.Storage.Elasticsearch;
using Google.Protobuf.Collections;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OpenTelemetry.Proto.Common.V1;
using OpenTelemetry.Proto.Logs.V1;
using Xunit;
using static Aspire.Tests.Shared.Telemetry.TelemetryTestHelpers;

namespace Aspire.Dashboard.Tests.Elasticsearch;

public class ElasticsearchLogMapperTests
{
    private static readonly DateTime s_testTime = new(2024, 6, 15, 10, 30, 0, DateTimeKind.Utc);

    [Fact]
    public void ToDocument_MapsBasicFields()
    {
        // Arrange
        var logEntry = CreateTestLogEntry(
            time: s_testTime,
            message: "Hello, World!",
            severity: SeverityNumber.Warn);

        // Act
        var doc = ElasticsearchLogMapper.ToDocument(logEntry);

        // Assert
        Assert.Equal(s_testTime, doc.Timestamp);
        Assert.Equal("Hello, World!", doc.Message);
        Assert.Equal("Warning", doc.LogLevel);
        Assert.Equal((int)SeverityNumber.Warn, doc.SeverityNumber);
    }

    [Fact]
    public void ToDocument_MapsResourceFields()
    {
        var logEntry = CreateTestLogEntry(
            resourceName: "MyService",
            resourceInstanceId: "instance-1",
            resourceAttributes: [new("service.version", "1.2.3")]);

        var doc = ElasticsearchLogMapper.ToDocument(logEntry);

        Assert.Equal("MyService", doc.ServiceName);
        Assert.Equal("instance-1", doc.ServiceInstanceId);
        Assert.Equal("1.2.3", doc.ServiceVersion);
    }

    [Fact]
    public void ToDocument_MapsScopeAsLoggerName()
    {
        // Arrange
        var logEntry = CreateTestLogEntry(scopeName: "MyApp.Services.OrderService");

        // Act
        var doc = ElasticsearchLogMapper.ToDocument(logEntry);

        // Assert
        Assert.Equal("MyApp.Services.OrderService", doc.LoggerName);
    }

    [Fact]
    public void ToDocument_MapsTraceAndSpanIds()
    {
        // Arrange — CreateLogRecord encodes the string as raw bytes, and OtlpLogEntry
        // converts those bytes to hex, so "abc123" becomes "616263313233".
        var logEntry = CreateTestLogEntry(traceId: "abc123", spanId: "def456");

        // Act
        var doc = ElasticsearchLogMapper.ToDocument(logEntry);

        // Assert
        Assert.Equal("616263313233", doc.TraceId);
        Assert.Equal("646566343536", doc.SpanId);
    }

    [Fact]
    public void ToDocument_ExtractsExceptionAttributes()
    {
        // Arrange
        var exceptionAttributes = new List<KeyValuePair<string, string>>
        {
            new("exception.type", "System.InvalidOperationException"),
            new("exception.message", "Operation failed"),
            new("exception.stacktrace", "at MyApp.Method() in file.cs:line 42"),
            new("{OriginalFormat}", "Error occurred"),
        };

        var logEntry = CreateTestLogEntry(
            severity: SeverityNumber.Error,
            attributes: exceptionAttributes);

        // Act
        var doc = ElasticsearchLogMapper.ToDocument(logEntry);

        // Assert
        Assert.Equal("System.InvalidOperationException", doc.ErrorType);
        Assert.Equal("Operation failed", doc.ErrorMessage);
        Assert.Equal("at MyApp.Method() in file.cs:line 42", doc.ErrorStackTrace);
    }

    [Fact]
    public void ToDocument_PlacesNonExceptionAttributesInLabels()
    {
        // Arrange
        var attributes = new List<KeyValuePair<string, string>>
        {
            new("{OriginalFormat}", "Processing order {OrderId}"),
            new("OrderId", "12345"),
            new("CustomerId", "cust-99"),
        };

        var logEntry = CreateTestLogEntry(attributes: attributes);

        // Act
        var doc = ElasticsearchLogMapper.ToDocument(logEntry);

        // Assert
        Assert.NotNull(doc.Labels);
        Assert.Equal("12345", doc.Labels["OrderId"]);
        Assert.Equal("cust-99", doc.Labels["CustomerId"]);
    }

    [Fact]
    public void ToDocument_NullLabelsWhenNoCustomAttributes()
    {
        // Arrange — create with only the OriginalFormat attribute (which gets filtered by OtlpLogEntry)
        var logEntry = CreateTestLogEntry(
            attributes: [new("{OriginalFormat}", "Simple message")]);

        // Act
        var doc = ElasticsearchLogMapper.ToDocument(logEntry);

        // Assert — OriginalFormat is filtered out by OtlpLogEntry constructor, so no labels
        Assert.Null(doc.Labels);
    }

    [Fact]
    public void ToDocument_MapsEventName()
    {
        // Arrange
        var logEntry = CreateTestLogEntry(eventName: "OrderCreated");

        // Act
        var doc = ElasticsearchLogMapper.ToDocument(logEntry);

        // Assert
        Assert.Equal("OrderCreated", doc.EventName);
    }

    [Fact]
    public void ToDocument_MapsOriginalFormat()
    {
        // Arrange
        var attributes = new List<KeyValuePair<string, string>>
        {
            new("{OriginalFormat}", "Processing order {OrderId}"),
            new("OrderId", "123"),
        };

        var logEntry = CreateTestLogEntry(attributes: attributes);

        // Act
        var doc = ElasticsearchLogMapper.ToDocument(logEntry);

        // Assert
        Assert.Equal("Processing order {OrderId}", doc.OriginalFormat);
    }

    [Theory]
    [InlineData(SeverityNumber.Trace, "Trace")]
    [InlineData(SeverityNumber.Debug, "Debug")]
    [InlineData(SeverityNumber.Info, "Information")]
    [InlineData(SeverityNumber.Warn, "Warning")]
    [InlineData(SeverityNumber.Error, "Error")]
    [InlineData(SeverityNumber.Fatal, "Critical")]
    public void ToDocument_MapsSeverityLevels(SeverityNumber severity, string expectedLevel)
    {
        // Arrange
        var logEntry = CreateTestLogEntry(severity: severity);

        // Act
        var doc = ElasticsearchLogMapper.ToDocument(logEntry);

        // Assert
        Assert.Equal(expectedLevel, doc.LogLevel);
        Assert.Equal((int)severity, doc.SeverityNumber);
    }

    [Fact]
    public void ToLogEntry_MapsBasicFields()
    {
        // Arrange
        var document = new ElasticsearchLogDocument
        {
            Timestamp = s_testTime,
            Message = "Hello from Elasticsearch",
            LogLevel = "Warning",
            SeverityNumber = (int)SeverityNumber.Warn,
            LoggerName = "MyApp.Logger",
            TraceId = GetHexId("trace-1"),
            SpanId = GetHexId("span-1"),
            ParentId = "parent-1",
            ServiceName = "MyService",
            ServiceInstanceId = "instance-1",
            ServiceVersion = "1.2.3",
            EventName = "OrderCreated",
            OriginalFormat = "Order {OrderId} created",
            Flags = 123
        };
        var mapper = CreateMapper();

        // Act
        var logEntry = mapper.ToLogEntry(document);

        // Assert
        Assert.Equal(s_testTime, logEntry.TimeStamp);
        Assert.Equal("Hello from Elasticsearch", logEntry.Message);
        Assert.Equal(LogLevel.Warning, logEntry.Severity);
        Assert.Equal((int)SeverityNumber.Warn, logEntry.SeverityNumber);
        Assert.Equal("MyApp.Logger", logEntry.Scope.Name);
        Assert.Equal(GetHexId("trace-1"), logEntry.TraceId);
        Assert.Equal(GetHexId("span-1"), logEntry.SpanId);
        Assert.Equal("parent-1", logEntry.ParentId);
        Assert.Equal("MyService", logEntry.ResourceView.Resource.ResourceName);
        Assert.Equal("instance-1", logEntry.ResourceView.ResourceKey.InstanceId);
        Assert.Contains(logEntry.ResourceView.Properties, p => p.Key == "service.version" && p.Value == "1.2.3");
        Assert.Equal("OrderCreated", logEntry.EventName);
        Assert.Equal("Order {OrderId} created", logEntry.OriginalFormat);
        Assert.Equal((uint)123, logEntry.Flags);
    }

    [Fact]
    public void ToLogEntry_MapsExceptionFieldsAndLabelsToAttributes()
    {
        // Arrange
        var document = new ElasticsearchLogDocument
        {
            Timestamp = s_testTime,
            Message = "boom",
            LogLevel = "Error",
            SeverityNumber = (int)SeverityNumber.Error,
            ServiceName = "MyService",
            ErrorType = "System.InvalidOperationException",
            ErrorMessage = "Operation failed",
            ErrorStackTrace = "at MyApp.Method()",
            Labels = new Dictionary<string, string>
            {
                ["OrderId"] = "123",
                ["CustomerId"] = "cust-99"
            }
        };
        var mapper = CreateMapper();

        // Act
        var logEntry = mapper.ToLogEntry(document);

        // Assert
        Assert.Contains(logEntry.Attributes, a => a.Key == OtlpLogEntry.ExceptionTypeField && a.Value == "System.InvalidOperationException");
        Assert.Contains(logEntry.Attributes, a => a.Key == OtlpLogEntry.ExceptionMessageField && a.Value == "Operation failed");
        Assert.Contains(logEntry.Attributes, a => a.Key == OtlpLogEntry.ExceptionStackTraceField && a.Value == "at MyApp.Method()");
        Assert.Contains(logEntry.Attributes, a => a.Key == "OrderId" && a.Value == "123");
        Assert.Contains(logEntry.Attributes, a => a.Key == "CustomerId" && a.Value == "cust-99");
    }

    [Fact]
    public void ToLogEntry_FallsBackToSeverityNumberWhenLogLevelCannotBeParsed()
    {
        // Arrange
        var document = new ElasticsearchLogDocument
        {
            Timestamp = s_testTime,
            Message = "fallback severity",
            LogLevel = "not-a-level",
            SeverityNumber = 17,
            ServiceName = "MyService"
        };
        var mapper = CreateMapper();

        // Act
        var logEntry = mapper.ToLogEntry(document);

        // Assert
        Assert.Equal(LogLevel.Error, logEntry.Severity);
    }

    [Fact]
    public void ToLogEntry_ReusesResourceViewAndScopeForRepeatedValues()
    {
        // Arrange
        var mapper = CreateMapper();
        var first = new ElasticsearchLogDocument
        {
            Timestamp = s_testTime,
            Message = "first",
            LogLevel = "Information",
            SeverityNumber = (int)SeverityNumber.Info,
            LoggerName = "SharedLogger",
            ServiceName = "SharedService",
            ServiceInstanceId = "instance-1"
        };
        var second = new ElasticsearchLogDocument
        {
            Timestamp = s_testTime.AddSeconds(1),
            Message = "second",
            LogLevel = "Information",
            SeverityNumber = (int)SeverityNumber.Info,
            LoggerName = "SharedLogger",
            ServiceName = "SharedService",
            ServiceInstanceId = "instance-1"
        };

        // Act
        var firstLogEntry = mapper.ToLogEntry(first);
        var secondLogEntry = mapper.ToLogEntry(second);

        // Assert
        Assert.Same(firstLogEntry.ResourceView, secondLogEntry.ResourceView);
        Assert.Same(firstLogEntry.Scope, secondLogEntry.Scope);
    }

    [Fact]
    public void ToLogEntry_UsesDifferentResourceViewsForDifferentServiceVersions()
    {
        var mapper = CreateMapper();
        var first = new ElasticsearchLogDocument
        {
            Timestamp = s_testTime,
            Message = "first",
            LogLevel = "Information",
            SeverityNumber = (int)SeverityNumber.Info,
            LoggerName = "SharedLogger",
            ServiceName = "SharedService",
            ServiceInstanceId = "instance-1",
            ServiceVersion = "1.0.0"
        };
        var second = new ElasticsearchLogDocument
        {
            Timestamp = s_testTime.AddSeconds(1),
            Message = "second",
            LogLevel = "Information",
            SeverityNumber = (int)SeverityNumber.Info,
            LoggerName = "SharedLogger",
            ServiceName = "SharedService",
            ServiceInstanceId = "instance-1",
            ServiceVersion = "2.0.0"
        };

        var firstLogEntry = mapper.ToLogEntry(first);
        var secondLogEntry = mapper.ToLogEntry(second);

        Assert.NotSame(firstLogEntry.ResourceView, secondLogEntry.ResourceView);
        Assert.Same(firstLogEntry.ResourceView.Resource, secondLogEntry.ResourceView.Resource);
        Assert.Contains(firstLogEntry.ResourceView.Properties, p => p.Key == "service.version" && p.Value == "1.0.0");
        Assert.Contains(secondLogEntry.ResourceView.Properties, p => p.Key == "service.version" && p.Value == "2.0.0");
    }

    private static OtlpLogEntry CreateTestLogEntry(
        DateTime? time = null,
        string? message = null,
        SeverityNumber? severity = null,
        string? resourceName = null,
        string? resourceInstanceId = null,
        string? scopeName = null,
        string? traceId = null,
        string? spanId = null,
        string? eventName = null,
        IEnumerable<KeyValuePair<string, string>>? attributes = null,
        IEnumerable<KeyValuePair<string, string>>? resourceAttributes = null)
    {
        var context = new OtlpContext
        {
            Logger = NullLogger.Instance,
            Options = new TelemetryLimitOptions()
        };

        var resource = new OtlpResource(
            resourceName ?? "TestService",
            resourceInstanceId ?? "TestId",
            uninstrumentedPeer: false,
            context);
        var resourceView = new OtlpResourceView(resource, CreateResourceAttributes(resourceAttributes));
        var scope = CreateOtlpScope(context, scopeName ?? "TestLogger");

        var logRecord = CreateLogRecord(
            time: time,
            message: message,
            severity: severity,
            attributes: attributes,
            traceId: traceId,
            spanId: spanId,
            eventName: eventName);

        return new OtlpLogEntry(logRecord, resourceView, scope, context);
    }

    private static RepeatedField<KeyValue> CreateResourceAttributes(IEnumerable<KeyValuePair<string, string>>? attributes)
    {
        var resourceAttributes = new RepeatedField<KeyValue>();

        if (attributes is not null)
        {
            foreach (var attribute in attributes)
            {
                resourceAttributes.Add(new KeyValue { Key = attribute.Key, Value = new AnyValue { StringValue = attribute.Value } });
            }
        }

        return resourceAttributes;
    }

    private static ElasticsearchLogMapper CreateMapper()
    {
        var context = new OtlpContext
        {
            Logger = NullLogger.Instance,
            Options = new TelemetryLimitOptions()
        };

        return new ElasticsearchLogMapper(context);
    }
}
