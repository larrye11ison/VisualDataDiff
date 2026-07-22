using VisualDataDiff.Models;
using VisualDataDiff.Services.Abstractions;
using VisualDataDiff.Utilities;

namespace VisualDataDiff.Services;

public sealed class DataDiffEngine : IDataDiffEngine
{
	public async Task<DiffResult> CompareAsync(
		TabularDataSet left,
		TabularDataSet right,
		IReadOnlyList<ColumnComparisonRule> columnRules,
		CancellationToken cancellationToken)
	{
		return await Task.Run(() => CompareCore(left, right, columnRules, cancellationToken), cancellationToken);
	}

	private static DiffResult CompareCore(
		TabularDataSet left,
		TabularDataSet right,
		IReadOnlyList<ColumnComparisonRule> columnRules,
		CancellationToken cancellationToken)
	{
		var maxColumns = Math.Max(left.Columns.Count, right.Columns.Count);
		var rulesByOrdinal = columnRules.ToDictionary(x => x.Ordinal);

		var columns = new List<DiffColumn>(maxColumns);
		for (var i = 0; i < maxColumns; i++)
		{
			var name = i < left.Columns.Count
				? left.Columns[i].Name
				: i < right.Columns.Count
					? right.Columns[i].Name
					: ExcelColumnNameHelper.ToColumnName(i);

			columns.Add(new DiffColumn
			{
				Ordinal = i,
				Name = name,
				HasDifferences = false
			});
		}

		var keyOrdinals = rulesByOrdinal.Values
			.Where(x => x.Role == ColumnRole.Key)
			.Select(x => x.Ordinal)
			.Where(x => x >= 0 && x < maxColumns)
			.Distinct()
			.OrderBy(x => x)
			.ToArray();

		var leftMap = BuildKeyMap(left, keyOrdinals, rulesByOrdinal, cancellationToken);
		var rightMap = BuildKeyMap(right, keyOrdinals, rulesByOrdinal, cancellationToken);

		var orderedKeys = leftMap.Keys.Union(rightMap.Keys).OrderBy(x => x, StringComparer.Ordinal).ToArray();
		var rows = new List<DiffRow>();

		foreach (var key in orderedKeys)
		{
			cancellationToken.ThrowIfCancellationRequested();

			leftMap.TryGetValue(key, out var leftIndices);
			rightMap.TryGetValue(key, out var rightIndices);

			leftIndices ??= new Queue<int>();
			rightIndices ??= new Queue<int>();

			while (leftIndices.Count > 0 && rightIndices.Count > 0)
			{
				var leftIndex = leftIndices.Dequeue();
				var rightIndex = rightIndices.Dequeue();
				rows.Add(BuildRow(left, right, leftIndex, rightIndex, maxColumns, rulesByOrdinal, columns));
			}

			while (leftIndices.Count > 0)
			{
				var leftIndex = leftIndices.Dequeue();
				rows.Add(BuildRow(left, right, leftIndex, null, maxColumns, rulesByOrdinal, columns));
			}

			while (rightIndices.Count > 0)
			{
				var rightIndex = rightIndices.Dequeue();
				rows.Add(BuildRow(left, right, null, rightIndex, maxColumns, rulesByOrdinal, columns));
			}
		}

		return new DiffResult
		{
			Columns = columns,
			Rows = rows
		};
	}

	private static Dictionary<string, Queue<int>> BuildKeyMap(
		TabularDataSet dataSet,
		IReadOnlyList<int> keyOrdinals,
		IReadOnlyDictionary<int, ColumnComparisonRule> rulesByOrdinal,
		CancellationToken cancellationToken)
	{
		var map = new Dictionary<string, Queue<int>>(StringComparer.Ordinal);

		for (var i = 0; i < dataSet.Rows.Count; i++)
		{
			cancellationToken.ThrowIfCancellationRequested();

			var key = keyOrdinals.Count == 0
				? $"__row_{i}"
				: BuildKey(dataSet.Rows[i], keyOrdinals, rulesByOrdinal);

			if (!map.TryGetValue(key, out var queue))
			{
				queue = new Queue<int>();
				map[key] = queue;
			}

			queue.Enqueue(i);
		}

		return map;
	}

	private static DiffRow BuildRow(
		TabularDataSet left,
		TabularDataSet right,
		int? leftIndex,
		int? rightIndex,
		int maxColumns,
		IReadOnlyDictionary<int, ColumnComparisonRule> rulesByOrdinal,
		IReadOnlyList<DiffColumn> columns)
	{
		var cells = new List<DiffCell>(maxColumns);
		var hasDifferences = leftIndex is null || rightIndex is null;

		for (var i = 0; i < maxColumns; i++)
		{
			var leftValue = GetCell(left, leftIndex, i);
			var rightValue = GetCell(right, rightIndex, i);
			rulesByOrdinal.TryGetValue(i, out var rule);

			var compareRule = rule ?? new ColumnComparisonRule { Ordinal = i };
			var isDifferent = compareRule.Role != ColumnRole.Ignored && IsDifferent(leftValue, rightValue, compareRule);
			if (isDifferent)
			{
				hasDifferences = true;
				columns[i].HasDifferences = true;
			}

			cells.Add(new DiffCell
			{
				Ordinal = i,
				LeftValue = leftValue,
				RightValue = rightValue,
				IsDifferent = isDifferent
			});
		}

		return new DiffRow
		{
			LeftRowIndex = leftIndex,
			RightRowIndex = rightIndex,
			IsLeftOrphan = rightIndex is null,
			IsRightOrphan = leftIndex is null,
			HasDifferences = hasDifferences,
			Cells = cells
		};
	}

	private static string BuildKey(
		IReadOnlyList<string?> row,
		IReadOnlyList<int> keyOrdinals,
		IReadOnlyDictionary<int, ColumnComparisonRule> rulesByOrdinal)
	{
		return string.Join("\u001f", keyOrdinals.Select(x => Normalize(GetValue(row, x), rulesByOrdinal.GetValueOrDefault(x))));
	}

	private static string? GetCell(TabularDataSet dataSet, int? rowIndex, int ordinal)
	{
		if (rowIndex is null || rowIndex < 0 || rowIndex >= dataSet.Rows.Count)
		{
			return null;
		}

		return GetValue(dataSet.Rows[rowIndex.Value], ordinal);
	}

	private static string? GetValue(IReadOnlyList<string?> row, int ordinal)
	{
		if (ordinal < 0 || ordinal >= row.Count)
		{
			return null;
		}

		return row[ordinal];
	}

	private static bool IsDifferent(string? left, string? right, ColumnComparisonRule rule)
	{
		var leftNormalized = Normalize(left, rule);
		var rightNormalized = Normalize(right, rule);

		var comparison = rule.CaseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
		return !string.Equals(leftNormalized, rightNormalized, comparison);
	}

	private static string Normalize(string? value, ColumnComparisonRule? rule)
	{
		if (value is null)
		{
			return string.Empty;
		}

		if (rule?.IgnoreLeadingAndTrailingWhitespace == true)
		{
			value = value.Trim();
		}

		return value;
	}
}
