namespace VisualDataDiff.Models;

public sealed class SearchRowMatch
{
	public required DiffRow Row { get; init; }

	public required IReadOnlySet<int> LeftMatchedOrdinals { get; init; }

	public required IReadOnlySet<int> RightMatchedOrdinals { get; init; }
}
