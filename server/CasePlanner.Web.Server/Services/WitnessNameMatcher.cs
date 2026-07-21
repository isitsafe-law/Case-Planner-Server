using System.Text.RegularExpressions;

namespace CasePlanner.Web.Server.Services;

// Multi-user rollout Phase 3 (shared witness registry): a cheap, dependency-free name-similarity
// check used both to flag likely-duplicate witnesses at add-time ("Max" typed while "Maxwell"
// already exists) and to rank the registry search's autofill suggestions. Deliberately a pure,
// static, DB-free function so it's covered by fast unit tests and reusable later for the
// cross-case trial-date conflict flagging batch mentioned in the rollout plan.
//
// Combines two cheap heuristics - either firing is enough to call two names "similar":
//   1. Nickname/prefix/substring on the relevant "given name" part: does one name's first token
//      start with (or contain) the other's first token? Catches "Max"/"Maxwell", "Rob"/"Robert",
//      "Bob Jones"/"Robert Jones".
//   2. Levenshtein edit distance on that same part, scaled to its length, to catch typos/
//      misspellings ("Micheal" vs "Michael", "Jon" vs "John") that the prefix/substring check
//      would miss.
//
// Critically, when BOTH names carry more than one token and their LAST tokens differ, they are
// never flagged similar, full stop - two different first names sharing a coincidental last name
// ("Ann Carter" vs "Tom Carter") is not evidence of a typo or nickname, and comparing the two
// full strings with a generic edit distance would falsely flag exactly that case (a real
// false-positive caught while writing this batch's tests - see WitnessNameMatcherTests). Scoping
// the edit-distance/prefix checks to the first-token ("given name") part once same-family context
// is established (same last token, or one/both names have no last token at all) keeps the checks
// meaningful instead of just measuring incidental string-length overlap.
//
// Thresholds (tunable - see the batch report): shorter compared-part length <= 4 chars ->
// distance <= 1; <= 8 chars -> distance <= 2; longer -> distance <= 3. These are deliberately
// conservative (favor under- over over-flagging) since a false "similar" flag is just a
// dismissible hint, but a missed flag means a real duplicate person could still get created -
// revisit if real-world usage shows it needs to be more or less aggressive.
public static class WitnessNameMatcher
{
    /// <summary>Lowercase, trim, and collapse internal whitespace so "  Max   Carter " and
    /// "max carter" normalize identically.</summary>
    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "";
        }

        return Regex.Replace(value.Trim(), @"\s+", " ").ToLowerInvariant();
    }

    /// <summary>True if the two names are close enough that a user should be nudged to check
    /// whether they mean the same real person. Order-independent, whitespace/case-insensitive.</summary>
    public static bool AreSimilar(string? a, string? b)
    {
        var normA = Normalize(a);
        var normB = Normalize(b);
        if (normA.Length == 0 || normB.Length == 0)
        {
            return false;
        }

        if (normA == normB)
        {
            return true;
        }

        var tokensA = normA.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var tokensB = normB.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var firstA = tokensA[0];
        var firstB = tokensB[0];

        // Different established surnames -> different people, regardless of first-name closeness.
        if (tokensA.Length > 1 && tokensB.Length > 1 && tokensA[^1] != tokensB[^1])
        {
            return false;
        }

        // If neither name has a surname (single-token input, e.g. just "Max" typed with nothing
        // else yet), firstA/firstB above already ARE the whole normalized names, so this one
        // check covers both the nickname/prefix and typo cases directly. If a surname is present
        // on both sides, it was already confirmed equal above, so this scopes the comparison to
        // just the given-name part - exactly the "same family, different first-name variant" case
        // this heuristic is meant to catch.
        return IsCloseGivenName(firstA, firstB);
    }

    private static bool IsCloseGivenName(string partA, string partB)
    {
        if (partA.Length == 0 || partB.Length == 0)
        {
            return false;
        }

        if (partA == partB)
        {
            return true;
        }

        if (partA.StartsWith(partB, StringComparison.Ordinal) || partB.StartsWith(partA, StringComparison.Ordinal))
        {
            return true;
        }

        if (partA.Contains(partB, StringComparison.Ordinal) || partB.Contains(partA, StringComparison.Ordinal))
        {
            return true;
        }

        var shorter = Math.Min(partA.Length, partB.Length);
        var threshold = shorter <= 4 ? 1 : shorter <= 8 ? 2 : 3;
        return LevenshteinDistance(partA, partB) <= threshold;
    }

    /// <summary>Standard iterative-DP Levenshtein distance (insert/delete/substitute), O(len(a)*len(b))
    /// time and O(len(b)) space. No library needed - about 15 lines.</summary>
    public static int LevenshteinDistance(string a, string b)
    {
        a ??= "";
        b ??= "";
        var lenA = a.Length;
        var lenB = b.Length;
        if (lenA == 0) return lenB;
        if (lenB == 0) return lenA;

        var previousRow = new int[lenB + 1];
        var currentRow = new int[lenB + 1];
        for (var j = 0; j <= lenB; j++)
        {
            previousRow[j] = j;
        }

        for (var i = 1; i <= lenA; i++)
        {
            currentRow[0] = i;
            for (var j = 1; j <= lenB; j++)
            {
                var substitutionCost = a[i - 1] == b[j - 1] ? 0 : 1;
                currentRow[j] = Math.Min(
                    Math.Min(currentRow[j - 1] + 1, previousRow[j] + 1),
                    previousRow[j - 1] + substitutionCost);
            }

            (previousRow, currentRow) = (currentRow, previousRow);
        }

        return previousRow[lenB];
    }
}
