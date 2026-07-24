using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using FuzzySharp;
using VisualDataDiff.Models;
using VisualDataDiff.Utilities;

namespace VisualDataDiff.ViewModels;

public sealed partial class ColumnOptionsViewModel : ObservableObject
{
	private readonly IReadOnlyList<TabularColumn> _leftColumns;
	private readonly IReadOnlyList<TabularColumn> _rightColumns;
	private bool _isSyncingSelection;

	public ColumnOptionsViewModel(
		int slotIndex,
		IReadOnlyList<TabularColumn> leftColumns,
		IReadOnlyList<TabularColumn> rightColumns,
		int? leftOrdinal,
		int? rightOrdinal,
		int? score,
		bool isAmbiguous)
	{
		SlotIndex = slotIndex;
		_leftColumns = leftColumns;
		_rightColumns = rightColumns;
		_score = score;
		_isAmbiguous = isAmbiguous;

		var rightAnchorName = rightOrdinal is int ro ? rightColumns[ro].Name : null;
		var leftAnchorName = leftOrdinal is int lo ? leftColumns[lo].Name : null;

		LeftOptions = new ObservableCollection<ColumnPickerOptionViewModel>(BuildOptions(leftColumns, rightAnchorName));
		RightOptions = new ObservableCollection<ColumnPickerOptionViewModel>(BuildOptions(rightColumns, leftAnchorName));

		_isSyncingSelection = true;
		_selectedLeftOption = LeftOptions.First(o => o.Ordinal == leftOrdinal);
		_selectedRightOption = RightOptions.First(o => o.Ordinal == rightOrdinal);
		_isSyncingSelection = false;
	}

	public int SlotIndex { get; }

	public int? LeftOrdinal => SelectedLeftOption?.Ordinal;

	public string? LeftName => LeftOrdinal is int lo ? _leftColumns[lo].Name : null;

	public int? RightOrdinal => SelectedRightOption?.Ordinal;

	public string? RightName => RightOrdinal is int ro ? _rightColumns[ro].Name : null;

	public bool IsUnmapped => LeftOrdinal is null || RightOrdinal is null;

	public string LeftDisplayName => LeftName ?? "(unmapped)";

	public string RightDisplayName => RightName ?? "(unmapped)";

	public string Name => LeftName ?? RightName ?? ExcelColumnNameHelper.ToColumnName(SlotIndex);

	// Only a real mismatch when both sides are actually mapped - a one-sided column isn't "mismatched
	// names," it just doesn't have a counterpart, which IsUnmapped already communicates on its own.
	public bool HasNameMismatch => LeftOrdinal is not null && RightOrdinal is not null && !HeaderNameComparer.AreEquivalent(LeftName, RightName);

	public string MatchStatusLabel => (IsUnmapped, IsManuallyMapped, IsAmbiguous, Score) switch
	{
		(true, _, _, _) => LeftOrdinal is null ? "Unmapped (Right only)" : "Unmapped (Left only)",
		(false, true, _, _) => "Manual",
		(false, false, true, int s) => $"Needs review ({s}%)",
		(false, false, true, null) => "Needs review",
		(false, false, false, 100) => "Exact match",
		(false, false, false, int s) => $"Match ({s}%)",
		_ => string.Empty
	};

	public ObservableCollection<ColumnPickerOptionViewModel> LeftOptions { get; }

	public ObservableCollection<ColumnPickerOptionViewModel> RightOptions { get; }

	public IReadOnlyList<ColumnRole> AvailableRoles { get; } = Enum.GetValues<ColumnRole>();

	[ObservableProperty]
	private ColumnPickerOptionViewModel? _selectedLeftOption;

	[ObservableProperty]
	private ColumnPickerOptionViewModel? _selectedRightOption;

	[ObservableProperty]
	private int? _score;

	[ObservableProperty]
	private bool _isAmbiguous;

	[ObservableProperty]
	private bool _isManuallyMapped;

	[ObservableProperty]
	private ColumnRole _role = ColumnRole.Normal;

	[ObservableProperty]
	private bool _caseSensitive;

	[ObservableProperty]
	private bool _ignoreLeadingAndTrailingWhitespace = true;

	partial void OnSelectedLeftOptionChanged(ColumnPickerOptionViewModel? value)
	{
		if (_isSyncingSelection)
		{
			return;
		}

		OnManualRemap();
		ReorderOptions(RightOptions, _rightColumns, anchorName: LeftName, currentSelection: SelectedRightOption, setSelection: v => SelectedRightOption = v);
	}

	partial void OnSelectedRightOptionChanged(ColumnPickerOptionViewModel? value)
	{
		if (_isSyncingSelection)
		{
			return;
		}

		OnManualRemap();
		ReorderOptions(LeftOptions, _leftColumns, anchorName: RightName, currentSelection: SelectedLeftOption, setSelection: v => SelectedLeftOption = v);
	}

	// A manual remap makes the original auto-match Score/IsAmbiguous meaningless - clear them and
	// notify every derived property that depends on LeftOrdinal/RightOrdinal, since those are now
	// computed from SelectedLeftOption/SelectedRightOption rather than plain init-only fields.
	private void OnManualRemap()
	{
		IsManuallyMapped = true;
		Score = null;
		IsAmbiguous = false;

		OnPropertyChanged(nameof(LeftOrdinal));
		OnPropertyChanged(nameof(LeftName));
		OnPropertyChanged(nameof(RightOrdinal));
		OnPropertyChanged(nameof(RightName));
		OnPropertyChanged(nameof(LeftDisplayName));
		OnPropertyChanged(nameof(RightDisplayName));
		OnPropertyChanged(nameof(Name));
		OnPropertyChanged(nameof(HasNameMismatch));
		OnPropertyChanged(nameof(IsUnmapped));
		OnPropertyChanged(nameof(MatchStatusLabel));
	}

	private void ReorderOptions(
		ObservableCollection<ColumnPickerOptionViewModel> options,
		IReadOnlyList<TabularColumn> columns,
		string? anchorName,
		ColumnPickerOptionViewModel? currentSelection,
		Action<ColumnPickerOptionViewModel> setSelection)
	{
		var reordered = BuildOptions(columns, anchorName);

		_isSyncingSelection = true;
		options.Clear();
		foreach (var option in reordered)
		{
			options.Add(option);
		}

		setSelection(options.FirstOrDefault(o => o.Ordinal == currentSelection?.Ordinal) ?? options[0]);
		_isSyncingSelection = false;
	}

	// Used by MainWindowViewModel to restore this row's full mapping state when the user discards
	// changes in the Column Setup dialog (Cancel -> Discard).
	public void RestoreMapping(int? leftOrdinal, int? rightOrdinal, int? score, bool isAmbiguous, bool isManuallyMapped)
	{
		_isSyncingSelection = true;
		ReorderOptions(LeftOptions, _leftColumns, rightOrdinal is int ro ? _rightColumns[ro].Name : null, SelectedLeftOption, v => SelectedLeftOption = v);
		ReorderOptions(RightOptions, _rightColumns, leftOrdinal is int lo ? _leftColumns[lo].Name : null, SelectedRightOption, v => SelectedRightOption = v);
		SelectedLeftOption = LeftOptions.First(o => o.Ordinal == leftOrdinal);
		SelectedRightOption = RightOptions.First(o => o.Ordinal == rightOrdinal);
		_isSyncingSelection = false;

		Score = score;
		IsAmbiguous = isAmbiguous;
		IsManuallyMapped = isManuallyMapped;

		OnPropertyChanged(nameof(LeftOrdinal));
		OnPropertyChanged(nameof(LeftName));
		OnPropertyChanged(nameof(RightOrdinal));
		OnPropertyChanged(nameof(RightName));
		OnPropertyChanged(nameof(LeftDisplayName));
		OnPropertyChanged(nameof(RightDisplayName));
		OnPropertyChanged(nameof(Name));
		OnPropertyChanged(nameof(HasNameMismatch));
		OnPropertyChanged(nameof(IsUnmapped));
		OnPropertyChanged(nameof(MatchStatusLabel));
	}

	// The unmapped sentinel always sorts first; real columns sort by fuzzy match quality against
	// whatever the other side currently holds (best first), or by their natural column order if the
	// other side isn't mapped at all yet (nothing to score against).
	private static List<ColumnPickerOptionViewModel> BuildOptions(IReadOnlyList<TabularColumn> columns, string? anchorName)
	{
		var scored = columns.Select(c => (Column: c, Score: anchorName is null ? (int?)null : Fuzz.WeightedRatio(anchorName, c.Name)));

		var ordered = anchorName is null
			? scored.OrderBy(x => x.Column.Ordinal)
			: scored.OrderByDescending(x => x.Score);

		var options = new List<ColumnPickerOptionViewModel> { ColumnPickerOptionViewModel.Unmapped };
		options.AddRange(ordered.Select(x => new ColumnPickerOptionViewModel
		{
			Ordinal = x.Column.Ordinal,
			Label = x.Score is int s ? $"{x.Column.Name} ({s}%)" : x.Column.Name
		}));

		return options;
	}
}
