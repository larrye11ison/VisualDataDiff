namespace VisualDataDiff.Models;

public sealed class ColumnMatch
{
	public int? LeftOrdinal { get; init; }

	public int? RightOrdinal { get; init; }

	public int? Score { get; init; }

	public required bool IsAmbiguous { get; init; }
}
