using VisualDataDiff.Models;
using VisualDataDiff.Services.Abstractions;
using VisualDataDiff.Services.Sources;

namespace VisualDataDiff.Services;

public sealed class TabularDataSourceFactory : ITabularDataSourceFactory
{
	private readonly Dictionary<SourceType, ITabularDataSource> _sources;

	public TabularDataSourceFactory()
	{
		_sources = new Dictionary<SourceType, ITabularDataSource>
		{
			[SourceType.Excel] = new ExcelTabularDataSource(),
			[SourceType.DelimitedText] = new DelimitedTextTabularDataSource()
		};
	}

	public ITabularDataSource GetSource(SourceType sourceType)
	{
		if (_sources.TryGetValue(sourceType, out var source))
		{
			return source;
		}

		throw new NotSupportedException($"Source type '{sourceType}' is not supported.");
	}
}
