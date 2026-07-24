using FuzzySharp;
using VisualDataDiff.Models;
using VisualDataDiff.Services.Abstractions;
using VisualDataDiff.Utilities;

namespace VisualDataDiff.Services;

public sealed class ColumnMatcher : IColumnMatcher
{
	private const int MinimumFuzzyScore = 60;
	private const int AmbiguousMargin = 3;

	public IReadOnlyList<ColumnMatch> Match(IReadOnlyList<TabularColumn> leftColumns, IReadOnlyList<TabularColumn> rightColumns)
	{
		var unmatchedLeft = new HashSet<int>(leftColumns.Select(c => c.Ordinal));
		var unmatchedRight = new HashSet<int>(rightColumns.Select(c => c.Ordinal));
		var results = new List<ColumnMatch>();

		// Step 1: safe exact pass - a normalized name that is unique on BOTH sides is paired directly.
		// A name that appears more than once on either side (duplicate columns) is deliberately excluded
		// here so it still gets a suggested pairing via the fuzzy pass below, flagged as ambiguous.
		// Case-insensitive to match HeaderNameComparer.AreEquivalent's definition of "the same name"
		// already used elsewhere in the app (e.g. the Column Setup mismatch highlight).
		var leftByName = leftColumns
			.GroupBy(c => HeaderNameComparer.Normalize(c.Name), StringComparer.OrdinalIgnoreCase)
			.ToDictionary(g => g.Key, g => g.ToArray(), StringComparer.OrdinalIgnoreCase);
		var rightByName = rightColumns
			.GroupBy(c => HeaderNameComparer.Normalize(c.Name), StringComparer.OrdinalIgnoreCase)
			.ToDictionary(g => g.Key, g => g.ToArray(), StringComparer.OrdinalIgnoreCase);

		foreach (var (name, leftGroup) in leftByName)
		{
			if (leftGroup.Length != 1 || !rightByName.TryGetValue(name, out var rightGroup) || rightGroup.Length != 1)
			{
				continue;
			}

			var leftOrdinal = leftGroup[0].Ordinal;
			var rightOrdinal = rightGroup[0].Ordinal;
			results.Add(new ColumnMatch { LeftOrdinal = leftOrdinal, RightOrdinal = rightOrdinal, Score = 100, IsAmbiguous = false });
			unmatchedLeft.Remove(leftOrdinal);
			unmatchedRight.Remove(rightOrdinal);
		}

		// Step 2: fuzzy pass over everything left unpaired (including duplicate-name groups).
		var leftRemaining = leftColumns.Where(c => unmatchedLeft.Contains(c.Ordinal)).ToArray();
		var rightRemaining = rightColumns.Where(c => unmatchedRight.Contains(c.Ordinal)).ToArray();

		var candidatePairs = new List<(int LeftOrdinal, int RightOrdinal, int Score)>();
		foreach (var leftColumn in leftRemaining)
		{
			foreach (var rightColumn in rightRemaining)
			{
				candidatePairs.Add((leftColumn.Ordinal, rightColumn.Ordinal, Fuzz.WeightedRatio(leftColumn.Name, rightColumn.Name)));
			}
		}

		var secondBestForLeft = ComputeSecondBestScore(candidatePairs, p => p.LeftOrdinal, p => p.Score);
		var secondBestForRight = ComputeSecondBestScore(candidatePairs, p => p.RightOrdinal, p => p.Score);

		var orderedPairs = candidatePairs
			.OrderByDescending(p => p.Score)
			.ThenBy(p => p.LeftOrdinal)
			.ThenBy(p => p.RightOrdinal);

		foreach (var pair in orderedPairs)
		{
			if (pair.Score < MinimumFuzzyScore)
			{
				break; // sorted descending - nothing further clears the floor either
			}

			if (!unmatchedLeft.Contains(pair.LeftOrdinal) || !unmatchedRight.Contains(pair.RightOrdinal))
			{
				continue;
			}

			// Ambiguous if either side had another candidate that was nearly as good as the one we're
			// accepting - this is also what catches the "two columns named FirstName" case, since both
			// duplicates score identically (100) against the single counterpart on the other side.
			var isAmbiguous =
				(secondBestForLeft.TryGetValue(pair.LeftOrdinal, out var secondLeft) && pair.Score - secondLeft <= AmbiguousMargin) ||
				(secondBestForRight.TryGetValue(pair.RightOrdinal, out var secondRight) && pair.Score - secondRight <= AmbiguousMargin);

			results.Add(new ColumnMatch { LeftOrdinal = pair.LeftOrdinal, RightOrdinal = pair.RightOrdinal, Score = pair.Score, IsAmbiguous = isAmbiguous });
			unmatchedLeft.Remove(pair.LeftOrdinal);
			unmatchedRight.Remove(pair.RightOrdinal);
		}

		// Step 3: anything still unclaimed has no real counterpart.
		foreach (var ordinal in unmatchedLeft.OrderBy(x => x))
		{
			results.Add(new ColumnMatch { LeftOrdinal = ordinal, RightOrdinal = null, Score = null, IsAmbiguous = false });
		}

		foreach (var ordinal in unmatchedRight.OrderBy(x => x))
		{
			results.Add(new ColumnMatch { LeftOrdinal = null, RightOrdinal = ordinal, Score = null, IsAmbiguous = false });
		}

		return results;
	}

	private static Dictionary<int, int> ComputeSecondBestScore(
		IReadOnlyList<(int LeftOrdinal, int RightOrdinal, int Score)> pairs,
		Func<(int LeftOrdinal, int RightOrdinal, int Score), int> keySelector,
		Func<(int LeftOrdinal, int RightOrdinal, int Score), int> scoreSelector)
	{
		var result = new Dictionary<int, int>();
		foreach (var group in pairs.GroupBy(keySelector))
		{
			var scores = group.Select(scoreSelector).OrderByDescending(s => s).ToArray();
			result[group.Key] = scores.Length > 1 ? scores[1] : -1;
		}

		return result;
	}
}
