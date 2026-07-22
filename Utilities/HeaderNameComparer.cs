namespace VisualDataDiff.Utilities;

public static class HeaderNameComparer
{
	public static bool AreEquivalent(string? left, string? right) =>
		string.Equals(Normalize(left), Normalize(right), StringComparison.OrdinalIgnoreCase);

	public static string Normalize(string? name)
	{
		if (string.IsNullOrEmpty(name))
		{
			return string.Empty;
		}

		return string.Concat(name.Where(c => !char.IsWhiteSpace(c)));
	}
}
