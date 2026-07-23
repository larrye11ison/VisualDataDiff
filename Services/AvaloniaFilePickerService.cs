using Avalonia.Controls;
using Avalonia.Platform.Storage;
using VisualDataDiff.Models;
using VisualDataDiff.Services.Abstractions;

namespace VisualDataDiff.Services;

public sealed class AvaloniaFilePickerService : IFilePickerService
{
	private readonly Func<Window?> _windowAccessor;

	public AvaloniaFilePickerService(Func<Window?> windowAccessor)
	{
		_windowAccessor = windowAccessor;
	}

	public async Task<string?> PickFileAsync(SourceType sourceType, CancellationToken cancellationToken)
	{
		var window = _windowAccessor();
		if (window?.StorageProvider is null)
		{
			return null;
		}

		var (title, fileTypes) = BuildPickerOptions(sourceType);

		var files = await window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
		{
			AllowMultiple = false,
			Title = title,
			FileTypeFilter = fileTypes
		});

		cancellationToken.ThrowIfCancellationRequested();

		return files.Count == 0 ? null : files[0].TryGetLocalPath();
	}

	private static (string Title, IReadOnlyList<FilePickerFileType> FileTypes) BuildPickerOptions(SourceType sourceType) => sourceType switch
	{
		SourceType.Excel => ("Select Excel file", new[]
		{
			new FilePickerFileType("Excel Files") { Patterns = ["*.xlsx", "*.xls"] }
		}),
		SourceType.DelimitedText => ("Select delimited text file", new[]
		{
			new FilePickerFileType("Delimited Text Files") { Patterns = ["*.csv", "*.txt", "*.tsv", "*.dat"] },
			FilePickerFileTypes.All
		}),
		_ => ("Select file", new[] { FilePickerFileTypes.All })
	};
}
