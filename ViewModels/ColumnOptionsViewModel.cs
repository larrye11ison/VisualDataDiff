using CommunityToolkit.Mvvm.ComponentModel;
using VisualDataDiff.Models;
using VisualDataDiff.Utilities;

namespace VisualDataDiff.ViewModels;

public sealed partial class ColumnOptionsViewModel : ObservableObject
{
	public ColumnOptionsViewModel(int ordinal, string? leftName, string? rightName)
	{
		Ordinal = ordinal;
		LeftName = leftName;
		RightName = rightName;
	}

	public int Ordinal { get; }

	public string? LeftName { get; }

	public string? RightName { get; }

	public string LeftDisplayName => LeftName ?? "(not present)";

	public string RightDisplayName => RightName ?? "(not present)";

	public string Name => LeftName ?? RightName ?? ExcelColumnNameHelper.ToColumnName(Ordinal);

	public bool HasNameMismatch => !HeaderNameComparer.AreEquivalent(LeftName, RightName);

	public IReadOnlyList<ColumnRole> AvailableRoles { get; } = Enum.GetValues<ColumnRole>();

	[ObservableProperty]
	private ColumnRole _role = ColumnRole.Normal;

	[ObservableProperty]
	private bool _caseSensitive;

	[ObservableProperty]
	private bool _ignoreLeadingAndTrailingWhitespace = true;
}
