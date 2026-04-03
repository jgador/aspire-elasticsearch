// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Aspire.Dashboard.Otlp.Model;

namespace Aspire.Dashboard.Otlp.Storage.Elasticsearch;

/// <summary>
/// Maps <see cref="OtlpLogEntry"/> instances to <see cref="ElasticsearchLogDocument"/> for Elasticsearch indexing.
/// </summary>
internal static class ElasticsearchDocumentMapper
{
    /// <summary>
    /// Converts an <see cref="OtlpLogEntry"/> to an <see cref="ElasticsearchLogDocument"/>.
    /// </summary>
    public static ElasticsearchLogDocument ToDocument(OtlpLogEntry logEntry)
    {
        var doc = new ElasticsearchLogDocument
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

        // Extract exception fields from attributes.
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

        doc.ErrorType = errorType;
        doc.ErrorMessage = errorMessage;
        doc.ErrorStackTrace = errorStackTrace;
        doc.Labels = labels;

        return doc;
    }

    private static string? NullIfEmpty(string value) =>
        string.IsNullOrEmpty(value) ? null : value;
}
