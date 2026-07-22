namespace VisualDataDiff.Models;

public sealed class DiffRow
{
	public int? LeftRowIndex { get; init; }

	public int? RightRowIndex { get; init; }

	public required bool IsLeftOrphan { get; init; }

	public required bool IsRightOrphan { get; init; }

	public required bool HasDifferences { get; init; }

	public required IReadOnlyList<DiffCell> Cells { get; init; }
}
