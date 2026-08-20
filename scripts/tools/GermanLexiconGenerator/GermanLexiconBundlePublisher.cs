using KnownFirst.Core.Text.German;

namespace KnownFirst.Tools.GermanLexicon;

/// <summary>
/// Publishes a single authoritative German lexicon runtime bundle file, replacing any
/// previously accepted bundle at the same path only after the newly staged bundle has been
/// fully validated by reopening it through the real production loader and confirmed to carry
/// the caller's intended provenance and counts.
///
/// Publication uses exactly one same-directory <see cref="File.Move(string, string, bool)"/>
/// (overwrite) after full validation — a single rename-style replacement, never a sequence of
/// independent file writes, so the stable path is only ever touched once per publish attempt.
/// This tool is a manually re-run offline maintenance utility, not a multi-writer production
/// service: if the underlying OS/filesystem operation were itself interrupted by a crash or
/// power loss, the correct recovery is simply re-running generation from the pinned upstream
/// commit, since every accepted bundle is fully reproducible from its recorded provenance.
///
/// Any failure — while writing the staged content, while reopening/validating it, or while
/// confirming its provenance/counts match what the caller intended — leaves the file at
/// <c>finalPath</c> byte-for-byte exactly as it was before the call; only a fully staged, fully
/// validated, semantically-confirmed bundle is ever moved into place.
/// </summary>
public static class GermanLexiconBundlePublisher
{
    /// <summary>
    /// Writes <paramref name="writeBundle"/>'s output to a temp file inside
    /// <paramref name="outDir"/>, reopens and validates it via
    /// <see cref="GeneratedGermanLexicon.LoadFromFile"/>, and — only if that succeeds and the
    /// reopened bundle's <see cref="GeneratedGermanLexicon.Provenance"/> and
    /// <see cref="GeneratedGermanLexicon.Counts"/> structurally equal
    /// <paramref name="expectedProvenance"/> and <paramref name="expectedCounts"/> — replaces
    /// <paramref name="finalFileName"/> in <paramref name="outDir"/> with it via a single
    /// same-directory rename. The temp file is always removed, whether publication succeeds or
    /// fails.
    /// </summary>
    public static GeneratedGermanLexicon Publish(
        string outDir,
        string finalFileName,
        Action<Stream> writeBundle,
        GermanLexiconBundleProvenance expectedProvenance,
        GermanLexiconBundleCounts expectedCounts)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outDir);
        ArgumentException.ThrowIfNullOrWhiteSpace(finalFileName);
        ArgumentNullException.ThrowIfNull(writeBundle);
        ArgumentNullException.ThrowIfNull(expectedProvenance);
        ArgumentNullException.ThrowIfNull(expectedCounts);

        Directory.CreateDirectory(outDir);
        var finalPath = Path.Combine(outDir, finalFileName);
        var tempPath = Path.Combine(outDir, $".{finalFileName}.staging-{Guid.NewGuid():N}");

        try
        {
            using (var tempStream = File.Create(tempPath))
            {
                writeBundle(tempStream);
            }

            // Reopen and fully validate the staged bundle through the real production loader
            // before it is ever visible at the stable path. A malformed/inconsistent staged
            // bundle throws here and never reaches the final path.
            var validated = GeneratedGermanLexicon.LoadFromFile(tempPath);

            // A staged bundle can be structurally well-formed and checksum-valid while still
            // being wrong: a writer/field-ordering bug could serialize internally
            // self-consistent but semantically incorrect provenance or counts, which would
            // otherwise round-trip cleanly through the checks above. Require the reopened
            // bundle to actually carry what the caller declares it intends to publish.
            if (validated.Provenance != expectedProvenance)
            {
                throw new InvalidDataException(
                    "Staged German lexicon runtime bundle provenance does not match the expected " +
                    "provenance the caller intended to publish.");
            }

            if (validated.Counts != expectedCounts)
            {
                throw new InvalidDataException(
                    "Staged German lexicon runtime bundle counts do not match the expected " +
                    "counts the caller intended to publish.");
            }

            File.Move(tempPath, finalPath, overwrite: true);
            return validated;
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }
}
