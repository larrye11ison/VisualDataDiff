namespace VisualDataDiff.Services.Abstractions;

public interface IFilePickerService
{
	Task<string?> PickExcelFileAsync(CancellationToken cancellationToken);
}
