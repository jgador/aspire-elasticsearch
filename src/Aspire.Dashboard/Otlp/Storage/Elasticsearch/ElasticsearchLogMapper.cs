// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Aspire.Dashboard.Otlp.Model;
using Google.Protobuf;
using Google.Protobuf.Collections;
using OpenTelemetry.Proto.Common.V1;
using OpenTelemetry.Proto.Logs.V1;
using SeverityNumberProto = OpenTelemetry.Proto.Logs.V1.SeverityNumber;

namespace Aspire.Dashboard.Otlp.Storage.Elasticsearch;

internal sealed class ElasticsearchLogMapper
{
    private readonly OtlpContext _otlpContext;
    private readonly Dictionary<ResourceKey, OtlpResourceView> _resourceViews = new();
    private readonly Dictionary<string, OtlpScope> _scopes = new(StringComparer.Ordinal);

    public ElasticsearchLogMapper(OtlpContext otlpContext)
    {
        _otlpContext = otlpContext;
    }

    public static ElasticsearchLogDocument ToDocument(OtlpLogEntry logEntry)
    {
        var document = new ElasticsearchLogDocument
        {
            Timestamp = logEntry.TimeStamp,
            Message = logEntry.Message,
            LogLevel = logEntry.Severity.ToString(),
            SeverityNumber = logEntry.SeverityNumber,
            LoggerName = logEntry.Scope.Name,
            OriginalFormat = logEntry.OriginalFormat,
            TraceId = NullIfEmpty(logEntry.TraceId),
            SpanId = NullIfEmpty(logEntry.SpanId),
            ParentId = NullIfEmpty(logEntry.ParentId),
            ServiceName = logEntry.ResourceView.Resource.ResourceName,
            ServiceInstanceId = logEntry.ResourceView.ResourceKey.InstanceId,
            EventName = logEntry.EventName,
            Flags = logEntry.Flags
        };

        string? errorType = null;
        string? errorMessage = null;
        string? errorStackTrace = null;
        Dictionary<string, string>? labels = null;

        foreach (var attr in logEntry.Attributes)
        {
            switch (attr.Key)
            {
                case OtlpLogEntry.ExceptionTypeField:
                    errorType = attr.Value;
                    break;
                case OtlpLogEntry.ExceptionMessageField:
                    errorMessage = attr.Value;
                    break;
                case OtlpLogEntry.ExceptionStackTraceField:
                    errorStackTrace = attr.Value;
                    break;
                default:
                    labels ??= new Dictionary<string, string>();
                    labels[attr.Key] = attr.Value;
                    break;
            }
        }

        document.ErrorType = errorType;
        document.ErrorMessage = errorMessage;
        document.ErrorStackTrace = errorStackTrace;
        document.Labels = labels;

        return document;
    }

    public OtlpLogEntry ToLogEntry(ElasticsearchLogDocument document)
    {
        var resourceKey = new ResourceKey(document.ServiceName ?? string.Empty, document.ServiceInstanceId);
        if (!_resourceViews.TryGetValue(resourceKey, out var resourceView))
        {
            var resource = new OtlpResource(resourceKey.Name, resourceKey.InstanceId, uninstrumentedPeer: false, _otlpContext)
            {
                HasLogs = true
            };

            resourceView = new OtlpResourceView(resource, new RepeatedField<KeyValue>());
            _resourceViews.Add(resourceKey, resourceView);
        }

        var scopeName = document.LoggerName ?? string.Empty;
        if (!_scopes.TryGetValue(scopeName, out var scope))
        {
            scope = new OtlpScope(scopeName, string.Empty, []);
            _scopes.Add(scopeName, scope);
        }

        var logRecord = CreateLogRecord(document);
        return new OtlpLogEntry(logRecord, resourceView, scope, _otlpContext);
    }

    private static LogRecord CreateLogRecord(ElasticsearchLogDocument document)
    {
        var logRecord = new LogRecord
        {
            TimeUnixNano = OtlpHelpers.DateTimeToUnixNanoseconds(document.Timestamp),
            SeverityNumber = ResolveSeverityNumber(document),
            Flags = document.Flags
        };

        if (document.Message is { } message)
        {
            logRecord.Body = new AnyValue { StringValue = message };
        }

        if (!string.IsNullOrEmpty(document.LogLevel))
        {
            logRecord.SeverityText = document.LogLevel;
        }

        if (TryHexToByteString(document.TraceId, out var traceId))
        {
            logRecord.TraceId = traceId;
        }

        if (TryHexToByteString(document.SpanId, out var spanId))
        {
            logRecord.SpanId = spanId;
        }

        if (!string.IsNullOrEmpty(document.EventName))
        {
            logRecord.EventName = document.EventName;
        }

        if (document.OriginalFormat is not null)
        {
            logRecord.Attributes.Add(CreateAttribute("{OriginalFormat}", document.OriginalFormat));
        }

        if (!string.IsNullOrEmpty(document.ParentId))
        {
            logRecord.Attributes.Add(CreateAttribute("ParentId", document.ParentId));
        }

        foreach (var attribute in BuildAttributes(document))
        {
            logRecord.Attributes.Add(CreateAttribute(attribute.Key, attribute.Value));
        }

        return logRecord;
    }

    private static KeyValuePair<string, string>[] BuildAttributes(ElasticsearchLogDocument document)
    {
        var attributes = new List<KeyValuePair<string, string>>();

        if (!string.IsNullOrEmpty(document.ErrorType))
        {
            attributes.Add(new KeyValuePair<string, string>(OtlpLogEntry.ExceptionTypeField, document.ErrorType));
        }

        if (!string.IsNullOrEmpty(document.ErrorMessage))
        {
            attributes.Add(new KeyValuePair<string, string>(OtlpLogEntry.ExceptionMessageField, document.ErrorMessage));
        }

        if (!string.IsNullOrEmpty(document.ErrorStackTrace))
        {
            attributes.Add(new KeyValuePair<string, string>(OtlpLogEntry.ExceptionStackTraceField, document.ErrorStackTrace));
        }

        if (document.Labels is { Count: > 0 })
        {
            foreach (var label in document.Labels.OrderBy(kvp => kvp.Key, StringComparer.Ordinal))
            {
                attributes.Add(label);
            }
        }

        return [.. attributes];
    }

    private static KeyValue CreateAttribute(string key, string value)
    {
        return new KeyValue
        {
            Key = key,
            Value = new AnyValue { StringValue = value }
        };
    }

    private static SeverityNumberProto ResolveSeverityNumber(ElasticsearchLogDocument document)
    {
        if (document.SeverityNumber is >= (int)SeverityNumberProto.Unspecified and <= (int)SeverityNumberProto.Fatal4)
        {
            return (SeverityNumberProto)document.SeverityNumber;
        }

        return ResolveSeverity(document) switch
        {
            LogLevel.Trace => SeverityNumberProto.Trace,
            LogLevel.Debug => SeverityNumberProto.Debug,
            LogLevel.Information => SeverityNumberProto.Info,
            LogLevel.Warning => SeverityNumberProto.Warn,
            LogLevel.Error => SeverityNumberProto.Error,
            LogLevel.Critical => SeverityNumberProto.Fatal,
            _ => SeverityNumberProto.Unspecified
        };
    }

    private static LogLevel ResolveSeverity(ElasticsearchLogDocument document)
    {
        if (Enum.TryParse<LogLevel>(document.LogLevel, ignoreCase: true, out var severity))
        {
            return severity;
        }

        return document.SeverityNumber switch
        {
            >= 21 => LogLevel.Critical,
            >= 17 => LogLevel.Error,
            >= 13 => LogLevel.Warning,
            >= 9 => LogLevel.Information,
            >= 5 => LogLevel.Debug,
            >= 1 => LogLevel.Trace,
            _ => LogLevel.None
        };
    }

    private static string? NullIfEmpty(string value)
    {
        return string.IsNullOrEmpty(value) ? null : value;
    }

    private static bool TryHexToByteString(string? hex, out ByteString value)
    {
        if (string.IsNullOrEmpty(hex))
        {
            value = ByteString.Empty;
            return false;
        }

        try
        {
            value = ByteString.CopyFrom(Convert.FromHexString(hex));
            return true;
        }
        catch (ArgumentException)
        {
        }
        catch (FormatException)
        {
        }

        value = ByteString.Empty;
        return false;
    }
}
