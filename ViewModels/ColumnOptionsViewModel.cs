using CommunityToolkit.Mvvm.ComponentModel;
using VisualDataDiff.Models;

namespace VisualDataDiff.ViewModels;

public sealed partial class ColumnOptionsViewModel : ObservableObject
{
	public ColumnOptionsViewModel(int ordinal, string name)
	{
		Ordinal = ordinal;
		Name = name;
	}

	public int Ordinal { get; }

	public string Name { get; }

	public IReadOnlyList<ColumnRole> AvailableRoles { get; } = Enum.GetValues<ColumnRole>();

	[ObservableProperty]
	private ColumnRole _role = ColumnRole.Normal;

	[ObservableProperty]
	private bool _caseSensitive;

	[ObservableProperty]
	private bool _ignoreLeadingAndTrailingWhitespace;
}
