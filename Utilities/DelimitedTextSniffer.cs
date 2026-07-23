namespace VisualDataDiff.Utilities;

public static class DelimitedTextSniffer
{
	private const int MaxSampleLines = 25;
	private const int MinSampleLinesForConfidence = 3;
	private const int MaxSampleBytes = 64 * 1024;
	private const double ConfidenceThreshold = 0.7;
	private static readonly char[] Candidates = ['\t', '|'];

	public readonly record struct SniffResult(char Delimiter, double Confidence);

	public static SniffResult? TrySniff(string path)
	{
		List<string> lines;
		try
		{
			lines = ReadSampleLines(path);
		}
		catch (Exception)
		{
			// Sniffing must never throw - worst case, the caller falls back to the Configure wizard.
			return null;
		}

		return TrySniff(lines);
	}

	public static SniffResult? TrySniff(IReadOnlyList<string> sampleLines)
	{
		var nonBlank = sampleLines.Where(l => !string.IsNullOrWhiteSpace(l)).Take(MaxSampleLines).ToArray();
		if (nonBlank.Length < MinSampleLinesForConfidence || nonBlank.Any(LooksBinary))
		{
			return null;
		}

		SniffResult? best = null;
		foreach (var candidate in Candidates)
		{
			var score = ScoreCandidate(nonBlank, candidate);
			if (score >= ConfidenceThreshold && (best is null || score > best.Value.Confidence))
			{
				best = new SniffResult(candidate, score);
			}
		}

		return best;
	}

	private static double ScoreCandidate(IReadOnlyList<string> lines, char delimiter)
	{
		var counts = lines.Select(l => l.Count(c => c == delimiter)).Where(c => c > 0).ToArray();
		if (counts.Length == 0)
		{
			return 0;
		}

		var modal = counts.GroupBy(c => c).OrderByDescending(g => g.Count()).First().Key;
		var matching = counts.Count(c => c == modal);

		return (double)matching / lines.Count;
	}

	private static bool LooksBinary(string line) => line.Any(ch => ch != '\t' && char.IsControl(ch));

	private static List<string> ReadSampleLines(string path)
	{
		using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
		using var reader = new StreamReader(stream);

		var lines = new List<string>();
		var charsRead = 0;

		while (lines.Count < MaxSampleLines * 2 && charsRead < MaxSampleBytes)
		{
			var line = reader.ReadLine();
			if (line is null)
			{
				break;
			}

			charsRead += line.Length;
			lines.Add(line);
		}

		return lines;
	}
}
