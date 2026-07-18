using System.Text.RegularExpressions;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace CasePlanner.Web.Server.Services;

// New document-platform merge engine (build-plan step 1, the thin slice). Extends the proven
// run-splitting-aware strategy from DocumentGenerationEngine.FillDocxTemplate with the one thing
// it doesn't have: {{#Section}}...{{/Section}} blocks that keep, drop, or repeat whole paragraphs
// (or table rows) without ever touching numbering.xml or a field code (SEQ/LISTNUM/etc.) - Word
// recomputes those on its own, so a paragraph that survives untouched keeps its own numbering
// working, and a paragraph that's removed takes its field code with it cleanly.
//
// Sections are non-nested and paragraph-granular only (table-row sections aren't built yet -
// they're a further generalization, not part of build-plan step 2's scope). A section marker
// must occupy its own paragraph, matching the same convention docxtemplater's paragraphLoop mode
// uses, so an attorney types {{#Drainage}} and {{/Drainage}} each on their own line. A section
// name in context.Loops repeats its content once per item (with that item's own fields merged
// in immediately, before the document-wide field pass ever sees it) instead of a plain
// include/exclude toggle - see MergeContext.
public static partial class DocxSectionMerger
{
    [GeneratedRegex(@"\{\{(\w+)\}\}")]
    private static partial Regex FieldRegex();

    [GeneratedRegex(@"^\{\{#(\w+)\}\}$")]
    private static partial Regex SectionOpenRegex();

    [GeneratedRegex(@"^\{\{/(\w+)\}\}$")]
    private static partial Regex SectionCloseRegex();

    // Internal, not private: DocxTemplateLinter's plain-English validation reuses these exact
    // patterns rather than risking a second, subtly different definition of "what a section
    // marker looks like" drifting out of sync with what the merge engine actually does.
    internal static readonly Regex Field = FieldRegex();
    internal static readonly Regex SectionOpen = SectionOpenRegex();
    internal static readonly Regex SectionClose = SectionCloseRegex();

    public static byte[] Render(byte[] templateBytes, MergeContext context, out List<string> missingFields)
    {
        var missing = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        using var stream = new MemoryStream();
        stream.Write(templateBytes, 0, templateBytes.Length);
        stream.Position = 0;

        using (var doc = WordprocessingDocument.Open(stream, true))
        {
            var containers = AllContainers(doc).ToList();

            foreach (var container in containers)
            {
                ResolveSections(container, context, missing);
            }

            foreach (var container in containers)
            {
                var leftover = container.Descendants<Paragraph>()
                    .Select(GetParagraphText)
                    .FirstOrDefault(text => SectionOpen.IsMatch(text) || SectionClose.IsMatch(text));
                if (leftover is not null)
                {
                    throw new InvalidOperationException($"Unresolved section marker: {leftover}");
                }
            }

            foreach (var container in containers)
            {
                foreach (var paragraph in container.Descendants<Paragraph>().ToList())
                {
                    MergeFieldsInParagraph(paragraph, context, missing);
                }
            }

            doc.MainDocumentPart!.Document.Save();
            if (doc.MainDocumentPart.HeaderParts is not null)
            {
                foreach (var header in doc.MainDocumentPart.HeaderParts) header.Header.Save();
            }

            if (doc.MainDocumentPart.FooterParts is not null)
            {
                foreach (var footer in doc.MainDocumentPart.FooterParts) footer.Footer.Save();
            }
        }

        missingFields = missing.ToList();
        return stream.ToArray();
    }

    // Body + every header/footer part - anywhere a section or a merge field can live in a docx.
    private static IEnumerable<OpenXmlCompositeElement> AllContainers(WordprocessingDocument doc)
    {
        var main = doc.MainDocumentPart ?? throw new InvalidOperationException("Document has no main part.");
        yield return main.Document.Body ?? throw new InvalidOperationException("Document has no body.");
        if (main.HeaderParts is not null)
        {
            foreach (var header in main.HeaderParts) yield return header.Header;
        }

        if (main.FooterParts is not null)
        {
            foreach (var footer in main.FooterParts) yield return footer.Footer;
        }
    }

    private static void ResolveSections(OpenXmlCompositeElement container, MergeContext context, SortedSet<string> missing)
    {
        var children = container.ChildElements.ToList();
        var i = 0;
        while (i < children.Count)
        {
            var openMatch = children[i] is Paragraph p ? SectionOpen.Match(GetParagraphText(p)) : Match.Empty;
            if (!openMatch.Success)
            {
                i++;
                continue;
            }

            var name = openMatch.Groups[1].Value;
            var closeIndex = -1;
            for (var j = i + 1; j < children.Count; j++)
            {
                if (children[j] is not Paragraph candidate) continue;
                var closeMatch = SectionClose.Match(GetParagraphText(candidate));
                if (closeMatch.Success && string.Equals(closeMatch.Groups[1].Value, name, StringComparison.OrdinalIgnoreCase))
                {
                    closeIndex = j;
                    break;
                }
            }

            if (closeIndex == -1)
            {
                throw new InvalidOperationException($"Section '{{{{#{name}}}}}' is opened but never closed.");
            }

            var openMarker = children[i];
            var closeMarker = children[closeIndex];
            var content = children.GetRange(i + 1, closeIndex - i - 1);

            if (context.Loops.TryGetValue(name, out var items))
            {
                // Each iteration gets the outer fields overlaid with that item's own fields, so
                // {{name}} inside the loop body resolves per-item while {{CaseNumber}} and every
                // other outer field still resolves the same way it would anywhere else in the
                // document. The per-item merge happens now, on the clone, before this content is
                // even part of the tree the document-wide field pass will later walk - by the
                // time that pass reaches these paragraphs their tokens are already gone, so it's
                // a safe no-op for them.
                foreach (var item in items)
                {
                    var iterationFields = new Dictionary<string, string>(context.Fields, StringComparer.OrdinalIgnoreCase);
                    foreach (var (key, value) in item) iterationFields[key] = value;
                    var iterationContext = new MergeContext(iterationFields, context.SelectedSections, context.Loops);

                    foreach (var node in content)
                    {
                        var clone = node.CloneNode(true);
                        container.InsertBefore(clone, openMarker);
                        if (clone is Paragraph clonedParagraph)
                        {
                            MergeFieldsInParagraph(clonedParagraph, iterationContext, missing);
                        }
                    }
                }

                openMarker.Remove();
                foreach (var node in content) node.Remove();
                closeMarker.Remove();
            }
            else if (context.SelectedSections.Contains(name))
            {
                openMarker.Remove();
                closeMarker.Remove();
            }
            else
            {
                openMarker.Remove();
                foreach (var node in content) node.Remove();
                closeMarker.Remove();
            }

            i = closeIndex + 1;
        }
    }

    // Word routinely splits one typed "{{Field}}" across multiple runs (autocorrect, spellcheck,
    // mid-typing formatting changes), so a token can't be found by looking at one run's text in
    // isolation - this combines every Text run in the paragraph into one string, finds each
    // {{field}} match's position in that combined string, then maps each match back onto exactly
    // the run(s) it overlaps. Only those runs are rewritten: the resolved value is spliced into
    // whichever run the match *starts* in, any other runs the match spans are trimmed down to
    // whatever they contain outside the match, and every run with no overlap at all - including
    // ones after the token in the same paragraph - keeps its original text and formatting
    // completely untouched. (An earlier version of this method wrote the whole paragraph's
    // combined text into the first run and blanked the rest, which silently discarded formatting
    // on any trailing run - fixed here.)
    private static void MergeFieldsInParagraph(Paragraph paragraph, MergeContext context, SortedSet<string> missing)
    {
        var texts = paragraph.Descendants<Text>().ToList();
        if (texts.Count == 0) return;

        var combined = string.Concat(texts.Select(t => t.Text));
        var matches = Field.Matches(combined);
        if (matches.Count == 0) return;

        var replacements = matches.Select(match =>
        {
            var rawTag = match.Groups[1].Value;
            if (context.Fields.TryGetValue(rawTag, out var value) && !string.IsNullOrWhiteSpace(value))
            {
                return ApplyTagCase(rawTag, value);
            }

            missing.Add(rawTag);
            return $"[MISSING: {rawTag}]";
        }).ToList();

        var runStart = 0;
        foreach (var text in texts)
        {
            var runText = text.Text;
            var runEnd = runStart + runText.Length;

            var overlapsAnyMatch = matches.Any(match => match.Index < runEnd && match.Index + match.Length > runStart);
            if (!overlapsAnyMatch)
            {
                runStart = runEnd;
                continue;
            }

            var sb = new System.Text.StringBuilder();
            var cursor = runStart;
            foreach (var (match, replacement) in matches.Zip(replacements))
            {
                var matchStart = match.Index;
                var matchEnd = match.Index + match.Length;
                if (matchEnd <= runStart || matchStart >= runEnd) continue;

                var overlapStart = Math.Max(matchStart, cursor);
                if (overlapStart > cursor)
                {
                    sb.Append(runText.AsSpan(cursor - runStart, overlapStart - cursor));
                    cursor = overlapStart;
                }

                if (matchStart >= runStart && matchStart < runEnd)
                {
                    sb.Append(replacement);
                }

                cursor = Math.Min(matchEnd, runEnd);
            }

            if (cursor < runEnd)
            {
                sb.Append(runText.AsSpan(cursor - runStart));
            }

            text.Text = sb.ToString();
            text.Space = SpaceProcessingModeValues.Preserve;
            runStart = runEnd;
        }
    }

    // A tag typed in all caps (inside an all-caps caption line, the standard legal-caption
    // convention) is a signal to render the value in caps there too - confirmed against a real
    // attorney-authored template where {{COUNTY}} appears next to {{County}} in the same document,
    // each deliberately cased to match its surroundings. A tag with no letters at all (can't be
    // upper or lower) is left alone.
    private static string ApplyTagCase(string rawTag, string value)
    {
        if (!rawTag.Any(char.IsLetter)) return value;
        var upper = rawTag.ToUpperInvariant();
        var lower = rawTag.ToLowerInvariant();
        if (rawTag == upper && rawTag != lower) return value.ToUpperInvariant();
        if (rawTag == lower && rawTag != upper) return value.ToLowerInvariant();
        return value;
    }

    internal static string GetParagraphText(Paragraph paragraph) =>
        string.Concat(paragraph.Descendants<Text>().Select(t => t.Text)).Trim();
}
