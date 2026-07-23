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

	public bool SupportsDelimiterConfiguration => SelectedSourceType == SourceType.DelimitedText;

	public bool HasLocation => !string.IsNullOrWhiteSpace(Location);

	public SourceTypeOptionViewModel? SelectedSourceTypeOption
	{
		get => SourceTypeOptionViewModel.All.FirstOrDefault(x => x.SourceType == SelectedSourceType);
		set
		{
			if (value is not null)
			{
				SelectedSourceType = value.SourceType;
			}
		}
	}

	[ObservableProperty]
	private SourceType _selectedSourceType;

	[ObservableProperty]
	private bool _supportsHeaderOption;

	[ObservableProperty]
	private bool _treatFirstRowAsHeader;

	[ObservableProperty]
	private string? _location;

	[ObservableProperty]
	private char _delimiterCharacter = ',';

	[ObservableProperty]
	private char? _quoteCharacter = '"';

	partial void OnLocationChanged(string? value)
	{
		OnPropertyChanged(nameof(DataGroupHeader));
		OnPropertyChanged(nameof(HasLocation));
	}

	partial void OnSelectedSourceTypeChanged(SourceType value)
	{
		OnPropertyChanged(nameof(SupportsDelimiterConfiguration));
		OnPropertyChanged(nameof(SelectedSourceTypeOption));
	}
}
