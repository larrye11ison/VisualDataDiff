using System.Collections.Specialized;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Models.TreeDataGrid;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Selection;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Avalonia.Visuals;
using VisualDataDiff.Models;
using VisualDataDiff.ViewModels;

namespace VisualDataDiff.Views;

public partial class MainWindow : Window
{
	private const string DiffCellTemplateKey = "DiffCellTemplate";

	private static readonly IBrush ActualDifferenceBrush = new SolidColorBrush(Color.Parse("#2B7F3F00"));
	private static readonly IBrush OrphanHatchBrush = CreateOrphanHatchBrush();
	private static readonly IBrush GridLineBrush = new SolidColorBrush(Color.Parse("#33FFFFFF"));
	private static readonly DiffGridCellViewModel EmptyCell = new()
	{
		Value = string.Empty,
		IsDifferent = false,
		IsOrphanPlaceholder = false,
		IsActualDifference = false
	};

	private MainWindowViewModel? _viewModel;
	private ScrollViewer? _leftScrollViewer;
	private ScrollViewer? _rightScrollViewer;
	private bool _isSynchronizingScroll;
	private bool _rebuildScheduled;
	private TreeDataGrid? _pivotReturnGrid;
	private int? _pivotReturnRowIndex;
	private int? _pivotReturnColumnIndex;
	private TreeDataGridCellSelectionModel<DiffGridRowViewModel>? _leftCellSelection;
	private TreeDataGridCellSelectionModel<DiffGridRowViewModel>? _rightCellSelection;
	private bool _isSynchronizingCellSelection;
	private int? _activeRowIndex;
	private int? _activeColumnIndex;

	public MainWindow()
	{
		InitializeComponent();

		Resources[DiffCellTemplateKey] = new FuncDataTemplate<DiffGridCellViewModel>((cell, _) =>
		{
			var effective = cell ?? EmptyCell;
			return CreateCellElement(effective);
		});

		DataContextChanged += OnDataContextChanged;
		Opened += (_, _) =>
		{
			ApplyMeasuredRowHeight();
			ScheduleRebuildTreeGridSources();
		};

		LeftTreeDataGrid.AddHandler(PointerPressedEvent, OnTreeGridPointerPressed, handledEventsToo: true);
		RightTreeDataGrid.AddHandler(PointerPressedEvent, OnTreeGridPointerPressed, handledEventsToo: true);
		LeftTreeDataGrid.AddHandler(KeyDownEvent, OnTreeGridKeyDown, handledEventsToo: true);
		RightTreeDataGrid.AddHandler(KeyDownEvent, OnTreeGridKeyDown, handledEventsToo: true);
		LeftTreeDataGrid.AddHandler(KeyDownEvent, OnTreeGridNavigationKeyDown, RoutingStrategies.Tunnel);
		RightTreeDataGrid.AddHandler(KeyDownEvent, OnTreeGridNavigationKeyDown, RoutingStrategies.Tunnel);
		AddHandler(KeyDownEvent, OnWindowKeyDown, handledEventsToo: true);
		AddHandler(DragDrop.DragOverEvent, OnWindowDragOver);
		AddHandler(DragDrop.DropEvent, OnWindowDrop);
	}

	private void OnDataContextChanged(object? sender, EventArgs e)
	{
		if (_viewModel is not null)
		{
			_viewModel.DisplayColumns.CollectionChanged -= OnDisplayColumnsChanged;
			_viewModel.PropertyChanged -= OnViewModelPropertyChanged;
		}

		_viewModel = DataContext as MainWindowViewModel;

		if (_viewModel is not null)
		{
			_viewModel.DisplayColumns.CollectionChanged += OnDisplayColumnsChanged;
			_viewModel.PropertyChanged += OnViewModelPropertyChanged;
		}

		ScheduleRebuildTreeGridSources();
	}

	private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
	{
		if (e.PropertyName == nameof(MainWindowViewModel.IsPivotOpen) && _viewModel is not null && !_viewModel.IsPivotOpen)
		{
			RestorePivotReturnFocus();
		}
	}

	private void OnDisplayColumnsChanged(object? sender, NotifyCollectionChangedEventArgs e)
	{
		ScheduleRebuildTreeGridSources();
	}

	private void ScheduleRebuildTreeGridSources()
	{
		if (_rebuildScheduled)
		{
			return;
		}

		_rebuildScheduled = true;
		Dispatcher.UIThread.Post(() =>
		{
			_rebuildScheduled = false;

			if (_viewModel is null)
			{
				return;
			}

			var (leftSource, rightSource) = BuildTreeGridSources();
			LeftTreeDataGrid.Source = leftSource;
			RightTreeDataGrid.Source = rightSource;
			AttachScrollSynchronization();
		}, DispatcherPriority.Background);
	}

	private void ApplyMeasuredRowHeight()
	{
		// TreeDataGridRow needs a fixed, known Height for the virtualizing panel to do
		// windowed scroll-position math instead of realizing/measuring huge row batches
		// to estimate scrollable extent. Derive it from the actual cell template (rather
		// than a guessed constant) so it stays correct if font size/padding ever changes,
		// and pad generously since a cell rendered standalone here may not pick up every
		// ambient style (e.g. inherited FontSize) that the real TreeDataGridCell applies.
		var sample = CreateCellElement(new DiffGridCellViewModel
		{
			Value = "Sample Ag",
			IsDifferent = false,
			IsOrphanPlaceholder = false,
			IsActualDifference = false
		});

		sample.Measure(Size.Infinity);

		Resources["DataRowHeight"] = Math.Ceiling(sample.DesiredSize.Height * 1.3) + 4;
	}

	private (FlatTreeDataGridSource<DiffGridRowViewModel> Left, FlatTreeDataGridSource<DiffGridRowViewModel> Right) BuildTreeGridSources()
	{
		var leftSource = new FlatTreeDataGridSource<DiffGridRowViewModel>(_viewModel!.DisplayRows);
		var rightSource = new FlatTreeDataGridSource<DiffGridRowViewModel>(_viewModel.DisplayRows);

		for (var i = 0; i < _viewModel.DisplayColumns.Count; i++)
		{
			var index = i;
			var header = _viewModel.DisplayColumns[index].Name;
			var width = _viewModel.DisplayColumns[index].Width;

			leftSource.Columns.Add(CreateStyledTemplateColumn(
				header,
				row => GetCell(row.LeftCells, index),
				width));

			rightSource.Columns.Add(CreateStyledTemplateColumn(
				header,
				row => GetCell(row.RightCells, index),
				width));
		}

		_leftCellSelection = new TreeDataGridCellSelectionModel<DiffGridRowViewModel>(leftSource) { SingleSelect = true };
		_rightCellSelection = new TreeDataGridCellSelectionModel<DiffGridRowViewModel>(rightSource) { SingleSelect = true };
		leftSource.Selection = _leftCellSelection;
		rightSource.Selection = _rightCellSelection;

		_leftCellSelection.SelectionChanged += (_, _) => OnCellSelectionChanged(_leftCellSelection!, _rightCellSelection!);
		_rightCellSelection.SelectionChanged += (_, _) => OnCellSelectionChanged(_rightCellSelection!, _leftCellSelection!);

		return (leftSource, rightSource);
	}

	private void OnCellSelectionChanged(
		TreeDataGridCellSelectionModel<DiffGridRowViewModel> source,
		TreeDataGridCellSelectionModel<DiffGridRowViewModel> target)
	{
		if (_isSynchronizingCellSelection)
		{
			return;
		}

		var index = source.SelectedIndex;
		var rowIndex = index.RowIndex.Count > 0 ? index.RowIndex[0] : -1;
		var columnIndex = index.ColumnIndex;

		if (rowIndex < 0 || columnIndex < 0)
		{
			return;
		}

		_isSynchronizingCellSelection = true;
		try
		{
			target.SelectedIndex = index;
		}
		finally
		{
			_isSynchronizingCellSelection = false;
		}

		_activeRowIndex = rowIndex;
		_activeColumnIndex = columnIndex;
		_viewModel?.SelectVisibleColumn(columnIndex);
	}

	private void SetActiveCell(int rowIndex, int columnIndex)
	{
		if (_leftCellSelection is null)
		{
			return;
		}

		_leftCellSelection.SelectedIndex = new CellIndex(columnIndex, new IndexPath(rowIndex));

		BringCellIntoView(LeftTreeDataGrid, rowIndex, columnIndex);
		BringCellIntoView(RightTreeDataGrid, rowIndex, columnIndex);
	}

	private static void BringCellIntoView(TreeDataGrid grid, int rowIndex, int columnIndex)
	{
		grid.RowsPresenter?.BringIntoView(rowIndex);
		grid.ColumnHeadersPresenter?.BringIntoView(columnIndex);
	}

	private static TemplateColumn<DiffGridRowViewModel, DiffGridCellViewModel> CreateStyledTemplateColumn(
		object header,
		Func<DiffGridRowViewModel, DiffGridCellViewModel> valueSelector,
		double width)
	{
		return new TemplateColumn<DiffGridRowViewModel, DiffGridCellViewModel>(
			header,
			valueSelector,
			DiffCellTemplateKey,
			null,
			new GridLength(width),
			null);
	}

	private static DiffGridCellViewModel GetCell(IReadOnlyList<DiffGridCellViewModel> cells, int index)
	{
		return index >= 0 && index < cells.Count ? cells[index] : EmptyCell;
	}

	private static Control CreateCellElement(DiffGridCellViewModel cell)
	{
		return new Border
		{
			Padding = new Thickness(4, 2),
			BorderBrush = GridLineBrush,
			BorderThickness = new Thickness(0, 0, 1, 1),
			Background = GetCellBackground(cell),
			Child = new TextBlock
			{
				Text = cell.DisplayValue,
				Foreground = cell.IsActualDifference ? Brushes.Goldenrod : Brushes.White
			}
		};
	}

	private static IBrush GetCellBackground(DiffGridCellViewModel cell)
	{
		if (cell.IsOrphanPlaceholder)
		{
			return OrphanHatchBrush;
		}

		if (cell.IsActualDifference)
		{
			return ActualDifferenceBrush;
		}

		return Brushes.Transparent;
	}

	private static IBrush CreateOrphanHatchBrush()
	{
		return new LinearGradientBrush
		{
			StartPoint = new RelativePoint(0, 0, RelativeUnit.Absolute),
			EndPoint = new RelativePoint(10, 10, RelativeUnit.Absolute),
			SpreadMethod = GradientSpreadMethod.Repeat,
			GradientStops =
			[
				new GradientStop(Colors.Transparent, 0.00),
				new GradientStop(Colors.Transparent, 0.34),
				new GradientStop(Color.Parse("#606060"), 0.34),
				new GradientStop(Color.Parse("#606060"), 0.40),
				new GradientStop(Colors.Transparent, 0.40),
				new GradientStop(Colors.Transparent, 1.00)
			]
		};
	}

	private void AttachScrollSynchronization()
	{
		if (_leftScrollViewer is not null)
		{
			_leftScrollViewer.ScrollChanged -= OnLeftScrollChanged;
		}

		if (_rightScrollViewer is not null)
		{
			_rightScrollViewer.ScrollChanged -= OnRightScrollChanged;
		}

		_leftScrollViewer = FindPrimaryScrollViewer(LeftTreeDataGrid);
		_rightScrollViewer = FindPrimaryScrollViewer(RightTreeDataGrid);

		if (_leftScrollViewer is null || _rightScrollViewer is null)
		{
			return;
		}

		_leftScrollViewer.ScrollChanged += OnLeftScrollChanged;
		_rightScrollViewer.ScrollChanged += OnRightScrollChanged;
	}

	private static ScrollViewer? FindPrimaryScrollViewer(Control control)
	{
		return control.GetVisualDescendants()
			.OfType<ScrollViewer>()
			.OrderByDescending(x => x.Bounds.Width * x.Bounds.Height)
			.FirstOrDefault();
	}

	private void OnLeftScrollChanged(object? sender, ScrollChangedEventArgs e)
	{
		if (_leftScrollViewer is null || _rightScrollViewer is null)
		{
			return;
		}

		SyncOffsets(_leftScrollViewer, _rightScrollViewer);
	}

	private void OnRightScrollChanged(object? sender, ScrollChangedEventArgs e)
	{
		if (_leftScrollViewer is null || _rightScrollViewer is null)
		{
			return;
		}

		SyncOffsets(_rightScrollViewer, _leftScrollViewer);
	}

	private void SyncOffsets(ScrollViewer source, ScrollViewer destination)
	{
		if (_isSynchronizingScroll)
		{
			return;
		}

		var sourceOffset = source.Offset;
		var destinationOffset = destination.Offset;
		if (Math.Abs(sourceOffset.X - destinationOffset.X) < 0.1 && Math.Abs(sourceOffset.Y - destinationOffset.Y) < 0.1)
		{
			return;
		}

		_isSynchronizingScroll = true;

		try
		{
			destination.Offset = sourceOffset;
		}
		finally
		{
			_isSynchronizingScroll = false;
		}
	}

	private void OnTreeGridPointerPressed(object? sender, PointerPressedEventArgs e)
	{
		if (_viewModel is null || sender is not TreeDataGrid grid)
		{
			return;
		}

		// Hit-test the exact pointer position ourselves instead of trusting e.Source:
		// TreeDataGridColumnHeader marks its own click handled (for its built-in sort
		// gesture), which would stop a plain bubble handler from ever seeing header
		// clicks. Doing our own hit-test also sidesteps any other control along the
		// way marking the event handled before it reaches us.
		var visual = grid.InputHitTest(e.GetPosition(grid)) as Visual;
		if (visual is null)
		{
			return;
		}

		var cell = visual.GetSelfAndVisualAncestors().OfType<TreeDataGridCell>().FirstOrDefault();
		if (cell is not null)
		{
			SetActiveCell(cell.RowIndex, cell.ColumnIndex);

			if (e.ClickCount == 2)
			{
				TryOpenPivotForRow(grid, cell);
			}

			return;
		}

		var header = visual.GetSelfAndVisualAncestors().OfType<TreeDataGridColumnHeader>().FirstOrDefault();
		if (header is not null)
		{
			_viewModel.SelectVisibleColumn(header.ColumnIndex);
		}
	}

	private void OnTreeGridKeyDown(object? sender, KeyEventArgs e)
	{
		if (_viewModel is null || e.Key != Key.Enter || sender is not TreeDataGrid grid)
		{
			return;
		}

		var visual = e.Source as Visual;
		var cell = visual?.GetSelfAndVisualAncestors().OfType<TreeDataGridCell>().FirstOrDefault();
		if (cell is null)
		{
			return;
		}

		TryOpenPivotForRow(grid, cell);
		e.Handled = true;
	}

	private void OnTreeGridNavigationKeyDown(object? sender, KeyEventArgs e)
	{
		if (_viewModel is null || _viewModel.IsPivotOpen)
		{
			return;
		}

		if (_viewModel.DisplayRows.Count == 0 || _viewModel.DisplayColumns.Count == 0)
		{
			return;
		}

		var maxRow = _viewModel.DisplayRows.Count - 1;
		var maxColumn = _viewModel.DisplayColumns.Count - 1;
		var currentRow = _activeRowIndex ?? 0;
		var currentColumn = _activeColumnIndex ?? 0;

		int targetRow;
		int targetColumn;

		switch (e.Key)
		{
			case Key.Up:
				targetRow = Math.Clamp(currentRow - 1, 0, maxRow);
				targetColumn = currentColumn;
				break;

			case Key.Down:
				targetRow = Math.Clamp(currentRow + 1, 0, maxRow);
				targetColumn = currentColumn;
				break;

			case Key.Left:
				targetRow = currentRow;
				targetColumn = Math.Clamp(currentColumn - 1, 0, maxColumn);
				break;

			case Key.Right:
				targetRow = currentRow;
				targetColumn = Math.Clamp(currentColumn + 1, 0, maxColumn);
				break;

			case Key.Home:
				targetRow = currentRow;
				targetColumn = 0;
				break;

			case Key.End:
				targetRow = currentRow;
				targetColumn = maxColumn;
				break;

			case Key.PageUp:
				targetRow = Math.Clamp(currentRow - GetPageRowCount(), 0, maxRow);
				targetColumn = currentColumn;
				break;

			case Key.PageDown:
				targetRow = Math.Clamp(currentRow + GetPageRowCount(), 0, maxRow);
				targetColumn = currentColumn;
				break;

			default:
				return;
		}

		SetActiveCell(targetRow, targetColumn);
		e.Handled = true;
	}

	private int GetPageRowCount()
	{
		var rowHeight = Resources.TryGetValue("DataRowHeight", out var value) && value is double height ? height : 24;
		var viewportHeight = _leftScrollViewer?.Viewport.Height ?? _rightScrollViewer?.Viewport.Height ?? 0;

		return Math.Max((int)Math.Floor(viewportHeight / rowHeight), 1);
	}

	private void OnWindowKeyDown(object? sender, KeyEventArgs e)
	{
		if (_viewModel is null || !_viewModel.IsPivotOpen)
		{
			return;
		}

		switch (e.Key)
		{
			case Key.Escape:
				_viewModel.ClosePivotViewCommand.Execute(null);
				e.Handled = true;
				break;

			case Key.Up when _viewModel.PivotPreviousRowCommand.CanExecute(null):
				_viewModel.PivotPreviousRowCommand.Execute(null);
				e.Handled = true;
				break;

			case Key.Down when _viewModel.PivotNextRowCommand.CanExecute(null):
				_viewModel.PivotNextRowCommand.Execute(null);
				e.Handled = true;
				break;
		}
	}

	private void OnWindowDragOver(object? sender, DragEventArgs e)
	{
		e.DragEffects = e.DataTransfer.Contains(DataFormat.File) ? DragDropEffects.Copy : DragDropEffects.None;
	}

	private void OnWindowDrop(object? sender, DragEventArgs e)
	{
		if (_viewModel is null)
		{
			return;
		}

		var files = e.DataTransfer.TryGetFiles();
		if (files is null || files.Length == 0)
		{
			return;
		}

		var excelPaths = files
			.Select(f => f.TryGetLocalPath())
			.Where(path => !string.IsNullOrWhiteSpace(path) && IsExcelFile(path))
			.Select(path => path!)
			.ToArray();

		if (excelPaths.Length == 0)
		{
			_viewModel.StatusText = "Drop only Excel files (.xlsx or .xls).";
			return;
		}

		if (excelPaths.Length > 2)
		{
			_viewModel.StatusText = "Only two files can be diffed — drop exactly one or two Excel files.";
			return;
		}

		if (excelPaths.Length == 2)
		{
			AssignSource(_viewModel.LeftSource, excelPaths[0]);
			AssignSource(_viewModel.RightSource, excelPaths[1]);
			_viewModel.StatusText = "Two files dropped — assigned to Left and Right sources.";
			return;
		}

		switch (DetermineDropZone(e))
		{
			case DropZone.Left:
				AssignSource(_viewModel.LeftSource, excelPaths[0]);
				break;

			case DropZone.Right:
				AssignSource(_viewModel.RightSource, excelPaths[0]);
				break;

			default:
				_viewModel.StatusText = "Drop a single file onto the Left or Right grid, or drop two files anywhere to fill both.";
				break;
		}
	}

	private static void AssignSource(SourcePaneViewModel source, string path)
	{
		source.SelectedSourceType = SourceType.Excel;
		source.Location = path;
	}

	private static bool IsExcelFile(string path)
	{
		var extension = Path.GetExtension(path);
		return extension.Equals(".xlsx", StringComparison.OrdinalIgnoreCase)
			|| extension.Equals(".xls", StringComparison.OrdinalIgnoreCase);
	}

	private DropZone DetermineDropZone(DragEventArgs e)
	{
		var visual = this.InputHitTest(e.GetPosition(this)) as Visual;
		if (visual is null)
		{
			return DropZone.None;
		}

		var ancestors = visual.GetSelfAndVisualAncestors().ToArray();

		if (ancestors.Contains(LeftSourceGroupBox))
		{
			return DropZone.Left;
		}

		if (ancestors.Contains(RightSourceGroupBox))
		{
			return DropZone.Right;
		}

		return DropZone.None;
	}

	private enum DropZone
	{
		None,
		Left,
		Right
	}

	private void TryOpenPivotForRow(TreeDataGrid grid, TreeDataGridCell cell)
	{
		if (_viewModel is null || cell.RowIndex < 0 || cell.RowIndex >= _viewModel.DisplayRows.Count)
		{
			return;
		}

		_pivotReturnGrid = grid;
		_pivotReturnRowIndex = cell.RowIndex;
		_pivotReturnColumnIndex = cell.ColumnIndex;

		_viewModel.OpenPivotView(_viewModel.DisplayRows[cell.RowIndex]);
	}

	private void RestorePivotReturnFocus()
	{
		var grid = _pivotReturnGrid;
		var rowIndex = _pivotReturnRowIndex;
		var columnIndex = _pivotReturnColumnIndex;

		_pivotReturnGrid = null;
		_pivotReturnRowIndex = null;
		_pivotReturnColumnIndex = null;

		if (grid is null || rowIndex is null || columnIndex is null)
		{
			return;
		}

		Dispatcher.UIThread.Post(() =>
		{
			var cell = grid.TryGetCell(columnIndex.Value, rowIndex.Value);
			if (cell is not null)
			{
				cell.Focus();
			}
			else
			{
				grid.Focus();
			}
		}, DispatcherPriority.Background);
	}
}
