using System.Collections.ObjectModel;
using System.ComponentModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VisualDataDiff.Models;
using VisualDataDiff.Services.Abstractions;
using VisualDataDiff.Services.Sources;
using VisualDataDiff.Utilities;

namespace VisualDataDiff.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly ITabularDataSourceFactory _sourceFactory;
    private readonly IDataDiffEngine _diffEngine;
    private readonly IFilePickerService _filePickerService;
    private readonly ISearchEngine _searchEngine;
    private readonly IColumnMatcher _columnMatcher;
    private CancellationTokenSource? _operationCts;
    private TabularDataSet? _leftDataSet;
    private TabularDataSet? _rightDataSet;
    private DiffResult? _diffResult;
    private DiffGridRowViewModel? _pivotedSourceRow;
    private ColumnSettingsSnapshotEntry[]? _columnSettingsSnapshot;
    private SearchResult? _searchResult;
    private DiffRow[] _searchQualifyingRows = [];
    private Dictionary<DiffRow, int> _diffRowOrdinalByRow = new();
    private CancellationTokenSource? _searchCts;
    private DispatcherTimer? _searchDebounceTimer;
    private bool _isSyncingSearchFilters;
    private SourcePaneViewModel? _configuringSource;
    private string? _delimitedTextPreviewSample;

    private const int PreviewSampleCharBudget = 64 * 1024;
    private const int PreviewRowCap = 40;

    public MainWindowViewModel(
        ITabularDataSourceFactory sourceFactory,
        IDataDiffEngine diffEngine,
        IFilePickerService filePickerService,
        ISearchEngine searchEngine,
        IColumnMatcher columnMatcher)
    {
        _sourceFactory = sourceFactory;
        _diffEngine = diffEngine;
        _filePickerService = filePickerService;
        _searchEngine = searchEngine;
        _columnMatcher = columnMatcher;

        LeftSource = new SourcePaneViewModel("Left");
        RightSource = new SourcePaneViewModel("Right");
        LeftSource.PropertyChanged += OnSourcePropertyChanged;
        RightSource.PropertyChanged += OnSourcePropertyChanged;

        AvailableSourceTypes = SourceTypeOptionViewModel.All;
        AvailableRowVisibilityOptions =
        [
            new RowVisibilityOptionViewModel { Mode = RowVisibilityMode.All, Label = "All rows" },
            new RowVisibilityOptionViewModel { Mode = RowVisibilityMode.DifferencesOnly, Label = "Only rows with differences" },
            new RowVisibilityOptionViewModel { Mode = RowVisibilityMode.DifferencesNoOrphans, Label = "Differences, no orphans" },
            new RowVisibilityOptionViewModel { Mode = RowVisibilityMode.OrphansOnly, Label = "Only orphans" },
            new RowVisibilityOptionViewModel { Mode = RowVisibilityMode.LeftOrphansOnly, Label = "Only left orphans" },
            new RowVisibilityOptionViewModel { Mode = RowVisibilityMode.RightOrphansOnly, Label = "Only right orphans" }
        ];

        ColumnOptions = [];
        DisplayColumns = [];
        DisplayRows = [];

        SetupLeftSourceCommand = new AsyncRelayCommand(() => SetupSourceAsync(LeftSource), () => !IsBusy);
        SetupRightSourceCommand = new AsyncRelayCommand(() => SetupSourceAsync(RightSource), () => !IsBusy);
        ConfigureLeftSourceCommand = new RelayCommand(() => ConfigureSource(LeftSource), () => !IsBusy && LeftSource.HasLocation);
        ConfigureRightSourceCommand = new RelayCommand(() => ConfigureSource(RightSource), () => !IsBusy && RightSource.HasLocation);
        DelimitedTextConfigOkCommand = new AsyncRelayCommand(DelimitedTextConfigOkAsync, () => !IsBusy && IsDelimitedTextConfigValid);
        DelimitedTextConfigCancelCommand = new RelayCommand(CancelDelimitedTextConfig);
        ReloadCommand = new AsyncRelayCommand(ReloadAsync, () => !IsBusy);
        CancelCommand = new RelayCommand(CancelCurrentOperation, () => IsBusy);
        ClosePivotViewCommand = new RelayCommand(ClosePivotView);
        PivotPreviousRowCommand = new RelayCommand(() => NavigatePivotRow(-1), () => CanNavigatePivotRow(-1));
        PivotNextRowCommand = new RelayCommand(() => NavigatePivotRow(1), () => CanNavigatePivotRow(1));
        OpenSearchCommand = new RelayCommand(OpenSearch);
        OpenColumnSettingsCommand = new RelayCommand(OpenColumnSettings);
        ColumnSettingsOkCommand = new AsyncRelayCommand(ColumnSettingsOkAsync, () => !IsBusy);
        ColumnSettingsCancelCommand = new RelayCommand(CancelColumnSettings);
        DiscardColumnSettingsChangesCommand = new RelayCommand(DiscardColumnSettingsChanges);
        KeepEditingColumnSettingsCommand = new RelayCommand(KeepEditingColumnSettings);

        StatusText = "Select a source for each side and run comparison.";
    }

    public SourcePaneViewModel LeftSource { get; }

    public SourcePaneViewModel RightSource { get; }

    public IReadOnlyList<SourceTypeOptionViewModel> AvailableSourceTypes { get; }

    public IReadOnlyList<DelimiterPresetOptionViewModel> AvailableDelimiterPresets => DelimiterPresetOptionViewModel.All;

    public IReadOnlyList<QuotePresetOptionViewModel> AvailableQuotePresets => QuotePresetOptionViewModel.All;

    public IReadOnlyList<RowVisibilityOptionViewModel> AvailableRowVisibilityOptions { get; }

    public RowVisibilityOptionViewModel? SelectedRowVisibilityOption
    {
        get => AvailableRowVisibilityOptions.FirstOrDefault(x => x.Mode == RowVisibilityMode);
        set
        {
            if (value is not null)
            {
                RowVisibilityMode = value.Mode;
            }
        }
    }

    public ObservableCollection<ColumnOptionsViewModel> ColumnOptions { get; }

    public BulkObservableCollection<DisplayColumnViewModel> DisplayColumns { get; }

    public BulkObservableCollection<DiffGridRowViewModel> DisplayRows { get; }

    public ObservableCollection<PivotedColumnViewModel> PivotedColumns { get; } = [];

    public ObservableCollection<SearchColumnFilterViewModel> SearchColumnFilters { get; } = [];

    public BulkObservableCollection<string> DelimitedTextPreviewLines { get; } = [];

    public IAsyncRelayCommand SetupLeftSourceCommand { get; }

    public IAsyncRelayCommand SetupRightSourceCommand { get; }

    public IRelayCommand ConfigureLeftSourceCommand { get; }

    public IRelayCommand ConfigureRightSourceCommand { get; }

    public IAsyncRelayCommand DelimitedTextConfigOkCommand { get; }

    public IRelayCommand DelimitedTextConfigCancelCommand { get; }

    public IAsyncRelayCommand ReloadCommand { get; }

    public IRelayCommand CancelCommand { get; }

    public IRelayCommand ClosePivotViewCommand { get; }

    public IRelayCommand PivotPreviousRowCommand { get; }

    public IRelayCommand PivotNextRowCommand { get; }

    public IRelayCommand OpenSearchCommand { get; }

    public IRelayCommand OpenColumnSettingsCommand { get; }

    public IAsyncRelayCommand ColumnSettingsOkCommand { get; }

    public IRelayCommand ColumnSettingsCancelCommand { get; }

    public IRelayCommand DiscardColumnSettingsChangesCommand { get; }

    public IRelayCommand KeepEditingColumnSettingsCommand { get; }

    [ObservableProperty]
    private bool _hideIdenticalColumns;

    [ObservableProperty]
    private bool _hideIdenticalKeyColumns;

    [ObservableProperty]
    private bool _hideIgnoredColumns;

    [ObservableProperty]
    private RowVisibilityMode _rowVisibilityMode = RowVisibilityMode.All;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusText = string.Empty;

    [ObservableProperty]
    private bool _isPivotOpen;

    [ObservableProperty]
    private bool _pivotShowAllColumns;

    [ObservableProperty]
    private bool _pivotSkipOrphanRows;

    [ObservableProperty]
    private bool _isSearchMode;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private bool _searchUseRegex;

    [ObservableProperty]
    private bool _searchCaseSensitive;

    [ObservableProperty]
    private bool _isColumnSettingsOpen;

    [ObservableProperty]
    private bool _isConfirmingDiscardColumnSettings;

    [ObservableProperty]
    private bool _isDelimitedTextConfigOpen;

    [ObservableProperty]
    private string _delimitedTextConfigTitle = string.Empty;

    [ObservableProperty]
    private DelimiterPresetOptionViewModel? _selectedDelimiterPreset;

    [ObservableProperty]
    private string _customDelimiterText = string.Empty;

    [ObservableProperty]
    private QuotePresetOptionViewModel? _selectedQuotePreset;

    [ObservableProperty]
    private string _customQuoteText = string.Empty;

    [ObservableProperty]
    private string? _delimitedTextConfigErrorText;

    public bool IsAnyOverlayOpen => IsPivotOpen || IsColumnSettingsOpen || IsDelimitedTextConfigOpen;

    public bool IsCustomDelimiterVisible => SelectedDelimiterPreset?.Kind == DelimiterPresetKind.Other;

    public bool IsCustomQuoteVisible => SelectedQuotePreset?.Kind == QuotePresetKind.Other;

    public bool HasDelimitedTextConfigError => !string.IsNullOrEmpty(DelimitedTextConfigErrorText);

    private bool IsDelimitedTextConfigValid => TryBuildEffectiveDelimitedTextSettings(out _, out _, out _);

    public bool HasKeyColumnSelected => ColumnOptions.Any(x => x.Role == ColumnRole.Key);

    public int MatchedColumnCount => ColumnOptions.Count(x => !x.IsUnmapped && !x.IsAmbiguous);

    public int NeedsReviewColumnCount => ColumnOptions.Count(x => !x.IsUnmapped && x.IsAmbiguous);

    public int UnmappedLeftColumnCount => ColumnOptions.Count(x => x.IsUnmapped && x.LeftOrdinal is not null);

    public int UnmappedRightColumnCount => ColumnOptions.Count(x => x.IsUnmapped && x.RightOrdinal is not null);

    public string PivotSubtitle => _pivotedSourceRow switch
    {
        null => string.Empty,
        { IsLeftOrphan: true } => "Left-only row (no matching right row)",
        { IsRightOrphan: true } => "Right-only row (no matching left row)",
        _ => "Matched row"
    };

    public string PivotHeaderText => IsSearchMode ? "Search" : "Pivoted Row View";

    public bool SearchHasRegexError => _searchResult?.HasRegexError ?? false;

    public int SearchAfterFilterLeftCount => SearchColumnFilters.Where(x => !x.IsAll && x.IsIncluded).Sum(x => x.LeftCount);

    public int SearchAfterFilterRightCount => SearchColumnFilters.Where(x => !x.IsAll && x.IsIncluded).Sum(x => x.RightCount);

    public int SearchAfterFilterTotalCount => SearchAfterFilterLeftCount + SearchAfterFilterRightCount;

    // Disabled along with the main-screen "Column Options" editor: column selection is now
    // only surfaced through the Column Setup popover. Kept for potential reuse later.
#if false
    private int? _currentVisibleColumnIndex;

    public int? CurrentVisibleColumnIndex
    {
        get => _currentVisibleColumnIndex;
        private set
        {
            if (SetProperty(ref _currentVisibleColumnIndex, value))
            {
                OnPropertyChanged(nameof(SelectedColumnOption));
                OnPropertyChanged(nameof(HasSelectedColumnOption));
                OnPropertyChanged(nameof(ColumnOptionsHeader));
            }
        }
    }

    public ColumnOptionsViewModel? SelectedColumnOption =>
        CurrentVisibleColumnIndex is int idx && idx >= 0 && idx < DisplayColumns.Count
            ? ColumnOptions.FirstOrDefault(x => x.Ordinal == DisplayColumns[idx].Ordinal)
            : null;

    public bool HasSelectedColumnOption => SelectedColumnOption is not null;

    public string ColumnOptionsHeader => SelectedColumnOption is null
        ? "Column Options"
        : $"Column Options - {SelectedColumnOption.Name}";
#endif

    public string LeftGridSummary => $"{LeftSource.DataGroupHeader} | {(_leftDataSet?.Rows.Count ?? 0):N0} rows, {DisplayRows.Count(x => !x.IsRightOrphan):N0} visible.";

    public string RightGridSummary => $"{RightSource.DataGroupHeader} | {(_rightDataSet?.Rows.Count ?? 0):N0} rows, {DisplayRows.Count(x => !x.IsLeftOrphan):N0} visible.";

    public bool ShowHideIdenticalKeyColumnsOption => HideIdenticalColumns;

    public bool ShowAllRows
    {
        get => RowVisibilityMode == RowVisibilityMode.All;
        set
        {
            if (value)
            {
                RowVisibilityMode = RowVisibilityMode.All;
            }
        }
    }

    public bool ShowDifferenceRows
    {
        get => RowVisibilityMode == RowVisibilityMode.DifferencesOnly;
        set
        {
            if (value)
            {
                RowVisibilityMode = RowVisibilityMode.DifferencesOnly;
            }
        }
    }

    public bool ShowOrphanRows
    {
        get => RowVisibilityMode == RowVisibilityMode.OrphansOnly;
        set
        {
            if (value)
            {
                RowVisibilityMode = RowVisibilityMode.OrphansOnly;
            }
        }
    }

    public bool ShowLeftOrphanRows
    {
        get => RowVisibilityMode == RowVisibilityMode.LeftOrphansOnly;
        set
        {
            if (value)
            {
                RowVisibilityMode = RowVisibilityMode.LeftOrphansOnly;
            }
        }
    }

    public bool ShowRightOrphanRows
    {
        get => RowVisibilityMode == RowVisibilityMode.RightOrphansOnly;
        set
        {
            if (value)
            {
                RowVisibilityMode = RowVisibilityMode.RightOrphansOnly;
            }
        }
    }

    partial void OnHideIdenticalColumnsChanged(bool value)
    {
        if (!value)
        {
            HideIdenticalKeyColumns = false;
        }

        OnPropertyChanged(nameof(ShowHideIdenticalKeyColumnsOption));
        ApplyVisibilityFilters();
    }

    partial void OnHideIdenticalKeyColumnsChanged(bool value)
    {
        ApplyVisibilityFilters();
    }

    partial void OnHideIgnoredColumnsChanged(bool value)
    {
        ApplyVisibilityFilters();
    }

    partial void OnRowVisibilityModeChanged(RowVisibilityMode value)
    {
        OnPropertyChanged(nameof(ShowAllRows));
        OnPropertyChanged(nameof(ShowDifferenceRows));
        OnPropertyChanged(nameof(ShowOrphanRows));
        OnPropertyChanged(nameof(ShowLeftOrphanRows));
        OnPropertyChanged(nameof(ShowRightOrphanRows));
        OnPropertyChanged(nameof(SelectedRowVisibilityOption));
        ApplyVisibilityFilters();
    }

    partial void OnPivotShowAllColumnsChanged(bool value)
    {
        RebuildPivotedColumns();
    }

    partial void OnPivotSkipOrphanRowsChanged(bool value)
    {
        NotifyPivotNavigationCanExecuteChanged();
    }

    partial void OnIsSearchModeChanged(bool value)
    {
        OnPropertyChanged(nameof(PivotHeaderText));
    }

    partial void OnSearchTextChanged(string value)
    {
        ScheduleSearchRecompute();
    }

    partial void OnSearchUseRegexChanged(bool value)
    {
        ScheduleSearchRecompute();
    }

    partial void OnSearchCaseSensitiveChanged(bool value)
    {
        ScheduleSearchRecompute();
    }

    partial void OnIsPivotOpenChanged(bool value)
    {
        OnPropertyChanged(nameof(IsAnyOverlayOpen));
    }

    partial void OnIsColumnSettingsOpenChanged(bool value)
    {
        OnPropertyChanged(nameof(IsAnyOverlayOpen));
    }

    partial void OnIsDelimitedTextConfigOpenChanged(bool value)
    {
        OnPropertyChanged(nameof(IsAnyOverlayOpen));
    }

    partial void OnSelectedDelimiterPresetChanged(DelimiterPresetOptionViewModel? value)
    {
        OnPropertyChanged(nameof(IsCustomDelimiterVisible));
        RefreshDelimitedTextPreview();
        DelimitedTextConfigOkCommand.NotifyCanExecuteChanged();
    }

    partial void OnCustomDelimiterTextChanged(string value)
    {
        RefreshDelimitedTextPreview();
        DelimitedTextConfigOkCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedQuotePresetChanged(QuotePresetOptionViewModel? value)
    {
        OnPropertyChanged(nameof(IsCustomQuoteVisible));
        RefreshDelimitedTextPreview();
        DelimitedTextConfigOkCommand.NotifyCanExecuteChanged();
    }

    partial void OnCustomQuoteTextChanged(string value)
    {
        RefreshDelimitedTextPreview();
        DelimitedTextConfigOkCommand.NotifyCanExecuteChanged();
    }

    partial void OnDelimitedTextConfigErrorTextChanged(string? value)
    {
        OnPropertyChanged(nameof(HasDelimitedTextConfigError));
    }

    partial void OnIsBusyChanged(bool value)
    {
        SetupLeftSourceCommand.NotifyCanExecuteChanged();
        SetupRightSourceCommand.NotifyCanExecuteChanged();
        ConfigureLeftSourceCommand.NotifyCanExecuteChanged();
        ConfigureRightSourceCommand.NotifyCanExecuteChanged();
        DelimitedTextConfigOkCommand.NotifyCanExecuteChanged();
        ReloadCommand.NotifyCanExecuteChanged();
        CancelCommand.NotifyCanExecuteChanged();
        ColumnSettingsOkCommand.NotifyCanExecuteChanged();
    }

    private void OnSourcePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(SourcePaneViewModel.DataGroupHeader) or nameof(SourcePaneViewModel.Location))
        {
            OnPropertyChanged(nameof(LeftGridSummary));
            OnPropertyChanged(nameof(RightGridSummary));
        }

        if (e.PropertyName == nameof(SourcePaneViewModel.Location))
        {
            if (ReferenceEquals(sender, LeftSource))
            {
                ConfigureLeftSourceCommand.NotifyCanExecuteChanged();
            }
            else if (ReferenceEquals(sender, RightSource))
            {
                ConfigureRightSourceCommand.NotifyCanExecuteChanged();
            }
        }
    }

    private async Task SetupSourceAsync(SourcePaneViewModel source)
    {
        var path = await _filePickerService.PickFileAsync(source.SelectedSourceType, CancellationToken.None);
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        source.Location = path;
        StatusText = $"{source.Title} source configured.";
    }

    private async Task ReloadAsync()
    {
        if (string.IsNullOrWhiteSpace(LeftSource.Location) || string.IsNullOrWhiteSpace(RightSource.Location))
        {
            StatusText = "Both left and right sources must be configured.";
            return;
        }

        using var cts = StartOperation();

        try
        {
            IsBusy = true;
            StatusText = "Loading sources...";

            var leftProvider = _sourceFactory.GetSource(LeftSource.SelectedSourceType);
            var rightProvider = _sourceFactory.GetSource(RightSource.SelectedSourceType);

            LeftSource.SupportsHeaderOption = leftProvider.SupportsHeaderOption;
            RightSource.SupportsHeaderOption = rightProvider.SupportsHeaderOption;

            var leftLoadTask = leftProvider.LoadAsync(new SourceConfiguration
            {
                SourceType = LeftSource.SelectedSourceType,
                SupportsHeaderOption = LeftSource.SupportsHeaderOption,
                TreatFirstRowAsHeader = LeftSource.TreatFirstRowAsHeader,
                Location = LeftSource.Location,
                Delimiter = LeftSource.DelimiterCharacter,
                QuoteCharacter = LeftSource.QuoteCharacter
            }, cts.Token);

            var rightLoadTask = rightProvider.LoadAsync(new SourceConfiguration
            {
                SourceType = RightSource.SelectedSourceType,
                SupportsHeaderOption = RightSource.SupportsHeaderOption,
                TreatFirstRowAsHeader = RightSource.TreatFirstRowAsHeader,
                Location = RightSource.Location,
                Delimiter = RightSource.DelimiterCharacter,
                QuoteCharacter = RightSource.QuoteCharacter
            }, cts.Token);

            await Task.WhenAll(leftLoadTask, rightLoadTask);

            var previousLeftColumns = _leftDataSet?.Columns;
            var previousRightColumns = _rightDataSet?.Columns;

            _leftDataSet = leftLoadTask.Result;
            _rightDataSet = rightLoadTask.Result;

            var headersChanged = !HeaderNamesUnchanged(previousLeftColumns, previousRightColumns, _leftDataSet.Columns, _rightDataSet.Columns);

            RebuildColumnOptions(_leftDataSet, _rightDataSet);

            if (headersChanged)
            {
                OpenColumnSettings();
            }

            await CompareUsingCurrentOptionsAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            StatusText = "Operation canceled.";
        }
        catch (Exception ex)
        {
            StatusText = $"Error: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
            EndOperation(cts);
        }
    }

    private async Task RunComparisonAsync()
    {
        using var cts = StartOperation();

        try
        {
            IsBusy = true;
            await CompareUsingCurrentOptionsAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            StatusText = "Operation canceled.";
        }
        catch (Exception ex)
        {
            StatusText = $"Error: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
            EndOperation(cts);
        }
    }

    private async Task CompareUsingCurrentOptionsAsync(CancellationToken cancellationToken)
    {
        if (_leftDataSet is null || _rightDataSet is null)
        {
            return;
        }

        if (!HasKeyColumnSelected)
        {
            StatusText = "Select at least one Key column before running the comparison.";
            OpenColumnSettings();
            return;
        }

        ClosePivotView();
        ResetSearchState();

        StatusText = "Comparing data...";
        _diffResult = await _diffEngine.CompareAsync(_leftDataSet, _rightDataSet, BuildRules(), cancellationToken);
        _diffRowOrdinalByRow = BuildRowOrdinalLookup(_diffResult);
        ApplyVisibilityFilters();
        StatusText = $"Comparison complete. Showing {DisplayRows.Count} rows and {DisplayColumns.Count} columns.";
    }

    private IReadOnlyList<ColumnComparisonRule> BuildRules()
    {
        // Preserve ColumnOptions' own order rather than re-sorting by LeftOrdinal - RebuildColumnOptions
        // already produces the desired final order, and a rule's position in this list becomes the
        // resulting DiffColumn/DiffCell "slot" ordinal, which several lookups elsewhere key off of
        // ColumnOptionsViewModel.SlotIndex. Re-sorting here (especially by a nullable LeftOrdinal,
        // where default ordering puts null first) would desync the two.
        return ColumnOptions
            .Select(x => new ColumnComparisonRule
            {
                LeftOrdinal = x.LeftOrdinal,
                RightOrdinal = x.RightOrdinal,
                Role = x.Role,
                CaseSensitive = x.CaseSensitive,
                IgnoreLeadingAndTrailingWhitespace = x.IgnoreLeadingAndTrailingWhitespace
            })
            .ToArray();
    }

    private static bool HeaderNamesUnchanged(
        IReadOnlyList<TabularColumn>? previousLeft,
        IReadOnlyList<TabularColumn>? previousRight,
        IReadOnlyList<TabularColumn> currentLeft,
        IReadOnlyList<TabularColumn> currentRight)
    {
        if (previousLeft is null || previousRight is null)
        {
            return false;
        }

        var previousLeftNames = NormalizeNames(previousLeft);
        var previousRightNames = NormalizeNames(previousRight);
        var currentLeftNames = NormalizeNames(currentLeft);
        var currentRightNames = NormalizeNames(currentRight);

        var sameSides = previousLeftNames.SequenceEqual(currentLeftNames, StringComparer.OrdinalIgnoreCase)
            && previousRightNames.SequenceEqual(currentRightNames, StringComparer.OrdinalIgnoreCase);

        var swappedSides = previousLeftNames.SequenceEqual(currentRightNames, StringComparer.OrdinalIgnoreCase)
            && previousRightNames.SequenceEqual(currentLeftNames, StringComparer.OrdinalIgnoreCase);

        return sameSides || swappedSides;
    }

    private static IReadOnlyList<string> NormalizeNames(IReadOnlyList<TabularColumn> columns) =>
        columns.Select(c => HeaderNameComparer.Normalize(c.Name)).ToArray();

    private void RebuildColumnOptions(TabularDataSet leftDataSet, TabularDataSet rightDataSet)
    {
        // Carry forward the user's prior Role/CaseSensitive/Whitespace choices by normalized column
        // name rather than by position - names are the stable identity a user actually thinks in
        // terms of ("the FirstName column"), and survive a reload even if columns get reordered or
        // the auto-match assigns different slot indexes than last time. Duplicate names just take the
        // first prior match found; this is a best-effort carry-forward, not a strict guarantee.
        var existingByName = new Dictionary<string, ColumnOptionsViewModel>(StringComparer.OrdinalIgnoreCase);
        foreach (var existing in ColumnOptions)
        {
            var key = HeaderNameComparer.Normalize(existing.LeftName ?? existing.RightName ?? string.Empty);
            if (!string.IsNullOrEmpty(key) && !existingByName.ContainsKey(key))
            {
                existingByName[key] = existing;
            }
        }

        foreach (var column in ColumnOptions)
        {
            column.PropertyChanged -= OnColumnOptionsPropertyChanged;
        }

        ColumnOptions.Clear();

        var matches = _columnMatcher.Match(leftDataSet.Columns, rightDataSet.Columns);

        // Present in the left file's natural column order (both mapped and left-only columns sort by
        // LeftOrdinal), with right-only columns - which have no position in the left file at all -
        // appended at the end in the right file's order.
        var orderedMatches = matches
            .OrderBy(m => m.LeftOrdinal ?? int.MaxValue)
            .ThenBy(m => m.RightOrdinal ?? int.MaxValue)
            .ToArray();

        for (var slot = 0; slot < orderedMatches.Length; slot++)
        {
            var match = orderedMatches[slot];
            var leftName = match.LeftOrdinal is int lo ? leftDataSet.Columns[lo].Name : null;
            var rightName = match.RightOrdinal is int ro ? rightDataSet.Columns[ro].Name : null;

            var vm = new ColumnOptionsViewModel(slot, match.LeftOrdinal, leftName, match.RightOrdinal, rightName, match.Score, match.IsAmbiguous);

            var lookupName = HeaderNameComparer.Normalize(leftName ?? rightName ?? string.Empty);
            if (!string.IsNullOrEmpty(lookupName) && existingByName.TryGetValue(lookupName, out var previous))
            {
                vm.Role = previous.Role;
                vm.CaseSensitive = previous.CaseSensitive;
                vm.IgnoreLeadingAndTrailingWhitespace = previous.IgnoreLeadingAndTrailingWhitespace;
            }
            else if (vm.IsUnmapped)
            {
                // No real counterpart to compare against - comparing would just always show
                // "different" on every row, which is noise, not signal.
                vm.Role = ColumnRole.Ignored;
            }

            vm.PropertyChanged += OnColumnOptionsPropertyChanged;
            ColumnOptions.Add(vm);
        }

        if (!ColumnOptions.Any(x => x.Role == ColumnRole.Key))
        {
            var firstMapped = ColumnOptions.FirstOrDefault(x => !x.IsUnmapped);
            if (firstMapped is not null)
            {
                firstMapped.Role = ColumnRole.Key;
            }
        }

        OnPropertyChanged(nameof(HasKeyColumnSelected));
        NotifyColumnMatchSummaryChanged();
    }

    private void NotifyColumnMatchSummaryChanged()
    {
        OnPropertyChanged(nameof(MatchedColumnCount));
        OnPropertyChanged(nameof(NeedsReviewColumnCount));
        OnPropertyChanged(nameof(UnmappedLeftColumnCount));
        OnPropertyChanged(nameof(UnmappedRightColumnCount));
    }

    private void OnColumnOptionsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ColumnOptionsViewModel.Role) && HideIgnoredColumns)
        {
            ApplyVisibilityFilters();
        }

        if (e.PropertyName is nameof(ColumnOptionsViewModel.Role))
        {
            OnPropertyChanged(nameof(HasKeyColumnSelected));
        }

        if (e.PropertyName is nameof(ColumnOptionsViewModel.CaseSensitive) or nameof(ColumnOptionsViewModel.IgnoreLeadingAndTrailingWhitespace) or nameof(ColumnOptionsViewModel.Role))
        {
            StatusText = "Column options changed. Run comparison to refresh results.";
            // Disabled along with the main-screen "Column Options" editor.
            // OnPropertyChanged(nameof(SelectedColumnOption));
            // OnPropertyChanged(nameof(HasSelectedColumnOption));
            // OnPropertyChanged(nameof(ColumnOptionsHeader));
        }
    }

    private void ApplyVisibilityFilters()
    {
        if (_diffResult is null)
        {
            DisplayRows.Clear();
            DisplayColumns.Clear();
            OnPropertyChanged(nameof(LeftGridSummary));
            OnPropertyChanged(nameof(RightGridSummary));
            return;
        }

        var optionsByOrdinal = ColumnOptions.ToDictionary(x => x.SlotIndex);
        var columnsWithNonOrphanDifferences = _diffResult.Rows
            .Where(row => !row.IsLeftOrphan && !row.IsRightOrphan)
            .SelectMany(row => row.Cells.Where(cell => cell.IsDifferent).Select(cell => cell.Ordinal))
            .ToHashSet();

        var filteredColumns = _diffResult.Columns
            .Where(column =>
            {
                optionsByOrdinal.TryGetValue(column.Ordinal, out var option);
                var role = option?.Role ?? ColumnRole.Normal;

                if (HideIgnoredColumns && role == ColumnRole.Ignored)
                {
                    return false;
                }

                var hasNonOrphanDifferences = columnsWithNonOrphanDifferences.Contains(column.Ordinal);
                if (HideIdenticalColumns && !hasNonOrphanDifferences)
                {
                    if (role == ColumnRole.Key && !HideIdenticalKeyColumns)
                    {
                        return true;
                    }

                    return false;
                }

                return true;
            })
            .Select(column =>
            {
                optionsByOrdinal.TryGetValue(column.Ordinal, out var option);
                return new DisplayColumnViewModel
                {
                    Ordinal = column.Ordinal,
                    Name = column.Name,
                    HasDifferences = column.HasDifferences,
                    IsIgnored = option?.Role == ColumnRole.Ignored,
                    IsKey = option?.Role == ColumnRole.Key,
                    Width = ComputeColumnWidth(column.Ordinal, column.Name)
                };
            })
            .OrderBy(x => x.Ordinal)
            .ToArray();

        var visibleOrdinals = filteredColumns.Select(x => x.Ordinal).ToArray();

        var filteredRows = _diffResult.Rows
            .Where(IsRowVisible)
            .Select(row => new DiffGridRowViewModel(row, visibleOrdinals))
            .ToArray();

        DisplayColumns.ReplaceAll(filteredColumns);
        DisplayRows.ReplaceAll(filteredRows);

        // NormalizeSelectedVisibleColumnIndex(); // disabled along with the main-screen "Column Options" editor
        OnPropertyChanged(nameof(LeftGridSummary));
        OnPropertyChanged(nameof(RightGridSummary));
    }

    private double ComputeColumnWidth(int ordinal, string headerName)
    {
        const double charWidth = 7.5;
        const double padding = 16;
        const double minWidth = 60;
        const double maxWidth = 400;

        var maxLength = headerName.Length;

        if (_diffResult is not null)
        {
            foreach (var row in _diffResult.Rows)
            {
                if (ordinal < 0 || ordinal >= row.Cells.Count)
                {
                    continue;
                }

                var cell = row.Cells[ordinal];
                if (cell.LeftValue is { Length: > 0 } left && left.Length > maxLength)
                {
                    maxLength = left.Length;
                }

                if (cell.RightValue is { Length: > 0 } right && right.Length > maxLength)
                {
                    maxLength = right.Length;
                }
            }
        }

        return Math.Clamp(maxLength * charWidth + padding, minWidth, maxWidth);
    }

    private bool IsRowVisible(DiffRow row)
    {
        return RowVisibilityMode switch
        {
            RowVisibilityMode.All => true,
            RowVisibilityMode.DifferencesOnly => row.HasDifferences,
            RowVisibilityMode.DifferencesNoOrphans => row.HasDifferences && !row.IsLeftOrphan && !row.IsRightOrphan,
            RowVisibilityMode.OrphansOnly => row.IsLeftOrphan || row.IsRightOrphan,
            RowVisibilityMode.LeftOrphansOnly => row.IsLeftOrphan,
            RowVisibilityMode.RightOrphansOnly => row.IsRightOrphan,
            _ => true
        };
    }

    public void OpenPivotView(DiffGridRowViewModel row)
    {
        IsSearchMode = false;
        _pivotedSourceRow = row;
        IsPivotOpen = true;
        OnPropertyChanged(nameof(PivotSubtitle));
        RebuildPivotedColumns();
        NotifyPivotNavigationCanExecuteChanged();
    }

    public event EventHandler? SearchFocusRequested;

    private void OpenSearch()
    {
        if (_diffResult is null)
        {
            StatusText = "Run a comparison before searching.";
            return;
        }

        if (IsColumnSettingsOpen)
        {
            CloseColumnSettingsWithoutSaving();
        }

        IsSearchMode = true;
        IsPivotOpen = true;
        OnPropertyChanged(nameof(PivotSubtitle));

        if (_searchResult is null)
        {
            _ = RunSearchAsync();
        }
        else
        {
            ReconcileSearchPivotedRow();
        }

        NotifyPivotNavigationCanExecuteChanged();

        // Fires every time OpenSearch runs (button click or Ctrl+F), even if IsSearchMode was
        // already true and therefore wouldn't raise its own PropertyChanged - Ctrl+F must always
        // refocus the search box, not just the first time search mode is entered.
        SearchFocusRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OpenColumnSettings()
    {
        if (IsPivotOpen)
        {
            ClosePivotView();
        }

        _columnSettingsSnapshot = ColumnOptions
            .Select(x => new ColumnSettingsSnapshotEntry(x.SlotIndex, x.Role, x.CaseSensitive, x.IgnoreLeadingAndTrailingWhitespace))
            .ToArray();
        IsConfirmingDiscardColumnSettings = false;
        IsColumnSettingsOpen = true;
    }

    private async Task ColumnSettingsOkAsync()
    {
        var hasChanges = HasPendingColumnSettingsChanges();
        CloseColumnSettingsWithoutSaving();

        if (hasChanges)
        {
            await RunComparisonAsync();
        }
    }

    private void CancelColumnSettings()
    {
        if (IsConfirmingDiscardColumnSettings)
        {
            return;
        }

        if (HasPendingColumnSettingsChanges())
        {
            IsConfirmingDiscardColumnSettings = true;
            return;
        }

        CloseColumnSettingsWithoutSaving();
    }

    private void DiscardColumnSettingsChanges()
    {
        RevertColumnSettingsToSnapshot();
        CloseColumnSettingsWithoutSaving();
    }

    private void KeepEditingColumnSettings()
    {
        IsConfirmingDiscardColumnSettings = false;
    }

    private void CloseColumnSettingsWithoutSaving()
    {
        _columnSettingsSnapshot = null;
        IsConfirmingDiscardColumnSettings = false;
        IsColumnSettingsOpen = false;
    }

    private bool HasPendingColumnSettingsChanges()
    {
        if (_columnSettingsSnapshot is null)
        {
            return false;
        }

        if (_columnSettingsSnapshot.Length != ColumnOptions.Count)
        {
            return true;
        }

        foreach (var entry in _columnSettingsSnapshot)
        {
            var current = ColumnOptions.FirstOrDefault(x => x.SlotIndex == entry.SlotIndex);
            if (current is null
                || current.Role != entry.Role
                || current.CaseSensitive != entry.CaseSensitive
                || current.IgnoreLeadingAndTrailingWhitespace != entry.IgnoreLeadingAndTrailingWhitespace)
            {
                return true;
            }
        }

        return false;
    }

    private void RevertColumnSettingsToSnapshot()
    {
        if (_columnSettingsSnapshot is null)
        {
            return;
        }

        foreach (var entry in _columnSettingsSnapshot)
        {
            var current = ColumnOptions.FirstOrDefault(x => x.SlotIndex == entry.SlotIndex);
            if (current is null)
            {
                continue;
            }

            current.Role = entry.Role;
            current.CaseSensitive = entry.CaseSensitive;
            current.IgnoreLeadingAndTrailingWhitespace = entry.IgnoreLeadingAndTrailingWhitespace;
        }
    }

    private readonly record struct ColumnSettingsSnapshotEntry(int SlotIndex, ColumnRole Role, bool CaseSensitive, bool IgnoreLeadingAndTrailingWhitespace);

    private void ClosePivotView()
    {
        if (!IsPivotOpen && _pivotedSourceRow is null)
        {
            return;
        }

        IsPivotOpen = false;
        IsSearchMode = false;
        _pivotedSourceRow = null;
        PivotedColumns.Clear();
        OnPropertyChanged(nameof(PivotSubtitle));
        NotifyPivotNavigationCanExecuteChanged();
    }

    private int? FindPivotNavigationTargetIndex(int delta)
    {
        if (_pivotedSourceRow is null)
        {
            return null;
        }

        var currentIndex = DisplayRows.IndexOf(_pivotedSourceRow);
        if (currentIndex < 0)
        {
            return null;
        }

        var index = currentIndex;
        do
        {
            index += delta;
            if (index < 0 || index >= DisplayRows.Count)
            {
                return null;
            }
        }
        while (PivotSkipOrphanRows && DisplayRows[index].IsOrphan);

        return index;
    }

    private bool CanNavigatePivotRow(int delta) =>
        IsSearchMode ? FindSearchPivotNavigationTarget(delta) is not null : FindPivotNavigationTargetIndex(delta) is not null;

    private void NavigatePivotRow(int delta)
    {
        if (IsSearchMode)
        {
            var target = FindSearchPivotNavigationTarget(delta);
            if (target is not null)
            {
                SetPivotedRow(target);
            }

            return;
        }

        var targetIndex = FindPivotNavigationTargetIndex(delta);
        if (targetIndex is null)
        {
            return;
        }

        _pivotedSourceRow = DisplayRows[targetIndex.Value];
        OnPropertyChanged(nameof(PivotSubtitle));
        RebuildPivotedColumns();
        NotifyPivotNavigationCanExecuteChanged();
    }

    private DiffRow? FindSearchPivotNavigationTarget(int delta)
    {
        if (_pivotedSourceRow is null || _searchQualifyingRows.Length == 0)
        {
            return null;
        }

        var currentIndex = Array.IndexOf(_searchQualifyingRows, _pivotedSourceRow.Row);
        if (currentIndex < 0)
        {
            return null;
        }

        var index = currentIndex;
        do
        {
            index += delta;
            if (index < 0 || index >= _searchQualifyingRows.Length)
            {
                return null;
            }
        }
        while (PivotSkipOrphanRows && IsOrphanRow(_searchQualifyingRows[index]));

        return _searchQualifyingRows[index];
    }

    private static bool IsOrphanRow(DiffRow row) => row.IsLeftOrphan || row.IsRightOrphan;

    private void SetPivotedRow(DiffRow row)
    {
        _pivotedSourceRow = new DiffGridRowViewModel(row, []);
        OnPropertyChanged(nameof(PivotSubtitle));
        RebuildPivotedColumns();
        NotifyPivotNavigationCanExecuteChanged();
    }

    private void NotifyPivotNavigationCanExecuteChanged()
    {
        PivotPreviousRowCommand.NotifyCanExecuteChanged();
        PivotNextRowCommand.NotifyCanExecuteChanged();
    }

    private void RebuildPivotedColumns()
    {
        PivotedColumns.Clear();

        if (_pivotedSourceRow is null || _diffResult is null)
        {
            return;
        }

        var optionsByOrdinal = ColumnOptions.ToDictionary(x => x.SlotIndex);
        var row = _pivotedSourceRow.Row;
        SearchRowMatch? rowMatch = null;
        _searchResult?.RowMatches.TryGetValue(row, out rowMatch);

        foreach (var column in _diffResult.Columns.OrderBy(x => x.Ordinal))
        {
            if (column.Ordinal < 0 || column.Ordinal >= row.Cells.Count)
            {
                continue;
            }

            optionsByOrdinal.TryGetValue(column.Ordinal, out var option);
            var isKey = option?.Role == ColumnRole.Key;
            var cell = row.Cells[column.Ordinal];

            var isSearchMatchLeft = IsSearchMode && rowMatch is not null && rowMatch.LeftMatchedOrdinals.Contains(column.Ordinal);
            var isSearchMatchRight = IsSearchMode && rowMatch is not null && rowMatch.RightMatchedOrdinals.Contains(column.Ordinal);

            if (!PivotShowAllColumns && !isKey && !cell.IsDifferent && !isSearchMatchLeft && !isSearchMatchRight)
            {
                continue;
            }

            PivotedColumns.Add(new PivotedColumnViewModel
            {
                Ordinal = column.Ordinal,
                ColumnName = column.Name,
                IsKeyColumn = isKey,
                IsDifferent = cell.IsDifferent,
                LeftCell = DiffGridCellFactory.CreateLeft(row, cell, isSearchMatchLeft),
                RightCell = DiffGridCellFactory.CreateRight(row, cell, isSearchMatchRight)
            });
        }
    }

    private void ScheduleSearchRecompute()
    {
        _searchDebounceTimer ??= new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(300) };
        _searchDebounceTimer.Stop();
        _searchDebounceTimer.Tick -= OnSearchDebounceTick;
        _searchDebounceTimer.Tick += OnSearchDebounceTick;
        _searchDebounceTimer.Start();
    }

    private void OnSearchDebounceTick(object? sender, EventArgs e)
    {
        _searchDebounceTimer!.Stop();
        _ = RunSearchAsync();
    }

    private async Task RunSearchAsync()
    {
        _searchCts?.Cancel();

        if (_diffResult is null)
        {
            return;
        }

        var cts = new CancellationTokenSource();
        _searchCts = cts;
        var options = new SearchOptions
        {
            QueryText = SearchText,
            UseRegex = SearchUseRegex,
            CaseSensitive = SearchCaseSensitive
        };

        try
        {
            var result = await _searchEngine.SearchAsync(_diffResult, options, cts.Token);
            if (cts.IsCancellationRequested)
            {
                return;
            }

            ApplySearchResult(result);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void ApplySearchResult(SearchResult result)
    {
        _searchResult = result;
        RebuildSearchColumnFilters(result);
        RefreshSearchQualifyingRows();
        ReconcileSearchPivotedRow();
        OnPropertyChanged(nameof(SearchHasRegexError));
        UpdateSearchAfterFilterSummary();
        NotifyPivotNavigationCanExecuteChanged();
    }

    private void RebuildSearchColumnFilters(SearchResult result)
    {
        foreach (var filter in SearchColumnFilters)
        {
            filter.PropertyChanged -= OnSearchColumnFilterPropertyChanged;
        }

        SearchColumnFilters.Clear();

        if (result.ColumnMatches.Count == 0)
        {
            return;
        }

        var allRow = new SearchColumnFilterViewModel
        {
            IsAll = true,
            Ordinal = -1,
            ColumnName = "ALL",
            LeftCount = result.ColumnMatches.Sum(x => x.LeftCount),
            RightCount = result.ColumnMatches.Sum(x => x.RightCount)
        };
        allRow.PropertyChanged += OnSearchColumnFilterPropertyChanged;
        SearchColumnFilters.Add(allRow);

        foreach (var columnMatch in result.ColumnMatches)
        {
            var filter = new SearchColumnFilterViewModel
            {
                IsAll = false,
                Ordinal = columnMatch.Ordinal,
                ColumnName = columnMatch.ColumnName,
                LeftCount = columnMatch.LeftCount,
                RightCount = columnMatch.RightCount
            };
            filter.PropertyChanged += OnSearchColumnFilterPropertyChanged;
            SearchColumnFilters.Add(filter);
        }
    }

    private void OnSearchColumnFilterPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(SearchColumnFilterViewModel.IsIncluded) || _isSyncingSearchFilters)
        {
            return;
        }

        if (sender is not SearchColumnFilterViewModel changed)
        {
            return;
        }

        _isSyncingSearchFilters = true;
        try
        {
            if (changed.IsAll)
            {
                foreach (var filter in SearchColumnFilters.Where(x => !x.IsAll))
                {
                    filter.IsIncluded = changed.IsIncluded;
                }
            }
            else
            {
                var allRow = SearchColumnFilters.FirstOrDefault(x => x.IsAll);
                if (allRow is not null)
                {
                    allRow.IsIncluded = changed.IsIncluded && SearchColumnFilters.Where(x => !x.IsAll).All(x => x.IsIncluded);
                }
            }
        }
        finally
        {
            _isSyncingSearchFilters = false;
        }

        RefreshSearchQualifyingRows();
        ReconcileSearchPivotedRow();
        UpdateSearchAfterFilterSummary();
        NotifyPivotNavigationCanExecuteChanged();
    }

    private void UpdateSearchAfterFilterSummary()
    {
        OnPropertyChanged(nameof(SearchAfterFilterLeftCount));
        OnPropertyChanged(nameof(SearchAfterFilterRightCount));
        OnPropertyChanged(nameof(SearchAfterFilterTotalCount));
    }

    private void RefreshSearchQualifyingRows()
    {
        if (_diffResult is null || _searchResult is null || _searchResult.ColumnMatches.Count == 0)
        {
            _searchQualifyingRows = [];
            return;
        }

        var includedOrdinals = SearchColumnFilters
            .Where(x => !x.IsAll && x.IsIncluded)
            .Select(x => x.Ordinal)
            .ToHashSet();

        _searchQualifyingRows = _diffResult.Rows
            .Where(row => _searchResult.RowMatches.TryGetValue(row, out var match)
                && (match.LeftMatchedOrdinals.Overlaps(includedOrdinals) || match.RightMatchedOrdinals.Overlaps(includedOrdinals)))
            .ToArray();
    }

    private void ReconcileSearchPivotedRow()
    {
        if (!IsSearchMode)
        {
            return;
        }

        if (_pivotedSourceRow is not null && _searchQualifyingRows.Contains(_pivotedSourceRow.Row))
        {
            RebuildPivotedColumns();
            NotifyPivotNavigationCanExecuteChanged();
            return;
        }

        if (_searchQualifyingRows.Length == 0)
        {
            _pivotedSourceRow = null;
            PivotedColumns.Clear();
            OnPropertyChanged(nameof(PivotSubtitle));
            NotifyPivotNavigationCanExecuteChanged();
            return;
        }

        var previousOrdinal = _pivotedSourceRow is not null && _diffRowOrdinalByRow.TryGetValue(_pivotedSourceRow.Row, out var ordinal)
            ? ordinal
            : -1;

        var next = _searchQualifyingRows.FirstOrDefault(r => _diffRowOrdinalByRow[r] >= previousOrdinal) ?? _searchQualifyingRows[^1];
        SetPivotedRow(next);
    }

    private void ResetSearchState()
    {
        _searchCts?.Cancel();
        _searchDebounceTimer?.Stop();
        _searchResult = null;
        _searchQualifyingRows = [];

        foreach (var filter in SearchColumnFilters)
        {
            filter.PropertyChanged -= OnSearchColumnFilterPropertyChanged;
        }

        SearchColumnFilters.Clear();
        SearchText = string.Empty;

        OnPropertyChanged(nameof(SearchHasRegexError));
        UpdateSearchAfterFilterSummary();
    }

    private static Dictionary<DiffRow, int> BuildRowOrdinalLookup(DiffResult result)
    {
        var map = new Dictionary<DiffRow, int>(result.Rows.Count);
        for (var i = 0; i < result.Rows.Count; i++)
        {
            map[result.Rows[i]] = i;
        }

        return map;
    }

    public void ConfigureSource(SourcePaneViewModel source)
    {
        if (IsPivotOpen)
        {
            ClosePivotView();
        }

        if (IsColumnSettingsOpen)
        {
            CloseColumnSettingsWithoutSaving();
        }

        _configuringSource = source;
        DelimitedTextConfigTitle = $"Configure {source.Title} Delimited Text Source";
        DelimitedTextConfigErrorText = null;

        SelectedDelimiterPreset = DelimiterPresetOptionViewModel.FromChar(source.DelimiterCharacter) ?? DelimiterPresetOptionViewModel.Other;
        CustomDelimiterText = SelectedDelimiterPreset.Kind == DelimiterPresetKind.Other ? source.DelimiterCharacter.ToString() : string.Empty;

        SelectedQuotePreset = QuotePresetOptionViewModel.FromChar(source.QuoteCharacter) ?? QuotePresetOptionViewModel.Other;
        CustomQuoteText = SelectedQuotePreset.Kind == QuotePresetKind.Other && source.QuoteCharacter is { } q ? q.ToString() : string.Empty;

        LoadDelimitedTextPreviewSample(source.Location);
        RefreshDelimitedTextPreview();

        IsDelimitedTextConfigOpen = true;
    }

    private async Task DelimitedTextConfigOkAsync()
    {
        if (_configuringSource is null)
        {
            return;
        }

        if (!TryBuildEffectiveDelimitedTextSettings(out var delimiter, out var quote, out var error))
        {
            DelimitedTextConfigErrorText = error;
            return;
        }

        _configuringSource.DelimiterCharacter = delimiter;
        _configuringSource.QuoteCharacter = quote;

        CloseDelimitedTextConfig();

        if (!string.IsNullOrWhiteSpace(LeftSource.Location) && !string.IsNullOrWhiteSpace(RightSource.Location))
        {
            await ReloadAsync();
        }
    }

    private void CancelDelimitedTextConfig()
    {
        CloseDelimitedTextConfig();
    }

    private void CloseDelimitedTextConfig()
    {
        _configuringSource = null;
        _delimitedTextPreviewSample = null;
        IsDelimitedTextConfigOpen = false;
        DelimitedTextConfigErrorText = null;
        DelimitedTextPreviewLines.ReplaceAll([]);
    }

    private bool TryBuildEffectiveDelimitedTextSettings(out char delimiter, out char? quote, out string? error)
    {
        delimiter = ',';
        quote = null;
        error = null;

        if (SelectedDelimiterPreset is null)
        {
            error = "Select a delimiter.";
            return false;
        }

        if (SelectedDelimiterPreset.Kind == DelimiterPresetKind.Other)
        {
            if (CustomDelimiterText.Length != 1)
            {
                error = "Enter exactly one custom delimiter character.";
                return false;
            }

            delimiter = CustomDelimiterText[0];
        }
        else
        {
            delimiter = SelectedDelimiterPreset.FixedValue!.Value;
        }

        if (SelectedQuotePreset is null)
        {
            error = "Select a quote character option.";
            return false;
        }

        switch (SelectedQuotePreset.Kind)
        {
            case QuotePresetKind.None:
                quote = null;
                break;

            case QuotePresetKind.Other:
                if (CustomQuoteText.Length != 1)
                {
                    error = "Enter exactly one custom quote character.";
                    return false;
                }

                quote = CustomQuoteText[0];
                break;

            default:
                quote = SelectedQuotePreset.FixedValue;
                break;
        }

        if (quote is { } q && q == delimiter)
        {
            error = "Quote character and delimiter must be different.";
            return false;
        }

        return true;
    }

    private void LoadDelimitedTextPreviewSample(string? path)
    {
        _delimitedTextPreviewSample = null;

        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream);
            var buffer = new char[PreviewSampleCharBudget];
            var read = reader.Read(buffer, 0, buffer.Length);
            _delimitedTextPreviewSample = new string(buffer, 0, read);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private void RefreshDelimitedTextPreview()
    {
        if (!TryBuildEffectiveDelimitedTextSettings(out var delimiter, out var quote, out var error))
        {
            DelimitedTextConfigErrorText = error;
            DelimitedTextPreviewLines.ReplaceAll([]);
            return;
        }

        if (_delimitedTextPreviewSample is null)
        {
            DelimitedTextConfigErrorText = "No preview available.";
            DelimitedTextPreviewLines.ReplaceAll([]);
            return;
        }

        try
        {
            using var textReader = new StringReader(_delimitedTextPreviewSample);
            var dataSet = DelimitedTextTabularDataSource.ReadAll(textReader, delimiter, quote, treatFirstRowAsHeader: false, PreviewRowCap, CancellationToken.None);
            DelimitedTextConfigErrorText = null;
            DelimitedTextPreviewLines.ReplaceAll(dataSet.Rows.Select(row => string.Join("  |  ", row.Select(v => v ?? string.Empty))));
        }
        catch (Exception ex)
        {
            DelimitedTextConfigErrorText = $"Preview error: {ex.Message}";
            DelimitedTextPreviewLines.ReplaceAll([]);
        }
    }

    // Disabled along with the main-screen "Column Options" editor. Kept for potential reuse later.
#if false
    public void SelectVisibleColumn(int visibleIndex)
    {
        if (visibleIndex < 0 || visibleIndex >= DisplayColumns.Count)
        {
            return;
        }

        CurrentVisibleColumnIndex = visibleIndex;
    }

    private void NormalizeSelectedVisibleColumnIndex()
    {
        if (DisplayColumns.Count == 0)
        {
            CurrentVisibleColumnIndex = null;
            return;
        }

        if (CurrentVisibleColumnIndex is null || CurrentVisibleColumnIndex < 0 || CurrentVisibleColumnIndex >= DisplayColumns.Count)
        {
            CurrentVisibleColumnIndex = 0;
        }
    }
#endif

    private CancellationTokenSource StartOperation()
    {
        CancelCurrentOperation();
        _operationCts = new CancellationTokenSource();
        return _operationCts;
    }

    private void EndOperation(CancellationTokenSource cts)
    {
        if (ReferenceEquals(_operationCts, cts))
        {
            _operationCts = null;
        }
    }

    private void CancelCurrentOperation()
    {
        _operationCts?.Cancel();
    }
}
