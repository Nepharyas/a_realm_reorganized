using System;
using System.Collections.Generic;
using System.Linq;

namespace ARealmReorganized.Services;

internal static class DictionaryPrune
{
    // Removes every key matching the predicate. Returns the number of removed entries.
    // ToList materializes the keys before the removal loop — mutating a Dictionary while
    // iterating its Keys throws InvalidOperationException.
    public static int RemoveKeysWhere<TKey, TValue>(
        Dictionary<TKey, TValue> dict,
        Func<TKey, bool> shouldRemove) where TKey : notnull
    {
        var keysToRemove = dict.Keys.Where(shouldRemove).ToList();
        foreach (var key in keysToRemove) dict.Remove(key);
        return keysToRemove.Count;
    }
}
