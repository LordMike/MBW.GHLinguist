namespace MBW.GHLinguist.Tests;

/// <summary>
/// The native bridge answers registry lookups from its own indexes instead of calling
/// <c>Linguist::Language.find_by_*</c>. These tests walk every language the embedded Linguist exposes and check
/// that each name, alias, extension, filename, and interpreter resolves the way Linguist's own indexes would,
/// so a Linguist upgrade that changes lookup semantics or introduces an index collision fails here instead of
/// silently diverging.
/// </summary>
public sealed class NativeLookupParityTests
{
    public static bool NativeIntegrationEnabled =>
        string.Equals(Environment.GetEnvironmentVariable("GHL_RUN_NATIVE_INTEGRATION"), "true", StringComparison.OrdinalIgnoreCase);

    [Fact(Skip = "Set GHL_RUN_NATIVE_INTEGRATION=true with staged native assets.", SkipUnless = nameof(NativeIntegrationEnabled))]
    public void NamesResolveLikeLinguistsNameIndex()
    {
        using LinguistRuntime runtime = LinguistRuntime.Create();
        Dictionary<string, LinguistLanguage> expected = new(StringComparer.Ordinal);

        foreach (LinguistLanguage language in runtime.Languages)
        {
            AddUnique(expected, language.Name.ToLowerInvariant(), language, "name");
            if (!string.IsNullOrEmpty(language.FileSystemName))
            {
                AddUnique(expected, language.FileSystemName.ToLowerInvariant(), language, "fs_name");
            }
        }

        foreach ((string key, LinguistLanguage language) in expected)
        {
            Assert.Equal(language, runtime.FindByName(key));
            Assert.Equal(language, runtime.FindByName(key.ToUpperInvariant()));
            Assert.Equal(language, runtime.FindByName($"{key}, trailing text after a comma is ignored"));
        }

        // Aliases live in a separate index upstream, so the default alias of a multi-word name must not match by name.
        foreach (LinguistLanguage language in runtime.Languages.Where(language => language.Name.Contains(' ', StringComparison.Ordinal)))
        {
            string defaultAlias = language.Name.ToLowerInvariant().Replace(' ', '-');
            if (!expected.ContainsKey(defaultAlias))
            {
                Assert.Null(runtime.FindByName(defaultAlias));
            }
        }
    }

    [Fact(Skip = "Set GHL_RUN_NATIVE_INTEGRATION=true with staged native assets.", SkipUnless = nameof(NativeIntegrationEnabled))]
    public void AliasesResolveLikeLinguistsAliasIndex()
    {
        using LinguistRuntime runtime = LinguistRuntime.Create();
        Dictionary<string, LinguistLanguage> expected = new(StringComparer.Ordinal);

        foreach (LinguistLanguage language in runtime.Languages)
        {
            Assert.NotEmpty(language.Aliases);
            foreach (string alias in language.Aliases)
            {
                AddUnique(expected, alias.ToLowerInvariant(), language, "alias");
            }
        }

        foreach ((string alias, LinguistLanguage language) in expected)
        {
            Assert.Equal(language, runtime.FindByAlias(alias));
            Assert.Equal(language, runtime.FindByAlias(alias.ToUpperInvariant()));
            Assert.Equal(language, runtime.FindByAlias($"{alias}, trailing text after a comma is ignored"));
        }
    }

    [Fact(Skip = "Set GHL_RUN_NATIVE_INTEGRATION=true with staged native assets.", SkipUnless = nameof(NativeIntegrationEnabled))]
    public void ExtensionsResolveLikeLinguistsExtensionIndex()
    {
        using LinguistRuntime runtime = LinguistRuntime.Create();
        int checkedExtensions = 0;

        foreach (LinguistLanguage language in runtime.Languages)
        {
            foreach (string extension in language.Extensions)
            {
                Assert.StartsWith(".", extension, StringComparison.Ordinal);
                // Linguist lowercases the basename and tries the longest dotted suffix first, so the file name
                // "sample<ext>" always reaches the index entry for <ext> itself.
                Assert.Contains(language, runtime.FindByExtension($"sample{extension}"));
                Assert.Contains(language, runtime.FindByExtension($"SAMPLE{extension.ToUpperInvariant()}"));
                Assert.Contains(language, runtime.FindByExtension($"some/dir.with.dots/sample{extension}"));
                checkedExtensions++;
            }
        }

        Assert.True(checkedExtensions > 500, $"Only {checkedExtensions} extensions were checked.");
        Assert.Empty(runtime.FindByExtension("sample"));
        Assert.Empty(runtime.FindByExtension("sample.no-such-extension-exists"));
    }

    [Fact(Skip = "Set GHL_RUN_NATIVE_INTEGRATION=true with staged native assets.", SkipUnless = nameof(NativeIntegrationEnabled))]
    public void FilenamesResolveLikeLinguistsFilenameIndex()
    {
        using LinguistRuntime runtime = LinguistRuntime.Create();
        int checkedFilenames = 0;

        foreach (LinguistLanguage language in runtime.Languages)
        {
            foreach (string filename in language.Filenames)
            {
                // Linguist indexes filenames case-sensitively and only strips the directory part.
                Assert.Contains(language, runtime.FindByFilename(filename));
                Assert.Contains(language, runtime.FindByFilename($"some/dir/{filename}"));
                checkedFilenames++;
            }
        }

        Assert.True(checkedFilenames > 50, $"Only {checkedFilenames} filenames were checked.");
        Assert.Empty(runtime.FindByFilename("no-such-filename-exists"));
    }

    [Fact(Skip = "Set GHL_RUN_NATIVE_INTEGRATION=true with staged native assets.", SkipUnless = nameof(NativeIntegrationEnabled))]
    public void InterpretersResolveLikeLinguistsInterpreterIndex()
    {
        using LinguistRuntime runtime = LinguistRuntime.Create();
        int checkedInterpreters = 0;

        foreach (LinguistLanguage language in runtime.Languages)
        {
            foreach (string interpreter in language.Interpreters)
            {
                // Linguist indexes interpreters verbatim.
                Assert.Contains(language, runtime.FindByInterpreter(interpreter));
                checkedInterpreters++;
            }
        }

        Assert.True(checkedInterpreters > 50, $"Only {checkedInterpreters} interpreters were checked.");
        Assert.Empty(runtime.FindByInterpreter("no-such-interpreter-exists"));
    }

    private static void AddUnique(Dictionary<string, LinguistLanguage> index, string key, LinguistLanguage language, string kind)
    {
        // Linguist's indexes are plain hashes where the last definition wins, while the native indexes keep the first.
        // The pinned Linguist has no collisions; if an upgrade introduces one, decide the winner explicitly instead of
        // letting the two implementations disagree.
        if (!index.TryAdd(key, language))
        {
            Assert.Fail($"The {kind} '{key}' is defined by both '{index[key].Name}' and '{language.Name}'. Resolve the collision before relying on native lookups.");
        }
    }
}
