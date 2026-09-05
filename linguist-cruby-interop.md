---
status: DRAFT
created: 2026-09-02T18:21:01+02:00
---

# GitHub Linguist CRuby interop

## Goal

Package GitHub Linguist 9.6.0 at commit `196b2a14418cab005065c72c9759370934c184bc` behind a stable C ABI and managed .NET facade. Support complete single-blob analysis, content classification, and language metadata without requiring Ruby installation on the consuming machine.

Pin CRuby 4.0.6 and lock every native and Ruby dependency by version, source hash, license, RID, and built-binary hash.

## Settled decisions

- Support `win-x64` and `linux-x64`.
- Provide one primary interop library plus an adjacent, pinned dependency closure.
- Package a managed facade and native assets together as `MBW.GHLinguist`.
- Initialize CRuby and Linguist once per process.
- Execute Ruby only on a dedicated native worker thread.
- Serialize calls through that worker initially.
- Keep copied native result objects independent of Ruby GC.
- Never expose Ruby objects, callbacks, allocators, or exceptions through the ABI.
- Include complete blob behavior but defer repository traversal, repository statistics, Rugged/libgit2, and `.gitattributes` orchestration.
- Include encoding, binary, generated, vendored, documentation, language-statistics eligibility, standard detection, unrestricted classification, strategy traces, and language-registry access.
- Preserve Linguist's default 50 KiB classifier prefix while allowing an explicitly smaller caller bound.
- Do not integrate the package into ID 21 as part of this plan.

CRuby's embedding APIs support a hosted interpreter, but its documented GVL entry point cannot turn an arbitrary foreign thread into a Ruby thread. The dedicated worker avoids invoking Ruby from arbitrary .NET thread-pool threads.

## C ABI

The public header is `include/ghlinguist.h`. Only symbols with the `ghl_` prefix are exported.

```c
#ifndef GHLINGUIST_H
#define GHLINGUIST_H

#include <stddef.h>
#include <stdint.h>

#if defined(_WIN32)
#define GHL_API __declspec(dllexport)
#define GHL_CALL __cdecl
#else
#define GHL_API __attribute__((visibility("default")))
#define GHL_CALL
#endif

#ifdef __cplusplus
extern "C" {
#endif

#define GHL_ABI_VERSION_MAJOR 1u
#define GHL_ABI_VERSION_MINOR 0u

typedef struct ghl_runtime ghl_runtime;
typedef struct ghl_analysis ghl_analysis;
typedef struct ghl_classification ghl_classification;
typedef struct ghl_language_id_list ghl_language_id_list;
typedef struct ghl_error ghl_error;

typedef int32_t ghl_status;
#define GHL_STATUS_OK                 ((ghl_status)0)
#define GHL_STATUS_INVALID_ARGUMENT   ((ghl_status)1)
#define GHL_STATUS_ABI_MISMATCH       ((ghl_status)2)
#define GHL_STATUS_UNSUPPORTED        ((ghl_status)3)
#define GHL_STATUS_NOT_FOUND          ((ghl_status)4)
#define GHL_STATUS_INVALID_UTF8       ((ghl_status)5)
#define GHL_STATUS_RUBY_EXCEPTION     ((ghl_status)6)
#define GHL_STATUS_NATIVE_FAILURE     ((ghl_status)7)
#define GHL_STATUS_OUT_OF_MEMORY      ((ghl_status)8)
#define GHL_STATUS_INTERNAL_ERROR     ((ghl_status)9)

typedef struct ghl_string_view {
    const char* data;
    size_t length;
} ghl_string_view;

typedef struct ghl_bytes_view {
    const uint8_t* data;
    size_t length;
} ghl_bytes_view;

typedef uint64_t ghl_capabilities;
#define GHL_CAP_LANGUAGE_REGISTRY     (UINT64_C(1) << 0)
#define GHL_CAP_STANDARD_DETECTION    (UINT64_C(1) << 1)
#define GHL_CAP_CONTENT_CLASSIFIER    (UINT64_C(1) << 2)
#define GHL_CAP_STRATEGY_TRACE        (UINT64_C(1) << 3)
#define GHL_CAP_ENCODING_BINARY       (UINT64_C(1) << 4)
#define GHL_CAP_GENERATED_DETECTION   (UINT64_C(1) << 5)
#define GHL_CAP_PATH_CLASSIFICATION   (UINT64_C(1) << 6)

typedef uint32_t ghl_language_type;
#define GHL_LANGUAGE_TYPE_UNKNOWN      0u
#define GHL_LANGUAGE_TYPE_DATA         1u
#define GHL_LANGUAGE_TYPE_MARKUP       2u
#define GHL_LANGUAGE_TYPE_PROGRAMMING  3u
#define GHL_LANGUAGE_TYPE_PROSE        4u

typedef uint32_t ghl_language_type_mask;
#define GHL_LANGUAGE_MASK_DATA         (1u << 0)
#define GHL_LANGUAGE_MASK_MARKUP       (1u << 1)
#define GHL_LANGUAGE_MASK_PROGRAMMING  (1u << 2)
#define GHL_LANGUAGE_MASK_PROSE        (1u << 3)
#define GHL_LANGUAGE_MASK_ALL          UINT32_C(0x0f)

typedef uint32_t ghl_strategy;
#define GHL_STRATEGY_NONE        0u
#define GHL_STRATEGY_MODELINE    1u
#define GHL_STRATEGY_FILENAME    2u
#define GHL_STRATEGY_SHEBANG     3u
#define GHL_STRATEGY_EXTENSION   4u
#define GHL_STRATEGY_XML         5u
#define GHL_STRATEGY_MANPAGE     6u
#define GHL_STRATEGY_HEURISTICS  7u
#define GHL_STRATEGY_CLASSIFIER  8u

typedef uint32_t ghl_strategy_mask;
#define GHL_STRATEGY_MASK_MODELINE    (1u << 0)
#define GHL_STRATEGY_MASK_FILENAME    (1u << 1)
#define GHL_STRATEGY_MASK_SHEBANG     (1u << 2)
#define GHL_STRATEGY_MASK_EXTENSION   (1u << 3)
#define GHL_STRATEGY_MASK_XML         (1u << 4)
#define GHL_STRATEGY_MASK_MANPAGE     (1u << 5)
#define GHL_STRATEGY_MASK_HEURISTICS  (1u << 6)
#define GHL_STRATEGY_MASK_CLASSIFIER  (1u << 7)
#define GHL_STRATEGY_MASK_DEFAULT     UINT32_C(0xff)

typedef uint32_t ghl_blob_input_flags;
#define GHL_BLOB_INPUT_SYMLINK      (1u << 0)
#define GHL_BLOB_INPUT_LFS_TRACKED  (1u << 1)

typedef uint64_t ghl_blob_result_flags;
#define GHL_BLOB_LIKELY_BINARY          (UINT64_C(1) << 0)
#define GHL_BLOB_BINARY                 (UINT64_C(1) << 1)
#define GHL_BLOB_TEXT                   (UINT64_C(1) << 2)
#define GHL_BLOB_IMAGE                  (UINT64_C(1) << 3)
#define GHL_BLOB_SOLID                  (UINT64_C(1) << 4)
#define GHL_BLOB_CSV                    (UINT64_C(1) << 5)
#define GHL_BLOB_PDF                    (UINT64_C(1) << 6)
#define GHL_BLOB_LARGE                  (UINT64_C(1) << 7)
#define GHL_BLOB_VIEWABLE               (UINT64_C(1) << 8)
#define GHL_BLOB_SAFE_TO_COLORIZE       (UINT64_C(1) << 9)
#define GHL_BLOB_HIGH_LONG_LINE_RATIO   (UINT64_C(1) << 10)
#define GHL_BLOB_LFS_POINTER            (UINT64_C(1) << 11)
#define GHL_BLOB_VENDORED               (UINT64_C(1) << 12)
#define GHL_BLOB_DOCUMENTATION          (UINT64_C(1) << 13)
#define GHL_BLOB_GENERATED              (UINT64_C(1) << 14)
#define GHL_BLOB_DETECTABLE             (UINT64_C(1) << 15)
#define GHL_BLOB_INCLUDE_IN_STATS       (UINT64_C(1) << 16)

typedef uint32_t ghl_analysis_option_flags;
#define GHL_ANALYSIS_ALLOW_EMPTY          (1u << 0)
#define GHL_ANALYSIS_INCLUDE_TRACE        (1u << 1)
#define GHL_ANALYSIS_INCLUDE_LINE_COUNTS  (1u << 2)

typedef uint32_t ghl_analysis_text_field;
#define GHL_ANALYSIS_TEXT_MIME_TYPE       1u
#define GHL_ANALYSIS_TEXT_CONTENT_TYPE    2u
#define GHL_ANALYSIS_TEXT_DISPOSITION     3u
#define GHL_ANALYSIS_TEXT_ENCODING        4u
#define GHL_ANALYSIS_TEXT_RUBY_ENCODING   5u
#define GHL_ANALYSIS_TEXT_TM_SCOPE        6u

typedef uint32_t ghl_language_text_field;
#define GHL_LANGUAGE_TEXT_NAME                    1u
#define GHL_LANGUAGE_TEXT_FS_NAME                 2u
#define GHL_LANGUAGE_TEXT_COLOR                   3u
#define GHL_LANGUAGE_TEXT_TM_SCOPE                4u
#define GHL_LANGUAGE_TEXT_ACE_MODE                5u
#define GHL_LANGUAGE_TEXT_CODEMIRROR_MODE         6u
#define GHL_LANGUAGE_TEXT_CODEMIRROR_MIME_TYPE    7u

typedef uint32_t ghl_language_collection;
#define GHL_LANGUAGE_COLLECTION_ALIASES       1u
#define GHL_LANGUAGE_COLLECTION_EXTENSIONS    2u
#define GHL_LANGUAGE_COLLECTION_INTERPRETERS  3u
#define GHL_LANGUAGE_COLLECTION_FILENAMES     4u

typedef uint32_t ghl_lookup_kind;
#define GHL_LOOKUP_NAME         1u
#define GHL_LOOKUP_ALIAS        2u
#define GHL_LOOKUP_FILENAME     3u
#define GHL_LOOKUP_EXTENSION    4u
#define GHL_LOOKUP_INTERPRETER  5u

typedef struct ghl_runtime_options {
    uint32_t struct_size;
    uint32_t flags;
    ghl_string_view asset_root;
    uint64_t reserved[4];
} ghl_runtime_options;

typedef struct ghl_blob_input {
    uint32_t struct_size;
    ghl_blob_input_flags flags;
    ghl_string_view path;
    ghl_string_view name;
    ghl_bytes_view data;
    uint64_t reserved[4];
} ghl_blob_input;

typedef struct ghl_analysis_options {
    uint32_t struct_size;
    ghl_analysis_option_flags flags;
    ghl_strategy_mask strategies;
    uint32_t reserved32;
    uint64_t reserved[4];
} ghl_analysis_options;

typedef struct ghl_classify_options {
    uint32_t struct_size;
    uint32_t flags;
    ghl_language_type_mask allowed_types;
    uint32_t maximum_bytes;
    const uint64_t* candidate_language_ids;
    size_t candidate_language_count;
    uint64_t reserved[4];
} ghl_classify_options;

typedef struct ghl_version_info {
    uint32_t struct_size;
    uint32_t abi_major;
    uint32_t abi_minor;
    uint32_t reserved32;
    ghl_string_view wrapper_version;
    ghl_string_view ruby_version;
    ghl_string_view linguist_version;
    ghl_string_view linguist_revision;
    ghl_string_view classifier_sha256;
    uint64_t reserved[4];
} ghl_version_info;

typedef struct ghl_language_info {
    uint32_t struct_size;
    ghl_language_type type;
    uint64_t language_id;
    uint64_t group_language_id;
    uint32_t flags;
    uint32_t alias_count;
    uint32_t extension_count;
    uint32_t interpreter_count;
    uint32_t filename_count;
    ghl_string_view name;
    ghl_string_view fs_name;
    ghl_string_view color;
    ghl_string_view tm_scope;
    ghl_string_view ace_mode;
    ghl_string_view codemirror_mode;
    ghl_string_view codemirror_mime_type;
    uint64_t reserved[4];
} ghl_language_info;

#define GHL_LANGUAGE_POPULAR  (1u << 0)
#define GHL_LANGUAGE_WRAP     (1u << 1)

typedef struct ghl_strategy_trace_entry {
    uint32_t struct_size;
    ghl_strategy strategy;
    uint32_t candidate_count;
    uint32_t reserved32;
    uint64_t reserved[4];
} ghl_strategy_trace_entry;

GHL_API uint32_t GHL_CALL ghl_abi_version_major(void);
GHL_API uint32_t GHL_CALL ghl_abi_version_minor(void);

GHL_API ghl_status GHL_CALL ghl_runtime_create(
    const ghl_runtime_options* options,
    ghl_runtime** out_runtime,
    ghl_error** out_error);

GHL_API void GHL_CALL ghl_runtime_release(ghl_runtime* runtime);

GHL_API ghl_capabilities GHL_CALL ghl_runtime_capabilities(
    const ghl_runtime* runtime);

GHL_API ghl_status GHL_CALL ghl_runtime_version(
    const ghl_runtime* runtime,
    ghl_version_info* out_version);

GHL_API size_t GHL_CALL ghl_runtime_language_count(
    const ghl_runtime* runtime);

GHL_API ghl_status GHL_CALL ghl_runtime_language_id_at(
    const ghl_runtime* runtime,
    size_t index,
    uint64_t* out_language_id);

GHL_API ghl_status GHL_CALL ghl_runtime_language_info(
    const ghl_runtime* runtime,
    uint64_t language_id,
    ghl_language_info* out_info);

GHL_API ghl_status GHL_CALL ghl_runtime_language_collection_value(
    const ghl_runtime* runtime,
    uint64_t language_id,
    ghl_language_collection collection,
    size_t index,
    ghl_string_view* out_value);

GHL_API ghl_status GHL_CALL ghl_runtime_lookup_languages(
    const ghl_runtime* runtime,
    ghl_lookup_kind kind,
    ghl_string_view value,
    ghl_language_id_list** out_languages,
    ghl_error** out_error);

GHL_API ghl_status GHL_CALL ghl_runtime_analyze(
    const ghl_runtime* runtime,
    const ghl_blob_input* blob,
    const ghl_analysis_options* options,
    ghl_analysis** out_analysis,
    ghl_error** out_error);

GHL_API ghl_status GHL_CALL ghl_runtime_classify(
    const ghl_runtime* runtime,
    ghl_bytes_view data,
    const ghl_classify_options* options,
    ghl_classification** out_classification,
    ghl_error** out_error);

GHL_API void GHL_CALL ghl_analysis_release(ghl_analysis* analysis);

GHL_API uint64_t GHL_CALL ghl_analysis_language_id(
    const ghl_analysis* analysis);

GHL_API ghl_strategy GHL_CALL ghl_analysis_strategy(
    const ghl_analysis* analysis);

GHL_API ghl_blob_result_flags GHL_CALL ghl_analysis_flags(
    const ghl_analysis* analysis);

GHL_API uint64_t GHL_CALL ghl_analysis_loc(
    const ghl_analysis* analysis);

GHL_API uint64_t GHL_CALL ghl_analysis_sloc(
    const ghl_analysis* analysis);

GHL_API ghl_status GHL_CALL ghl_analysis_text(
    const ghl_analysis* analysis,
    ghl_analysis_text_field field,
    ghl_string_view* out_value);

GHL_API size_t GHL_CALL ghl_analysis_trace_count(
    const ghl_analysis* analysis);

GHL_API ghl_status GHL_CALL ghl_analysis_trace_entry(
    const ghl_analysis* analysis,
    size_t index,
    ghl_strategy_trace_entry* out_entry);

GHL_API ghl_status GHL_CALL ghl_analysis_trace_candidate(
    const ghl_analysis* analysis,
    size_t trace_index,
    size_t candidate_index,
    uint64_t* out_language_id);

GHL_API void GHL_CALL ghl_classification_release(
    ghl_classification* classification);

GHL_API size_t GHL_CALL ghl_classification_count(
    const ghl_classification* classification);

GHL_API uint32_t GHL_CALL ghl_classification_considered_bytes(
    const ghl_classification* classification);

GHL_API ghl_status GHL_CALL ghl_classification_result(
    const ghl_classification* classification,
    size_t index,
    uint64_t* out_language_id,
    double* out_score);

GHL_API void GHL_CALL ghl_language_id_list_release(
    ghl_language_id_list* languages);

GHL_API size_t GHL_CALL ghl_language_id_list_count(
    const ghl_language_id_list* languages);

GHL_API ghl_status GHL_CALL ghl_language_id_list_at(
    const ghl_language_id_list* languages,
    size_t index,
    uint64_t* out_language_id);

GHL_API ghl_status GHL_CALL ghl_error_status(
    const ghl_error* error);

GHL_API ghl_string_view GHL_CALL ghl_error_message(
    const ghl_error* error);

GHL_API ghl_string_view GHL_CALL ghl_error_ruby_class(
    const ghl_error* error);

GHL_API ghl_string_view GHL_CALL ghl_error_ruby_backtrace(
    const ghl_error* error);

GHL_API void GHL_CALL ghl_error_release(ghl_error* error);

#ifdef __cplusplus
}
#endif

#endif
```

## ABI semantics

- Input spans are borrowed only for the synchronous call.
- Output string views live until their containing runtime, result, list, or error handle is released.
- A null pointer with nonzero length is invalid.
- All input paths, names, and lookup values are strict UTF-8.
- Analysis receives complete blob bytes; prefix-only operation belongs to `ghl_runtime_classify`.
- `maximum_bytes == 0` uses Linguist's 50 KiB default.
- A nonzero classifier bound cannot exceed 50 KiB in ABI v1.
- Classification scores are similarity scores, not probabilities or confidence.
- `language_id == UINT64_MAX` means no detected language; zero is a valid Linguist language ID.
- `group_language_id == UINT64_MAX` means the language is not grouped under another language.
- Every exported function is thread-safe.
- Ruby work is synchronous but serialized through the process worker.
- `ghl_runtime_create` blocks until CRuby, bridge code, language data, heuristics, generated rules, MIME data, and classifier centroids are loaded.
- Additional runtime handles share the initialized process runtime.
- Releasing the final runtime handle does not finalize CRuby.
- Runtime unloading, reinitialization, collectible-load-context unloading, and post-fork use are unsupported in v1.
- Ruby exceptions are caught with `rb_protect` and copied into `ghl_error`.
- No native allocation is returned without an associated release function.

## Managed facade

Add `src/MBW.GHLinguist` targeting `net10.0`. Use source-generated `LibraryImport`, `SafeHandle`, strict UTF-8 marshaling, checked spans, immutable managed result records, deterministic disposal, a native-library resolver, asset hash verification, and status-to-exception translation.

The public managed surface is:

```csharp
public sealed class LinguistRuntime : IDisposable
{
    public static LinguistRuntime Create();
    public LinguistVersionInfo Version { get; }
    public LinguistCapabilities Capabilities { get; }
    public IReadOnlyList<LinguistLanguage> Languages { get; }

    public BlobAnalysis Analyze(
        ReadOnlySpan<byte> data,
        BlobInput? input = null,
        BlobAnalysisOptions? options = null);

    public ClassificationResults Classify(
        ReadOnlySpan<byte> data,
        ClassificationOptions? options = null);

    public LinguistLanguage? FindByName(string name);
    public LinguistLanguage? FindByAlias(string alias);
    public IReadOnlyList<LinguistLanguage> FindByFilename(string filename);
    public IReadOnlyList<LinguistLanguage> FindByExtension(string filename);
    public IReadOnlyList<LinguistLanguage> FindByInterpreter(string interpreter);

    public void Dispose();
}
```

`BlobInput` groups optional path, filename, symlink, and Git LFS metadata so
callers cannot accidentally swap adjacent `path` and `name` arguments.
Omitting `BlobInput` removes path and filename metadata but does not turn
`Analyze` into classifier-only operation: blob checks and enabled detection
strategies still run. `Classify` is the classifier-only API.

The lookup methods mirror Linguist's Ruby names and return shapes: name and
alias lookups return one language or `null`, while filename, extension, and
interpreter lookups return read-only lists. `FindByExtension` accepts a complete
filename or path, matching Linguist's `find_by_extension`, rather than a bare
extension.

All public runtime instance methods and properties that require native state
throw `ObjectDisposedException` after disposal. Disposal is idempotent and
serialized with active calls. Returned languages, analyses, classifications,
and traces are copied managed values and remain usable after their runtime is
disposed. Every public API ships XML documentation with usage examples, example
results, and links to the corresponding pinned Linguist documentation or source
when available.

Classifier candidate IDs preserve Ruby's `nil` versus empty-array distinction:
`null` means unrestricted classification, while an empty list returns no
matches without invoking the classifier. Zero, duplicate, and unknown language
IDs are rejected by the managed facade before native classification.

The managed facade checks `LinguistCapabilities` before dispatch. Registry
operations require `LanguageRegistry`; complete analysis additionally requires
standard detection, encoding/binary, generated detection, and path
classification, plus trace or classifier capabilities when those features are
enabled. Direct non-empty classification requires both language-registry and
content-classifier capabilities.

## NuGet package

Publish one managed package and one runtime package for each supported RID. The
runtime packages depend on the exact matching version of the managed package.

The managed `MBW.GHLinguist` package layout is:

```text
lib/net10.0/MBW.GHLinguist.dll
lib/net10.0/MBW.GHLinguist.xml
buildTransitive/MBW.GHLinguist.targets
LICENSE
THIRD-PARTY-NOTICES.md
native/include/ghlinguist.h
```

Each `MBW.GHLinguist.Runtime.<rid>` package contains only its matching closure:

```text
runtimes/<rid>/native/ghlinguist.dll|so
nativeassets/<rid>/*
nativeassets/<rid>/licenses/*
buildTransitive/MBW.GHLinguist.Runtime.<rid>.targets
LICENSE
THIRD-PARTY-NOTICES.md
```

Use `buildTransitive` to copy the complete selected closure into an
`MBW.GHLinguist` directory beside the managed assembly while preserving
subdirectories. Prefer an explicit supported `RuntimeIdentifier`; when it is
absent, select the .NET SDK host RID. Referencing a runtime package must provide
the managed assembly and native closure automatically. Referencing only the
managed package must fail with guidance to select exactly one supported runtime
package.

## Work

1. Obtain approval for the project documentation changes, then document the C ABI, process-lifetime runtime, supported RIDs, copied-result ownership, serialized execution, unsupported unloading, package layout, and deferred repository API without changing schema, format, or documentation versions.
2. Pin CRuby 4.0.6 and Linguist 9.6.0 commit `196b2a14418cab005065c72c9759370934c184bc`. Generate a locked dependency manifest for `charlock_holmes`, ICU, `mini_mime`, CGI, `zlib`, `resolv`, Psych/libyaml, and the Linguist tokenizer extension. Exclude Rugged/libgit2 because repository behavior is deferred. Record source and binary SHA-256 values, licenses, patches, build flags, and redistribution decisions.
3. Add `src/MBW.GHLinguist.Native` with CMake-based Windows and Linux builds, the public header, C ABI implementation, worker queue, process runtime, Ruby bridge, export lists, symbol-visibility controls, and deterministic asset-location logic.
4. Start one native worker on the first runtime creation and initialize CRuby on that thread. Cache Ruby constants, method IDs, language metadata, classifier data, and immutable native copies. Send every Ruby operation through the synchronous request queue. Do not use `rb_thread_call_with_gvl` from arbitrary managed threads.
5. Add a Ruby `InteropBlob` matching Linguist's blob contract. Implement standard strategy detection, strategy tracing, unrestricted or candidate-filtered classification, language registry projection, binary and encoding analysis, MIME results, generated/vendored/documentation predicates, line counts, and statistics eligibility. Copy all returned data into native-owned result objects before leaving the worker.
6. Produce reviewed `win-x64` and `linux-x64` directories containing the primary interop library, CRuby runtime, Linguist tokenizer, Charlock Holmes, ICU, Psych/libyaml, required Ruby standard-library files, Linguist Ruby files, classifier data, language data, MIME data, and licenses. Configure Windows adjacent-DLL loading and Linux `$ORIGIN` lookup without consulting system Ruby or system ICU.
7. Add the managed facade and keep all native handles internal. Copy native result values into immutable managed records so callers do not manage result lifetimes or native string views.
8. Pack the managed facade, public header, and notices as `MBW.GHLinguist`. Pack each native dependency closure, provenance file, and RID-specific copy target as `MBW.GHLinguist.Runtime.<rid>` with an exact dependency on the managed package version.
9. Add `tests/MBW.GHLinguist.Tests` as an xUnit v3 project that references the managed facade and invokes the actual native runtime.
10. Pack all three packages into an ignored `.tmp/packages` feed and restore a minimal fixture consumer using one RID runtime `PackageReference`. Build and run it without Ruby, Linguist, ICU, or other development tools installed globally.
11. Add the managed package project, both runtime package projects, and `MBW.GHLinguist.Tests` to `MBW.GHLinguist.slnx`. Keep native build orchestration beneath the interop project rather than registering a non-MSBuild project directly.

## Tests

- Verify ABI, wrapper, Ruby, Linguist, revision, and classifier-hash reporting.
- Verify complete enumeration and lookup of known languages.
- Verify detection by filename, extension, shebang, modeline, XML declaration, heuristic, and classifier.
- Verify content-only C#, Python, JavaScript, JSON, XML, YAML, and broken or truncated source classification.
- Verify candidate filters and programming, markup, data, and prose type filters.
- Verify that the classifier considers no more than the first 50 KiB.
- Verify binary, encoding, MIME, generated, vendored, documentation, and language-statistics results.
- Verify strategy trace ordering and candidate lists.
- Verify empty input, invalid UTF-8 metadata, invalid IDs, invalid indexes, null pointers, Ruby exceptions, and native failures.
- Verify repeated runtime creation shares one initialized runtime.
- Verify concurrent calls from managed thread-pool threads.
- Verify disposal, forced GC, repeated result creation, and no use-after-release.
- Verify missing, corrupt, incorrect-RID, and hash-mismatched assets.
- Verify exact result equality across `win-x64` and `linux-x64`.
- Verify the managed nupkg contains the notices, header, managed assembly, and no runtime closure assets. Verify each runtime nupkg contains only its expected RID closure, provenance, licenses, copy target, and exact managed-package dependency, with no build-machine paths.

## Validation

```powershell
dotnet build src/MBW.GHLinguist/MBW.GHLinguist.csproj --configuration Release --nologo
dotnet pack src/MBW.GHLinguist/MBW.GHLinguist.csproj --configuration Release --no-build --nologo --output .tmp/packages
dotnet build src/MBW.GHLinguist.Runtime.win-x64/MBW.GHLinguist.Runtime.win-x64.csproj --configuration Release --nologo
dotnet pack src/MBW.GHLinguist.Runtime.win-x64/MBW.GHLinguist.Runtime.win-x64.csproj --configuration Release --no-build --nologo --output .tmp/packages -p:ValidateRidAssets=true
dotnet build src/MBW.GHLinguist.Runtime.linux-x64/MBW.GHLinguist.Runtime.linux-x64.csproj --configuration Release --nologo
dotnet pack src/MBW.GHLinguist.Runtime.linux-x64/MBW.GHLinguist.Runtime.linux-x64.csproj --configuration Release --no-build --nologo --output .tmp/packages -p:ValidateRidAssets=true
dotnet build MBW.GHLinguist.slnx --configuration Release --nologo
dotnet test --solution MBW.GHLinguist.slnx --configuration Release --no-build --minimum-expected-tests 1
git diff --check
```

Inspect Windows exports with `dumpbin /exports`, Linux exports with `nm -D`, dependency closure with `dumpbin /dependents` and `ldd`, exact package contents, hashes, licenses, and documentation references.

## Completion

This plan is complete when a clean .NET consumer can add one matching runtime
package reference, receive the exact managed dependency automatically, initialize
the runtime once, invoke the complete blob and classifier APIs repeatedly from
arbitrary managed threads, receive deterministic copied results through the
managed facade or documented C ABI, and run successfully on both supported RIDs
without a system Ruby installation or runtime downloads.

## Deferred

- Integration with `SourceTextExtractor` or replacement of Tree-sitter.
- Thresholding and admission policy for Originary ID 21.
- Git repository traversal and language-percentage calculation.
- Rugged/libgit2.
- `.gitattributes` parsing and Linguist override orchestration.
- Streaming callbacks or `ContentAccessor` integration.
- Asynchronous requests and cancellation.
- Multiple concurrent Ruby workers or processes.
- macOS and ARM64 RIDs.
- NativeAOT, trimming, single-file publishing, and collectible assembly unloading.
- Syntax highlighting or TextMate grammar execution.

## References

- [GitHub Linguist 9.6.0](https://github.com/github-linguist/linguist/tree/196b2a14418cab005065c72c9759370934c184bc)
- [Linguist classifier implementation](https://github.com/github-linguist/linguist/blob/196b2a14418cab005065c72c9759370934c184bc/lib/linguist/classifier.rb)
- [Linguist language registry](https://github.com/github-linguist/linguist/blob/196b2a14418cab005065c72c9759370934c184bc/lib/linguist/language.rb)
- [CRuby embedding API](https://docs.ruby-lang.org/capi/en/master/d3/d97/interpreter_8h.html)
- [CRuby thread API](https://docs.ruby-lang.org/capi/en/master/d6/dfb/include_2ruby_2thread_8h.html)
- [.NET native interoperability best practices](https://learn.microsoft.com/dotnet/standard/native-interop/best-practices)
- [NuGet native files](https://learn.microsoft.com/nuget/create-packages/native-files-in-net-packages)
