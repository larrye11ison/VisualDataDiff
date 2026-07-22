namespace VisualDataDiff.Models;

public sealed class DiffResult
{
	public required IReadOnlyList<DiffColumn> Columns { get; init; }

	public required IReadOnlyList<DiffRow> Rows { get; init; }
}
