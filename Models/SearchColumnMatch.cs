namespace VisualDataDiff.Models;

public sealed class SearchColumnMatch
{
	public required int Ordinal { get; init; }

	public required string ColumnName { get; init; }

	public required int LeftCount { get; init; }

	public required int RightCount { get; init; }

	public int TotalCount => LeftCount + RightCount;
}
