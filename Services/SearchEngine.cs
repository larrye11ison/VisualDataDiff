using System.Text.RegularExpressions;
using VisualDataDiff.Models;
using VisualDataDiff.Services.Abstractions;

namespace VisualDataDiff.Services;

public sealed class SearchEngine : ISearchEngine
{
	private static readonly IReadOnlySet<int> EmptyOrdinalSet = new HashSet<int>();
	private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(2);

	public async Task<SearchResult> SearchAsync(
		DiffResult diffResult,
		SearchOptions options,
		CancellationToken cancellationToken)
	{
		return await Task.Run(() => SearchCore(diffResult, options, cancellationToken), cancellationToken);
	}

	private static SearchResult SearchCore(DiffResult diffResult, SearchOptions options, CancellationToken cancellationToken)
	{
		if (string.IsNullOrEmpty(options.QueryText))
		{
			return EmptyResult(hasQuery: false, hasRegexError: false);
		}

		Func<string?, bool> isMatch;
		if (options.UseRegex)
		{
			Regex regex;
			try
			{
				regex = new Regex(
					options.QueryText,
					options.CaseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase,
					RegexTimeout);
			}
			catch (ArgumentException)
			{
				return EmptyResult(hasQuery: true, hasRegexError: true);
			}

			isMatch = value => value is not null && SafeIsMatch(regex, value);
		}
		else
		{
			var comparison = options.CaseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
			var query = options.QueryText;
			isMatch = value => value is not null && value.Contains(query, comparison);
		}

		var leftCounts = new Dictionary<int, int>();
		var rightCounts = new Dictionary<int, int>();
		var rowMatches = new Dictionary<DiffRow, SearchRowMatch>();
		var columnNames = diffResult.Columns.ToDictionary(x => x.Ordinal, x => x.Name);

		foreach (var row in diffResult.Rows)
		{
			cancellationToken.ThrowIfCancellationRequested();

			HashSet<int>? leftOrdinals = null;
			HashSet<int>? rightOrdinals = null;

			foreach (var cell in row.Cells)
			{
				if (isMatch(cell.LeftValue))
				{
					(leftOrdinals ??= []).Add(cell.Ordinal);
					leftCounts[cell.Ordinal] = leftCounts.GetValueOrDefault(cell.Ordinal) + 1;
				}

				if (isMatch(cell.RightValue))
				{
					(rightOrdinals ??= []).Add(cell.Ordinal);
					rightCounts[cell.Ordinal] = rightCounts.GetValueOrDefault(cell.Ordinal) + 1;
				}
			}

			if (leftOrdinals is not null || rightOrdinals is not null)
			{
				rowMatches[row] = new SearchRowMatch
				{
					Row = row,
					LeftMatchedOrdinals = (IReadOnlySet<int>?)leftOrdinals ?? EmptyOrdinalSet,
					RightMatchedOrdinals = (IReadOnlySet<int>?)rightOrdinals ?? EmptyOrdinalSet
				};
			}
		}

		var columnMatches = leftCounts.Keys
			.Union(rightCounts.Keys)
			.OrderBy(x => x)
			.Select(ordinal => new SearchColumnMatch
			{
				Ordinal = ordinal,
				ColumnName = columnNames.GetValueOrDefault(ordinal, $"Column {ordinal}"),
				LeftCount = leftCounts.GetValueOrDefault(ordinal),
				RightCount = rightCounts.GetValueOrDefault(ordinal)
			})
			.ToArray();

		return new SearchResult
		{
			ColumnMatches = columnMatches,
			RowMatches = rowMatches,
			HasRegexError = false,
			HasQuery = true
		};
	}

	private static bool SafeIsMatch(Regex regex, string value)
	{
		try
		{
			return regex.IsMatch(value);
		}
		catch (RegexMatchTimeoutException)
		{
			return false;
		}
	}

	private static SearchResult EmptyResult(bool hasQuery, bool hasRegexError)
	{
		return new SearchResult
		{
			ColumnMatches = [],
			RowMatches = new Dictionary<DiffRow, SearchRowMatch>(),
			HasRegexError = hasRegexError,
			HasQuery = hasQuery
		};
	}
}
