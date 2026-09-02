#include "ghlinguist.h"

#include <new>
#include <string>

struct ghl_runtime { uint32_t magic = 0x47484C31u; };
struct ghl_analysis { uint32_t unused = 0; };
struct ghl_classification { uint32_t unused = 0; };
struct ghl_language_id_list { uint32_t unused = 0; };
struct ghl_error { ghl_status status; std::string message; };

namespace {

constexpr uint32_t kRuntimeMagic = 0x47484C31u;
constexpr char kWrapperVersion[] = "MBW.GHLinguist.Native unimplemented bridge";
constexpr char kRubyVersion[] = "unimplemented";
constexpr char kLinguistVersion[] = "unimplemented";
constexpr char kLinguistRevision[] = "unimplemented";
constexpr char kClassifierSha256[] = "unimplemented";
constexpr char kUnsupported[] = "GitHub Linguist Ruby bridge is not implemented in this native build.";

static_assert(sizeof(ghl_string_view) == sizeof(void*) + sizeof(size_t));
static_assert(sizeof(ghl_bytes_view) == sizeof(void*) + sizeof(size_t));
static_assert(sizeof(ghl_runtime_options) == (sizeof(void*) == 8 ? 56 : 40));
static_assert(sizeof(ghl_blob_input) == (sizeof(void*) == 8 ? 88 : 64));
static_assert(sizeof(ghl_analysis_options) == 48);
static_assert(sizeof(ghl_classify_options) == (sizeof(void*) == 8 ? 64 : 48));
static_assert(sizeof(ghl_version_info) == (sizeof(void*) == 8 ? 128 : 104));
static_assert(sizeof(ghl_language_info) == (sizeof(void*) == 8 ? 192 : 152));
static_assert(sizeof(ghl_strategy_trace_entry) == 48);

ghl_string_view view(const char* text) { return {text, std::char_traits<char>::length(text)}; }
ghl_string_view empty_view() { return {nullptr, 0}; }

bool reserved_zero(const uint64_t (&reserved)[4]) {
    return reserved[0] == 0 && reserved[1] == 0 && reserved[2] == 0 && reserved[3] == 0;
}

bool valid_bytes(ghl_bytes_view value) { return value.data != nullptr || value.length == 0; }

bool valid_utf8(ghl_string_view value) {
    if (value.data == nullptr) return value.length == 0;
    for (size_t i = 0; i < value.length;) {
        const uint8_t lead = static_cast<uint8_t>(value.data[i]);
        if (lead <= 0x7f) { ++i; continue; }
        size_t extra = 0;
        uint32_t code_point = 0;
        if (lead >= 0xc2 && lead <= 0xdf) { extra = 1; code_point = lead & 0x1f; }
        else if (lead >= 0xe0 && lead <= 0xef) { extra = 2; code_point = lead & 0x0f; }
        else if (lead >= 0xf0 && lead <= 0xf4) { extra = 3; code_point = lead & 0x07; }
        else return false;
        if (value.length - i <= extra) return false;
        for (size_t offset = 1; offset <= extra; ++offset) {
            const uint8_t continuation = static_cast<uint8_t>(value.data[i + offset]);
            if ((continuation & 0xc0) != 0x80) return false;
            code_point = (code_point << 6) | (continuation & 0x3f);
        }
        if ((extra == 2 && code_point < 0x800) || (extra == 3 && code_point < 0x10000) ||
            (code_point >= 0xd800 && code_point <= 0xdfff) || code_point > 0x10ffff) return false;
        i += extra + 1;
    }
    return true;
}

bool valid_runtime(const ghl_runtime* runtime) { return runtime != nullptr && runtime->magic == kRuntimeMagic; }

void clear_error(ghl_error** out_error) { if (out_error != nullptr) *out_error = nullptr; }

ghl_status fail(ghl_status status, const char* message, ghl_error** out_error) {
    clear_error(out_error);
    if (out_error == nullptr) return status;
    ghl_error* error = new (std::nothrow) ghl_error{status, message};
    if (error == nullptr) return GHL_STATUS_OUT_OF_MEMORY;
    *out_error = error;
    return status;
}

ghl_status unsupported(ghl_error** out_error) { return fail(GHL_STATUS_UNSUPPORTED, kUnsupported, out_error); }
ghl_status invalid(const char* message, ghl_error** out_error = nullptr) { return fail(GHL_STATUS_INVALID_ARGUMENT, message, out_error); }

bool valid_runtime_options(const ghl_runtime_options* options) {
    return options != nullptr && options->struct_size >= sizeof(*options) && options->flags == 0 &&
        valid_utf8(options->asset_root) && reserved_zero(options->reserved);
}

bool valid_blob(const ghl_blob_input* blob) {
    return blob != nullptr && blob->struct_size >= sizeof(*blob) &&
        (blob->flags & ~(GHL_BLOB_INPUT_SYMLINK | GHL_BLOB_INPUT_LFS_TRACKED)) == 0 &&
        valid_utf8(blob->path) && valid_utf8(blob->name) && valid_bytes(blob->data) && reserved_zero(blob->reserved);
}

bool valid_analysis_options(const ghl_analysis_options* options) {
    return options != nullptr && options->struct_size >= sizeof(*options) &&
        (options->flags & ~(GHL_ANALYSIS_ALLOW_EMPTY | GHL_ANALYSIS_INCLUDE_TRACE | GHL_ANALYSIS_INCLUDE_LINE_COUNTS)) == 0 &&
        (options->strategies & ~GHL_STRATEGY_MASK_DEFAULT) == 0 && options->reserved32 == 0 && reserved_zero(options->reserved);
}

bool valid_classify_options(const ghl_classify_options* options) {
    if (options == nullptr || options->struct_size < sizeof(*options) || options->flags != 0 ||
        (options->allowed_types & ~GHL_LANGUAGE_MASK_ALL) != 0 || options->maximum_bytes > 51200 ||
        (options->candidate_language_ids == nullptr && options->candidate_language_count != 0) || !reserved_zero(options->reserved)) return false;
    for (size_t i = 0; i < options->candidate_language_count; ++i) {
        if (options->candidate_language_ids[i] == 0) return false;
    }
    return true;
}

template <typename T>
bool valid_output(const T* output) { return output != nullptr && output->struct_size >= sizeof(T); }

bool valid_version_output(const ghl_version_info* output) {
    return valid_output(output) && output->reserved32 == 0 && reserved_zero(output->reserved);
}

bool valid_language_output(const ghl_language_info* output) {
    return valid_output(output) && reserved_zero(output->reserved);
}

bool valid_trace_output(const ghl_strategy_trace_entry* output) {
    return valid_output(output) && output->reserved32 == 0 && reserved_zero(output->reserved);
}

} // namespace

extern "C" {

uint32_t GHL_CALL ghl_abi_version_major(void) { return GHL_ABI_VERSION_MAJOR; }
uint32_t GHL_CALL ghl_abi_version_minor(void) { return GHL_ABI_VERSION_MINOR; }

ghl_status GHL_CALL ghl_runtime_create(const ghl_runtime_options* options, ghl_runtime** out_runtime, ghl_error** out_error) {
    clear_error(out_error);
    if (out_runtime == nullptr) return invalid("out_runtime must not be null.", out_error);
    *out_runtime = nullptr;
    if (!valid_runtime_options(options)) return invalid("runtime options are invalid or use an incompatible layout.", out_error);
    ghl_runtime* runtime = new (std::nothrow) ghl_runtime();
    if (runtime == nullptr) return fail(GHL_STATUS_OUT_OF_MEMORY, "Unable to allocate runtime handle.", out_error);
    *out_runtime = runtime;
    return GHL_STATUS_OK;
}

void GHL_CALL ghl_runtime_release(ghl_runtime* runtime) { if (runtime != nullptr) { runtime->magic = 0; delete runtime; } }
ghl_capabilities GHL_CALL ghl_runtime_capabilities(const ghl_runtime* runtime) { return valid_runtime(runtime) ? 0 : 0; }

ghl_status GHL_CALL ghl_runtime_version(const ghl_runtime* runtime, ghl_version_info* out_version) {
    if (!valid_runtime(runtime)) return GHL_STATUS_INVALID_ARGUMENT;
    if (!valid_version_output(out_version)) return GHL_STATUS_ABI_MISMATCH;
    *out_version = {};
    out_version->struct_size = sizeof(*out_version);
    out_version->abi_major = GHL_ABI_VERSION_MAJOR;
    out_version->abi_minor = GHL_ABI_VERSION_MINOR;
    out_version->wrapper_version = view(kWrapperVersion);
    out_version->ruby_version = view(kRubyVersion);
    out_version->linguist_version = view(kLinguistVersion);
    out_version->linguist_revision = view(kLinguistRevision);
    out_version->classifier_sha256 = view(kClassifierSha256);
    return GHL_STATUS_OK;
}

size_t GHL_CALL ghl_runtime_language_count(const ghl_runtime* runtime) { return valid_runtime(runtime) ? 0 : 0; }
ghl_status GHL_CALL ghl_runtime_language_id_at(const ghl_runtime* runtime, size_t, uint64_t* out_language_id) {
    if (out_language_id == nullptr || !valid_runtime(runtime)) return GHL_STATUS_INVALID_ARGUMENT;
    *out_language_id = 0;
    return GHL_STATUS_UNSUPPORTED;
}
ghl_status GHL_CALL ghl_runtime_language_info(const ghl_runtime* runtime, uint64_t, ghl_language_info* out_info) {
    if (!valid_runtime(runtime)) return GHL_STATUS_INVALID_ARGUMENT;
    if (!valid_language_output(out_info)) return GHL_STATUS_ABI_MISMATCH;
    *out_info = {};
    out_info->struct_size = sizeof(*out_info);
    return GHL_STATUS_UNSUPPORTED;
}
ghl_status GHL_CALL ghl_runtime_language_collection_value(const ghl_runtime* runtime, uint64_t, ghl_language_collection, size_t, ghl_string_view* out_value) {
    if (out_value == nullptr || !valid_runtime(runtime)) return GHL_STATUS_INVALID_ARGUMENT;
    *out_value = empty_view();
    return GHL_STATUS_UNSUPPORTED;
}
ghl_status GHL_CALL ghl_runtime_lookup_languages(const ghl_runtime* runtime, ghl_lookup_kind, ghl_string_view value, ghl_language_id_list** out_languages, ghl_error** out_error) {
    clear_error(out_error);
    if (out_languages == nullptr || !valid_runtime(runtime) || !valid_utf8(value)) return invalid("lookup arguments are invalid.", out_error);
    *out_languages = nullptr;
    return unsupported(out_error);
}
ghl_status GHL_CALL ghl_runtime_analyze(const ghl_runtime* runtime, const ghl_blob_input* blob, const ghl_analysis_options* options, ghl_analysis** out_analysis, ghl_error** out_error) {
    clear_error(out_error);
    if (out_analysis == nullptr || !valid_runtime(runtime) || !valid_blob(blob) || !valid_analysis_options(options)) return invalid("analysis arguments are invalid or use an incompatible layout.", out_error);
    *out_analysis = nullptr;
    return unsupported(out_error);
}
ghl_status GHL_CALL ghl_runtime_classify(const ghl_runtime* runtime, ghl_bytes_view data, const ghl_classify_options* options, ghl_classification** out_classification, ghl_error** out_error) {
    clear_error(out_error);
    if (out_classification == nullptr || !valid_runtime(runtime) || !valid_bytes(data) || !valid_classify_options(options)) return invalid("classification arguments are invalid or use an incompatible layout.", out_error);
    *out_classification = nullptr;
    return unsupported(out_error);
}

void GHL_CALL ghl_analysis_release(ghl_analysis* analysis) { delete analysis; }
uint64_t GHL_CALL ghl_analysis_language_id(const ghl_analysis*) { return 0; }
ghl_strategy GHL_CALL ghl_analysis_strategy(const ghl_analysis*) { return GHL_STRATEGY_NONE; }
ghl_blob_result_flags GHL_CALL ghl_analysis_flags(const ghl_analysis*) { return 0; }
uint64_t GHL_CALL ghl_analysis_loc(const ghl_analysis*) { return 0; }
uint64_t GHL_CALL ghl_analysis_sloc(const ghl_analysis*) { return 0; }
ghl_status GHL_CALL ghl_analysis_text(const ghl_analysis* analysis, ghl_analysis_text_field, ghl_string_view* out_value) { if (out_value == nullptr || analysis == nullptr) return GHL_STATUS_INVALID_ARGUMENT; *out_value = empty_view(); return GHL_STATUS_UNSUPPORTED; }
size_t GHL_CALL ghl_analysis_trace_count(const ghl_analysis*) { return 0; }
ghl_status GHL_CALL ghl_analysis_trace_entry(const ghl_analysis* analysis, size_t, ghl_strategy_trace_entry* out_entry) { if (analysis == nullptr) return GHL_STATUS_INVALID_ARGUMENT; if (!valid_trace_output(out_entry)) return GHL_STATUS_ABI_MISMATCH; *out_entry = {}; out_entry->struct_size = sizeof(*out_entry); return GHL_STATUS_UNSUPPORTED; }
ghl_status GHL_CALL ghl_analysis_trace_candidate(const ghl_analysis* analysis, size_t, size_t, uint64_t* out_language_id) { if (analysis == nullptr || out_language_id == nullptr) return GHL_STATUS_INVALID_ARGUMENT; *out_language_id = 0; return GHL_STATUS_UNSUPPORTED; }

void GHL_CALL ghl_classification_release(ghl_classification* classification) { delete classification; }
size_t GHL_CALL ghl_classification_count(const ghl_classification*) { return 0; }
uint32_t GHL_CALL ghl_classification_considered_bytes(const ghl_classification*) { return 0; }
ghl_status GHL_CALL ghl_classification_result(const ghl_classification* classification, size_t, uint64_t* out_language_id, double* out_score) { if (classification == nullptr || out_language_id == nullptr || out_score == nullptr) return GHL_STATUS_INVALID_ARGUMENT; *out_language_id = 0; *out_score = 0; return GHL_STATUS_UNSUPPORTED; }

void GHL_CALL ghl_language_id_list_release(ghl_language_id_list* languages) { delete languages; }
size_t GHL_CALL ghl_language_id_list_count(const ghl_language_id_list*) { return 0; }
ghl_status GHL_CALL ghl_language_id_list_at(const ghl_language_id_list* languages, size_t, uint64_t* out_language_id) { if (languages == nullptr || out_language_id == nullptr) return GHL_STATUS_INVALID_ARGUMENT; *out_language_id = 0; return GHL_STATUS_UNSUPPORTED; }

ghl_status GHL_CALL ghl_error_status(const ghl_error* error) { return error == nullptr ? GHL_STATUS_INVALID_ARGUMENT : error->status; }
ghl_string_view GHL_CALL ghl_error_message(const ghl_error* error) { return error == nullptr ? empty_view() : ghl_string_view{error->message.data(), error->message.size()}; }
ghl_string_view GHL_CALL ghl_error_ruby_class(const ghl_error*) { return empty_view(); }
ghl_string_view GHL_CALL ghl_error_ruby_backtrace(const ghl_error*) { return empty_view(); }
void GHL_CALL ghl_error_release(ghl_error* error) { delete error; }

} // extern "C"
