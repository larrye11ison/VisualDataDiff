using VisualDataDiff.Models;

namespace VisualDataDiff.ViewModels;

public sealed class SourceTypeOptionViewModel
{
	public required SourceType SourceType { get; init; }

	public required string Label { get; init; }

	public override string ToString() => Label;

	public static readonly IReadOnlyList<SourceTypeOptionViewModel> All =
	[
		new() { SourceType = SourceType.Excel, Label = "Excel" },
		new() { SourceType = SourceType.DelimitedText, Label = "Delimited Text" }
	];
}
