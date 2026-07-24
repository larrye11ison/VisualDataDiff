using CommunityToolkit.Mvvm.ComponentModel;
using VisualDataDiff.Models;
using VisualDataDiff.Utilities;

namespace VisualDataDiff.ViewModels;

public sealed partial class ColumnOptionsViewModel : ObservableObject
{
	public ColumnOptionsViewModel(
		int slotIndex,
		int? leftOrdinal,
		string? leftName,
		int? rightOrdinal,
		string? rightName,
		int? score,
		bool isAmbiguous)
	{
		SlotIndex = slotIndex;
		LeftOrdinal = leftOrdinal;
		LeftName = leftName;
		RightOrdinal = rightOrdinal;
		RightName = rightName;
		Score = score;
		IsAmbiguous = isAmbiguous;
	}

	public int SlotIndex { get; }

	public int? LeftOrdinal { get; }

	public string? LeftName { get; }

	public int? RightOrdinal { get; }

	public string? RightName { get; }

	public int? Score { get; }

	public bool IsAmbiguous { get; }

	public bool IsUnmapped => LeftOrdinal is null || RightOrdinal is null;

	public string LeftDisplayName => LeftName ?? "(unmapped)";

	public string RightDisplayName => RightName ?? "(unmapped)";

	public string Name => LeftName ?? RightName ?? ExcelColumnNameHelper.ToColumnName(SlotIndex);

	// Only a real mismatch when both sides are actually mapped - a one-sided column isn't "mismatched
	// names," it just doesn't have a counterpart, which IsUnmapped already communicates on its own.
	public bool HasNameMismatch => LeftOrdinal is not null && RightOrdinal is not null && !HeaderNameComparer.AreEquivalent(LeftName, RightName);

	public string MatchStatusLabel => (IsUnmapped, IsAmbiguous, Score) switch
	{
		(true, _, _) => LeftOrdinal is null ? "Unmapped (Right only)" : "Unmapped (Left only)",
		(false, true, int s) => $"Needs review ({s}%)",
		(false, true, null) => "Needs review",
		(false, false, 100) => "Exact match",
		(false, false, int s) => $"Match ({s}%)",
		_ => string.Empty
	};

	public IReadOnlyList<ColumnRole> AvailableRoles { get; } = Enum.GetValues<ColumnRole>();

	[ObservableProperty]
	private ColumnRole _role = ColumnRole.Normal;

	[ObservableProperty]
	private bool _caseSensitive;

	[ObservableProperty]
	private bool _ignoreLeadingAndTrailingWhitespace = true;
}
