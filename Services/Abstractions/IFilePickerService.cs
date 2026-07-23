using VisualDataDiff.Models;

namespace VisualDataDiff.Services.Abstractions;

public interface IFilePickerService
{
	Task<string?> PickFileAsync(SourceType sourceType, CancellationToken cancellationToken);
}
