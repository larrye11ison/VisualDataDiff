using VisualDataDiff.Models;

namespace VisualDataDiff.Services.Abstractions;

public interface ITabularDataSourceFactory
{
	ITabularDataSource GetSource(SourceType sourceType);
}
