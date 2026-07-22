namespace VisualDataDiff.Models;

public sealed class DiffCell
{
	public required int Ordinal { get; init; }

	public string? LeftValue { get; init; }

	public string? RightValue { get; init; }

	public required bool IsDifferent { get; init; }
}
