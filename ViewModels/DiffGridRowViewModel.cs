using VisualDataDiff.Models;
using VisualDataDiff.Utilities;

namespace VisualDataDiff.ViewModels;

public sealed class DiffGridRowViewModel
{
	public DiffGridRowViewModel(DiffRow row, IReadOnlyList<int> visibleOrdinals)
	{
		Row = row;

		LeftCells = visibleOrdinals
			.Select(x => DiffGridCellFactory.CreateLeft(row, row.Cells[x]))
			.ToArray();

		RightCells = visibleOrdinals
			.Select(x => DiffGridCellFactory.CreateRight(row, row.Cells[x]))
			.ToArray();
	}

	public DiffRow Row { get; }

	public IReadOnlyList<DiffGridCellViewModel> LeftCells { get; }

	public IReadOnlyList<DiffGridCellViewModel> RightCells { get; }

	public bool HasDifferences => Row.HasDifferences;

	public bool IsOrphan => Row.IsLeftOrphan || Row.IsRightOrphan;

	public bool IsLeftOrphan => Row.IsLeftOrphan;

	public bool IsRightOrphan => Row.IsRightOrphan;
}
