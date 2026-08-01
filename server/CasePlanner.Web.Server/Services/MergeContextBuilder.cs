namespace CasePlanner.Web.Server.Services;

// Thin-slice version: normalizes whatever fields/sections/loops a caller already assembled into
// a MergeContext. Deriving fields from CaseRecord/OrgDefaults and sections from case issue tags
// plus the attorney's generation-time checklist selection is data-model-cutover work (build-plan
// step 3) - this only owns the normalization rule (case-insensitive keys) so DocxSectionMerger
// never has to think about where a field, section selection, or loop's items actually came from.
public static class MergeContextBuilder
{
    public static MergeContext Build(
        IReadOnlyDictionary<string, string> fields,
        IEnumerable<string> selectedSectionKeys,
        IReadOnlyDictionary<string, IReadOnlyList<IReadOnlyDictionary<string, string>>>? loops = null)
    {
        var normalizedFields = new Dictionary<string, string>(fields, StringComparer.OrdinalIgnoreCase);
        var normalizedSections = new HashSet<string>(selectedSectionKeys, StringComparer.OrdinalIgnoreCase);

        var normalizedLoops = new Dictionary<string, IReadOnlyList<IReadOnlyDictionary<string, string>>>(StringComparer.OrdinalIgnoreCase);
        if (loops is not null)
        {
            foreach (var (name, items) in loops)
            {
                normalizedLoops[name] = items
                    .Select(item => (IReadOnlyDictionary<string, string>)new Dictionary<string, string>(item, StringComparer.OrdinalIgnoreCase))
                    .ToList();
            }
        }

        return new MergeContext(normalizedFields, normalizedSections, normalizedLoops);
    }
}
