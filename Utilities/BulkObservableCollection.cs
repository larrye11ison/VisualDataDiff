using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace VisualDataDiff.Utilities;

/// <summary>
/// ObservableCollection that replaces its entire contents with a single Reset
/// notification instead of a Clear + per-item Add sequence. Large row/column
/// rebuilds must go through this so bound UI controls don't process thousands
/// of individual change notifications.
/// </summary>
public sealed class BulkObservableCollection<T> : ObservableCollection<T>
{
	public void ReplaceAll(IEnumerable<T> items)
	{
		Items.Clear();
		foreach (var item in items)
		{
			Items.Add(item);
		}

		OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
		OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
		OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
	}
}
