namespace VisualDataDiff.Models;

public sealed class TabularDataSet
{
	public required IReadOnlyList<TabularColumn> Columns { get; init; }

	public required IReadOnlyList<IReadOnlyList<string?>> Rows { get; init; }
}
