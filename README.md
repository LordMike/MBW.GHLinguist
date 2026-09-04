# MBW.GHLinguist

MBW.GHLinguist hosts GitHub Linguist behind a synchronous .NET 10 API. It is
designed for complete analysis of an in-memory blob, direct content
classification, and access to Linguist's language registry without exposing
Ruby or native handles to callers.

> [!IMPORTANT]
> This project is entirely vibe-coded and exists because I use it in another of
> my projects. Managed contracts, native integration, package layout, and
> clean-package consumers are exercised in CI, but the project has not been
> independently audited or benchmarked. Consumers should evaluate the documented
> limitations and native-runtime constraints for their own workloads.

## Links

- [Source repository](https://github.com/LordMike/MBW.GHLinguist)
- [GitHub Actions](https://github.com/LordMike/MBW.GHLinguist/actions)
- [GitHub Packages feed](https://nuget.pkg.github.com/LordMike/index.json)
- [NuGet.org package page (tagged releases, when published)](https://www.nuget.org/packages/MBW.GHLinguist)
- [GitHub Linguist](https://github.com/github-linguist/linguist)
- [How Linguist works](https://github.com/github-linguist/linguist/blob/196b2a14418cab005065c72c9759370934c184bc/docs/how-linguist-works.md)

## Status

The managed API, native ABI bridge, and relocatable CRuby dependency closures
are implemented for the supported runtime identifiers. The package does not
require a system Ruby installation or runtime downloads.

The supported runtime identifiers are `win-x64` and `linux-x64`. The package
targets .NET 10 and currently embeds CRuby 4.0.6 and GitHub Linguist 9.6.0 at
revision `196b2a14418cab005065c72c9759370934c184bc`.
Its explicit Ruby gem closure includes `zlib` 3.2.3 and `resolv` 0.7.2.

Development packages are published to GitHub Packages as incrementing
prereleases. NuGet.org publication is reserved for explicitly approved tagged
releases; the NuGet.org link above may therefore have no published versions yet.

## Prerequisites

- The .NET 10 SDK, not only the .NET runtime
- An x64 Windows or Linux development and deployment environment
- A consuming executable that targets `net10.0`
- A classic GitHub personal access token with `read:packages` while the package
  is distributed through GitHub Packages

Run `dotnet --info` if the SDK or host architecture is uncertain. macOS, ARM64,
.NET 9 and earlier, NativeAOT, trimming, and single-file deployment are not
supported.

## Install

The current package source is GitHub Packages. GitHub requires authentication
for its NuGet registry, including package downloads. Use a classic personal
access token with `read:packages`, and ensure that your GitHub account has access
to the package. See GitHub's
[NuGet registry documentation](https://docs.github.com/en/packages/working-with-a-github-packages-registry/working-with-the-nuget-registry)
for token and package-access details.

Add this `NuGet.config` beside your solution. Package source mapping keeps
`MBW.GHLinguist` on the GitHub feed while normal dependencies continue to come
from NuGet.org. If the solution already has a `NuGet.config`, merge these entries
instead of replacing its other sources and mappings:

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
    <add key="github_lordmike" value="https://nuget.pkg.github.com/LordMike/index.json" />
  </packageSources>
  <packageSourceMapping>
    <packageSource key="github_lordmike">
      <package pattern="MBW.GHLinguist" />
    </packageSource>
    <packageSource key="nuget.org">
      <package pattern="*" />
    </packageSource>
  </packageSourceMapping>
</configuration>
```

Keep credentials outside the file. From the directory containing the consuming
`.csproj`, set credentials for the current PowerShell session and add the latest
prerelease:

```powershell
$env:GITHUB_PACKAGES_TOKEN = "YOUR_CLASSIC_PAT"
$env:NuGetPackageSourceCredentials_github_lordmike = "Username=YOUR_GITHUB_USERNAME;Password=$env:GITHUB_PACKAGES_TOKEN;ValidAuthenticationTypes=Basic"

dotnet package add MBW.GHLinguist --prerelease
```

The environment-variable name must end with the exact source key from
`NuGet.config`: `github_lordmike`. Do not commit a token or a generated
configuration containing a clear-text password. In GitHub Actions, prefer the
workflow's `GITHUB_TOKEN` with `packages: read` after granting that repository
access to the package.

The package is large because it carries complete Windows and Linux CRuby,
Linguist, gem, tokenizer, and ICU closures. `dotnet package add` resolves the
latest eligible prerelease once and writes that exact version to the project;
review and commit the resulting `PackageReference`.

Common authentication failures:

- `401` or `NU1301`: verify the feed URL, token, and the exact
  `NuGetPackageSourceCredentials_github_lordmike` environment-variable name.
- `403`: the token's GitHub account usually lacks access to the package. The
  username must be the GitHub login, not an email address.

## Quick start

Start with `LinguistRuntime`. Create one runtime, reuse it for calls, and dispose
it when the application no longer needs Linguist. Replace the consuming console
project's `Program.cs` with:

```csharp
using MBW.GHLinguist;

using LinguistRuntime runtime = LinguistRuntime.Create();

byte[] source = "puts 'Hello'\n"u8.ToArray();
BlobAnalysis analysis = runtime.Analyze(
    source,
    new BlobInput
    {
        Path = "src/hello.rb",
        Name = "hello.rb",
    });

Console.WriteLine(analysis.Language?.Name); // Ruby
Console.WriteLine(analysis.Strategy);       // Extension
```

Run the example from the consuming project directory:

```powershell
dotnet run --runtime win-x64
```

Use `linux-x64` on Linux. For a long-running application, register one
`LinguistRuntime` as an application-lifetime singleton rather than creating one
per request or file.

Expected output:

```text
Ruby
Extension
```

Returned languages, analyses, classifications, and traces are copied managed
objects. They remain usable after the runtime is disposed.

## Choose an API

| Goal | API | Behavior |
|---|---|---|
| Run Linguist's embedded single-blob detection pipeline | `Analyze` | Uses complete bytes, filename/path metadata, binary checks, and the ordered detection strategies |
| Rank languages using only source content | `Classify` | Uses Linguist's classifier and at most the configured leading 50 KiB |
| Inspect every known language | `Languages` | Returns the registry in Linguist's registry order |
| Resolve registry metadata | `FindBy...` | Follows the corresponding Linguist lookup behavior and return shapes |

Use `Analyze` for normal file-language detection. Use `Classify` when the
caller deliberately wants classifier scores without filename, shebang, modeline,
XML, or heuristic strategy selection.

`Analyze(data)` is not equivalent to `Classify(data)`. Without a `BlobInput`,
`Analyze` still performs blob checks and runs the enabled detection strategies;
it simply has no path or filename metadata. `Classify` performs classifier-only
ranking.

## Linguist feature coverage

This package exposes a useful subset of Linguist, not the complete repository
pipeline used by GitHub.com.

| Feature | Support |
|---|---|
| Language registry and language metadata | Supported |
| Name, alias, filename, extension, and interpreter lookup | Supported |
| Single-blob detection using modeline, filename, shebang, extension, XML, manpage, heuristics, and classifier strategies | Supported |
| Binary, encoding, MIME, image, PDF, CSV, generated, vendored, documentation, LFS-pointer, detectability, and statistics-eligibility results | Supported |
| Optional physical/source line counts and strategy traces | Supported |
| Direct content classification with language-type, candidate, and byte-limit filters | Supported |
| Repository traversal and repository language percentages | Not supported |
| Git history, Rugged, or libgit2 integration | Not supported |
| `.gitattributes` parsing or `linguist-*` overrides | Not supported |
| Fetching Git LFS objects | Not supported; `IsLfsTracked` is caller-supplied metadata |
| Syntax highlighting or TextMate grammar execution | Not supported; scope metadata is returned only |
| Streaming inputs, asynchronous calls, or cancellation | Not supported |
| Parallel Ruby workers | Not supported; all Ruby work is process-wide and serialized |

The `IsIncludedInLanguageStatistics` result is a decision for one supplied blob.
It does not aggregate byte counts or reproduce GitHub's repository language bar.
Generated, vendored, and documentation results use Linguist's embedded rules for
the supplied path and bytes, but repository-specific `.gitattributes` overrides
are not applied. These omissions are the main reason results can differ from
GitHub.com even when the same Linguist revision is involved.

The result flags are not mutually exclusive. A generated or vendored blob can
still have a detected language, and `IsDetectable` does not mean that the blob
belongs in repository statistics. Check `IsIncludedInLanguageStatistics`
separately.

Classification also has narrower coverage than the registry: a registered
language can be absent from classifier results when Linguist has no classifier
centroid for it.

## Blob metadata

`BlobInput` groups metadata separately from analysis behavior so path and name
cannot be accidentally swapped:

```csharp
BlobAnalysis result = runtime.Analyze(
    data,
    new BlobInput
    {
        Path = "vendor/generated/client.cs",
        Name = "client.cs",
        IsSymlink = false,
        IsLfsTracked = false,
    },
    new BlobAnalysisOptions
    {
        IncludeLineCounts = true,
        IncludeStrategyTrace = true,
    });
```

`Path` should normally be repository-relative and use forward slashes. It
affects generated, vendored, documentation, and statistics rules. `Name`
affects filename and extension strategies. If `Name` is `null`, the native
bridge derives it from `Path` when possible. Empty strings are passed to
Linguist unchanged and are not equivalent to `null`.

Pass the actual path of one blob, not a glob or `.gitattributes` pattern.
`IsSymlink` and `IsLfsTracked` are caller assertions; `IsLfsPointer` is detected
from the supplied content. The API does not inspect Git metadata to infer either
input flag or retrieve the target of a symlink or LFS pointer.

Inspect the analysis result directly; a detected language does not imply that
the blob should count toward repository statistics:

```csharp
Console.WriteLine($"Language: {result.Language?.Name ?? "unknown"}");
Console.WriteLine($"Strategy: {result.Strategy}");
Console.WriteLine($"Encoding: {result.Encoding ?? "unknown"}");
Console.WriteLine($"Generated: {result.IsGenerated}");
Console.WriteLine($"Vendored: {result.IsVendored}");
Console.WriteLine($"Include in statistics: {result.IsIncludedInLanguageStatistics}");
```

## Language lookup

The lookup names and return shapes intentionally follow Linguist's Ruby API:

| Method | Input behavior | Result |
|---|---|---|
| `FindByName("ruby")` | Canonical/filesystem name, case-insensitive | One language or `null` |
| `FindByAlias("cpp")` | Alias, case-insensitive | One language or `null` |
| `FindByFilename("src/Cakefile")` | Exact basename, case-sensitive | Read-only list |
| `FindByExtension("src/example.rb")` | A complete filename/path, lowercased by Linguist | Read-only list |
| `FindByInterpreter("bash")` | Exact interpreter, case-sensitive | Read-only list |

`FindByExtension` is the most likely lookup footgun: pass `"example.rb"` or
`"src/example.rb"`, not the bare string `"rb"`. Linguist considers recognized
compound extensions in its own precedence order.

Name and alias lookups return `null` for an empty string. Inputs are not trimmed,
so whitespace remains significant. Passing `null` to a required lookup argument
throws `ArgumentNullException`.

`LinguistLanguage.Id` is the stable language identity used by equality and
hashing. Names and aliases are metadata and can change between Linguist
revisions. Do not persist IDs across Linguist upgrades without validating them
against the newly loaded registry.

## Classification filters

Classification is unrestricted by default:

```csharp
ClassificationResults results = runtime.Classify(source);
ClassificationResult? best = results.Results.FirstOrDefault();
Console.WriteLine(best?.Language.Name);
```

Restrict classification using language IDs from the same runtime:

```csharp
LinguistLanguage ruby = runtime.FindByName("Ruby")
    ?? throw new InvalidOperationException("Ruby is missing from the registry.");

ClassificationResults rubyOnly = runtime.Classify(
    source,
    new ClassificationOptions
    {
        CandidateLanguageIds = [ruby.Id],
        AllowedTypes = LanguageTypeMask.Programming,
        MaximumBytes = 16 * 1024,
    });
```

Candidate-list semantics are deliberate:

| `CandidateLanguageIds` | Meaning |
|---|---|
| `null` | All eligible registry languages may be classified |
| Empty list | Return an empty result without invoking the classifier |
| Non-empty list | Restrict classification to exactly those IDs |

The no-language sentinel (`ulong.MaxValue`), duplicate IDs, and IDs absent from
the active runtime are rejected. Language ID `0` is valid.
Classifier scores are similarities, not probabilities or confidence
percentages. Applications should establish their own admission policy and
inspect `ClassificationResults.ConsideredBytes` when input truncation matters.

## Lifetime and threading

- Runtime creation is synchronous and loads the embedded CRuby and Linguist
  environment. Create it during application startup when possible.
- A runtime is thread-safe, but calls through one instance are mutually
  exclusive, and all runtime instances ultimately share one process-wide Ruby
  worker. Concurrent work queues rather than running Ruby in parallel.
- There is no asynchronous or cancellation API. A caller and `Dispose` wait for
  the active native operation to finish.
- `Dispose` waits for an active call and for the native handle release to finish.
- Calling `Dispose` repeatedly or concurrently is safe.
- Every state-dependent public member throws `ObjectDisposedException` after
  disposal.
- Results contain no native handles and remain readable after disposal.
- `Dispose` releases that runtime handle, but it does not unload CRuby or stop
  the process-wide worker.
- The v1 process runtime cannot be unloaded or reinitialized after CRuby starts,
  and all runtime instances in one process must use the same asset root.

Prefer one long-lived runtime rather than constructing a runtime per file.
Creating and disposing runtimes repeatedly does not repeatedly unload the native
runtime and provides no parallelism benefit.

> [!WARNING]
> The first `LinguistRuntime.Create()` call that reaches embedded CRuby
> initialization permanently determines native process state, including a failed
> initialization. If CRuby startup fails because the closure is missing, corrupt,
> or incompatible, repair the deployment and restart the process. Retrying in the
> same process will not recover. Failures before native initialization begins,
> such as locating the primary bridge, do not poison the worker.

After all managed runtime handles are disposed, the native worker thread, queue,
CRuby heap, loaded libraries, and Ruby state remain resident until process exit.
Disposal is not a native-memory reclamation mechanism.

## Native deployment and footguns

The package contains native code and a full private Ruby environment. Treat the
RID-specific closure as one immutable deployment unit.

The managed assembly, `ghlinguist` ABI bridge, and native closure are a matched
unit. Do not manually combine files from different package versions.

### Select a supported RID

Set the runtime identifier on the executable application, especially for
publishing or cross-building:

```xml
<PropertyGroup>
  <RuntimeIdentifier>win-x64</RuntimeIdentifier>
</PropertyGroup>
```

Use `linux-x64` on Linux. If no RID is specified, the package falls back to the
.NET SDK host RID. That is convenient for local builds but unsafe for
cross-publishing. An unsupported RID can produce a successful managed build
without copying a native closure, followed by `DllNotFoundException` when the
application starts.

Restore/build success therefore does not prove that native deployment is valid.
Publish for the target RID and run the produced directory on that platform:

```powershell
dotnet publish --configuration Release --runtime win-x64 --self-contained false
```

In CI, verify that the publish directory contains `ghlinguist.dll` on Windows or
`ghlinguist.so` on Linux, plus the complete adjacent Ruby/Linguist closure.

### Preserve the complete output layout

The package's transitive build target copies the selected closure beside the
managed assembly while preserving subdirectories. Do not copy only
`ghlinguist.dll` or `ghlinguist.so`, and do not flatten, rename, trim, or
selectively clean the copied files. The bridge also needs the adjacent CRuby
runtime, Ruby standard library, gems, ICU libraries, Linguist sources and data,
and tokenizer extension.

Some resources, including classifier data, can be loaded on first use rather
than at `LinguistRuntime.Create()`. Do not replace or delete the native closure
while the process is running. Deploy package upgrades as a complete unit and
restart the process.

Publish upgrades into a fresh directory or atomically swap whole directories.
Do not overlay a new package onto an old output: incremental copying does not
guarantee that files removed by a newer closure disappear from the destination.

### Unsupported deployment modes

These configurations are not supported:

- Single-file publishing
- NativeAOT
- Trimming
- Collectible `AssemblyLoadContext` unloading
- Loading different package/asset versions into one process
- Continuing to use an initialized runtime in a forked child process
- `win-arm64`, `linux-arm64`, macOS, or any RID other than `win-x64` and
  `linux-x64`

Directory-based framework-dependent publishing is exercised by CI. A
self-contained directory publish is compatible with the intended layout but
must be tested by the consumer. Always test the exact publish directory on the
same RID used in production.

Do not use plugin isolation to load different package versions or asset roots.
Unloading a collectible `AssemblyLoadContext` cannot unload the process-wide
CRuby or native libraries; use one version and one asset root per process.

### Bound memory and throughput

`Analyze` accepts and copies the complete supplied blob into native request
storage. It has no streaming interface and no built-in maximum file size.
Applications processing untrusted repositories should enforce their own size,
queue, timeout, and admission limits before calling the API. `Classify` is
different: it considers at most the leading 50 KiB.

During `Analyze`, the caller's complete buffer remains live while native request
storage holds another copy, before Ruby and result allocations are considered.
The operation cannot be interrupted by an in-process timeout or cancellation.
For hostile or untrusted workloads, enforce hard per-blob and total in-flight
byte limits and consider a separate worker process that the application can
terminate.

Because all Ruby work is serialized, adding concurrent callers increases queue
depth rather than Linguist throughput. If this becomes a bottleneck, scale with
separate processes, not additional `LinguistRuntime` instances in one process.
Thread-safe means serialized, not parallel, bounded, fair, or guaranteed to
finish within a particular time. No benchmark results are currently published;
measure startup, latency, working set, and queueing with representative data.

## Runtime capabilities

`LinguistRuntime.Capabilities` describes features exposed by the loaded native
runtime. The managed facade checks required capabilities before starting an
operation and throws `NotSupportedException` rather than returning a partial or
misleading result.

| Operation | Required capabilities |
|---|---|
| `Languages` and every `FindBy...` method | `LanguageRegistry` |
| `Analyze` | `LanguageRegistry`, `StandardDetection`, `EncodingAndBinaryDetection`, `GeneratedDetection`, and `PathClassification` |
| `Analyze` with `IncludeStrategyTrace` | The normal analysis capabilities plus `StrategyTrace` |
| `Analyze` with the `Classifier` strategy enabled | The normal analysis capabilities plus `ContentClassifier` |
| `Classify` | `LanguageRegistry` and `ContentClassifier` |
| `Classify` with an explicit empty candidate list | No classifier call is made; an empty result is returned |

The default analysis strategy mask includes `Classifier`, so normal
`Analyze(...)` calls require `ContentClassifier`. A caller can explicitly remove
that strategy when working with a runtime that supports the rest of the analysis
pipeline.

## Exceptions

| Exception | Meaning |
|---|---|
| `DllNotFoundException` | The native ABI library or a dependency is unavailable |
| `BadImageFormatException` | A native asset targets the wrong architecture or platform |
| `ArgumentNullException` | A required managed argument is `null` |
| `ArgumentException` | Metadata, candidate IDs, or UTF-16 input is invalid |
| `NotSupportedException` | The loaded runtime lacks a capability required by the operation |
| `OutOfMemoryException` | Managed or native allocation failed, commonly because an input or workload was not bounded |
| `ObjectDisposedException` | A state-dependent member was used after disposal |
| `LinguistException` | The native runtime returned malformed data or another native failure |
| `LinguistRubyException` | The native bridge captured and copied a Ruby exception |

## Discovering the API

Begin with these types in IntelliSense:

- `LinguistRuntime` for lifecycle and operations
- `BlobInput` for path and filename metadata
- `BlobAnalysisOptions` for optional trace and line-count work
- `ClassificationOptions` for classifier bounds and filters
- `BlobAnalysis` and `ClassificationResults` for returned values
- `LinguistLanguage` for registry metadata and stable language identity

The NuGet package includes `MBW.GHLinguist.xml`, so public members include
IntelliSense descriptions, examples, example results, exceptions, and links to
the corresponding pinned GitHub Linguist source where available.

## Build from source

```powershell
dotnet restore MBW.GHLinguist.slnx
dotnet build MBW.GHLinguist.slnx --configuration Release --no-restore --nologo
dotnet test --solution MBW.GHLinguist.slnx --configuration Release --no-build --minimum-expected-tests 1
```

Those commands exercise managed contracts but do not stage or run the embedded
CRuby closure. For a Windows native integration run:

```powershell
./eng/linguist/build-windows.ps1
$env:GHL_RUN_NATIVE_INTEGRATION = "true"
dotnet test --project tests/MBW.GHLinguist.Tests/MBW.GHLinguist.Tests.csproj --configuration Release --runtime win-x64 -p:RunNativeIntegrationTests=true --minimum-expected-tests 1
```

For Linux, run `./eng/linguist/build-docker.ps1` and use `linux-x64` in the test
command. See `eng/linguist/README.md` for the native build prerequisites.

## Building native assets

RID-specific native builds stage package-ready files beneath
`.tmp/artifacts/native/<rid>`. The package exposes the ABI bridge under
`runtimes/<rid>/native` and stores each complete relocatable closure under
`nativeassets/<rid>`. Its `buildTransitive` target copies the selected closure
with its relative layout intact for build and publish. When a project does not
set `RuntimeIdentifier`, the target selects the .NET SDK host RID.
