namespace VisualDataDiff.ViewModels;

public sealed class PivotedColumnViewModel
{
	public required int Ordinal { get; init; }

	public required string ColumnName { get; init; }

	public required bool IsKeyColumn { get; init; }

	public required bool IsDifferent { get; init; }

	public required DiffGridCellViewModel LeftCell { get; init; }

	public required DiffGridCellViewModel RightCell { get; init; }
}
