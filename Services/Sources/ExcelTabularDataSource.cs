using System.Text;
using ExcelDataReader;
using VisualDataDiff.Models;
using VisualDataDiff.Services.Abstractions;
using VisualDataDiff.Utilities;

namespace VisualDataDiff.Services.Sources;

public sealed class ExcelTabularDataSource : ITabularDataSource
{
	private static int _encodingRegistered;

	public SourceType SourceType => SourceType.Excel;

	public bool SupportsHeaderOption => true;

	public async Task<TabularDataSet> LoadAsync(SourceConfiguration configuration, CancellationToken cancellationToken)
	{
		if (string.IsNullOrWhiteSpace(configuration.Location))
		{
			throw new InvalidOperationException("No Excel file path was provided.");
		}

		EnsureEncodingsRegistered();

		return await Task.Run(() =>
		{
			cancellationToken.ThrowIfCancellationRequested();

			using var stream = File.Open(configuration.Location, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
			using var reader = ExcelReaderFactory.CreateReader(stream);

			var rows = new List<IReadOnlyList<string?>>();
			List<TabularColumn>? columns = null;

			do
			{
				while (reader.Read())
				{
					cancellationToken.ThrowIfCancellationRequested();

					var fieldCount = reader.FieldCount;
					if (fieldCount == 0)
					{
						continue;
					}

					var values = new string?[fieldCount];
					for (var i = 0; i < fieldCount; i++)
					{
						values[i] = reader.GetValue(i)?.ToString();
					}

					if (columns is null)
					{
						columns = TabularColumnBuilder.BuildColumns(values, configuration.TreatFirstRowAsHeader);
						if (configuration.TreatFirstRowAsHeader)
						{
							continue;
						}
					}

					rows.Add(values);
				}

				if (columns is not null || rows.Count > 0)
				{
					break;
				}
			}
			while (reader.NextResult());

			columns ??= [];

			return new TabularDataSet
			{
				Columns = columns,
				Rows = rows
			};
		}, cancellationToken);
	}

	private static void EnsureEncodingsRegistered()
	{
		if (Interlocked.Exchange(ref _encodingRegistered, 1) == 0)
		{
			Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
		}
	}
}
