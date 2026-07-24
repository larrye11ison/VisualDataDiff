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
		var columns = new List<DiffColumn>(columnRules.Count);
		for (var slot = 0; slot < columnRules.Count; slot++)
		{
			var rule = columnRules[slot];
			var name = rule.LeftOrdinal is int lo && lo >= 0 && lo < left.Columns.Count
				? left.Columns[lo].Name
				: rule.RightOrdinal is int ro && ro >= 0 && ro < right.Columns.Count
					? right.Columns[ro].Name
					: ExcelColumnNameHelper.ToColumnName(slot);

			columns.Add(new DiffColumn
			{
				Ordinal = slot,
				Name = name,
				HasDifferences = false
			});
		}

		// A Key column must be mapped on both sides to be usable for matching rows at all - a column
		// that only exists on one side can't pair rows between the two datasets. The UI is responsible
		// for not letting the user mark a one-sided column as Key; this filter is just a defensive
		// backstop so a stray unmapped Key rule is silently excluded from row-matching rather than
		// throwing on the forced-unwrap of a null ordinal.
		var keyRules = columnRules
			.Where(x => x.Role == ColumnRole.Key && x.LeftOrdinal is not null && x.RightOrdinal is not null)
			.ToArray();

		var leftKeyOrdinals = keyRules.Select(x => x.LeftOrdinal!.Value).ToArray();
		var rightKeyOrdinals = keyRules.Select(x => x.RightOrdinal!.Value).ToArray();

		var leftMap = BuildKeyMap(left, leftKeyOrdinals, keyRules, cancellationToken);
		var rightMap = BuildKeyMap(right, rightKeyOrdinals, keyRules, cancellationToken);

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
				rows.Add(BuildRow(left, right, leftIndex, rightIndex, columnRules, columns));
			}

			while (leftIndices.Count > 0)
			{
				var leftIndex = leftIndices.Dequeue();
				rows.Add(BuildRow(left, right, leftIndex, null, columnRules, columns));
			}

			while (rightIndices.Count > 0)
			{
				var rightIndex = rightIndices.Dequeue();
				rows.Add(BuildRow(left, right, null, rightIndex, columnRules, columns));
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
		IReadOnlyList<int> keyOrdinalsForThisSide,
		IReadOnlyList<ColumnComparisonRule> keyRules,
		CancellationToken cancellationToken)
	{
		var map = new Dictionary<string, Queue<int>>(StringComparer.Ordinal);

		for (var i = 0; i < dataSet.Rows.Count; i++)
		{
			cancellationToken.ThrowIfCancellationRequested();

			var key = keyOrdinalsForThisSide.Count == 0
				? $"__row_{i}"
				: BuildKey(dataSet.Rows[i], keyOrdinalsForThisSide, keyRules);

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
		IReadOnlyList<ColumnComparisonRule> columnRules,
		IReadOnlyList<DiffColumn> columns)
	{
		var cells = new List<DiffCell>(columnRules.Count);
		var hasDifferences = leftIndex is null || rightIndex is null;

		for (var slot = 0; slot < columnRules.Count; slot++)
		{
			var rule = columnRules[slot];
			var leftValue = rule.LeftOrdinal is int lo ? GetCell(left, leftIndex, lo) : null;
			var rightValue = rule.RightOrdinal is int ro ? GetCell(right, rightIndex, ro) : null;

			var isDifferent = rule.Role != ColumnRole.Ignored && IsDifferent(leftValue, rightValue, rule);
			if (isDifferent)
			{
				hasDifferences = true;
				columns[slot].HasDifferences = true;
			}

			cells.Add(new DiffCell
			{
				Ordinal = slot,
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
		IReadOnlyList<int> keyOrdinalsForThisSide,
		IReadOnlyList<ColumnComparisonRule> keyRules)
	{
		return string.Join(
			"",
			keyOrdinalsForThisSide.Select((ordinal, index) => Normalize(GetValue(row, ordinal), keyRules[index])));
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
