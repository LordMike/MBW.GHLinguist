---
status: DRAFT
created: 2026-09-02T18:21:01+02:00
---

# GitHub Linguist CRuby interop

## Goal

Package GitHub Linguist 9.6.0 at commit `196b2a14418cab005065c72c9759370934c184bc` behind a stable C ABI and managed .NET facade. Support complete single-blob analysis, content classification, and language metadata without requiring Ruby installation on the consuming machine.

Pin CRuby 4.0.1 and lock every native and Ruby dependency by version, source hash, license, RID, and built-binary hash.

## Settled decisions

- Support `win-x64` and `linux-x64`.
- Provide one primary interop library plus an adjacent, pinned dependency closure.
- Package a managed facade and native assets together as `Originary.Linguist.Interop`.
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

The public header is `include/originary_linguist.h`. Only symbols with the `ol_` prefix are exported.

```c
#ifndef ORIGINARY_LINGUIST_H
#define ORIGINARY_LINGUIST_H

#include <stddef.h>
#include <stdint.h>

#if defined(_WIN32)
#define OL_API __declspec(dllexport)
#define OL_CALL __cdecl
#else
#define OL_API __attribute__((visibility("default")))
#define OL_CALL
#endif

#ifdef __cplusplus
extern "C" {
#endif

#define OL_ABI_VERSION_MAJOR 1u
#define OL_ABI_VERSION_MINOR 0u

typedef struct ol_runtime ol_runtime;
typedef struct ol_analysis ol_analysis;
typedef struct ol_classification ol_classification;
typedef struct ol_language_id_list ol_language_id_list;
typedef struct ol_error ol_error;

typedef int32_t ol_status;
#define OL_STATUS_OK                 ((ol_status)0)
#define OL_STATUS_INVALID_ARGUMENT   ((ol_status)1)
#define OL_STATUS_ABI_MISMATCH       ((ol_status)2)
#define OL_STATUS_UNSUPPORTED        ((ol_status)3)
#define OL_STATUS_NOT_FOUND          ((ol_status)4)
#define OL_STATUS_INVALID_UTF8       ((ol_status)5)
#define OL_STATUS_RUBY_EXCEPTION     ((ol_status)6)
#define OL_STATUS_NATIVE_FAILURE     ((ol_status)7)
#define OL_STATUS_OUT_OF_MEMORY      ((ol_status)8)
#define OL_STATUS_INTERNAL_ERROR     ((ol_status)9)

typedef struct ol_string_view {
    const char* data;
    size_t length;
} ol_string_view;

typedef struct ol_bytes_view {
    const uint8_t* data;
    size_t length;
} ol_bytes_view;

typedef uint64_t ol_capabilities;
#define OL_CAP_LANGUAGE_REGISTRY     (UINT64_C(1) << 0)
#define OL_CAP_STANDARD_DETECTION    (UINT64_C(1) << 1)
#define OL_CAP_CONTENT_CLASSIFIER    (UINT64_C(1) << 2)
#define OL_CAP_STRATEGY_TRACE        (UINT64_C(1) << 3)
#define OL_CAP_ENCODING_BINARY       (UINT64_C(1) << 4)
#define OL_CAP_GENERATED_DETECTION   (UINT64_C(1) << 5)
#define OL_CAP_PATH_CLASSIFICATION   (UINT64_C(1) << 6)

typedef uint32_t ol_language_type;
#define OL_LANGUAGE_TYPE_UNKNOWN      0u
#define OL_LANGUAGE_TYPE_DATA         1u
#define OL_LANGUAGE_TYPE_MARKUP       2u
#define OL_LANGUAGE_TYPE_PROGRAMMING  3u
#define OL_LANGUAGE_TYPE_PROSE        4u

typedef uint32_t ol_language_type_mask;
#define OL_LANGUAGE_MASK_DATA         (1u << 0)
#define OL_LANGUAGE_MASK_MARKUP       (1u << 1)
#define OL_LANGUAGE_MASK_PROGRAMMING  (1u << 2)
#define OL_LANGUAGE_MASK_PROSE        (1u << 3)
#define OL_LANGUAGE_MASK_ALL          UINT32_C(0x0f)

typedef uint32_t ol_strategy;
#define OL_STRATEGY_NONE        0u
#define OL_STRATEGY_MODELINE    1u
#define OL_STRATEGY_FILENAME    2u
#define OL_STRATEGY_SHEBANG     3u
#define OL_STRATEGY_EXTENSION   4u
#define OL_STRATEGY_XML         5u
#define OL_STRATEGY_MANPAGE     6u
#define OL_STRATEGY_HEURISTICS  7u
#define OL_STRATEGY_CLASSIFIER  8u

typedef uint32_t ol_strategy_mask;
#define OL_STRATEGY_MASK_MODELINE    (1u << 0)
#define OL_STRATEGY_MASK_FILENAME    (1u << 1)
#define OL_STRATEGY_MASK_SHEBANG     (1u << 2)
#define OL_STRATEGY_MASK_EXTENSION   (1u << 3)
#define OL_STRATEGY_MASK_XML         (1u << 4)
#define OL_STRATEGY_MASK_MANPAGE     (1u << 5)
#define OL_STRATEGY_MASK_HEURISTICS  (1u << 6)
#define OL_STRATEGY_MASK_CLASSIFIER  (1u << 7)
#define OL_STRATEGY_MASK_DEFAULT     UINT32_C(0xff)

typedef uint32_t ol_blob_input_flags;
#define OL_BLOB_INPUT_SYMLINK      (1u << 0)
#define OL_BLOB_INPUT_LFS_TRACKED  (1u << 1)

typedef uint64_t ol_blob_result_flags;
#define OL_BLOB_LIKELY_BINARY          (UINT64_C(1) << 0)
#define OL_BLOB_BINARY                 (UINT64_C(1) << 1)
#define OL_BLOB_TEXT                   (UINT64_C(1) << 2)
#define OL_BLOB_IMAGE                  (UINT64_C(1) << 3)
#define OL_BLOB_SOLID                  (UINT64_C(1) << 4)
#define OL_BLOB_CSV                    (UINT64_C(1) << 5)
#define OL_BLOB_PDF                    (UINT64_C(1) << 6)
#define OL_BLOB_LARGE                  (UINT64_C(1) << 7)
#define OL_BLOB_VIEWABLE               (UINT64_C(1) << 8)
#define OL_BLOB_SAFE_TO_COLORIZE       (UINT64_C(1) << 9)
#define OL_BLOB_HIGH_LONG_LINE_RATIO   (UINT64_C(1) << 10)
#define OL_BLOB_LFS_POINTER            (UINT64_C(1) << 11)
#define OL_BLOB_VENDORED               (UINT64_C(1) << 12)
#define OL_BLOB_DOCUMENTATION          (UINT64_C(1) << 13)
#define OL_BLOB_GENERATED              (UINT64_C(1) << 14)
#define OL_BLOB_DETECTABLE             (UINT64_C(1) << 15)
#define OL_BLOB_INCLUDE_IN_STATS       (UINT64_C(1) << 16)

typedef uint32_t ol_analysis_flags;
#define OL_ANALYSIS_ALLOW_EMPTY          (1u << 0)
#define OL_ANALYSIS_INCLUDE_TRACE        (1u << 1)
#define OL_ANALYSIS_INCLUDE_LINE_COUNTS  (1u << 2)

typedef uint32_t ol_analysis_text_field;
#define OL_ANALYSIS_TEXT_MIME_TYPE       1u
#define OL_ANALYSIS_TEXT_CONTENT_TYPE    2u
#define OL_ANALYSIS_TEXT_DISPOSITION     3u
#define OL_ANALYSIS_TEXT_ENCODING        4u
#define OL_ANALYSIS_TEXT_RUBY_ENCODING   5u
#define OL_ANALYSIS_TEXT_TM_SCOPE        6u

typedef uint32_t ol_language_text_field;
#define OL_LANGUAGE_TEXT_NAME                    1u
#define OL_LANGUAGE_TEXT_FS_NAME                 2u
#define OL_LANGUAGE_TEXT_COLOR                   3u
#define OL_LANGUAGE_TEXT_TM_SCOPE                4u
#define OL_LANGUAGE_TEXT_ACE_MODE                5u
#define OL_LANGUAGE_TEXT_CODEMIRROR_MODE         6u
#define OL_LANGUAGE_TEXT_CODEMIRROR_MIME_TYPE    7u

typedef uint32_t ol_language_collection;
#define OL_LANGUAGE_COLLECTION_ALIASES       1u
#define OL_LANGUAGE_COLLECTION_EXTENSIONS    2u
#define OL_LANGUAGE_COLLECTION_INTERPRETERS  3u
#define OL_LANGUAGE_COLLECTION_FILENAMES     4u

typedef uint32_t ol_lookup_kind;
#define OL_LOOKUP_NAME         1u
#define OL_LOOKUP_ALIAS        2u
#define OL_LOOKUP_FILENAME     3u
#define OL_LOOKUP_EXTENSION    4u
#define OL_LOOKUP_INTERPRETER  5u

typedef struct ol_runtime_options {
    uint32_t struct_size;
    uint32_t flags;
    ol_string_view asset_root;
    uint64_t reserved[4];
} ol_runtime_options;

typedef struct ol_blob_input {
    uint32_t struct_size;
    ol_blob_input_flags flags;
    ol_string_view path;
    ol_string_view name;
    ol_bytes_view data;
    uint64_t reserved[4];
} ol_blob_input;

typedef struct ol_analysis_options {
    uint32_t struct_size;
    ol_analysis_flags flags;
    ol_strategy_mask strategies;
    uint32_t reserved32;
    uint64_t reserved[4];
} ol_analysis_options;

typedef struct ol_classify_options {
    uint32_t struct_size;
    uint32_t flags;
    ol_language_type_mask allowed_types;
    uint32_t maximum_bytes;
    const uint64_t* candidate_language_ids;
    size_t candidate_language_count;
    uint64_t reserved[4];
} ol_classify_options;

typedef struct ol_version_info {
    uint32_t struct_size;
    uint32_t abi_major;
    uint32_t abi_minor;
    uint32_t reserved32;
    ol_string_view wrapper_version;
    ol_string_view ruby_version;
    ol_string_view linguist_version;
    ol_string_view linguist_revision;
    ol_string_view classifier_sha256;
    uint64_t reserved[4];
} ol_version_info;

typedef struct ol_language_info {
    uint32_t struct_size;
    ol_language_type type;
    uint64_t language_id;
    uint64_t group_language_id;
    uint32_t flags;
    uint32_t alias_count;
    uint32_t extension_count;
    uint32_t interpreter_count;
    uint32_t filename_count;
    ol_string_view name;
    ol_string_view fs_name;
    ol_string_view color;
    ol_string_view tm_scope;
    ol_string_view ace_mode;
    ol_string_view codemirror_mode;
    ol_string_view codemirror_mime_type;
    uint64_t reserved[4];
} ol_language_info;

#define OL_LANGUAGE_POPULAR  (1u << 0)
#define OL_LANGUAGE_WRAP     (1u << 1)

typedef struct ol_strategy_trace_entry {
    uint32_t struct_size;
    ol_strategy strategy;
    uint32_t candidate_count;
    uint32_t reserved32;
    uint64_t reserved[4];
} ol_strategy_trace_entry;

OL_API uint32_t OL_CALL ol_abi_version_major(void);
OL_API uint32_t OL_CALL ol_abi_version_minor(void);

OL_API ol_status OL_CALL ol_runtime_create(
    const ol_runtime_options* options,
    ol_runtime** out_runtime,
    ol_error** out_error);

OL_API void OL_CALL ol_runtime_release(ol_runtime* runtime);

OL_API ol_capabilities OL_CALL ol_runtime_capabilities(
    const ol_runtime* runtime);

OL_API ol_status OL_CALL ol_runtime_version(
    const ol_runtime* runtime,
    ol_version_info* out_version);

OL_API size_t OL_CALL ol_runtime_language_count(
    const ol_runtime* runtime);

OL_API ol_status OL_CALL ol_runtime_language_id_at(
    const ol_runtime* runtime,
    size_t index,
    uint64_t* out_language_id);

OL_API ol_status OL_CALL ol_runtime_language_info(
    const ol_runtime* runtime,
    uint64_t language_id,
    ol_language_info* out_info);

OL_API ol_status OL_CALL ol_runtime_language_collection_value(
    const ol_runtime* runtime,
    uint64_t language_id,
    ol_language_collection collection,
    size_t index,
    ol_string_view* out_value);

OL_API ol_status OL_CALL ol_runtime_lookup_languages(
    const ol_runtime* runtime,
    ol_lookup_kind kind,
    ol_string_view value,
    ol_language_id_list** out_languages,
    ol_error** out_error);

OL_API ol_status OL_CALL ol_runtime_analyze(
    const ol_runtime* runtime,
    const ol_blob_input* blob,
    const ol_analysis_options* options,
    ol_analysis** out_analysis,
    ol_error** out_error);

OL_API ol_status OL_CALL ol_runtime_classify(
    const ol_runtime* runtime,
    ol_bytes_view data,
    const ol_classify_options* options,
    ol_classification** out_classification,
    ol_error** out_error);

OL_API void OL_CALL ol_analysis_release(ol_analysis* analysis);

OL_API uint64_t OL_CALL ol_analysis_language_id(
    const ol_analysis* analysis);

OL_API ol_strategy OL_CALL ol_analysis_strategy(
    const ol_analysis* analysis);

OL_API ol_blob_result_flags OL_CALL ol_analysis_flags(
    const ol_analysis* analysis);

OL_API uint64_t OL_CALL ol_analysis_loc(
    const ol_analysis* analysis);

OL_API uint64_t OL_CALL ol_analysis_sloc(
    const ol_analysis* analysis);

OL_API ol_status OL_CALL ol_analysis_text(
    const ol_analysis* analysis,
    ol_analysis_text_field field,
    ol_string_view* out_value);

OL_API size_t OL_CALL ol_analysis_trace_count(
    const ol_analysis* analysis);

OL_API ol_status OL_CALL ol_analysis_trace_entry(
    const ol_analysis* analysis,
    size_t index,
    ol_strategy_trace_entry* out_entry);

OL_API ol_status OL_CALL ol_analysis_trace_candidate(
    const ol_analysis* analysis,
    size_t trace_index,
    size_t candidate_index,
    uint64_t* out_language_id);

OL_API void OL_CALL ol_classification_release(
    ol_classification* classification);

OL_API size_t OL_CALL ol_classification_count(
    const ol_classification* classification);

OL_API uint32_t OL_CALL ol_classification_considered_bytes(
    const ol_classification* classification);

OL_API ol_status OL_CALL ol_classification_result(
    const ol_classification* classification,
    size_t index,
    uint64_t* out_language_id,
    double* out_score);

OL_API void OL_CALL ol_language_id_list_release(
    ol_language_id_list* languages);

OL_API size_t OL_CALL ol_language_id_list_count(
    const ol_language_id_list* languages);

OL_API ol_status OL_CALL ol_language_id_list_at(
    const ol_language_id_list* languages,
    size_t index,
    uint64_t* out_language_id);

OL_API ol_status OL_CALL ol_error_status(
    const ol_error* error);

OL_API ol_string_view OL_CALL ol_error_message(
    const ol_error* error);

OL_API ol_string_view OL_CALL ol_error_ruby_class(
    const ol_error* error);

OL_API ol_string_view OL_CALL ol_error_ruby_backtrace(
    const ol_error* error);

OL_API void OL_CALL ol_error_release(ol_error* error);

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
- Analysis receives complete blob bytes; prefix-only operation belongs to `ol_runtime_classify`.
- `maximum_bytes == 0` uses Linguist's 50 KiB default.
- A nonzero classifier bound cannot exceed 50 KiB in ABI v1.
- Classification scores are similarity scores, not probabilities or confidence.
- `language_id == 0` means no detected language.
- Every exported function is thread-safe.
- Ruby work is synchronous but serialized through the process worker.
- `ol_runtime_create` blocks until CRuby, bridge code, language data, heuristics, generated rules, MIME data, and classifier centroids are loaded.
- Additional runtime handles share the initialized process runtime.
- Releasing the final runtime handle does not finalize CRuby.
- Runtime unloading, reinitialization, collectible-load-context unloading, and post-fork use are unsupported in v1.
- Ruby exceptions are caught with `rb_protect` and copied into `ol_error`.
- No native allocation is returned without an associated release function.

## Managed facade

Add `src/Originary.Linguist.Interop` targeting `net10.0`. Use source-generated `LibraryImport`, `SafeHandle`, strict UTF-8 marshaling, checked spans, immutable managed result records, deterministic disposal, a native-library resolver, asset hash verification, and status-to-exception translation.

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
        string? path = null,
        string? name = null,
        BlobAnalysisOptions? options = null);

    public ClassificationResults Classify(
        ReadOnlySpan<byte> data,
        ClassificationOptions? options = null);

    public IReadOnlyList<LinguistLanguage> Lookup(
        LanguageLookupKind kind,
        string value);
}
```

## NuGet package

Make `Originary.Linguist.Interop.csproj` packable and include the managed assembly plus native closures under `runtimes/win-x64/native/` and `runtimes/linux-x64/native/`.

The package layout is:

```text
lib/net10.0/Originary.Linguist.Interop.dll
runtimes/win-x64/native/*
runtimes/linux-x64/native/*
buildTransitive/Originary.Linguist.Interop.targets
LICENSES/*
THIRD-PARTY-NOTICES.txt
native/include/originary_linguist.h
```

Use `buildTransitive` only for non-library Ruby or data files that require preserved subdirectories. Normal build and publish consumption must be automatic after one `PackageReference`.

## Work

1. Obtain approval for the project documentation changes, then document the C ABI, process-lifetime runtime, supported RIDs, copied-result ownership, serialized execution, unsupported unloading, package layout, and deferred repository API without changing schema, format, or documentation versions.
2. Pin CRuby 4.0.1 and Linguist 9.6.0 commit `196b2a14418cab005065c72c9759370934c184bc`. Generate a locked dependency manifest for `charlock_holmes`, ICU, `mini_mime`, CGI, Psych/libyaml, and the Linguist tokenizer extension. Exclude Rugged/libgit2 because repository behavior is deferred. Record source and binary SHA-256 values, licenses, patches, build flags, and redistribution decisions.
3. Add `src/Originary.Linguist.Native` with CMake-based Windows and Linux builds, the public header, C ABI implementation, worker queue, process runtime, Ruby bridge, export lists, symbol-visibility controls, and deterministic asset-location logic.
4. Start one native worker on the first runtime creation and initialize CRuby on that thread. Cache Ruby constants, method IDs, language metadata, classifier data, and immutable native copies. Send every Ruby operation through the synchronous request queue. Do not use `rb_thread_call_with_gvl` from arbitrary managed threads.
5. Add a Ruby `InteropBlob` matching Linguist's blob contract. Implement standard strategy detection, strategy tracing, unrestricted or candidate-filtered classification, language registry projection, binary and encoding analysis, MIME results, generated/vendored/documentation predicates, line counts, and statistics eligibility. Copy all returned data into native-owned result objects before leaving the worker.
6. Produce reviewed `win-x64` and `linux-x64` directories containing the primary interop library, CRuby runtime, Linguist tokenizer, Charlock Holmes, ICU, Psych/libyaml, required Ruby standard-library files, Linguist Ruby files, classifier data, language data, MIME data, and licenses. Configure Windows adjacent-DLL loading and Linux `$ORIGIN` lookup without consulting system Ruby or system ICU.
7. Add the managed facade and keep all native handles internal. Copy native result values into immutable managed records so callers do not manage result lifetimes or native string views.
8. Pack the managed facade, native dependency closures, public header, provenance, and third-party notices as `Originary.Linguist.Interop`.
9. Add `tests/Originary.Linguist.Interop.Tests` as an xUnit v3 project that references the managed facade and invokes the actual native runtime.
10. Pack into an ignored `.tmp/packages` feed and restore a minimal fixture consumer using only `PackageReference`. Build and run it without Ruby, Linguist, ICU, or other development tools installed globally.
11. Add `Originary.Linguist.Interop` under `/src/` and `Originary.Linguist.Interop.Tests` under `/tests/` in `Originary.slnx`. Keep native build orchestration beneath the interop project rather than registering a non-MSBuild project directly.

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
- Verify the nupkg contains the expected RID assets, notices, header, managed assembly, and no build-machine paths.

## Validation

```powershell
dotnet build src/Originary.Linguist.Interop/Originary.Linguist.Interop.csproj -c Release --nologo
dotnet test tests/Originary.Linguist.Interop.Tests/Originary.Linguist.Interop.Tests.csproj -c Release --no-build --nologo
dotnet pack src/Originary.Linguist.Interop/Originary.Linguist.Interop.csproj -c Release --no-build --nologo -o .tmp/packages
dotnet build Originary.slnx -c Release --nologo
dotnet test Originary.slnx -c Release --no-build --nologo
git diff --check
```

Inspect Windows exports with `dumpbin /exports`, Linux exports with `nm -D`, dependency closure with `dumpbin /dependents` and `ldd`, exact package contents, hashes, licenses, and documentation references.

## Completion

This plan is complete when a clean .NET consumer can add one NuGet reference, initialize the runtime once, invoke the complete blob and classifier APIs repeatedly from arbitrary managed threads, receive deterministic copied results through the managed facade or documented C ABI, and run successfully on both supported RIDs without a system Ruby installation or runtime downloads.

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
