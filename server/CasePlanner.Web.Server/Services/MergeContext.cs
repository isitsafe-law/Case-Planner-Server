namespace CasePlanner.Web.Server.Services;

// The flat, resolved input to a document merge: case/org/runtime-input fields, which named
// {{#Section}} blocks the attorney has checked in for this generation, and which named
// {{#Section}} blocks are list loops (one repetition per item, e.g. one per landowner) rather
// than a plain include/exclude toggle. Field lookup is case-insensitive (see DocxSectionMerger)
// because real templates use tag casing as a formatting signal, not just a name - e.g.
// {{COUNTY}} inside an all-caps caption line means "render the value in caps here too."
//
// A name in Loops takes precedence over the same name in SelectedSections - a template author
// only ever puts a given section name in one or the other, so this only matters as a documented
// tie-breaker, not a real conflict callers need to think about.
public sealed class MergeContext
{
    public IReadOnlyDictionary<string, string> Fields { get; }
    public IReadOnlySet<string> SelectedSections { get; }
    public IReadOnlyDictionary<string, IReadOnlyList<IReadOnlyDictionary<string, string>>> Loops { get; }
    public CaseStyleFormatting? CaseStyleFormatting { get; }

    public MergeContext(
        IReadOnlyDictionary<string, string> fields,
        IReadOnlySet<string> selectedSections,
        IReadOnlyDictionary<string, IReadOnlyList<IReadOnlyDictionary<string, string>>>? loops = null,
        CaseStyleFormatting? caseStyleFormatting = null)
    {
        Fields = fields;
        SelectedSections = selectedSections;
        Loops = loops ?? new Dictionary<string, IReadOnlyList<IReadOnlyDictionary<string, string>>>(StringComparer.OrdinalIgnoreCase);
        CaseStyleFormatting = caseStyleFormatting;
    }
}

public sealed class CaseStyleFormatting
{
    public bool Bold { get; set; } = true;
    public string Alignment { get; set; } = "center";
    public int FontSize { get; set; } = 12;
}
