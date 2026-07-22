using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VisualDataDiff.Models;
using VisualDataDiff.Services.Abstractions;
using VisualDataDiff.Utilities;

namespace VisualDataDiff.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly ITabularDataSourceFactory _sourceFactory;
    private readonly IDataDiffEngine _diffEngine;
    private readonly IFilePickerService _filePickerService;
    private CancellationTokenSource? _operationCts;
    private TabularDataSet? _leftDataSet;
    private TabularDataSet? _rightDataSet;
    private DiffResult? _diffResult;
    private DiffGridRowViewModel? _pivotedSourceRow;

    public MainWindowViewModel(
        ITabularDataSourceFactory sourceFactory,
        IDataDiffEngine diffEngine,
        IFilePickerService filePickerService)
    {
        _sourceFactory = sourceFactory;
        _diffEngine = diffEngine;
        _filePickerService = filePickerService;

        LeftSource = new SourcePaneViewModel("Left");
        RightSource = new SourcePaneViewModel("Right");
        LeftSource.PropertyChanged += OnSourcePropertyChanged;
        RightSource.PropertyChanged += OnSourcePropertyChanged;

        AvailableSourceTypes = Enum.GetValues<SourceType>();
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
        LoadAndCompareCommand = new AsyncRelayCommand(LoadAndCompareAsync, () => !IsBusy);
        RecompareCommand = new AsyncRelayCommand(RecompareAsync, () => !IsBusy && _leftDataSet is not null && _rightDataSet is not null);
        CancelCommand = new RelayCommand(CancelCurrentOperation, () => IsBusy);
        ClosePivotViewCommand = new RelayCommand(ClosePivotView);
        PivotPreviousRowCommand = new RelayCommand(() => NavigatePivotRow(-1), () => CanNavigatePivotRow(-1));
        PivotNextRowCommand = new RelayCommand(() => NavigatePivotRow(1), () => CanNavigatePivotRow(1));

        StatusText = "Select a source for each side and run comparison.";
    }

    public SourcePaneViewModel LeftSource { get; }

    public SourcePaneViewModel RightSource { get; }

    public IReadOnlyList<SourceType> AvailableSourceTypes { get; }

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

    public IAsyncRelayCommand SetupLeftSourceCommand { get; }

    public IAsyncRelayCommand SetupRightSourceCommand { get; }

    public IAsyncRelayCommand LoadAndCompareCommand { get; }

    public IAsyncRelayCommand RecompareCommand { get; }

    public IRelayCommand CancelCommand { get; }

    public IRelayCommand ClosePivotViewCommand { get; }

    public IRelayCommand PivotPreviousRowCommand { get; }

    public IRelayCommand PivotNextRowCommand { get; }

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

    public string PivotSubtitle => _pivotedSourceRow switch
    {
        null => string.Empty,
        { IsLeftOrphan: true } => "Left-only row (no matching right row)",
        { IsRightOrphan: true } => "Right-only row (no matching left row)",
        _ => "Matched row"
    };

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

    partial void OnIsBusyChanged(bool value)
    {
        SetupLeftSourceCommand.NotifyCanExecuteChanged();
        SetupRightSourceCommand.NotifyCanExecuteChanged();
        LoadAndCompareCommand.NotifyCanExecuteChanged();
        RecompareCommand.NotifyCanExecuteChanged();
        CancelCommand.NotifyCanExecuteChanged();
    }

    private void OnSourcePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(SourcePaneViewModel.DataGroupHeader) or nameof(SourcePaneViewModel.Location))
        {
            OnPropertyChanged(nameof(LeftGridSummary));
            OnPropertyChanged(nameof(RightGridSummary));
        }
    }

    private async Task SetupSourceAsync(SourcePaneViewModel source)
    {
        var path = await _filePickerService.PickExcelFileAsync(CancellationToken.None);
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        source.Location = path;
        StatusText = $"{source.Title} source configured.";
    }

    private async Task LoadAndCompareAsync()
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
                Location = LeftSource.Location
            }, cts.Token);

            var rightLoadTask = rightProvider.LoadAsync(new SourceConfiguration
            {
                SourceType = RightSource.SelectedSourceType,
                SupportsHeaderOption = RightSource.SupportsHeaderOption,
                TreatFirstRowAsHeader = RightSource.TreatFirstRowAsHeader,
                Location = RightSource.Location
            }, cts.Token);

            await Task.WhenAll(leftLoadTask, rightLoadTask);

            _leftDataSet = leftLoadTask.Result;
            _rightDataSet = rightLoadTask.Result;

            RebuildColumnOptions(_leftDataSet, _rightDataSet);
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

    private async Task RecompareAsync()
    {
        if (_leftDataSet is null || _rightDataSet is null)
        {
            StatusText = "Load both sources before running comparison.";
            return;
        }

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

        ClosePivotView();

        StatusText = "Comparing data...";
        _diffResult = await _diffEngine.CompareAsync(_leftDataSet, _rightDataSet, BuildRules(), cancellationToken);
        ApplyVisibilityFilters();
        StatusText = $"Comparison complete. Showing {DisplayRows.Count} rows and {DisplayColumns.Count} columns.";
    }

    private IReadOnlyList<ColumnComparisonRule> BuildRules()
    {
        return ColumnOptions
            .Select(x => new ColumnComparisonRule
            {
                Ordinal = x.Ordinal,
                Role = x.Role,
                CaseSensitive = x.CaseSensitive,
                IgnoreLeadingAndTrailingWhitespace = x.IgnoreLeadingAndTrailingWhitespace
            })
            .OrderBy(x => x.Ordinal)
            .ToArray();
    }

    private void RebuildColumnOptions(TabularDataSet leftDataSet, TabularDataSet rightDataSet)
    {
        var existing = ColumnOptions.ToDictionary(x => x.Ordinal);
        foreach (var column in ColumnOptions)
        {
            column.PropertyChanged -= OnColumnOptionsPropertyChanged;
        }

        ColumnOptions.Clear();

        var maxColumns = Math.Max(leftDataSet.Columns.Count, rightDataSet.Columns.Count);
        for (var i = 0; i < maxColumns; i++)
        {
            var name = i < leftDataSet.Columns.Count
                ? leftDataSet.Columns[i].Name
                : i < rightDataSet.Columns.Count
                    ? rightDataSet.Columns[i].Name
                    : ExcelColumnNameHelper.ToColumnName(i);

            var vm = new ColumnOptionsViewModel(i, name);
            if (existing.TryGetValue(i, out var previous))
            {
                vm.Role = previous.Role;
                vm.CaseSensitive = previous.CaseSensitive;
                vm.IgnoreLeadingAndTrailingWhitespace = previous.IgnoreLeadingAndTrailingWhitespace;
            }
            else if (i == 0)
            {
                vm.Role = ColumnRole.Key;
            }

            vm.PropertyChanged += OnColumnOptionsPropertyChanged;
            ColumnOptions.Add(vm);
        }
    }

    private void OnColumnOptionsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ColumnOptionsViewModel.Role) && HideIgnoredColumns)
        {
            ApplyVisibilityFilters();
        }

        if (e.PropertyName is nameof(ColumnOptionsViewModel.CaseSensitive) or nameof(ColumnOptionsViewModel.IgnoreLeadingAndTrailingWhitespace) or nameof(ColumnOptionsViewModel.Role))
        {
            StatusText = "Column options changed. Run comparison to refresh results.";
            OnPropertyChanged(nameof(SelectedColumnOption));
            OnPropertyChanged(nameof(HasSelectedColumnOption));
            OnPropertyChanged(nameof(ColumnOptionsHeader));
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

        var optionsByOrdinal = ColumnOptions.ToDictionary(x => x.Ordinal);
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

        NormalizeSelectedVisibleColumnIndex();
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
        _pivotedSourceRow = row;
        IsPivotOpen = true;
        OnPropertyChanged(nameof(PivotSubtitle));
        RebuildPivotedColumns();
        NotifyPivotNavigationCanExecuteChanged();
    }

    private void ClosePivotView()
    {
        if (!IsPivotOpen && _pivotedSourceRow is null)
        {
            return;
        }

        IsPivotOpen = false;
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

    private bool CanNavigatePivotRow(int delta) => FindPivotNavigationTargetIndex(delta) is not null;

    private void NavigatePivotRow(int delta)
    {
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

        var optionsByOrdinal = ColumnOptions.ToDictionary(x => x.Ordinal);
        var row = _pivotedSourceRow.Row;

        foreach (var column in _diffResult.Columns.OrderBy(x => x.Ordinal))
        {
            if (column.Ordinal < 0 || column.Ordinal >= row.Cells.Count)
            {
                continue;
            }

            optionsByOrdinal.TryGetValue(column.Ordinal, out var option);
            var isKey = option?.Role == ColumnRole.Key;
            var cell = row.Cells[column.Ordinal];

            if (!PivotShowAllColumns && !isKey && !cell.IsDifferent)
            {
                continue;
            }

            PivotedColumns.Add(new PivotedColumnViewModel
            {
                Ordinal = column.Ordinal,
                ColumnName = column.Name,
                IsKeyColumn = isKey,
                IsDifferent = cell.IsDifferent,
                LeftCell = DiffGridCellFactory.CreateLeft(row, cell),
                RightCell = DiffGridCellFactory.CreateRight(row, cell)
            });
        }
    }

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
