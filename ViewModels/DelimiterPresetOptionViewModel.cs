namespace VisualDataDiff.ViewModels;

public enum DelimiterPresetKind
{
	Comma,
	Tab,
	Semicolon,
	Pipe,
	Space,
	Other
}

public sealed class DelimiterPresetOptionViewModel
{
	public required DelimiterPresetKind Kind { get; init; }

	public required string Label { get; init; }

	public char? FixedValue { get; init; }

	public override string ToString() => Label;

	public static readonly IReadOnlyList<DelimiterPresetOptionViewModel> All =
	[
		new() { Kind = DelimiterPresetKind.Comma, Label = "Comma (,)", FixedValue = ',' },
		new() { Kind = DelimiterPresetKind.Tab, Label = "Tab", FixedValue = '\t' },
		new() { Kind = DelimiterPresetKind.Semicolon, Label = "Semicolon (;)", FixedValue = ';' },
		new() { Kind = DelimiterPresetKind.Pipe, Label = "Pipe (|)", FixedValue = '|' },
		new() { Kind = DelimiterPresetKind.Space, Label = "Space", FixedValue = ' ' },
		new() { Kind = DelimiterPresetKind.Other, Label = "Other...", FixedValue = null }
	];

	public static DelimiterPresetOptionViewModel Other => All.First(x => x.Kind == DelimiterPresetKind.Other);

	public static DelimiterPresetOptionViewModel? FromChar(char value) =>
		All.FirstOrDefault(x => x.FixedValue == value);
}
