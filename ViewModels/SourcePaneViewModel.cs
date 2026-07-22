using CommunityToolkit.Mvvm.ComponentModel;
using VisualDataDiff.Models;

namespace VisualDataDiff.ViewModels;

public sealed partial class SourcePaneViewModel : ObservableObject
{
	public SourcePaneViewModel(string title)
	{
		Title = title;
		SelectedSourceType = SourceType.Excel;
		SupportsHeaderOption = true;
		TreatFirstRowAsHeader = true;
	}

	public string Title { get; }

	public string DataGroupHeader => string.IsNullOrWhiteSpace(Location) ? $"{Title} Data" : Location;

	[ObservableProperty]
	private SourceType _selectedSourceType;

	[ObservableProperty]
	private bool _supportsHeaderOption;

	[ObservableProperty]
	private bool _treatFirstRowAsHeader;

	[ObservableProperty]
	private string? _location;

	partial void OnLocationChanged(string? value)
	{
		OnPropertyChanged(nameof(DataGroupHeader));
	}
}
