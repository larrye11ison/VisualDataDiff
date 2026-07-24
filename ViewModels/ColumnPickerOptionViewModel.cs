namespace VisualDataDiff.ViewModels;

public sealed class ColumnPickerOptionViewModel
{
	public int? Ordinal { get; init; }

	public required string Label { get; init; }

	public override string ToString() => Label;

	public static ColumnPickerOptionViewModel Unmapped => new() { Ordinal = null, Label = "(unmapped)" };
}
