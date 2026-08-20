using System.IO.Compression;
using System.Security.Cryptography;
using KnownFirst.Core.Text;
using KnownFirst.Core.Text.German;
using KnownFirst.Tools.GermanLexicon;

const string UpstreamProjectName = "german-morph-dictionaries";
const string UpstreamRepositoryUrl = "https://github.com/DuyguA/german-morph-dictionaries";
const string SourceAssetPathInRepo = "morf_dict.zip";
const string SourceEntryName = "DE_morph_dict.txt";
const string DataLicenseIdentifier = "CC BY-SA 4.0 (Creative Commons Attribution-ShareAlike 4.0 International)";
const string BundleFileName = "german-lexicon.v2.kfgl";

if (args.Length == 0)
{
    PrintUsage();
    return 1;
}

try
{
    return args[0] switch
    {
        "fetch" => await RunFetchAsync(args[1..]),
        "generate" => RunGenerate(args[1..]),
        "update" => await RunUpdateAsync(args[1..]),
        _ => Unknown(args[0]),
    };
}
catch (Exception ex)
{
    Console.Error.WriteLine($"ERROR: {ex.Message}");
    return 1;
}

static int Unknown(string command)
{
    Console.Error.WriteLine($"Unknown command '{command}'.");
    PrintUsage();
    return 1;
}

static void PrintUsage()
{
    Console.Error.WriteLine("""
        Usage:
          GermanLexiconGenerator fetch --commit <sha> --out <dir>
          GermanLexiconGenerator generate --source-zip <path> --commit <sha> --source-sha256 <sha256> --license <path> --out <dir>
          GermanLexiconGenerator update --commit <sha> --out <dir>

        'commit' must always be an explicit, exact upstream commit SHA. There is no
        "latest"/branch-HEAD mode: every invocation pins the exact upstream revision.
        """);
}

static Dictionary<string, string> ParseOptions(string[] args)
{
    var options = new Dictionary<string, string>(StringComparer.Ordinal);
    for (var i = 0; i < args.Length - 1; i += 2)
    {
        var key = args[i];
        if (!key.StartsWith("--", StringComparison.Ordinal))
        {
            throw new ArgumentException($"Expected an option starting with '--', got '{key}'.");
        }

        options[key[2..]] = args[i + 1];
    }

    return options;
}

static string RequireOption(Dictionary<string, string> options, string name) =>
    options.TryGetValue(name, out var value)
        ? value
        : throw new ArgumentException($"Missing required option --{name}.");

async Task<int> RunFetchAsync(string[] rawArgs)
{
    var options = ParseOptions(rawArgs);
    var commit = RequireOption(options, "commit");
    var outDir = RequireOption(options, "out");

    UpstreamCommitValidation.EnsureValid(commit);

    Directory.CreateDirectory(outDir);
    var stagingDir = outDir + ".staging-" + Guid.NewGuid().ToString("N");
    Directory.CreateDirectory(stagingDir);

    using var http = new HttpClient();
    http.DefaultRequestHeaders.UserAgent.ParseAdd("KnownFirst-GermanLexiconGenerator/1.0");

    var licensePath = Path.Combine(stagingDir, "LICENSE");
    var zipPath = Path.Combine(stagingDir, SourceAssetPathInRepo);

    await DownloadAsync(http, RawUrl(commit, "LICENSE"), licensePath);
    await DownloadAsync(http, RawUrl(commit, SourceAssetPathInRepo), zipPath);

    var licenseText = await File.ReadAllTextAsync(licensePath);
    UpstreamLicenseValidation.EnsureValid(licenseText);

    var sha256 = Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(zipPath)));

    Directory.CreateDirectory(outDir);
    File.Copy(licensePath, Path.Combine(outDir, "LICENSE"), overwrite: true);
    File.Copy(zipPath, Path.Combine(outDir, SourceAssetPathInRepo), overwrite: true);
    Directory.Delete(stagingDir, recursive: true);

    Console.WriteLine($"Fetched {SourceAssetPathInRepo} at commit {commit}: sha256={sha256}");
    Console.WriteLine("License check passed: LICENSE still designates CC BY-SA 4.0.");
    return 0;
}

static async Task DownloadAsync(HttpClient http, string url, string destinationPath)
{
    using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
    response.EnsureSuccessStatusCode();
    await using var target = File.Create(destinationPath);
    await response.Content.CopyToAsync(target);
}

static string RawUrl(string commit, string path) =>
    $"https://raw.githubusercontent.com/DuyguA/german-morph-dictionaries/{commit}/{path}";

int RunGenerate(string[] rawArgs)
{
    var options = ParseOptions(rawArgs);
    var sourceZipPath = RequireOption(options, "source-zip");
    var commit = RequireOption(options, "commit");
    var expectedSourceSha256 = RequireOption(options, "source-sha256");
    var licensePath = RequireOption(options, "license");
    var outDir = RequireOption(options, "out");

    UpstreamCommitValidation.EnsureValid(commit);

    var actualSourceSha256 = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(sourceZipPath)));
    if (!string.Equals(actualSourceSha256, expectedSourceSha256, StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException(
            $"Source archive SHA-256 mismatch: expected {expectedSourceSha256}, computed {actualSourceSha256}.");
    }

    var licenseText = File.ReadAllText(licensePath);
    UpstreamLicenseValidation.EnsureValid(licenseText);

    GermanLexiconIndexBuildResult buildResult;
    using (var archive = ZipFile.OpenRead(sourceZipPath))
    {
        var entries = archive.Entries.Where(e => e.FullName == SourceEntryName).ToList();
        if (entries.Count != 1)
        {
            throw new InvalidOperationException(
                $"Expected exactly one '{SourceEntryName}' entry in '{sourceZipPath}', found {entries.Count}.");
        }

        using var entryStream = entries[0].Open();
        using var reader = new StreamReader(entryStream, System.Text.Encoding.UTF8);
        buildResult = GermanLexiconIndexBuilder.Build(GermanMorphDictionaryParser.Parse(reader));
    }

    if (buildResult.Lemmas.Count == 0)
    {
        throw new InvalidOperationException("Generated zero lemma entries; refusing to publish an empty lexicon.");
    }

    var provenance = new GermanLexiconBundleProvenance(
        UpstreamProjectName: UpstreamProjectName,
        UpstreamRepositoryUrl: UpstreamRepositoryUrl,
        UpstreamCommit: commit,
        UpstreamSourceAssetPath: SourceAssetPathInRepo,
        UpstreamSourceSha256: actualSourceSha256,
        DataLicenseIdentifier: DataLicenseIdentifier,
        ProvenanceStatement:
            "This runtime lexical index is processed/derived from the upstream German " +
            "morphological dictionary data listed above (CC BY-SA 4.0). It contains no " +
            "definitions, translations, provider state, or user vocabulary. KnownFirst " +
            "generator/reader source code that produces and reads this bundle is Apache-2.0.");

    var counts = new GermanLexiconBundleCounts(
        buildResult.Statistics.TotalDistinctWordForms,
        buildResult.Statistics.AmbiguousWordForms,
        buildResult.Statistics.UnsupportedCategoryWordForms,
        buildResult.Statistics.InflectedFormWordFormsExcluded,
        buildResult.Statistics.UnambiguousBaseFormLemmaEntries,
        buildResult.Statistics.ImperativeSingularCandidateForms,
        buildResult.Statistics.AmbiguousImperativeSingularForms,
        buildResult.Statistics.DerivedVerbStemCollisionExclusions,
        buildResult.Statistics.DerivedVerbStemEntries);

    GermanLexiconBundlePublisher.Publish(
        outDir,
        BundleFileName,
        stream => GermanLexiconRuntimeAssetWriter.Write(stream, provenance, counts, buildResult.Lemmas, buildResult.Stems),
        provenance,
        counts);

    var finalPath = Path.Combine(outDir, BundleFileName);
    var bundleByteLength = new FileInfo(finalPath).Length;
    var bundleSha256 = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(finalPath)));

    Console.WriteLine($"Published bundle: {bundleByteLength} bytes, sha256={bundleSha256}");
    Console.WriteLine($"Lemma entries: {buildResult.Lemmas.Count}, Stem entries: {buildResult.Stems.Count}");
    Console.WriteLine($"Ambiguous word forms excluded: {buildResult.Statistics.AmbiguousWordForms}");
    Console.WriteLine($"Unsupported-category word forms excluded: {buildResult.Statistics.UnsupportedCategoryWordForms}");
    Console.WriteLine($"Inflected (non-base-form) word forms excluded: {buildResult.Statistics.InflectedFormWordFormsExcluded}");
    Console.WriteLine($"Derived verb stem entries: {buildResult.Statistics.DerivedVerbStemEntries} " +
        $"(collision exclusions: {buildResult.Statistics.DerivedVerbStemCollisionExclusions})");
    Console.WriteLine($"Wrote {finalPath}");
    return 0;
}

async Task<int> RunUpdateAsync(string[] rawArgs)
{
    var options = ParseOptions(rawArgs);
    var commit = RequireOption(options, "commit");
    var outDir = RequireOption(options, "out");

    var fetchStagingDir = Path.Combine(Path.GetTempPath(), "knownfirst-german-lexicon-fetch-" + Guid.NewGuid().ToString("N"));
    var fetchResult = await RunFetchAsync(["--commit", commit, "--out", fetchStagingDir]);
    if (fetchResult != 0)
    {
        return fetchResult;
    }

    var sourceZipPath = Path.Combine(fetchStagingDir, SourceAssetPathInRepo);
    var licensePath = Path.Combine(fetchStagingDir, "LICENSE");
    var sourceSha256 = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(sourceZipPath)));

    var generateResult = RunGenerate(
    [
        "--source-zip", sourceZipPath,
        "--commit", commit,
        "--source-sha256", sourceSha256,
        "--license", licensePath,
        "--out", outDir,
    ]);

    Directory.Delete(fetchStagingDir, recursive: true);
    return generateResult;
}
