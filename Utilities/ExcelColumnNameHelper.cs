namespace VisualDataDiff.Utilities;

public static class ExcelColumnNameHelper
{
	public static string ToColumnName(int index)
	{
		var ordinal = index + 1;
		var chars = new Stack<char>();

		while (ordinal > 0)
		{
			var rem = (ordinal - 1) % 26;
			chars.Push((char)('A' + rem));
			ordinal = (ordinal - 1) / 26;
		}

		return new string(chars.ToArray());
	}
}
