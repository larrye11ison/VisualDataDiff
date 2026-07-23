using VisualDataDiff.Models;

namespace VisualDataDiff.Services.Abstractions;

public interface ISearchEngine
{
	Task<SearchResult> SearchAsync(
		DiffResult diffResult,
		SearchOptions options,
		CancellationToken cancellationToken);
}
