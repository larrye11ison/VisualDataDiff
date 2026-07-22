namespace VisualDataDiff.Models;

public sealed class ColumnComparisonRule
{
	public int Ordinal { get; set; }

	public ColumnRole Role { get; set; } = ColumnRole.Normal;

	public bool CaseSensitive { get; set; }

	public bool IgnoreLeadingAndTrailingWhitespace { get; set; }
}
