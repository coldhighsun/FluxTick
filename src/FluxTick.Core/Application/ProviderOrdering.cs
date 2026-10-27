namespace FluxTick.Core.Application;

/// <summary>
/// Puts the data sources in the order the user arranged them.
/// </summary>
public static class ProviderOrdering
{
    /// <summary>
    /// Sorts provider ids by the saved order. An id that is not in the saved order, such as a provider added by an
    /// update, comes after those that are, in the order it was given; an id in the saved order that is not given is ignored.
    /// </summary>
    /// <param name="ids">
    /// The ids of the available providers.
    /// </param>
    /// <param name="saved">
    /// The saved order.
    /// </param>
    /// <returns>
    /// The ids in display order.
    /// </returns>
    public static List<string> Sort(IEnumerable<string> ids, IReadOnlyList<string> saved)
    {
        var rest = ids.Distinct().ToList();
        var sorted = new List<string>(rest.Count);
        foreach (var id in saved)
        {
            if (rest.Remove(id))
            {
                sorted.Add(id);
            }
        }

        sorted.AddRange(rest);
        return sorted;
    }
}
