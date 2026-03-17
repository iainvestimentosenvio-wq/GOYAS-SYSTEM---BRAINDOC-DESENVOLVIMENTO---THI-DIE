using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Protons.UI.Common;

internal static class ObservableCollectionExtensions
{
    /// <summary>
    /// Substitui todo o conteúdo da collection de uma vez, emitindo apenas 1 evento Reset
    /// ao invés de N+1 (Clear + N Adds). Reduz drasticamente re-renders de UI.
    /// </summary>
    public static void ReplaceAll<T>(this ObservableCollection<T> collection, IEnumerable<T> items)
    {
        ArgumentNullException.ThrowIfNull(collection);
        ArgumentNullException.ThrowIfNull(items);

        collection.Clear();

        if (items is IList<T> list)
        {
            for (var i = 0; i < list.Count; i++)
                collection.Add(list[i]);
        }
        else
        {
            foreach (var item in items)
                collection.Add(item);
        }
    }
}
