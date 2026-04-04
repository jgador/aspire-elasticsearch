// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Globalization;
using Aspire.Dashboard.Otlp.Storage.Elasticsearch;
using Microsoft.AspNetCore.Components;
using Microsoft.FluentUI.AspNetCore.Components;

namespace Aspire.Dashboard.Components.Pages;

public partial class HistoricalLogs : ComponentBase
{
    private string _startTimeText = DateTime.UtcNow.AddHours(-1).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
    private string _endTimeText = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
    private string _selectedService = "All";
    private string _selectedLogLevel = "All";
    private string _searchText = string.Empty;
    private bool _isLoading;
    private int _currentPage;
    private int _totalPages;
    private const int PageSize = 50;

    private HistoricalLogsQueryResult? _result;
    private List<string> _serviceOptions = ["All"];
    private readonly List<string> _logLevelOptions = ["All", "Trace", "Debug", "Information", "Warning", "Error", "Critical"];

    private readonly GridSort<HistoricalLogEntry> _timestampSort = GridSort<HistoricalLogEntry>.ByDescending(e => e.Timestamp);

    [Inject]
    internal ElasticsearchLogQueryService QueryService { get; init; } = null!;

    protected override async Task OnInitializedAsync()
    {
        await LoadServiceNamesAsync();
    }

    private async Task LoadServiceNamesAsync()
    {
        var names = await QueryService.GetServiceNamesAsync();
        _serviceOptions = ["All", .. names.OrderBy(n => n)];
    }

    private async Task SearchAsync()
    {
        _currentPage = 0;
        await ExecuteQueryAsync();
    }

    private async Task PreviousPageAsync()
    {
        if (_currentPage > 0)
        {
            _currentPage--;
            await ExecuteQueryAsync();
        }
    }

    private async Task NextPageAsync()
    {
        if (_currentPage < _totalPages - 1)
        {
            _currentPage++;
            await ExecuteQueryAsync();
        }
    }

    private async Task ExecuteQueryAsync()
    {
        _isLoading = true;
        StateHasChanged();

        try
        {
            var request = new HistoricalLogsQueryRequest
            {
                StartTime = TryParseDateTime(_startTimeText),
                EndTime = TryParseDateTime(_endTimeText),
                ServiceName = _selectedService == "All" ? null : _selectedService,
                LogLevel = _selectedLogLevel == "All" ? null : _selectedLogLevel,
                SearchText = string.IsNullOrWhiteSpace(_searchText) ? null : _searchText,
                PageIndex = _currentPage,
                PageSize = PageSize
            };

            _result = await QueryService.QueryLogsAsync(request);
            _totalPages = (int)Math.Ceiling((double)_result.TotalCount / PageSize);

            // Refresh service names in case new services have appeared.
            await LoadServiceNamesAsync();
        }
        finally
        {
            _isLoading = false;
            StateHasChanged();
        }
    }

    private static DateTime? TryParseDateTime(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        string[] formats =
        [
            "yyyy-MM-dd HH:mm",
            "yyyy-MM-dd HH:mm:ss",
            "yyyy-MM-ddTHH:mm",
            "yyyy-MM-ddTHH:mm:ss",
            "yyyy-MM-dd"
        ];

        if (DateTime.TryParseExact(text, formats, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var result))
        {
            return result;
        }

        return null;
    }

    private static string GetLogLevelClass(string logLevel)
    {
        return logLevel.ToLowerInvariant() switch
        {
            "trace" => "log-level-trace",
            "debug" => "log-level-debug",
            "information" => "log-level-information",
            "warning" => "log-level-warning",
            "error" => "log-level-error",
            "critical" => "log-level-critical",
            _ => string.Empty
        };
    }
}
