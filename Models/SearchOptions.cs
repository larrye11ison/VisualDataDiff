namespace VisualDataDiff.Models;

public sealed class SearchOptions
{
	public required string QueryText { get; init; }

	public required bool UseRegex { get; init; }

	public required bool CaseSensitive { get; init; }
}
