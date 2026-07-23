namespace VisualDataDiff.Models;

public sealed class SearchResult
{
	public required IReadOnlyList<SearchColumnMatch> ColumnMatches { get; init; }

	public required IReadOnlyDictionary<DiffRow, SearchRowMatch> RowMatches { get; init; }

	public required bool HasRegexError { get; init; }

	public required bool HasQuery { get; init; }
}
