// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Aspire.Dashboard.Configuration;
using Aspire.Dashboard.Otlp.Model;
using Aspire.Dashboard.Otlp.Storage.Elasticsearch;
using Google.Protobuf.Collections;
using Microsoft.Extensions.Logging.Abstractions;
using OpenTelemetry.Proto.Common.V1;
using OpenTelemetry.Proto.Logs.V1;
using Xunit;
using static Aspire.Tests.Shared.Telemetry.TelemetryTestHelpers;

namespace Aspire.Dashboard.Tests.Elasticsearch;

public class ElasticsearchDocumentMapperTests
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
        var doc = ElasticsearchDocumentMapper.ToDocument(logEntry);

        // Assert
        Assert.Equal(s_testTime, doc.Timestamp);
        Assert.Equal("Hello, World!", doc.Message);
        Assert.Equal("Warning", doc.LogLevel);
        Assert.Equal((int)SeverityNumber.Warn, doc.SeverityNumber);
    }

    [Fact]
    public void ToDocument_MapsResourceFields()
    {
        // Arrange
        var logEntry = CreateTestLogEntry(
            resourceName: "MyService",
            resourceInstanceId: "instance-1");

        // Act
        var doc = ElasticsearchDocumentMapper.ToDocument(logEntry);

        // Assert
        Assert.Equal("MyService", doc.ServiceName);
        Assert.Equal("instance-1", doc.ServiceInstanceId);
    }

    [Fact]
    public void ToDocument_MapsScopeAsLoggerName()
    {
        // Arrange
        var logEntry = CreateTestLogEntry(scopeName: "MyApp.Services.OrderService");

        // Act
        var doc = ElasticsearchDocumentMapper.ToDocument(logEntry);

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
        var doc = ElasticsearchDocumentMapper.ToDocument(logEntry);

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
        var doc = ElasticsearchDocumentMapper.ToDocument(logEntry);

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
        var doc = ElasticsearchDocumentMapper.ToDocument(logEntry);

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
        var doc = ElasticsearchDocumentMapper.ToDocument(logEntry);

        // Assert — OriginalFormat is filtered out by OtlpLogEntry constructor, so no labels
        Assert.Null(doc.Labels);
    }

    [Fact]
    public void ToDocument_MapsEventName()
    {
        // Arrange
        var logEntry = CreateTestLogEntry(eventName: "OrderCreated");

        // Act
        var doc = ElasticsearchDocumentMapper.ToDocument(logEntry);

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
        var doc = ElasticsearchDocumentMapper.ToDocument(logEntry);

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
        var doc = ElasticsearchDocumentMapper.ToDocument(logEntry);

        // Assert
        Assert.Equal(expectedLevel, doc.LogLevel);
        Assert.Equal((int)severity, doc.SeverityNumber);
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
        IEnumerable<KeyValuePair<string, string>>? attributes = null)
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
        var resourceView = new OtlpResourceView(resource, new RepeatedField<KeyValue>());
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
}
