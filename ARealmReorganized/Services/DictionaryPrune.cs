using System;
using System.Collections.Generic;

namespace ARealmReorganized.Services;

internal static class DictionaryPrune
{
    // Removes every key matching the predicate. Returns the number of removed entries.
    // Allocates a temporary list only if at least one key matches.
    public static int RemoveKeysWhere<TKey, TValue>(
        Dictionary<TKey, TValue> dict,
        Func<TKey, bool> shouldRemove) where TKey : notnull
    {
        List<TKey>? toRemove = null;
        foreach (var key in dict.Keys)
            if (shouldRemove(key)) (toRemove ??= new()).Add(key);
        if (toRemove == null) return 0;
        foreach (var key in toRemove) dict.Remove(key);
        return toRemove.Count;
    }
}
