using VisualDataDiff.Models;

namespace VisualDataDiff.ViewModels;

public sealed class RowVisibilityOptionViewModel
{
	public required RowVisibilityMode Mode { get; init; }

	public required string Label { get; init; }

	public override string ToString() => Label;
}
