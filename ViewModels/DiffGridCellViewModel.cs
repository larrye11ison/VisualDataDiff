namespace VisualDataDiff.ViewModels;

public sealed class DiffGridCellViewModel
{
	public required string Value { get; init; }

	public required bool IsDifferent { get; init; }

	public required bool IsOrphanPlaceholder { get; init; }

	public required bool IsActualDifference { get; init; }

	public string DisplayValue => Value;
}
