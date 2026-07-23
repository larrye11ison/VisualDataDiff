namespace VisualDataDiff.ViewModels;

public enum QuotePresetKind
{
	DoubleQuote,
	SingleQuote,
	None,
	Other
}

public sealed class QuotePresetOptionViewModel
{
	public required QuotePresetKind Kind { get; init; }

	public required string Label { get; init; }

	public char? FixedValue { get; init; }

	public override string ToString() => Label;

	public static readonly IReadOnlyList<QuotePresetOptionViewModel> All =
	[
		new() { Kind = QuotePresetKind.DoubleQuote, Label = "Double quote (\")", FixedValue = '"' },
		new() { Kind = QuotePresetKind.SingleQuote, Label = "Single quote (')", FixedValue = '\'' },
		new() { Kind = QuotePresetKind.None, Label = "None (no quoting)", FixedValue = null },
		new() { Kind = QuotePresetKind.Other, Label = "Other...", FixedValue = null }
	];

	public static QuotePresetOptionViewModel Other => All.First(x => x.Kind == QuotePresetKind.Other);

	// null must resolve to the "None" preset, not fall through to "Other" - both have FixedValue == null.
	public static QuotePresetOptionViewModel? FromChar(char? value) =>
		value is null
			? All.First(x => x.Kind == QuotePresetKind.None)
			: All.FirstOrDefault(x => x.Kind != QuotePresetKind.Other && x.FixedValue == value);
}
