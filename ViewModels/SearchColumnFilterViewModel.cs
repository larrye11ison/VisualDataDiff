using CommunityToolkit.Mvvm.ComponentModel;

namespace VisualDataDiff.ViewModels;

public sealed partial class SearchColumnFilterViewModel : ObservableObject
{
	public required bool IsAll { get; init; }

	public required int Ordinal { get; init; }

	public required string ColumnName { get; init; }

	public required int LeftCount { get; init; }

	public required int RightCount { get; init; }

	public int TotalCount => LeftCount + RightCount;

	[ObservableProperty]
	private bool _isIncluded = true;
}
