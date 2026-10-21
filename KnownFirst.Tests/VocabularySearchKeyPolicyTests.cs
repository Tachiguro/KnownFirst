using System.Text;
using KnownFirst.Core.Text;

namespace KnownFirst.Tests;

[TestClass]
public sealed class VocabularySearchKeyPolicyTests
{
    [TestMethod]
    [DataRow("bank", "W:bank", "W:banl")]
    [DataRow("Bank", "W:bank", "W:banl")]
    [DataRow("BANK", "W:bank", "W:banl")]
    [DataRow("Network", "W:network", "W:networl")]
    public void CreatePlan_EnglishOrdinaryWord_LowercasesWordRangeKey(
        string rawQuery,
        string expectedLowerBound,
        string expectedUpperBound)
    {
        var plan = VocabularySearchKeyPolicy.CreatePlan("en", rawQuery);

        Assert.AreEqual(VocabularySearchQueryStatus.Active, plan.Status);
        Assert.IsTrue(plan.IsValid);
        Assert.IsTrue(plan.IsSearchActive);
        Assert.IsFalse(plan.UseRecentOverview);
        Assert.AreEqual("en", plan.SourceLanguage);

        var wordRange = plan.GetRange(TokenKind.Word);
        Assert.AreEqual(expectedLowerBound, wordRange.InclusiveLowerBound);
        Assert.AreEqual(expectedUpperBound, wordRange.ExclusiveUpperBound);
    }

    [TestMethod]
    [DataRow("Haus", "W:haus", "W:haut")]
    [DataRow("NETZWERK", "W:netzwerk", "W:netzwerl")]
    [DataRow("Schreibmaschine", "W:schreibmaschine", "W:schreibmaschinf")]
    public void CreatePlan_GermanOrdinaryWord_LowercasesWordRangeKey(
        string rawQuery,
        string expectedLowerBound,
        string expectedUpperBound)
    {
        var plan = VocabularySearchKeyPolicy.CreatePlan("de", rawQuery);

        Assert.AreEqual(VocabularySearchQueryStatus.Active, plan.Status);
        Assert.AreEqual("de", plan.SourceLanguage);

        var wordRange = plan.GetRange(TokenKind.Word);
        Assert.AreEqual(expectedLowerBound, wordRange.InclusiveLowerBound);
        Assert.AreEqual(expectedUpperBound, wordRange.ExclusiveUpperBound);
    }

    [TestMethod]
    public void CreatePlan_UnicodeNfcComposedAndDecomposed_ProduceIdenticalPlans()
    {
        var composedCafe = VocabularySearchKeyPolicy.CreatePlan("en", "caf\u00E9");
        var decomposedCafe = VocabularySearchKeyPolicy.CreatePlan("en", "cafe\u0301");

        Assert.AreEqual(VocabularySearchQueryStatus.Active, composedCafe.Status);
        Assert.AreEqual(VocabularySearchQueryStatus.Active, decomposedCafe.Status);
        Assert.AreEqual("caf\u00E9", composedCafe.NormalizedQuery);
        Assert.AreEqual(composedCafe.NormalizedQuery, decomposedCafe.NormalizedQuery);
        Assert.AreEqual(4, composedCafe.ScalarCount);
        Assert.AreEqual(composedCafe.ScalarCount, decomposedCafe.ScalarCount);
        CollectionAssert.AreEqual(composedCafe.KeyRanges.ToArray(), decomposedCafe.KeyRanges.ToArray());

        var composedHaeuser = VocabularySearchKeyPolicy.CreatePlan("de", "H\u00E4user");
        var decomposedHaeuser = VocabularySearchKeyPolicy.CreatePlan("de", "Ha\u0308user");

        Assert.AreEqual("H\u00E4user", composedHaeuser.NormalizedQuery);
        Assert.AreEqual(composedHaeuser.NormalizedQuery, decomposedHaeuser.NormalizedQuery);
        CollectionAssert.AreEqual(composedHaeuser.KeyRanges.ToArray(), decomposedHaeuser.KeyRanges.ToArray());
    }

    [TestMethod]
    public void CreatePlan_GermanUmlautsEszettAndAccentedUnicode_PreservesDiacritics()
    {
        var umlautPlan = VocabularySearchKeyPolicy.CreatePlan("de", "Äpfel");
        Assert.AreEqual("W:äpfel", umlautPlan.GetRange(TokenKind.Word).InclusiveLowerBound);
        Assert.AreEqual("W:äpfem", umlautPlan.GetRange(TokenKind.Word).ExclusiveUpperBound);
        Assert.AreEqual("A:Äpfel", umlautPlan.GetRange(TokenKind.Acronym).InclusiveLowerBound);

        var eszettPlan = VocabularySearchKeyPolicy.CreatePlan("de", "Straße");
        Assert.AreEqual("W:straße", eszettPlan.GetRange(TokenKind.Word).InclusiveLowerBound);
        Assert.AreEqual("W:straßf", eszettPlan.GetRange(TokenKind.Word).ExclusiveUpperBound);
        Assert.IsFalse(eszettPlan.MatchesUtf8("W:strasse"));
        Assert.IsTrue(eszettPlan.MatchesUtf8("W:straßenbahn"));

        var cafePlan = VocabularySearchKeyPolicy.CreatePlan("en", "café");
        Assert.AreEqual("W:caf\u00E9", cafePlan.GetRange(TokenKind.Word).InclusiveLowerBound);
        Assert.AreEqual("W:caf\u00EA", cafePlan.GetRange(TokenKind.Word).ExclusiveUpperBound);
        Assert.IsFalse(cafePlan.MatchesUtf8("W:cafe"));
        Assert.IsTrue(cafePlan.MatchesUtf8("W:caf\u00E9s"));
    }

    [TestMethod]
    public void CreatePlan_NonBmpUnicodeScalars_CountsScalarsAndIncrementsByRune()
    {
        // U+10348 (Gothic letter Hwair) -> U+10349
        var hwairPlan = VocabularySearchKeyPolicy.CreatePlan("en", "\U00010348");
        Assert.AreEqual(VocabularySearchQueryStatus.Active, hwairPlan.Status);
        Assert.AreEqual(1, hwairPlan.ScalarCount);
        Assert.AreEqual("W:\U00010348", hwairPlan.GetRange(TokenKind.Word).InclusiveLowerBound);
        Assert.AreEqual("W:\U00010349", hwairPlan.GetRange(TokenKind.Word).ExclusiveUpperBound);

        // U+103FF (low surrogate is 0xDFFF in UTF-16) -> must advance to U+10400, not invalid UTF-16
        var boundaryNonBmpPlan = VocabularySearchKeyPolicy.CreatePlan("en", "x\U000103FF");
        Assert.AreEqual(VocabularySearchQueryStatus.Active, boundaryNonBmpPlan.Status);
        Assert.AreEqual(2, boundaryNonBmpPlan.ScalarCount);
        Assert.AreEqual("T:x\U000103FF", boundaryNonBmpPlan.GetRange(TokenKind.TechnicalTerm).InclusiveLowerBound);
        Assert.AreEqual("T:x\U00010400", boundaryNonBmpPlan.GetRange(TokenKind.TechnicalTerm).ExclusiveUpperBound);
        Assert.IsTrue(boundaryNonBmpPlan.MatchesUtf8("T:x\U000103FFz"));
        Assert.IsFalse(boundaryNonBmpPlan.MatchesUtf8("T:x\U00010400"));
    }

    [TestMethod]
    public void CreatePlan_AsciiAndUnicodePrefixUpperBounds_RespectSqliteUtf8BinaryOrdering()
    {
        // Surrogate gap boundary: U+D7FF must step over U+D800..U+DFFF to U+E000.
        var surrogateGapPlan = VocabularySearchKeyPolicy.CreatePlan("en", "a\uD7FF");
        Assert.AreEqual("W:a\uD7FF", surrogateGapPlan.GetRange(TokenKind.Word).InclusiveLowerBound);
        Assert.AreEqual("W:a\uE000", surrogateGapPlan.GetRange(TokenKind.Word).ExclusiveUpperBound);

        // BMP ceiling: U+FFFF must step to U+10000 in UTF-8 ordering, never using prefix + U+FFFF.
        var bmpCeilingPlan = VocabularySearchKeyPolicy.CreatePlan("en", "a\uFFFF");
        Assert.AreEqual("W:a\uFFFF", bmpCeilingPlan.GetRange(TokenKind.Word).InclusiveLowerBound);
        Assert.AreEqual("W:a\U00010000", bmpCeilingPlan.GetRange(TokenKind.Word).ExclusiveUpperBound);
        Assert.AreNotEqual("W:a\uFFFF\uFFFF", bmpCeilingPlan.GetRange(TokenKind.Word).ExclusiveUpperBound);

        // Verify that a non-BMP continuation after an ASCII prefix is included by the UTF-8 range
        // (whereas the broken "prefix + U+FFFF" shortcut would fail in UTF-8 because 0xF0 > 0xEF).
        var asciiPlan = VocabularySearchKeyPolicy.CreatePlan("en", "test");
        var wordRange = asciiPlan.GetRange(TokenKind.Word);
        Assert.AreEqual("W:test", wordRange.InclusiveLowerBound);
        Assert.AreEqual("W:tesu", wordRange.ExclusiveUpperBound);
        Assert.IsTrue(wordRange.MatchesUtf8("W:test\U00010000"));
        Assert.IsTrue(wordRange.MatchesUtf8("W:test\U0010FFFF"));
        Assert.IsFalse(wordRange.MatchesUtf8("W:tesu"));
    }

    [TestMethod]
    public void CreatePlan_MaximalUnicodeScalarBoundary_TruncatesTrailingMaxScalarsAndIncrementsPrecedingScalar()
    {
        // Trailing U+10FFFF after 'c' -> increments 'c' to 'd' and drops trailing U+10FFFF
        var withPrefix = VocabularySearchKeyPolicy.CreatePlan("en", "abc\U0010FFFF");
        Assert.AreEqual(VocabularySearchQueryStatus.Active, withPrefix.Status);
        Assert.AreEqual(4, withPrefix.ScalarCount);
        var wordRange = withPrefix.GetRange(TokenKind.Word);
        Assert.AreEqual("W:abc\U0010FFFF", wordRange.InclusiveLowerBound);
        Assert.AreEqual("W:abd", wordRange.ExclusiveUpperBound);
        Assert.IsTrue(wordRange.MatchesUtf8("W:abc\U0010FFFF"));
        Assert.IsTrue(wordRange.MatchesUtf8("W:abc\U0010FFFF\U0010FFFF"));
        Assert.IsFalse(wordRange.MatchesUtf8("W:abc\U0010FFFE"));
        Assert.IsFalse(wordRange.MatchesUtf8("W:abd"));

        // Query consisting solely of U+10FFFF -> increments ':' (U+003A) to ';' (U+003B)
        var maxOnly = VocabularySearchKeyPolicy.CreatePlan("en", "\U0010FFFF");
        Assert.AreEqual(VocabularySearchQueryStatus.Active, maxOnly.Status);
        Assert.AreEqual(1, maxOnly.ScalarCount);
        Assert.AreEqual("W:\U0010FFFF", maxOnly.GetRange(TokenKind.Word).InclusiveLowerBound);
        Assert.AreEqual("W;", maxOnly.GetRange(TokenKind.Word).ExclusiveUpperBound);
        Assert.AreEqual("A:\U0010FFFF", maxOnly.GetRange(TokenKind.Acronym).InclusiveLowerBound);
        Assert.AreEqual("A;", maxOnly.GetRange(TokenKind.Acronym).ExclusiveUpperBound);
        Assert.IsTrue(maxOnly.MatchesUtf8("W:\U0010FFFF"));
        Assert.IsTrue(maxOnly.MatchesUtf8("W:\U0010FFFFextra"));
        Assert.IsFalse(maxOnly.MatchesUtf8("W:a"));
        Assert.IsFalse(maxOnly.MatchesUtf8("W;"));

        // Raw unprefixed string of only U+10FFFF has no valid upper bound in Unicode.
        Assert.IsFalse(VocabularySearchKeyPolicy.TryComputeExclusiveUpperBound("\U0010FFFF", out _));
    }

    [TestMethod]
    public void CreatePlan_CaseSensitiveAcronymIdentity_PreservesAcronymCase()
    {
        var upper = VocabularySearchKeyPolicy.CreatePlan("en", "SHA");
        Assert.AreEqual("A:SHA", upper.GetRange(TokenKind.Acronym).InclusiveLowerBound);
        Assert.AreEqual("A:SHB", upper.GetRange(TokenKind.Acronym).ExclusiveUpperBound);
        Assert.IsTrue(upper.GetRange(TokenKind.Acronym).MatchesUtf8("A:SHA"));
        Assert.IsFalse(upper.GetRange(TokenKind.Acronym).MatchesUtf8("A:sha"));

        var lower = VocabularySearchKeyPolicy.CreatePlan("en", "sha");
        Assert.AreEqual("A:sha", lower.GetRange(TokenKind.Acronym).InclusiveLowerBound);
        Assert.AreEqual("A:shb", lower.GetRange(TokenKind.Acronym).ExclusiveUpperBound);
        Assert.IsFalse(lower.GetRange(TokenKind.Acronym).MatchesUtf8("A:SHA"));
    }

    [TestMethod]
    public void CreatePlan_ItVersusIT_PreservesDistinctionBetweenWordAndAcronym()
    {
        var wordIt = VocabularyIdentityPolicy.Resolve("it", TokenKind.Word, "en").Identity;
        var acronymIt = VocabularyIdentityPolicy.Resolve("IT", TokenKind.Acronym, "en").Identity;
        Assert.AreEqual("W:it", wordIt);
        Assert.AreEqual("A:IT", acronymIt);

        var lowercaseQueryPlan = VocabularySearchKeyPolicy.CreatePlan("en", "it");
        Assert.IsTrue(lowercaseQueryPlan.MatchesUtf8(wordIt));
        Assert.IsFalse(lowercaseQueryPlan.MatchesUtf8(acronymIt));
        Assert.AreEqual("W:it", lowercaseQueryPlan.GetRange(TokenKind.Word).InclusiveLowerBound);
        Assert.AreEqual("A:it", lowercaseQueryPlan.GetRange(TokenKind.Acronym).InclusiveLowerBound);

        var uppercaseQueryPlan = VocabularySearchKeyPolicy.CreatePlan("en", "IT");
        Assert.IsTrue(uppercaseQueryPlan.GetRange(TokenKind.Acronym).MatchesUtf8(acronymIt));
        Assert.IsFalse(uppercaseQueryPlan.GetRange(TokenKind.Acronym).MatchesUtf8(wordIt));
        Assert.AreEqual("A:IT", uppercaseQueryPlan.GetRange(TokenKind.Acronym).InclusiveLowerBound);
        Assert.AreEqual("A:IU", uppercaseQueryPlan.GetRange(TokenKind.Acronym).ExclusiveUpperBound);
    }

    [TestMethod]
    public void CreatePlan_AbbreviationAndTechnicalToken_PreserveCaseAndPunctuation()
    {
        var abbreviationPlan = VocabularySearchKeyPolicy.CreatePlan("en", "Dr.");
        var abbreviationRange = abbreviationPlan.GetRange(TokenKind.Abbreviation);
        Assert.AreEqual("B:Dr.", abbreviationRange.InclusiveLowerBound);
        Assert.AreEqual("B:Dr/", abbreviationRange.ExclusiveUpperBound);
        Assert.IsTrue(abbreviationRange.MatchesUtf8("B:Dr."));
        Assert.IsFalse(abbreviationRange.MatchesUtf8("B:dr."));

        var technicalPlan = VocabularySearchKeyPolicy.CreatePlan("en", "OAuth2");
        var technicalRange = technicalPlan.GetRange(TokenKind.TechnicalTerm);
        Assert.AreEqual("T:OAuth2", technicalRange.InclusiveLowerBound);
        Assert.AreEqual("T:OAuth3", technicalRange.ExclusiveUpperBound);
        Assert.IsTrue(technicalRange.MatchesUtf8("T:OAuth2"));
        Assert.IsFalse(technicalRange.MatchesUtf8("T:oauth2"));

        var hyphenatedPlan = VocabularySearchKeyPolicy.CreatePlan("en", "state-of-the-art");
        Assert.AreEqual("T:state-of-the-art", hyphenatedPlan.GetRange(TokenKind.TechnicalTerm).InclusiveLowerBound);
        Assert.AreEqual("T:state-of-the-aru", hyphenatedPlan.GetRange(TokenKind.TechnicalTerm).ExclusiveUpperBound);
    }

    [TestMethod]
    public void CreatePlan_SqlWildcardCharacters_AreTreatedAsLiteralSearchContent()
    {
        var percentPlan = VocabularySearchKeyPolicy.CreatePlan("en", "100%");
        var percentWordRange = percentPlan.GetRange(TokenKind.Word);
        Assert.AreEqual("W:100%", percentWordRange.InclusiveLowerBound);
        Assert.AreEqual("W:100&", percentWordRange.ExclusiveUpperBound);
        Assert.IsTrue(percentWordRange.MatchesUtf8("W:100%"));
        Assert.IsTrue(percentWordRange.MatchesUtf8("W:100%ers"));
        Assert.IsFalse(percentWordRange.MatchesUtf8("W:1000"));
        Assert.IsFalse(percentWordRange.MatchesUtf8("W:100a"));

        var underscorePlan = VocabularySearchKeyPolicy.CreatePlan("en", "foo_");
        var underscoreWordRange = underscorePlan.GetRange(TokenKind.Word);
        Assert.AreEqual("W:foo_", underscoreWordRange.InclusiveLowerBound);
        Assert.AreEqual("W:foo`", underscoreWordRange.ExclusiveUpperBound);
        Assert.IsTrue(underscoreWordRange.MatchesUtf8("W:foo_"));
        Assert.IsTrue(underscoreWordRange.MatchesUtf8("W:foo_bar"));
        Assert.IsFalse(underscoreWordRange.MatchesUtf8("W:fooa"));
        Assert.IsFalse(underscoreWordRange.MatchesUtf8("W:foo1"));
    }

    [TestMethod]
    [DataRow("I", "W:i", "W:j")]
    [DataRow("i", "W:i", "W:j")]
    [DataRow("me", "W:me", "W:mf")]
    [DataRow("ME", "W:me", "W:mf")]
    [DataRow("my", "W:my", "W:mz")]
    [DataRow("My", "W:my", "W:mz")]
    public void CreatePlan_EnglishFirstPersonSingularPronouns_IncludeCanonicalAliasAndRetainPrefixRanges(
        string query,
        string expectedWordLower,
        string expectedWordUpper)
    {
        var plan = VocabularySearchKeyPolicy.CreatePlan("en", query);

        Assert.AreEqual(VocabularySearchQueryStatus.Active, plan.Status);
        CollectionAssert.AreEqual(new[] { "W:i" }, plan.ExactAliases.ToArray());
        CollectionAssert.AllItemsAreUnique(plan.ExactAliases.ToArray());

        var wordRange = plan.GetRange(TokenKind.Word);
        Assert.AreEqual(expectedWordLower, wordRange.InclusiveLowerBound);
        Assert.AreEqual(expectedWordUpper, wordRange.ExclusiveUpperBound);

        Assert.IsTrue(plan.MatchesUtf8("W:i"));
        Assert.IsTrue(plan.MatchesUtf8($"{expectedWordLower}aning"));
    }

    [TestMethod]
    [DataRow("de", "I")]
    [DataRow("de", "i")]
    [DataRow("de", "me")]
    [DataRow("de", "my")]
    [DataRow("de", "ich")]
    [DataRow("de", "mir")]
    [DataRow("de", "mich")]
    [DataRow("en", "mine")]
    [DataRow("en", "myself")]
    [DataRow("en", "mean")]
    [DataRow("en", "myth")]
    [DataRow("en", "we")]
    [DataRow("en", "us")]
    [DataRow("en", "you")]
    [DataRow("en", "your")]
    [DataRow("en", "he")]
    [DataRow("en", "him")]
    [DataRow("en", "his")]
    [DataRow("en", "she")]
    [DataRow("en", "her")]
    [DataRow("en", "they")]
    [DataRow("en", "them")]
    [DataRow("en", "their")]
    [DataRow("en", "it")]
    [DataRow("en", "its")]
    public void CreatePlan_NonAliasQueriesAndNonEnglishLanguages_DoNotIncludePronounAliases(
        string sourceLanguage,
        string query)
    {
        var plan = VocabularySearchKeyPolicy.CreatePlan(sourceLanguage, query);

        Assert.AreEqual(VocabularySearchQueryStatus.Active, plan.Status);
        Assert.IsEmpty(plan.ExactAliases);
    }

    [TestMethod]
    public void CreatePlan_DoesNotPerformBroadStemming()
    {
        var banksPlan = VocabularySearchKeyPolicy.CreatePlan("en", "banks");
        Assert.AreEqual("W:banks", banksPlan.GetRange(TokenKind.Word).InclusiveLowerBound);
        Assert.AreEqual("W:bankt", banksPlan.GetRange(TokenKind.Word).ExclusiveUpperBound);
        Assert.IsFalse(banksPlan.MatchesUtf8("W:bank"));

        var networkingPlan = VocabularySearchKeyPolicy.CreatePlan("en", "networking");
        Assert.IsFalse(networkingPlan.MatchesUtf8("W:network"));

        var haeuserPlan = VocabularySearchKeyPolicy.CreatePlan("de", "Häuser");
        Assert.IsFalse(haeuserPlan.MatchesUtf8("W:haus"));
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("   ")]
    [DataRow("ru")]
    [DataRow("RU")]
    [DataRow("fr")]
    [DataRow("es")]
    [DataRow("system")]
    [DataRow("en-US")]
    [DataRow("de-DE")]
    [DataRow("invalid")]
    public void CreatePlan_InvalidOrUnsupportedSourceLanguage_FailsSafely(string? sourceLanguage)
    {
        var succeeded = VocabularySearchKeyPolicy.TryCreatePlan(sourceLanguage, "network", out var plan);

        Assert.IsFalse(succeeded);
        Assert.AreEqual(VocabularySearchQueryStatus.InvalidSourceLanguage, plan.Status);
        Assert.IsFalse(plan.IsValid);
        Assert.IsFalse(plan.IsSearchActive);
        Assert.IsFalse(plan.UseRecentOverview);
        Assert.AreEqual(string.Empty, plan.SourceLanguage);
        Assert.AreEqual(string.Empty, plan.NormalizedQuery);
        Assert.AreEqual(0, plan.ScalarCount);
        Assert.IsEmpty(plan.KeyRanges);
        Assert.IsEmpty(plan.ExactAliases);
    }

    [TestMethod]
    [DataRow("en", "en")]
    [DataRow("EN", "en")]
    [DataRow(" en ", "en")]
    [DataRow("de", "de")]
    [DataRow("DE", "de")]
    [DataRow(" de ", "de")]
    public void CreatePlan_SupportedSourceLanguages_AreNormalizedIndependentlyOfUiLanguage(
        string rawSourceLanguage,
        string expectedNormalizedLanguage)
    {
        var plan = VocabularySearchKeyPolicy.CreatePlan(rawSourceLanguage, "test");

        Assert.AreEqual(VocabularySearchQueryStatus.Active, plan.Status);
        Assert.AreEqual(expectedNormalizedLanguage, plan.SourceLanguage);
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow(" ")]
    [DataRow("   \t\r\n  ")]
    [DataRow("\u00A0\u2003")]
    public void CreatePlan_EmptyOrWhitespaceOnlyQuery_ReturnsEmptyQueryStatusForRecentOverview(
        string? rawQuery)
    {
        var succeeded = VocabularySearchKeyPolicy.TryCreatePlan("en", rawQuery, out var plan);

        Assert.IsFalse(succeeded);
        Assert.AreEqual(VocabularySearchQueryStatus.EmptyQuery, plan.Status);
        Assert.IsTrue(plan.IsValid);
        Assert.IsFalse(plan.IsSearchActive);
        Assert.IsTrue(plan.UseRecentOverview);
        Assert.AreEqual("en", plan.SourceLanguage);
        Assert.AreEqual(string.Empty, plan.NormalizedQuery);
        Assert.AreEqual(0, plan.ScalarCount);
        Assert.IsEmpty(plan.KeyRanges);
        Assert.IsEmpty(plan.ExactAliases);
    }

    [TestMethod]
    public void CreatePlan_TrimsExternalWhitespaceWithoutAlteringInternalContent()
    {
        var plan = VocabularySearchKeyPolicy.CreatePlan("en", "  \t OAuth2 \r\n ");

        Assert.AreEqual(VocabularySearchQueryStatus.Active, plan.Status);
        Assert.AreEqual("OAuth2", plan.NormalizedQuery);
        Assert.AreEqual(6, plan.ScalarCount);
        Assert.AreEqual("T:OAuth2", plan.GetRange(TokenKind.TechnicalTerm).InclusiveLowerBound);
    }

    [TestMethod]
    public void CreatePlan_Maximum80UnicodeScalarValues_AcceptsBoundaryAndRejectsOversized()
    {
        var exact80Ascii = new string('a', 80);
        var ascii80Plan = VocabularySearchKeyPolicy.CreatePlan("en", $"  {exact80Ascii}  ");
        Assert.AreEqual(VocabularySearchQueryStatus.Active, ascii80Plan.Status);
        Assert.AreEqual(80, ascii80Plan.ScalarCount);

        // 80 non-BMP scalars = 160 UTF-16 chars, still 80 Unicode scalar values
        var exact80NonBmp = string.Concat(Enumerable.Repeat("\U00010348", 80));
        Assert.AreEqual(160, exact80NonBmp.Length);
        var nonBmp80Plan = VocabularySearchKeyPolicy.CreatePlan("en", exact80NonBmp);
        Assert.AreEqual(VocabularySearchQueryStatus.Active, nonBmp80Plan.Status);
        Assert.AreEqual(80, nonBmp80Plan.ScalarCount);

        // 80 decomposed "e\u0301" = 160 scalars before NFC, 80 scalars after NFC
        var exact80Decomposed = string.Concat(Enumerable.Repeat("e\u0301", 80));
        var decomposed80Plan = VocabularySearchKeyPolicy.CreatePlan("en", exact80Decomposed);
        Assert.AreEqual(VocabularySearchQueryStatus.Active, decomposed80Plan.Status);
        Assert.AreEqual(80, decomposed80Plan.ScalarCount);

        // 81 ASCII scalars -> rejected
        var oversized81Ascii = new string('a', 81);
        var oversizedAsciiResult = VocabularySearchKeyPolicy.TryCreatePlan("en", oversized81Ascii, out var oversizedAsciiPlan);
        Assert.IsFalse(oversizedAsciiResult);
        Assert.AreEqual(VocabularySearchQueryStatus.QueryTooLong, oversizedAsciiPlan.Status);
        Assert.IsFalse(oversizedAsciiPlan.IsValid);
        Assert.IsFalse(oversizedAsciiPlan.IsSearchActive);
        Assert.IsFalse(oversizedAsciiPlan.UseRecentOverview);
        Assert.IsEmpty(oversizedAsciiPlan.KeyRanges);
        Assert.IsEmpty(oversizedAsciiPlan.ExactAliases);

        // 81 non-BMP scalars -> rejected
        var oversized81NonBmp = string.Concat(Enumerable.Repeat("\U00010348", 81));
        var oversizedNonBmpPlan = VocabularySearchKeyPolicy.CreatePlan("de", oversized81NonBmp);
        Assert.AreEqual(VocabularySearchQueryStatus.QueryTooLong, oversizedNonBmpPlan.Status);
        Assert.IsEmpty(oversizedNonBmpPlan.KeyRanges);
    }

    [TestMethod]
    public void CreatePlan_InvalidUnicodeUnpairedSurrogates_FailsSafelyWithoutThrowing()
    {
        string[] malformedQueries =
        [
            new string(['\uD800']),
            new string(['\uDC00']),
            string.Concat("abc", new string(['\uD83D'])),
            string.Concat("abc", new string(['\uDE00']), "def"),
            new string(['\uDC00', '\uD800'])
        ];

        foreach (var malformedQuery in malformedQueries)
        {
            Assert.IsFalse(VocabularySearchKeyPolicy.IsValidUnicode(malformedQuery));

            var succeeded = VocabularySearchKeyPolicy.TryCreatePlan("en", malformedQuery, out var plan);

            Assert.IsFalse(succeeded);
            Assert.AreEqual(VocabularySearchQueryStatus.InvalidUnicode, plan.Status);
            Assert.IsFalse(plan.IsValid);
            Assert.IsFalse(plan.IsSearchActive);
            Assert.IsFalse(plan.UseRecentOverview);
            Assert.AreEqual(string.Empty, plan.NormalizedQuery);
            Assert.AreEqual(0, plan.ScalarCount);
            Assert.IsEmpty(plan.KeyRanges);
            Assert.IsEmpty(plan.ExactAliases);
        }
    }

    [TestMethod]
    public void CreatePlan_KeyRangesHaveStableDeterministicOrderAndImmutableCollections()
    {
        var first = VocabularySearchKeyPolicy.CreatePlan("en", "my");
        var second = VocabularySearchKeyPolicy.CreatePlan("en", "my");

        CollectionAssert.AreEqual(
            new[]
            {
                TokenKind.Word,
                TokenKind.Acronym,
                TokenKind.Abbreviation,
                TokenKind.TechnicalTerm
            },
            first.KeyRanges.Select(range => range.TokenKind).ToArray());

        CollectionAssert.AreEqual(first.KeyRanges.ToArray(), second.KeyRanges.ToArray());
        CollectionAssert.AreEqual(first.ExactAliases.ToArray(), second.ExactAliases.ToArray());

        var mutableRanges = (ICollection<VocabularySearchKeyRange>)first.KeyRanges;
        Assert.IsTrue(mutableRanges.IsReadOnly);
        Assert.Throws<NotSupportedException>(() =>
            mutableRanges.Add(new VocabularySearchKeyRange(TokenKind.Word, "W:a", "W:b")));

        var mutableAliases = (ICollection<string>)first.ExactAliases;
        Assert.IsTrue(mutableAliases.IsReadOnly);
        Assert.Throws<NotSupportedException>(() => mutableAliases.Add("W:other"));
    }
}
