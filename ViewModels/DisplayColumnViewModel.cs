namespace VisualDataDiff.ViewModels;

public sealed class DisplayColumnViewModel
{
	public required int Ordinal { get; init; }

	public required string Name { get; init; }

	public required bool IsKey { get; init; }

	public required bool IsIgnored { get; init; }

	public required bool HasDifferences { get; init; }

	public required double Width { get; init; }
}
