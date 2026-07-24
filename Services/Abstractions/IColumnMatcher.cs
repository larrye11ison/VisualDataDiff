using VisualDataDiff.Models;

namespace VisualDataDiff.Services.Abstractions;

public interface IColumnMatcher
{
	IReadOnlyList<ColumnMatch> Match(IReadOnlyList<TabularColumn> leftColumns, IReadOnlyList<TabularColumn> rightColumns);
}
