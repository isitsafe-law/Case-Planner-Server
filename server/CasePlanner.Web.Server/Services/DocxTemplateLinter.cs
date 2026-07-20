using System.Text.RegularExpressions;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace CasePlanner.Web.Server.Services;

// Upload-time validation for a template before it's ever used to generate a real document.
// Reports every issue it finds in plain English rather than stopping at the first one - an
// attorney reviewing an upload needs the whole list, not one cryptic error at a time. Reuses
// DocxSectionMerger's exact field/section-marker patterns (see its internal Field/SectionOpen/
// SectionClose/GetParagraphText) so "what counts as a marker" can't drift between what this
// linter checks and what the merge engine actually does.
public static class DocxTemplateLinter
{
    public static IReadOnlyList<string> Validate(byte[] templateBytes, IReadOnlySet<string>? knownFields = null)
    {
        var issues = new List<string>();
        using var stream = new MemoryStream(templateBytes);
        using var doc = WordprocessingDocument.Open(stream, false);
        var main = doc.MainDocumentPart ?? throw new InvalidOperationException("Document has no main part.");

        var containers = new List<(string Label, OpenXmlCompositeElement Container)>
        {
            ("the document body", main.Document.Body ?? throw new InvalidOperationException("Document has no body.")),
        };
        if (main.HeaderParts is not null)
        {
            var n = 0;
            foreach (var header in main.HeaderParts) containers.Add(($"header {++n}", header.Header));
        }

        if (main.FooterParts is not null)
        {
            var n = 0;
            foreach (var footer in main.FooterParts) containers.Add(($"footer {++n}", footer.Footer));
        }

        foreach (var (label, container) in containers)
        {
            CheckSectionBalance(container, label, issues);
            CheckFieldsAndStrayBraces(container, label, knownFields, issues);
        }

        return issues;
    }

    // Upload-time auto-registration (see CasePlannerRepository.DocumentPlatform.UploadDocumentTemplateAsync)
    // needs the same "what counts as a section" answer as CheckSectionBalance above, so this walks
    // the exact same containers/top-level-paragraphs and reuses DocxSectionMerger.SectionOpen rather
    // than re-deriving the marker shape. Keys are returned in first-appearance order, de-duplicated,
    // regardless of whether the block is well-formed (a caller only wants "what block keys does this
    // file mention" - balance problems are still reported separately by Validate above and block the
    // upload before this would ever be consulted for a broken file).
    public static IReadOnlyList<string> ExtractSectionKeys(byte[] templateBytes)
    {
        var keys = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        using var stream = new MemoryStream(templateBytes);
        using var doc = WordprocessingDocument.Open(stream, false);
        var main = doc.MainDocumentPart ?? throw new InvalidOperationException("Document has no main part.");

        var containers = new List<OpenXmlCompositeElement>
        {
            main.Document.Body ?? throw new InvalidOperationException("Document has no body."),
        };
        if (main.HeaderParts is not null)
        {
            foreach (var header in main.HeaderParts) containers.Add(header.Header);
        }

        if (main.FooterParts is not null)
        {
            foreach (var footer in main.FooterParts) containers.Add(footer.Footer);
        }

        foreach (var container in containers)
        {
            foreach (var paragraph in container.ChildElements.OfType<Paragraph>())
            {
                var text = DocxSectionMerger.GetParagraphText(paragraph);
                var openMatch = DocxSectionMerger.SectionOpen.Match(text);
                if (!openMatch.Success) continue;

                var name = openMatch.Groups[1].Value;
                if (seen.Add(name)) keys.Add(name);
            }
        }

        return keys;
    }

    // Walks each container's own top-level paragraphs as a simple stack: an open marker pushes,
    // a close marker pops (or reports why it can't). Anything left on the stack at the end is
    // unclosed. Sections can't nest in this engine, so a push while something's already open is
    // reported too - the merge engine would otherwise fail later with a much less specific
    // "unresolved section marker" error once it can't find a same-named close for the outer one.
    private static void CheckSectionBalance(OpenXmlCompositeElement container, string label, List<string> issues)
    {
        var stack = new List<string>();
        foreach (var paragraph in container.ChildElements.OfType<Paragraph>())
        {
            var text = DocxSectionMerger.GetParagraphText(paragraph);

            var openMatch = DocxSectionMerger.SectionOpen.Match(text);
            if (openMatch.Success)
            {
                var name = openMatch.Groups[1].Value;
                if (stack.Count > 0)
                {
                    issues.Add($"In {label}: section '{{{{#{name}}}}}' opens inside '{{{{#{stack[^1]}}}}}' — sections can't be nested yet; close '{{{{/{stack[^1]}}}}}' first.");
                }

                stack.Add(name);
                continue;
            }

            var closeMatch = DocxSectionMerger.SectionClose.Match(text);
            if (!closeMatch.Success) continue;

            var closeName = closeMatch.Groups[1].Value;
            if (stack.Count == 0)
            {
                issues.Add($"In {label}: '{{{{/{closeName}}}}}' closes a section that was never opened.");
            }
            else if (!string.Equals(stack[^1], closeName, StringComparison.OrdinalIgnoreCase))
            {
                issues.Add($"In {label}: '{{{{/{closeName}}}}}' doesn't match the most recently opened section '{{{{#{stack[^1]}}}}}'.");
            }
            else
            {
                stack.RemoveAt(stack.Count - 1);
            }
        }

        foreach (var unclosed in stack)
        {
            issues.Add($"In {label}: section '{{{{#{unclosed}}}}}' is opened but never closed.");
        }
    }

    // Runs over every paragraph anywhere in the container (including inside tables, unlike the
    // section-balance check above, since a plain merge field can legally live in a table cell
    // even though a section marker can't yet). Flags a field name the caller doesn't recognize,
    // and separately flags a stray "{" or "}" left over once every valid field/section match is
    // stripped out - the signature of a token whose "{{"/"}}" landed on opposite sides of a
    // paragraph break, which the merge engine (working paragraph-by-paragraph) can never
    // reassemble.
    private static void CheckFieldsAndStrayBraces(OpenXmlCompositeElement container, string label, IReadOnlySet<string>? knownFields, List<string> issues)
    {
        foreach (var paragraph in container.Descendants<Paragraph>())
        {
            var text = DocxSectionMerger.GetParagraphText(paragraph);
            if (text.Length == 0) continue;
            if (DocxSectionMerger.SectionOpen.IsMatch(text) || DocxSectionMerger.SectionClose.IsMatch(text)) continue;

            if (knownFields is not null)
            {
                foreach (Match match in DocxSectionMerger.Field.Matches(text))
                {
                    var name = match.Groups[1].Value;
                    if (!knownFields.Contains(name))
                    {
                        issues.Add($"In {label}: unknown field '{{{{{name}}}}}' — check the spelling, or add it to the merge field catalog if it's new.");
                    }
                }
            }

            var stripped = DocxSectionMerger.Field.Replace(text, "");
            if (stripped.Contains('{') || stripped.Contains('}'))
            {
                var excerpt = text.Length > 80 ? text[..80] + "..." : text;
                issues.Add($"In {label}: possible broken merge tag near \"{excerpt}\" — check whether Word split a token across a paragraph break, or a stray brace was typed.");
            }
        }
    }
}
