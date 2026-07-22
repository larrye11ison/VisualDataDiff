using VisualDataDiff.Models;

namespace VisualDataDiff.Services.Abstractions;

public interface IDataDiffEngine
{
	Task<DiffResult> CompareAsync(
		TabularDataSet left,
		TabularDataSet right,
		IReadOnlyList<ColumnComparisonRule> columnRules,
		CancellationToken cancellationToken);
}
