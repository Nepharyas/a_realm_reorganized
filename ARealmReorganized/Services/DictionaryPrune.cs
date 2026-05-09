using System;
using System.Collections.Generic;

namespace ARealmReorganized.Services;

internal static class DictionaryPrune
{
    // Removes every key matching the predicate. Returns the number of removed entries.
    // Two passes are intentional: mutating a Dictionary while iterating its Keys throws
    // InvalidOperationException, so we collect the matching keys first, then remove them.
    public static int RemoveKeysWhere<TKey, TValue>(
        Dictionary<TKey, TValue> dict,
        Func<TKey, bool> shouldRemove) where TKey : notnull
    {
        var toRemove = new List<TKey>();
        foreach (var key in dict.Keys)
            if (shouldRemove(key)) toRemove.Add(key);
        foreach (var key in toRemove) dict.Remove(key);
        return toRemove.Count;
    }
}
