using Avalonia.Controls;
using Avalonia.Platform.Storage;
using VisualDataDiff.Services.Abstractions;

namespace VisualDataDiff.Services;

public sealed class AvaloniaFilePickerService : IFilePickerService
{
	private readonly Func<Window?> _windowAccessor;

	public AvaloniaFilePickerService(Func<Window?> windowAccessor)
	{
		_windowAccessor = windowAccessor;
	}

	public async Task<string?> PickExcelFileAsync(CancellationToken cancellationToken)
	{
		var window = _windowAccessor();
		if (window?.StorageProvider is null)
		{
			return null;
		}

		var files = await window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
		{
			AllowMultiple = false,
			Title = "Select Excel file",
			FileTypeFilter =
			[
				new FilePickerFileType("Excel Files")
				{
					Patterns = ["*.xlsx", "*.xls"]
				}
			]
		});

		cancellationToken.ThrowIfCancellationRequested();

		return files.Count == 0 ? null : files[0].TryGetLocalPath();
	}
}
