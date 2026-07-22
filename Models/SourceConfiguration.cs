namespace VisualDataDiff.Models;

public sealed class SourceConfiguration
{
	public SourceType SourceType { get; set; } = SourceType.Excel;

	public bool SupportsHeaderOption { get; set; }

	public bool TreatFirstRowAsHeader { get; set; } = true;

	public string? Location { get; set; }
}
