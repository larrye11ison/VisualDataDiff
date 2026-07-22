namespace VisualDataDiff.Models;

public sealed class DiffColumn
{
	public required int Ordinal { get; init; }

	public required string Name { get; init; }

	public required bool HasDifferences { get; set; }
}
