namespace VisualDataDiff.Models;

public sealed class ColumnComparisonRule
{
	public int? LeftOrdinal { get; set; }

	public int? RightOrdinal { get; set; }

	public ColumnRole Role { get; set; } = ColumnRole.Normal;

	public bool CaseSensitive { get; set; }

	public bool IgnoreLeadingAndTrailingWhitespace { get; set; }
}
