using VisualDataDiff.Models;

namespace VisualDataDiff.Services.Abstractions;

public interface ITabularDataSource
{
	SourceType SourceType { get; }

	bool SupportsHeaderOption { get; }

	Task<TabularDataSet> LoadAsync(SourceConfiguration configuration, CancellationToken cancellationToken);
}
