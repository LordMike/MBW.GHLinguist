#ifndef GHLINGUIST_H
#define GHLINGUIST_H

#include <stddef.h>
#include <stdint.h>

#if defined(_WIN32)
#if defined(GHLINGUIST_BUILDING_LIBRARY)
#define GHL_API __declspec(dllexport)
#else
#define GHL_API __declspec(dllimport)
#endif
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
#define GHL_STATUS_OK ((ghl_status)0)
#define GHL_STATUS_INVALID_ARGUMENT ((ghl_status)1)
#define GHL_STATUS_ABI_MISMATCH ((ghl_status)2)
#define GHL_STATUS_UNSUPPORTED ((ghl_status)3)
#define GHL_STATUS_NOT_FOUND ((ghl_status)4)
#define GHL_STATUS_INVALID_UTF8 ((ghl_status)5)
#define GHL_STATUS_RUBY_EXCEPTION ((ghl_status)6)
#define GHL_STATUS_NATIVE_FAILURE ((ghl_status)7)
#define GHL_STATUS_OUT_OF_MEMORY ((ghl_status)8)
#define GHL_STATUS_INTERNAL_ERROR ((ghl_status)9)

typedef struct ghl_string_view { const char* data; size_t length; } ghl_string_view;
typedef struct ghl_bytes_view { const uint8_t* data; size_t length; } ghl_bytes_view;

typedef uint64_t ghl_capabilities;
#define GHL_LANGUAGE_ID_NONE UINT64_MAX
#define GHL_CAP_LANGUAGE_REGISTRY (UINT64_C(1) << 0)
#define GHL_CAP_STANDARD_DETECTION (UINT64_C(1) << 1)
#define GHL_CAP_CONTENT_CLASSIFIER (UINT64_C(1) << 2)
#define GHL_CAP_STRATEGY_TRACE (UINT64_C(1) << 3)
#define GHL_CAP_ENCODING_BINARY (UINT64_C(1) << 4)
#define GHL_CAP_GENERATED_DETECTION (UINT64_C(1) << 5)
#define GHL_CAP_PATH_CLASSIFICATION (UINT64_C(1) << 6)

typedef uint32_t ghl_language_type;
#define GHL_LANGUAGE_TYPE_UNKNOWN 0u
#define GHL_LANGUAGE_TYPE_DATA 1u
#define GHL_LANGUAGE_TYPE_MARKUP 2u
#define GHL_LANGUAGE_TYPE_PROGRAMMING 3u
#define GHL_LANGUAGE_TYPE_PROSE 4u
typedef uint32_t ghl_language_type_mask;
#define GHL_LANGUAGE_MASK_DATA (1u << 0)
#define GHL_LANGUAGE_MASK_MARKUP (1u << 1)
#define GHL_LANGUAGE_MASK_PROGRAMMING (1u << 2)
#define GHL_LANGUAGE_MASK_PROSE (1u << 3)
#define GHL_LANGUAGE_MASK_ALL UINT32_C(0x0f)

typedef uint32_t ghl_strategy;
#define GHL_STRATEGY_NONE 0u
#define GHL_STRATEGY_MODELINE 1u
#define GHL_STRATEGY_FILENAME 2u
#define GHL_STRATEGY_SHEBANG 3u
#define GHL_STRATEGY_EXTENSION 4u
#define GHL_STRATEGY_XML 5u
#define GHL_STRATEGY_MANPAGE 6u
#define GHL_STRATEGY_HEURISTICS 7u
#define GHL_STRATEGY_CLASSIFIER 8u
typedef uint32_t ghl_strategy_mask;
#define GHL_STRATEGY_MASK_MODELINE (1u << 0)
#define GHL_STRATEGY_MASK_FILENAME (1u << 1)
#define GHL_STRATEGY_MASK_SHEBANG (1u << 2)
#define GHL_STRATEGY_MASK_EXTENSION (1u << 3)
#define GHL_STRATEGY_MASK_XML (1u << 4)
#define GHL_STRATEGY_MASK_MANPAGE (1u << 5)
#define GHL_STRATEGY_MASK_HEURISTICS (1u << 6)
#define GHL_STRATEGY_MASK_CLASSIFIER (1u << 7)
#define GHL_STRATEGY_MASK_DEFAULT UINT32_C(0xff)

typedef uint32_t ghl_blob_input_flags;
#define GHL_BLOB_INPUT_SYMLINK (1u << 0)
#define GHL_BLOB_INPUT_LFS_TRACKED (1u << 1)
typedef uint64_t ghl_blob_result_flags;
#define GHL_BLOB_LIKELY_BINARY (UINT64_C(1) << 0)
#define GHL_BLOB_BINARY (UINT64_C(1) << 1)
#define GHL_BLOB_TEXT (UINT64_C(1) << 2)
#define GHL_BLOB_IMAGE (UINT64_C(1) << 3)
#define GHL_BLOB_SOLID (UINT64_C(1) << 4)
#define GHL_BLOB_CSV (UINT64_C(1) << 5)
#define GHL_BLOB_PDF (UINT64_C(1) << 6)
#define GHL_BLOB_LARGE (UINT64_C(1) << 7)
#define GHL_BLOB_VIEWABLE (UINT64_C(1) << 8)
#define GHL_BLOB_SAFE_TO_COLORIZE (UINT64_C(1) << 9)
#define GHL_BLOB_HIGH_LONG_LINE_RATIO (UINT64_C(1) << 10)
#define GHL_BLOB_LFS_POINTER (UINT64_C(1) << 11)
#define GHL_BLOB_VENDORED (UINT64_C(1) << 12)
#define GHL_BLOB_DOCUMENTATION (UINT64_C(1) << 13)
#define GHL_BLOB_GENERATED (UINT64_C(1) << 14)
#define GHL_BLOB_DETECTABLE (UINT64_C(1) << 15)
#define GHL_BLOB_INCLUDE_IN_STATS (UINT64_C(1) << 16)

/* The ABI specification calls this ghl_analysis_flags, but that typedef would
 * conflict with the exported ghl_analysis_flags function in C and C++. */
typedef uint32_t ghl_analysis_option_flags;
#define GHL_ANALYSIS_ALLOW_EMPTY (1u << 0)
#define GHL_ANALYSIS_INCLUDE_TRACE (1u << 1)
#define GHL_ANALYSIS_INCLUDE_LINE_COUNTS (1u << 2)
typedef uint32_t ghl_analysis_text_field;
#define GHL_ANALYSIS_TEXT_MIME_TYPE 1u
#define GHL_ANALYSIS_TEXT_CONTENT_TYPE 2u
#define GHL_ANALYSIS_TEXT_DISPOSITION 3u
#define GHL_ANALYSIS_TEXT_ENCODING 4u
#define GHL_ANALYSIS_TEXT_RUBY_ENCODING 5u
#define GHL_ANALYSIS_TEXT_TM_SCOPE 6u
typedef uint32_t ghl_language_text_field;
#define GHL_LANGUAGE_TEXT_NAME 1u
#define GHL_LANGUAGE_TEXT_FS_NAME 2u
#define GHL_LANGUAGE_TEXT_COLOR 3u
#define GHL_LANGUAGE_TEXT_TM_SCOPE 4u
#define GHL_LANGUAGE_TEXT_ACE_MODE 5u
#define GHL_LANGUAGE_TEXT_CODEMIRROR_MODE 6u
#define GHL_LANGUAGE_TEXT_CODEMIRROR_MIME_TYPE 7u
typedef uint32_t ghl_language_collection;
#define GHL_LANGUAGE_COLLECTION_ALIASES 1u
#define GHL_LANGUAGE_COLLECTION_EXTENSIONS 2u
#define GHL_LANGUAGE_COLLECTION_INTERPRETERS 3u
#define GHL_LANGUAGE_COLLECTION_FILENAMES 4u
typedef uint32_t ghl_lookup_kind;
#define GHL_LOOKUP_NAME 1u
#define GHL_LOOKUP_ALIAS 2u
#define GHL_LOOKUP_FILENAME 3u
#define GHL_LOOKUP_EXTENSION 4u
#define GHL_LOOKUP_INTERPRETER 5u

typedef struct ghl_runtime_options { uint32_t struct_size; uint32_t flags; ghl_string_view asset_root; uint64_t reserved[4]; } ghl_runtime_options;
typedef struct ghl_blob_input { uint32_t struct_size; ghl_blob_input_flags flags; ghl_string_view path; ghl_string_view name; ghl_bytes_view data; uint64_t reserved[4]; } ghl_blob_input;
typedef struct ghl_analysis_options { uint32_t struct_size; ghl_analysis_option_flags flags; ghl_strategy_mask strategies; uint32_t reserved32; uint64_t reserved[4]; } ghl_analysis_options;
typedef struct ghl_classify_options { uint32_t struct_size; uint32_t flags; ghl_language_type_mask allowed_types; uint32_t maximum_bytes; const uint64_t* candidate_language_ids; size_t candidate_language_count; uint64_t reserved[4]; } ghl_classify_options;
typedef struct ghl_version_info { uint32_t struct_size; uint32_t abi_major; uint32_t abi_minor; uint32_t reserved32; ghl_string_view wrapper_version; ghl_string_view ruby_version; ghl_string_view linguist_version; ghl_string_view linguist_revision; ghl_string_view classifier_sha256; uint64_t reserved[4]; } ghl_version_info;
typedef struct ghl_language_info { uint32_t struct_size; ghl_language_type type; uint64_t language_id; uint64_t group_language_id; uint32_t flags; uint32_t alias_count; uint32_t extension_count; uint32_t interpreter_count; uint32_t filename_count; ghl_string_view name; ghl_string_view fs_name; ghl_string_view color; ghl_string_view tm_scope; ghl_string_view ace_mode; ghl_string_view codemirror_mode; ghl_string_view codemirror_mime_type; uint64_t reserved[4]; } ghl_language_info;
#define GHL_LANGUAGE_POPULAR (1u << 0)
#define GHL_LANGUAGE_WRAP (1u << 1)
typedef struct ghl_strategy_trace_entry { uint32_t struct_size; ghl_strategy strategy; uint32_t candidate_count; uint32_t reserved32; uint64_t reserved[4]; } ghl_strategy_trace_entry;

GHL_API uint32_t GHL_CALL ghl_abi_version_major(void);
GHL_API uint32_t GHL_CALL ghl_abi_version_minor(void);
GHL_API ghl_status GHL_CALL ghl_runtime_create(const ghl_runtime_options* options, ghl_runtime** out_runtime, ghl_error** out_error);
GHL_API void GHL_CALL ghl_runtime_release(ghl_runtime* runtime);
GHL_API ghl_capabilities GHL_CALL ghl_runtime_capabilities(const ghl_runtime* runtime);
GHL_API ghl_status GHL_CALL ghl_runtime_version(const ghl_runtime* runtime, ghl_version_info* out_version);
GHL_API size_t GHL_CALL ghl_runtime_language_count(const ghl_runtime* runtime);
GHL_API ghl_status GHL_CALL ghl_runtime_language_id_at(const ghl_runtime* runtime, size_t index, uint64_t* out_language_id);
GHL_API ghl_status GHL_CALL ghl_runtime_language_info(const ghl_runtime* runtime, uint64_t language_id, ghl_language_info* out_info);
GHL_API ghl_status GHL_CALL ghl_runtime_language_collection_value(const ghl_runtime* runtime, uint64_t language_id, ghl_language_collection collection, size_t index, ghl_string_view* out_value);
GHL_API ghl_status GHL_CALL ghl_runtime_lookup_languages(const ghl_runtime* runtime, ghl_lookup_kind kind, ghl_string_view value, ghl_language_id_list** out_languages, ghl_error** out_error);
GHL_API ghl_status GHL_CALL ghl_runtime_analyze(const ghl_runtime* runtime, const ghl_blob_input* blob, const ghl_analysis_options* options, ghl_analysis** out_analysis, ghl_error** out_error);
GHL_API ghl_status GHL_CALL ghl_runtime_classify(const ghl_runtime* runtime, ghl_bytes_view data, const ghl_classify_options* options, ghl_classification** out_classification, ghl_error** out_error);
GHL_API void GHL_CALL ghl_analysis_release(ghl_analysis* analysis);
GHL_API uint64_t GHL_CALL ghl_analysis_language_id(const ghl_analysis* analysis);
GHL_API ghl_strategy GHL_CALL ghl_analysis_strategy(const ghl_analysis* analysis);
GHL_API ghl_blob_result_flags GHL_CALL ghl_analysis_flags(const ghl_analysis* analysis);
GHL_API uint64_t GHL_CALL ghl_analysis_loc(const ghl_analysis* analysis);
GHL_API uint64_t GHL_CALL ghl_analysis_sloc(const ghl_analysis* analysis);
GHL_API ghl_status GHL_CALL ghl_analysis_text(const ghl_analysis* analysis, ghl_analysis_text_field field, ghl_string_view* out_value);
GHL_API size_t GHL_CALL ghl_analysis_trace_count(const ghl_analysis* analysis);
GHL_API ghl_status GHL_CALL ghl_analysis_trace_entry(const ghl_analysis* analysis, size_t index, ghl_strategy_trace_entry* out_entry);
GHL_API ghl_status GHL_CALL ghl_analysis_trace_candidate(const ghl_analysis* analysis, size_t trace_index, size_t candidate_index, uint64_t* out_language_id);
GHL_API void GHL_CALL ghl_classification_release(ghl_classification* classification);
GHL_API size_t GHL_CALL ghl_classification_count(const ghl_classification* classification);
GHL_API uint32_t GHL_CALL ghl_classification_considered_bytes(const ghl_classification* classification);
GHL_API ghl_status GHL_CALL ghl_classification_result(const ghl_classification* classification, size_t index, uint64_t* out_language_id, double* out_score);
GHL_API void GHL_CALL ghl_language_id_list_release(ghl_language_id_list* languages);
GHL_API size_t GHL_CALL ghl_language_id_list_count(const ghl_language_id_list* languages);
GHL_API ghl_status GHL_CALL ghl_language_id_list_at(const ghl_language_id_list* languages, size_t index, uint64_t* out_language_id);
GHL_API ghl_status GHL_CALL ghl_error_status(const ghl_error* error);
GHL_API ghl_string_view GHL_CALL ghl_error_message(const ghl_error* error);
GHL_API ghl_string_view GHL_CALL ghl_error_ruby_class(const ghl_error* error);
GHL_API ghl_string_view GHL_CALL ghl_error_ruby_backtrace(const ghl_error* error);
GHL_API void GHL_CALL ghl_error_release(ghl_error* error);

#ifdef __cplusplus
}
#endif

#endif
