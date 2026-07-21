using CasePlanner.Web.Server.Services;

namespace CasePlanner.Web.Server.Tests;

// Multi-user rollout Phase 3 (shared witness registry): pure, DB-free coverage of the
// nickname/prefix + edit-distance similarity check used both by the registry search's "similar"
// ranking and the "Add Witness" modal's non-blocking flag. See WitnessNameMatcher.cs for the
// exact thresholds chosen (shorter-name length <= 4 -> distance <= 1, <= 8 -> distance <= 2,
// longer -> distance <= 3) and the rationale for erring conservative (favor missing an edge-case
// flag over annoying users with false positives on genuinely unrelated names).
public class WitnessNameMatcherTests
{
    [Fact]
    public void AreSimilar_NicknamePrefix_MaxAndMaxwell_Flagged()
    {
        Assert.True(WitnessNameMatcher.AreSimilar("Max Johnson", "Maxwell Johnson"));
    }

    [Fact]
    public void AreSimilar_NicknamePrefix_RobAndRobert_Flagged()
    {
        Assert.True(WitnessNameMatcher.AreSimilar("Rob Anderson", "Robert Anderson"));
    }

    [Fact]
    public void AreSimilar_ExactMatch_IgnoringCaseAndWhitespace_Flagged()
    {
        Assert.True(WitnessNameMatcher.AreSimilar("  Jane   Doe ", "jane doe"));
    }

    [Fact]
    public void AreSimilar_ClearlyUnrelatedNames_NotFlagged()
    {
        Assert.False(WitnessNameMatcher.AreSimilar("John Smith", "Maria Garcia"));
    }

    [Fact]
    public void AreSimilar_UnrelatedShortNames_NotFlagged()
    {
        Assert.False(WitnessNameMatcher.AreSimilar("Ann Carter", "Tom Carter"));
    }

    [Fact]
    public void AreSimilar_PlausibleTypo_TransposedLetters_Flagged()
    {
        // "Micheal" vs "Michael" - a common real-world misspelling, small edit distance.
        Assert.True(WitnessNameMatcher.AreSimilar("Micheal Chen", "Michael Chen"));
    }

    [Fact]
    public void AreSimilar_PlausibleTypo_MissingLetter_Flagged()
    {
        Assert.True(WitnessNameMatcher.AreSimilar("Jon Reeves", "John Reeves"));
    }

    [Fact]
    public void AreSimilar_EmptyOrBlankName_NeverFlagged()
    {
        Assert.False(WitnessNameMatcher.AreSimilar("", "Max Johnson"));
        Assert.False(WitnessNameMatcher.AreSimilar("   ", "Max Johnson"));
        Assert.False(WitnessNameMatcher.AreSimilar(null, "Max Johnson"));
    }

    [Fact]
    public void Normalize_CollapsesWhitespaceAndLowercases()
    {
        Assert.Equal("max carter", WitnessNameMatcher.Normalize("  Max   Carter "));
    }

    [Fact]
    public void LevenshteinDistance_IdenticalStrings_IsZero()
    {
        Assert.Equal(0, WitnessNameMatcher.LevenshteinDistance("maxwell", "maxwell"));
    }

    [Fact]
    public void LevenshteinDistance_SingleSubstitution_IsOne()
    {
        Assert.Equal(1, WitnessNameMatcher.LevenshteinDistance("max", "mox"));
    }
}
