// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics;
using Elastic.Clients.Elasticsearch;
using Microsoft.Extensions.Options;

namespace Aspire.Dashboard.Otlp.Storage.Elasticsearch;

/// <summary>
/// Background service that subscribes to new log entries via <see cref="TelemetryRepository.WatchLogsAsync"/>
/// and persists them to an Elasticsearch data stream in batches.
/// </summary>
internal sealed class ElasticsearchLogPersistenceService : BackgroundService
{
    private static readonly TimeSpan s_setupRetryDelay = TimeSpan.FromSeconds(5);

    private readonly TelemetryRepository _telemetryRepository;
    private readonly ElasticsearchClient _client;
    private readonly ElasticsearchDataStreamSetup _dataStreamSetup;
    private readonly ElasticsearchOptions _options;
    private readonly ILogger<ElasticsearchLogPersistenceService> _logger;

    public ElasticsearchLogPersistenceService(
        TelemetryRepository telemetryRepository,
        ElasticsearchClient client,
        ElasticsearchDataStreamSetup dataStreamSetup,
        IOptions<ElasticsearchOptions> options,
        ILogger<ElasticsearchLogPersistenceService> logger)
    {
        _telemetryRepository = telemetryRepository;
        _client = client;
        _dataStreamSetup = dataStreamSetup;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Elasticsearch log persistence service starting. Data stream: '{DataStreamName}', Endpoint: '{Endpoint}'.",
            _options.DataStreamName, _options.Endpoint);

        await WaitForDataStreamReadyAsync(stoppingToken).ConfigureAwait(false);

        _logger.LogInformation("Elasticsearch data stream ready. Beginning log ingestion.");

        var batch = new List<ElasticsearchLogDocument>(_options.BatchSize);
        var flushInterval = TimeSpan.FromSeconds(_options.FlushIntervalSeconds);
        PeriodicTimer? flushTimer = null;

        try
        {
            // Start the log watcher — resourceKey: null watches all resources, filters: null means no filtering.
            var logStream = _telemetryRepository.WatchLogsAsync(
                resourceKey: null,
                filters: null,
                cancellationToken: stoppingToken);

            var logEnumerator = logStream.GetAsyncEnumerator(stoppingToken);

            await using (logEnumerator.ConfigureAwait(false))
            {
                Task<bool> ResetFlushTimer()
                {
                    flushTimer?.Dispose();
                    flushTimer = new PeriodicTimer(flushInterval);

                    return flushTimer.WaitForNextTickAsync(stoppingToken).AsTask();
                }

                var flushTimerTask = ResetFlushTimer();
                var nextLogTask = logEnumerator.MoveNextAsync().AsTask();

                while (true)
                {
                    var completedTask = await Task.WhenAny(nextLogTask, flushTimerTask).ConfigureAwait(false);

                    if (completedTask == nextLogTask)
                    {
                        if (!await nextLogTask.ConfigureAwait(false))
                        {
                            break;
                        }

                        batch.Add(ElasticsearchLogMapper.ToDocument(logEnumerator.Current));

                        if (batch.Count >= _options.BatchSize)
                        {
                            await FlushBatchAsync(batch, stoppingToken).ConfigureAwait(false);
                            flushTimerTask = ResetFlushTimer();
                        }

                        nextLogTask = logEnumerator.MoveNextAsync().AsTask();
                    }
                    else
                    {
                        if (!await flushTimerTask.ConfigureAwait(false))
                        {
                            break;
                        }

                        if (batch.Count > 0)
                        {
                            await FlushBatchAsync(batch, stoppingToken).ConfigureAwait(false);
                        }

                        flushTimerTask = ResetFlushTimer();
                    }
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal shutdown.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Elasticsearch log persistence service encountered an error.");
        }
        finally
        {
            flushTimer?.Dispose();

            // Flush any remaining logs in the batch on shutdown.
            if (batch.Count > 0)
            {
                try
                {
                    using var shutdownCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                    await FlushBatchAsync(batch, shutdownCts.Token).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to flush remaining {Count} log(s) during shutdown.", batch.Count);
                }
            }

            _logger.LogInformation("Elasticsearch log persistence service stopped.");
        }
    }

    private async Task WaitForDataStreamReadyAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                await _dataStreamSetup.EnsureDataStreamAsync(cancellationToken).ConfigureAwait(false);
                return;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Failed to set up Elasticsearch data stream. Retrying in {RetryDelaySeconds} second(s).",
                    s_setupRetryDelay.TotalSeconds);

                await Task.Delay(s_setupRetryDelay, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task FlushBatchAsync(List<ElasticsearchLogDocument> batch, CancellationToken cancellationToken)
    {
        var count = batch.Count;
        var sw = Stopwatch.StartNew();

        try
        {
            var bulkResponse = await _client.BulkAsync(b =>
            {
                b.Index(_options.DataStreamName);
                b.CreateMany(batch);
            }, cancellationToken).ConfigureAwait(false);

            sw.Stop();

            if (bulkResponse.IsValidResponse)
            {
                if (bulkResponse.Errors)
                {
                    var errorCount = bulkResponse.ItemsWithErrors.Count();
                    _logger.LogWarning("Bulk write to Elasticsearch completed with {ErrorCount} error(s) out of {TotalCount} documents in {ElapsedMs}ms.",
                        errorCount, count, sw.ElapsedMilliseconds);

                    foreach (var item in bulkResponse.ItemsWithErrors)
                    {
                        _logger.LogDebug("Elasticsearch bulk item error: {Error}", item.Error?.Reason);
                    }
                }
                else
                {
                    _logger.LogDebug("Flushed {Count} log(s) to Elasticsearch data stream '{DataStreamName}' in {ElapsedMs}ms.",
                        count, _options.DataStreamName, sw.ElapsedMilliseconds);
                }
            }
            else
            {
                _logger.LogError("Failed to write {Count} log(s) to Elasticsearch: {Error}",
                    count, bulkResponse.DebugInformation);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Exception while flushing {Count} log(s) to Elasticsearch.", count);
        }
        finally
        {
            batch.Clear();
        }
    }

}
