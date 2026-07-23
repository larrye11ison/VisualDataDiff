using VisualDataDiff.Models;

namespace VisualDataDiff.Utilities;

public static class TabularColumnBuilder
{
	public static List<TabularColumn> BuildColumns(IReadOnlyList<string?> firstRow, bool useHeader)
	{
		var columns = new List<TabularColumn>(firstRow.Count);

		for (var i = 0; i < firstRow.Count; i++)
		{
			var name = useHeader ? firstRow[i] : null;
			if (string.IsNullOrWhiteSpace(name))
			{
				name = ExcelColumnNameHelper.ToColumnName(i);
			}

			columns.Add(new TabularColumn(i, name));
		}

		return columns;
	}
}
