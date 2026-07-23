using System.Globalization;
using CsvHelper;
using CsvHelper.Configuration;
using VisualDataDiff.Models;
using VisualDataDiff.Services.Abstractions;
using VisualDataDiff.Utilities;

namespace VisualDataDiff.Services.Sources;

public sealed class DelimitedTextTabularDataSource : ITabularDataSource
{
	public SourceType SourceType => SourceType.DelimitedText;

	public bool SupportsHeaderOption => true;

	public async Task<TabularDataSet> LoadAsync(SourceConfiguration configuration, CancellationToken cancellationToken)
	{
		if (string.IsNullOrWhiteSpace(configuration.Location))
		{
			throw new InvalidOperationException("No delimited text file path was provided.");
		}

		return await Task.Run(() =>
		{
			cancellationToken.ThrowIfCancellationRequested();

			using var stream = File.Open(configuration.Location, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
			using var streamReader = new StreamReader(stream);

			return ReadAll(
				streamReader,
				configuration.Delimiter,
				configuration.QuoteCharacter,
				configuration.TreatFirstRowAsHeader,
				rowCap: null,
				cancellationToken);
		}, cancellationToken);
	}

	// Shared by LoadAsync and the Configure wizard's live preview (MainWindowViewModel).
	internal static TabularDataSet ReadAll(
		TextReader textReader,
		char delimiter,
		char? quoteCharacter,
		bool treatFirstRowAsHeader,
		int? rowCap,
		CancellationToken cancellationToken)
	{
		var csvConfiguration = BuildConfiguration(delimiter, quoteCharacter);
		using var parser = new CsvParser(textReader, csvConfiguration);

		var rows = new List<IReadOnlyList<string?>>();
		List<TabularColumn>? columns = null;

		while (parser.Read())
		{
			cancellationToken.ThrowIfCancellationRequested();

			var record = parser.Record;
			if (record is null || record.Length == 0)
			{
				continue;
			}

			IReadOnlyList<string?> values = record;

			if (columns is null)
			{
				columns = TabularColumnBuilder.BuildColumns(values, treatFirstRowAsHeader);
				if (treatFirstRowAsHeader)
				{
					continue;
				}
			}

			rows.Add(values);

			if (rowCap is int cap && rows.Count >= cap)
			{
				break;
			}
		}

		columns ??= [];
		return new TabularDataSet { Columns = columns, Rows = rows };
	}

	private static CsvConfiguration BuildConfiguration(char delimiter, char? quoteCharacter)
	{
		return new CsvConfiguration(CultureInfo.InvariantCulture)
		{
			Delimiter = delimiter.ToString(),
			Quote = quoteCharacter ?? '"',
			Mode = quoteCharacter.HasValue ? CsvMode.RFC4180 : CsvMode.NoEscape
		};
	}
}
