using System.Security.Cryptography;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace MBW.GHLinguist;

internal static class NativeAssetIntegrity
{
    private const int ProvenanceSchemaVersion = 2;

    internal static string GetCurrentRuntimeIdentifier()
    {
        if (RuntimeInformation.ProcessArchitecture != Architecture.X64)
        {
            throw new PlatformNotSupportedException(
                $"MBW.GHLinguist supports only x64 processes; the current process architecture is {RuntimeInformation.ProcessArchitecture}.");
        }

        if (OperatingSystem.IsWindows())
        {
            return "win-x64";
        }

        if (OperatingSystem.IsLinux())
        {
            return "linux-x64";
        }

        throw new PlatformNotSupportedException("MBW.GHLinguist supports only win-x64 and linux-x64.");
    }

    internal static void Validate(string assetRoot, string expectedRuntimeIdentifier)
    {
        try
        {
            ValidateCore(assetRoot, expectedRuntimeIdentifier);
        }
        catch (LinguistException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            throw new LinguistException(
                $"Native asset integrity validation failed: {exception.Message}",
                exception);
        }
    }

    private static void ValidateCore(string assetRoot, string expectedRuntimeIdentifier)
    {
        string canonicalRoot = Path.GetFullPath(assetRoot);
        string provenancePath = Path.Combine(canonicalRoot, "provenance.json");
        if (!File.Exists(provenancePath))
        {
            throw Failure("provenance.json is missing from the deployed native asset directory.");
        }

        using FileStream provenanceStream = File.OpenRead(provenancePath);
        using JsonDocument document = JsonDocument.Parse(provenanceStream);
        JsonElement root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw Failure("provenance.json must contain a JSON object.");
        }

        if (!root.TryGetProperty("schemaVersion", out JsonElement schemaVersion) ||
            !schemaVersion.TryGetInt32(out int schema) ||
            schema != ProvenanceSchemaVersion)
        {
            throw Failure($"provenance.json must use schema version {ProvenanceSchemaVersion}.");
        }

        if (!root.TryGetProperty("platform", out JsonElement platform) ||
            platform.ValueKind != JsonValueKind.String ||
            !string.Equals(platform.GetString(), expectedRuntimeIdentifier, StringComparison.Ordinal))
        {
            throw Failure($"provenance.json does not describe the current runtime identifier '{expectedRuntimeIdentifier}'.");
        }

        if (!root.TryGetProperty("files", out JsonElement files) ||
            files.ValueKind != JsonValueKind.Array ||
            files.GetArrayLength() == 0)
        {
            throw Failure("provenance.json does not describe any native asset files.");
        }

        string rootPrefix = canonicalRoot.EndsWith(Path.DirectorySeparatorChar)
            ? canonicalRoot
            : canonicalRoot + Path.DirectorySeparatorChar;
        StringComparison pathComparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        HashSet<string> validatedPaths = new(StringComparer.Ordinal);

        foreach (JsonElement file in files.EnumerateArray())
        {
            if (file.ValueKind != JsonValueKind.Object ||
                !file.TryGetProperty("path", out JsonElement pathElement) ||
                pathElement.ValueKind != JsonValueKind.String ||
                !file.TryGetProperty("sha256", out JsonElement hashElement) ||
                hashElement.ValueKind != JsonValueKind.String)
            {
                throw Failure("provenance.json contains an invalid file entry.");
            }

            string relativePath = pathElement.GetString()!;
            string expectedSha256 = hashElement.GetString()!;
            string normalizedPath = relativePath.Replace('\\', '/');
            string[] segments = normalizedPath.Split('/');
            if (string.IsNullOrWhiteSpace(relativePath) ||
                Path.IsPathRooted(relativePath) ||
                segments.Any(segment => segment.Length == 0 || segment is "." or "..") ||
                string.Equals(normalizedPath, "provenance.json", StringComparison.OrdinalIgnoreCase))
            {
                throw Failure($"provenance.json contains an invalid asset path '{relativePath}'.");
            }

            if (!validatedPaths.Add(normalizedPath))
            {
                throw Failure($"provenance.json contains the duplicate asset path '{normalizedPath}'.");
            }

            if (expectedSha256.Length != 64 || expectedSha256.Any(character => character is not (>= '0' and <= '9') and not (>= 'a' and <= 'f')))
            {
                throw Failure($"provenance.json contains an invalid SHA-256 for '{normalizedPath}'.");
            }

            string assetPath = Path.GetFullPath(Path.Combine(canonicalRoot, normalizedPath.Replace('/', Path.DirectorySeparatorChar)));
            if (!assetPath.StartsWith(rootPrefix, pathComparison))
            {
                throw Failure($"provenance.json asset path '{normalizedPath}' escapes the deployed native asset directory.");
            }

            if (!File.Exists(assetPath))
            {
                throw Failure($"the deployed native asset '{normalizedPath}' is missing.");
            }

            using FileStream assetStream = File.OpenRead(assetPath);
            string actualSha256 = Convert.ToHexString(SHA256.HashData(assetStream)).ToLowerInvariant();
            if (!string.Equals(actualSha256, expectedSha256, StringComparison.Ordinal))
            {
                throw Failure($"the deployed native asset '{normalizedPath}' does not match its recorded SHA-256.");
            }
        }
    }

    private static LinguistException Failure(string message) =>
        new($"Native asset integrity validation failed: {message}");
}
