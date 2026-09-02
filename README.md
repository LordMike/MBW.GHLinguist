# MBW.GHLinguist

MBW.GHLinguist hosts GitHub Linguist behind a synchronous .NET 10 API. It is
designed for complete analysis of an in-memory blob, direct content
classification, and access to Linguist's language registry without exposing
Ruby or native handles to callers.

## Status

The managed API and native ABI projection are implemented, but the
`originary_linguist` native bridge and its CRuby dependency closure are not yet
packaged. Until that bridge is available, `LinguistRuntime.Create()` throws
`DllNotFoundException`. The existing `linguist.so` assets are Linguist's Ruby
tokenizer extension, not the public C ABI library.

The intended supported runtime identifiers are `win-x64` and `linux-x64`.

## Intended usage

Once the native bridge is packaged, start with `LinguistRuntime`. Create one
runtime, reuse it for calls, and dispose it when the application no longer needs
Linguist:

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

Returned languages, analyses, classifications, and traces are copied managed
objects. They remain usable after the runtime is disposed.

## Choose an API

| Goal | API | Behavior |
|---|---|---|
| Detect a file as Linguist normally would | `Analyze` | Uses complete bytes, filename/path metadata, binary checks, and the ordered detection strategies |
| Rank languages using only source content | `Classify` | Uses Linguist's classifier and at most the configured leading 50 KiB |
| Inspect every known language | `Languages` | Returns the registry in Linguist's registry order |
| Resolve registry metadata | `FindBy...` | Mirrors Linguist's Ruby lookup methods and return shapes |

Use `Analyze` for normal file-language detection. Use `Classify` when the
caller deliberately wants classifier scores without filename, shebang, modeline,
XML, or heuristic strategy selection.

`Analyze(data)` is not equivalent to `Classify(data)`. Without a `BlobInput`,
`Analyze` still performs blob checks and runs the enabled detection strategies;
it simply has no path or filename metadata. `Classify` performs classifier-only
ranking.

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
revisions.

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

Zero IDs, duplicate IDs, and IDs absent from the active runtime are rejected.
Classifier scores are similarities, not probabilities or confidence
percentages. Applications should establish their own admission policy.

## Lifetime and threading

- A runtime is thread-safe, but calls through one instance are mutually
  exclusive.
- `Dispose` waits for an active call and for the native handle release to finish.
- Calling `Dispose` repeatedly or concurrently is safe.
- Every state-dependent public member throws `ObjectDisposedException` after
  disposal.
- Results contain no native handles and remain readable after disposal.
- The v1 process runtime cannot be unloaded or reinitialized after CRuby starts.

Prefer one long-lived runtime rather than constructing a runtime per file.

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

## Build

```powershell
dotnet restore MBW.GHLinguist.slnx
dotnet build MBW.GHLinguist.slnx --configuration Release --no-restore --nologo
dotnet test --solution MBW.GHLinguist.slnx --configuration Release --no-build --minimum-expected-tests 1
```

To invoke only the test project, pass the current supported RID explicitly:

```powershell
dotnet test --project tests/MBW.GHLinguist.Tests/MBW.GHLinguist.Tests.csproj --configuration Release --runtime win-x64 --minimum-expected-tests 1
```

Use `linux-x64` instead when running on Linux.

## Native assets

RID-specific native builds stage package-ready files beneath
`.tmp/artifacts/native/<rid>`. The library project copies selected RID assets
during RID-specific builds and packages both RID trees under
`runtimes/<rid>/native`.
