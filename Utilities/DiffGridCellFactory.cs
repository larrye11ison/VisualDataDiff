using VisualDataDiff.Models;
using VisualDataDiff.ViewModels;

namespace VisualDataDiff.Utilities;

/// <summary>
/// Single place that decides how a <see cref="DiffCell"/> maps to the left/right
/// <see cref="DiffGridCellViewModel"/> shown in any grid (side-by-side or pivoted).
/// Keep all "is this cell different / missing" logic here so every view stays consistent.
/// </summary>
public static class DiffGridCellFactory
{
	public static DiffGridCellViewModel CreateLeft(DiffRow row, DiffCell cell, bool isSearchMatch = false)
	{
		var hasOrphan = row.IsLeftOrphan || row.IsRightOrphan;
		return new DiffGridCellViewModel
		{
			Value = cell.LeftValue ?? string.Empty,
			IsDifferent = cell.IsDifferent,
			IsOrphanPlaceholder = row.IsRightOrphan,
			IsOrphanRowData = row.IsLeftOrphan,
			IsActualDifference = cell.IsDifferent && !hasOrphan,
			IsSearchMatch = isSearchMatch
		};
	}

	public static DiffGridCellViewModel CreateRight(DiffRow row, DiffCell cell, bool isSearchMatch = false)
	{
		var hasOrphan = row.IsLeftOrphan || row.IsRightOrphan;
		return new DiffGridCellViewModel
		{
			Value = cell.RightValue ?? string.Empty,
			IsDifferent = cell.IsDifferent,
			IsOrphanPlaceholder = row.IsLeftOrphan,
			IsOrphanRowData = row.IsRightOrphan,
			IsActualDifference = cell.IsDifferent && !hasOrphan,
			IsSearchMatch = isSearchMatch
		};
	}
}
