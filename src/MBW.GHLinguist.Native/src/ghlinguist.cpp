#include "ghlinguist.h"

#include <condition_variable>
#include <algorithm>
#include <cctype>
#include <cstring>
#include <filesystem>
#include <functional>
#include <future>
#include <memory>
#include <mutex>
#include <new>
#include <queue>
#include <string>
#include <thread>
#include <unordered_map>
#include <utility>
#include <vector>

#if defined(GHL_RUBY_EMBEDDING)
#include <ruby.h>
#endif

struct NativeLanguage {
    uint64_t id = 0;
    uint64_t group_id = 0;
    ghl_language_type type = GHL_LANGUAGE_TYPE_UNKNOWN;
    uint32_t flags = 0;
    std::string name;
    std::string fs_name;
    std::string color;
    std::string tm_scope;
    std::string ace_mode;
    std::string codemirror_mode;
    std::string codemirror_mime_type;
    std::vector<std::string> aliases;
    std::vector<std::string> extensions;
    std::vector<std::string> interpreters;
    std::vector<std::string> filenames;
};

struct LanguageRegistry {
    std::vector<NativeLanguage> languages;
    std::unordered_map<uint64_t, size_t> by_id;
    std::unordered_map<std::string, uint64_t> names;
    std::unordered_map<std::string, uint64_t> aliases;
    std::unordered_map<std::string, std::vector<uint64_t>> filenames;
    std::unordered_map<std::string, std::vector<uint64_t>> extensions;
    std::unordered_map<std::string, std::vector<uint64_t>> interpreters;
};

struct ghl_runtime {
    uint32_t magic = 0x47484C31u;
    std::string ruby_version;
    std::string linguist_version;
    std::shared_ptr<const LanguageRegistry> languages;
};
struct ghl_analysis { uint32_t unused = 0; };
struct ghl_classification { uint32_t unused = 0; };
struct ghl_language_id_list { std::vector<uint64_t> ids; };
struct ghl_error {
    ghl_status status;
    std::string message;
    std::string ruby_class;
    std::string ruby_backtrace;
};

namespace {

constexpr uint32_t kRuntimeMagic = 0x47484C31u;
constexpr char kWrapperVersion[] = "MBW.GHLinguist.Native CRuby bootstrap";
constexpr char kUnavailable[] = "unavailable";
constexpr char kUnsupported[] = "GitHub Linguist Ruby embedding is not enabled in this native build.";

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
ghl_string_view view(const std::string& text) { return {text.data(), text.size()}; }
ghl_string_view empty_view() { return {nullptr, 0}; }

std::string lowercase(std::string value) {
    std::transform(value.begin(), value.end(), value.begin(), [](unsigned char ch) { return static_cast<char>(std::tolower(ch)); });
    return value;
}

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

ghl_status fail(ghl_status status, const std::string& message, ghl_error** out_error,
    std::string ruby_class = {}, std::string ruby_backtrace = {}) {
    clear_error(out_error);
    if (out_error == nullptr) return status;
    ghl_error* error = new (std::nothrow) ghl_error{status, message, std::move(ruby_class), std::move(ruby_backtrace)};
    if (error == nullptr) return GHL_STATUS_OUT_OF_MEMORY;
    *out_error = error;
    return status;
}

ghl_status unsupported(ghl_error** out_error) { return fail(GHL_STATUS_UNSUPPORTED, kUnsupported, out_error); }
ghl_status invalid(const char* message, ghl_error** out_error = nullptr) { return fail(GHL_STATUS_INVALID_ARGUMENT, message, out_error); }

bool valid_runtime_options(const ghl_runtime_options* options) {
    return options != nullptr && options->struct_size >= sizeof(*options) && options->flags == 0 &&
        options->asset_root.data != nullptr && options->asset_root.length != 0 && valid_utf8(options->asset_root) &&
        reserved_zero(options->reserved);
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
bool valid_version_output(const ghl_version_info* output) { return valid_output(output) && output->reserved32 == 0 && reserved_zero(output->reserved); }
bool valid_language_output(const ghl_language_info* output) { return valid_output(output) && reserved_zero(output->reserved); }
bool valid_trace_output(const ghl_strategy_trace_entry* output) { return valid_output(output) && output->reserved32 == 0 && reserved_zero(output->reserved); }

struct WorkerResult {
    ghl_status status = GHL_STATUS_INTERNAL_ERROR;
    std::string message;
    std::string ruby_class;
    std::string ruby_backtrace;
    std::string ruby_version;
    std::string linguist_version;
    std::shared_ptr<const LanguageRegistry> languages;
};

#if defined(GHL_RUBY_EMBEDDING)
struct RubyErrorDetails {
    std::string message;
    std::string ruby_class;
    std::string ruby_backtrace;
};

std::string ruby_string(VALUE value) {
    return std::string(RSTRING_PTR(value), static_cast<size_t>(RSTRING_LEN(value)));
}

std::string ruby_optional_string(VALUE value) {
    return NIL_P(value) ? std::string() : ruby_string(value);
}

uint64_t ruby_integer(VALUE value) { return NUM2ULL(value); }

ghl_language_type ruby_language_type(VALUE value) {
    const std::string type = ruby_optional_string(value);
    if (type == "data") return GHL_LANGUAGE_TYPE_DATA;
    if (type == "markup") return GHL_LANGUAGE_TYPE_MARKUP;
    if (type == "programming") return GHL_LANGUAGE_TYPE_PROGRAMMING;
    if (type == "prose") return GHL_LANGUAGE_TYPE_PROSE;
    return GHL_LANGUAGE_TYPE_UNKNOWN;
}

void append_ruby_strings(VALUE values, std::vector<std::string>* target) {
    const long count = RARRAY_LEN(values);
    target->reserve(static_cast<size_t>(count));
    for (long index = 0; index < count; ++index) target->push_back(ruby_string(rb_ary_entry(values, index)));
}

void add_lookup(std::unordered_map<std::string, std::vector<uint64_t>>* index, const std::string& key, uint64_t id, bool fold_case) {
    (*index)[fold_case ? lowercase(key) : key].push_back(id);
}

struct RubyErrorContext { VALUE exception; RubyErrorDetails* details; };

VALUE capture_ruby_error(VALUE opaque) {
    auto* context = reinterpret_cast<RubyErrorContext*>(opaque);
    context->details->ruby_class = ruby_string(rb_class_name(CLASS_OF(context->exception)));
    context->details->message = ruby_string(rb_funcall(context->exception, rb_intern("message"), 0));
    VALUE backtrace = rb_funcall(context->exception, rb_intern("backtrace"), 0);
    if (!NIL_P(backtrace)) {
        context->details->ruby_backtrace = ruby_string(rb_funcall(backtrace, rb_intern("join"), 1, rb_str_new_cstr("\n")));
    }
    return Qnil;
}

RubyErrorDetails ruby_error_details() {
    RubyErrorDetails details;
    const VALUE exception = rb_errinfo();
    RubyErrorContext context{exception, &details};
    int state = 0;
    rb_protect(capture_ruby_error, reinterpret_cast<VALUE>(&context), &state);
    rb_set_errinfo(Qnil);
    if (state != 0) {
        details.message = "Ruby raised an exception while formatting an earlier exception.";
        details.ruby_class.clear();
        details.ruby_backtrace.clear();
    }
    return details;
}

struct RubyStartupContext {
    std::string asset_root;
    std::string linguist_root;
    std::string ruby_version;
    std::string linguist_version;
    std::shared_ptr<LanguageRegistry> languages;
};

std::shared_ptr<LanguageRegistry> project_languages() {
    auto registry = std::make_shared<LanguageRegistry>();
    const VALUE language_class = rb_path2class("Linguist::Language");
    const VALUE languages = rb_funcall(language_class, rb_intern("all"), 0);
    const long count = RARRAY_LEN(languages);
    registry->languages.reserve(static_cast<size_t>(count));
    for (long index = 0; index < count; ++index) {
        const VALUE language = rb_ary_entry(languages, index);
        NativeLanguage native;
        native.id = ruby_integer(rb_funcall(language, rb_intern("language_id"), 0));
        native.group_id = ruby_integer(rb_funcall(rb_funcall(language, rb_intern("group"), 0), rb_intern("language_id"), 0));
        native.type = ruby_language_type(rb_funcall(language, rb_intern("type"), 0));
        native.name = ruby_string(rb_funcall(language, rb_intern("name"), 0));
        native.fs_name = ruby_optional_string(rb_funcall(language, rb_intern("fs_name"), 0));
        native.color = ruby_optional_string(rb_funcall(language, rb_intern("color"), 0));
        native.tm_scope = ruby_optional_string(rb_funcall(language, rb_intern("tm_scope"), 0));
        native.ace_mode = ruby_optional_string(rb_funcall(language, rb_intern("ace_mode"), 0));
        native.codemirror_mode = ruby_optional_string(rb_funcall(language, rb_intern("codemirror_mode"), 0));
        native.codemirror_mime_type = ruby_optional_string(rb_funcall(language, rb_intern("codemirror_mime_type"), 0));
        if (RTEST(rb_funcall(language, rb_intern("popular?"), 0))) native.flags |= GHL_LANGUAGE_POPULAR;
        if (RTEST(rb_funcall(language, rb_intern("wrap"), 0))) native.flags |= GHL_LANGUAGE_WRAP;
        append_ruby_strings(rb_funcall(language, rb_intern("aliases"), 0), &native.aliases);
        append_ruby_strings(rb_funcall(language, rb_intern("extensions"), 0), &native.extensions);
        append_ruby_strings(rb_funcall(language, rb_intern("interpreters"), 0), &native.interpreters);
        append_ruby_strings(rb_funcall(language, rb_intern("filenames"), 0), &native.filenames);
        const size_t native_index = registry->languages.size();
        registry->by_id.emplace(native.id, native_index);
        registry->names.emplace(lowercase(native.name), native.id);
        if (!native.fs_name.empty()) registry->names.emplace(lowercase(native.fs_name), native.id);
        for (const std::string& value : native.aliases) registry->aliases.emplace(lowercase(value), native.id);
        for (const std::string& value : native.extensions) add_lookup(&registry->extensions, value, native.id, true);
        for (const std::string& value : native.interpreters) add_lookup(&registry->interpreters, value, native.id, false);
        for (const std::string& value : native.filenames) add_lookup(&registry->filenames, value, native.id, false);
        registry->languages.push_back(std::move(native));
    }
    return registry;
}

VALUE load_runtime_assets(VALUE opaque) {
    auto* context = reinterpret_cast<RubyStartupContext*>(opaque);
    VALUE load_path = rb_gv_get("$LOAD_PATH");
    rb_ary_unshift(load_path, rb_utf8_str_new(context->linguist_root.data(), static_cast<long>(context->linguist_root.size())));
    rb_ary_unshift(load_path, rb_utf8_str_new(context->asset_root.data(), static_cast<long>(context->asset_root.size())));
    rb_funcall(rb_mKernel, rb_intern("require"), 1, rb_utf8_str_new_cstr("linguist/language"));

    int state = 0;
    VALUE ruby_version = rb_eval_string_protect("RUBY_VERSION", &state);
    if (state != 0) rb_jump_tag(state);
    context->ruby_version = ruby_string(ruby_version);
    VALUE linguist_version = rb_eval_string_protect("Linguist::VERSION", &state);
    if (state != 0) rb_jump_tag(state);
    context->linguist_version = ruby_string(linguist_version);
    context->languages = project_languages();
    return Qnil;
}
#endif

class RubyWorker {
public:
    RubyWorker() : thread_([this] { run(); }) {}

    WorkerResult ensure_initialized(std::string asset_root) {
        return invoke([this, asset_root = std::move(asset_root)] { return initialize_on_worker(asset_root); });
    }

private:
    template <typename F>
    WorkerResult invoke(F&& operation) {
        auto completion = std::make_shared<std::promise<WorkerResult>>();
        std::future<WorkerResult> result = completion->get_future();
        try {
            {
                std::lock_guard<std::mutex> lock(mutex_);
                jobs_.push([operation = std::forward<F>(operation), completion]() mutable {
                    try {
                        completion->set_value(operation());
                    } catch (const std::exception& exception) {
                        completion->set_value({GHL_STATUS_NATIVE_FAILURE, exception.what(), {}, {}, {}, {}});
                    } catch (...) {
                        completion->set_value({GHL_STATUS_NATIVE_FAILURE, "Ruby worker failed unexpectedly.", {}, {}, {}, {}});
                    }
                });
            }
            wake_.notify_one();
            return result.get();
        } catch (const std::exception& exception) {
            return {GHL_STATUS_NATIVE_FAILURE, exception.what(), {}, {}, {}, {}};
        } catch (...) {
            return {GHL_STATUS_NATIVE_FAILURE, "Unable to schedule work on the Ruby worker.", {}, {}, {}, {}};
        }
    }

    WorkerResult initialize_on_worker(const std::string& asset_root) {
        if (initialization_attempted_) {
            if (asset_root != asset_root_) {
                return {GHL_STATUS_NATIVE_FAILURE, "CRuby is already initialized for a different asset_root.", {}, {}, {}, {}};
            }
            return initialization_result_;
        }

        initialization_attempted_ = true;
        asset_root_ = asset_root;
#if defined(GHL_RUBY_EMBEDDING)
        RubyStartupContext context{asset_root, (std::filesystem::u8path(asset_root) / "lib").u8string(), {}, {}};
        char program_name[] = "ghlinguist";
        char* arguments[] = {program_name, nullptr};
        char** argument_vector = arguments;
        int argument_count = 1;
        ruby_sysinit(&argument_count, &argument_vector);
        RUBY_INIT_STACK;
        ruby_init();
        ruby_init_loadpath();
        ruby_script("ghlinguist");
        int state = 0;
        rb_protect(load_runtime_assets, reinterpret_cast<VALUE>(&context), &state);
        if (state != 0) {
            RubyErrorDetails details = ruby_error_details();
            initialization_result_ = {GHL_STATUS_RUBY_EXCEPTION, std::move(details.message),
                std::move(details.ruby_class), std::move(details.ruby_backtrace), {}, {}, {}};
        } else {
            initialization_result_ = {GHL_STATUS_OK, {}, {}, {}, std::move(context.ruby_version), std::move(context.linguist_version), std::move(context.languages)};
        }
#else
        initialization_result_ = {GHL_STATUS_UNSUPPORTED, kUnsupported, {}, {}, {}, {}, {}};
#endif
        return initialization_result_;
    }

    void run() {
        for (;;) {
            std::function<void()> job;
            {
                std::unique_lock<std::mutex> lock(mutex_);
                wake_.wait(lock, [this] { return !jobs_.empty(); });
                job = std::move(jobs_.front());
                jobs_.pop();
            }
            job();
        }
    }

    std::mutex mutex_;
    std::condition_variable wake_;
    std::queue<std::function<void()>> jobs_;
    std::thread thread_;
    bool initialization_attempted_ = false;
    std::string asset_root_;
    WorkerResult initialization_result_;
};

RubyWorker& ruby_worker() {
    // CRuby is intentionally process-lifetime: it cannot be safely unloaded with runtime handles.
    static RubyWorker* worker = new RubyWorker();
    return *worker;
}

bool validate_asset_root(const ghl_string_view asset_root, std::string* normalized, std::string* message) {
    std::error_code error;
    const std::filesystem::path root = std::filesystem::u8path(std::string(asset_root.data, asset_root.length));
    if (!std::filesystem::is_directory(root, error) || error) {
        *message = "asset_root must name an existing directory.";
        return false;
    }
    const std::filesystem::path linguist_root = root / "lib" / "linguist";
    if (!std::filesystem::is_regular_file(linguist_root / "version.rb", error) || error ||
        !std::filesystem::is_regular_file(linguist_root / "language.rb", error) || error) {
        *message = "asset_root must contain lib/linguist/version.rb and lib/linguist/language.rb.";
        return false;
    }
    const std::filesystem::path canonical = std::filesystem::weakly_canonical(root, error);
    if (error) {
        *message = "asset_root could not be canonicalized.";
        return false;
    }
    *normalized = canonical.u8string();
    return true;
}

const NativeLanguage* language_by_id(const ghl_runtime* runtime, uint64_t id) {
    const auto iterator = runtime->languages->by_id.find(id);
    return iterator == runtime->languages->by_id.end() ? nullptr : &runtime->languages->languages[iterator->second];
}

const std::vector<std::string>* language_collection(const NativeLanguage& language, ghl_language_collection collection) {
    switch (collection) {
    case GHL_LANGUAGE_COLLECTION_ALIASES: return &language.aliases;
    case GHL_LANGUAGE_COLLECTION_EXTENSIONS: return &language.extensions;
    case GHL_LANGUAGE_COLLECTION_INTERPRETERS: return &language.interpreters;
    case GHL_LANGUAGE_COLLECTION_FILENAMES: return &language.filenames;
    default: return nullptr;
    }
}

std::vector<uint64_t> find_languages(const LanguageRegistry& registry, ghl_lookup_kind kind, const std::string& value) {
    const auto comma = value.find(',');
    const std::string alternate = comma == std::string::npos ? value : value.substr(0, comma);
    if (kind == GHL_LOOKUP_NAME || kind == GHL_LOOKUP_ALIAS) {
        const auto& index = kind == GHL_LOOKUP_NAME ? registry.names : registry.aliases;
        auto iterator = index.find(lowercase(value));
        if (iterator == index.end() && alternate != value) iterator = index.find(lowercase(alternate));
        return iterator == index.end() ? std::vector<uint64_t>() : std::vector<uint64_t>{iterator->second};
    }
    if (kind == GHL_LOOKUP_FILENAME) {
        const size_t separator = value.find_last_of("/\\");
        const auto iterator = registry.filenames.find(value.substr(separator == std::string::npos ? 0 : separator + 1));
        return iterator == registry.filenames.end() ? std::vector<uint64_t>() : iterator->second;
    }
    if (kind == GHL_LOOKUP_EXTENSION) {
        const std::string name = lowercase(value.substr(value.find_last_of("/\\") + 1));
        size_t dot = name.find('.');
        while (dot != std::string::npos) {
            const auto iterator = registry.extensions.find(name.substr(dot));
            if (iterator != registry.extensions.end() && !iterator->second.empty()) return iterator->second;
            dot = name.find('.', dot + 1);
        }
        return {};
    }
    if (kind == GHL_LOOKUP_INTERPRETER) {
        const auto iterator = registry.interpreters.find(value);
        return iterator == registry.interpreters.end() ? std::vector<uint64_t>() : iterator->second;
    }
    return {};
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

    std::string asset_root;
    std::string asset_error;
    if (!validate_asset_root(options->asset_root, &asset_root, &asset_error)) return fail(GHL_STATUS_INVALID_ARGUMENT, asset_error, out_error);
    WorkerResult worker = ruby_worker().ensure_initialized(std::move(asset_root));
    if (worker.status != GHL_STATUS_OK) {
        return fail(worker.status, worker.message.empty() ? "Ruby initialization failed." : worker.message, out_error,
            std::move(worker.ruby_class), std::move(worker.ruby_backtrace));
    }

    ghl_runtime* runtime = new (std::nothrow) ghl_runtime{kRuntimeMagic, std::move(worker.ruby_version), std::move(worker.linguist_version), std::move(worker.languages)};
    if (runtime == nullptr) return fail(GHL_STATUS_OUT_OF_MEMORY, "Unable to allocate runtime handle.", out_error);
    *out_runtime = runtime;
    return GHL_STATUS_OK;
}

void GHL_CALL ghl_runtime_release(ghl_runtime* runtime) { if (runtime != nullptr) { runtime->magic = 0; delete runtime; } }
ghl_capabilities GHL_CALL ghl_runtime_capabilities(const ghl_runtime* runtime) {
    return valid_runtime(runtime) && runtime->languages ? GHL_CAP_LANGUAGE_REGISTRY : 0;
}

ghl_status GHL_CALL ghl_runtime_version(const ghl_runtime* runtime, ghl_version_info* out_version) {
    if (!valid_runtime(runtime)) return GHL_STATUS_INVALID_ARGUMENT;
    if (!valid_version_output(out_version)) return GHL_STATUS_ABI_MISMATCH;
    *out_version = {};
    out_version->struct_size = sizeof(*out_version);
    out_version->abi_major = GHL_ABI_VERSION_MAJOR;
    out_version->abi_minor = GHL_ABI_VERSION_MINOR;
    out_version->wrapper_version = view(kWrapperVersion);
    out_version->ruby_version = view(runtime->ruby_version);
    out_version->linguist_version = runtime->linguist_version.empty() ? view(kUnavailable) : view(runtime->linguist_version);
    out_version->linguist_revision = view(kUnavailable);
    out_version->classifier_sha256 = view(kUnavailable);
    return GHL_STATUS_OK;
}

size_t GHL_CALL ghl_runtime_language_count(const ghl_runtime* runtime) {
    return valid_runtime(runtime) && runtime->languages ? runtime->languages->languages.size() : 0;
}
ghl_status GHL_CALL ghl_runtime_language_id_at(const ghl_runtime* runtime, size_t index, uint64_t* out_language_id) {
    if (out_language_id == nullptr || !valid_runtime(runtime)) return GHL_STATUS_INVALID_ARGUMENT;
    *out_language_id = 0;
    if (!runtime->languages || index >= runtime->languages->languages.size()) return GHL_STATUS_NOT_FOUND;
    *out_language_id = runtime->languages->languages[index].id;
    return GHL_STATUS_OK;
}
ghl_status GHL_CALL ghl_runtime_language_info(const ghl_runtime* runtime, uint64_t language_id, ghl_language_info* out_info) {
    if (!valid_runtime(runtime)) return GHL_STATUS_INVALID_ARGUMENT;
    if (!valid_language_output(out_info)) return GHL_STATUS_ABI_MISMATCH;
    *out_info = {};
    out_info->struct_size = sizeof(*out_info);
    if (!runtime->languages) return GHL_STATUS_UNSUPPORTED;
    const NativeLanguage* language = language_by_id(runtime, language_id);
    if (language == nullptr) return GHL_STATUS_NOT_FOUND;
    out_info->type = language->type;
    out_info->language_id = language->id;
    out_info->group_language_id = language->group_id;
    out_info->flags = language->flags;
    out_info->alias_count = static_cast<uint32_t>(language->aliases.size());
    out_info->extension_count = static_cast<uint32_t>(language->extensions.size());
    out_info->interpreter_count = static_cast<uint32_t>(language->interpreters.size());
    out_info->filename_count = static_cast<uint32_t>(language->filenames.size());
    out_info->name = view(language->name);
    out_info->fs_name = view(language->fs_name);
    out_info->color = view(language->color);
    out_info->tm_scope = view(language->tm_scope);
    out_info->ace_mode = view(language->ace_mode);
    out_info->codemirror_mode = view(language->codemirror_mode);
    out_info->codemirror_mime_type = view(language->codemirror_mime_type);
    return GHL_STATUS_OK;
}
ghl_status GHL_CALL ghl_runtime_language_collection_value(const ghl_runtime* runtime, uint64_t language_id, ghl_language_collection collection, size_t index, ghl_string_view* out_value) {
    if (out_value == nullptr || !valid_runtime(runtime)) return GHL_STATUS_INVALID_ARGUMENT;
    *out_value = empty_view();
    if (!runtime->languages) return GHL_STATUS_UNSUPPORTED;
    const NativeLanguage* language = language_by_id(runtime, language_id);
    if (language == nullptr) return GHL_STATUS_NOT_FOUND;
    const std::vector<std::string>* values = language_collection(*language, collection);
    if (values == nullptr || index >= values->size()) return GHL_STATUS_NOT_FOUND;
    *out_value = view((*values)[index]);
    return GHL_STATUS_OK;
}
ghl_status GHL_CALL ghl_runtime_lookup_languages(const ghl_runtime* runtime, ghl_lookup_kind kind, ghl_string_view value, ghl_language_id_list** out_languages, ghl_error** out_error) {
    clear_error(out_error);
    if (out_languages == nullptr || !valid_runtime(runtime) || !valid_utf8(value)) return invalid("lookup arguments are invalid.", out_error);
    *out_languages = nullptr;
    if (kind < GHL_LOOKUP_NAME || kind > GHL_LOOKUP_INTERPRETER) return invalid("lookup kind is invalid.", out_error);
    if (!runtime->languages) return unsupported(out_error);
    ghl_language_id_list* languages = new (std::nothrow) ghl_language_id_list{find_languages(*runtime->languages, kind, std::string(value.data, value.length))};
    if (languages == nullptr) return fail(GHL_STATUS_OUT_OF_MEMORY, "Unable to allocate language lookup results.", out_error);
    *out_languages = languages;
    return GHL_STATUS_OK;
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
size_t GHL_CALL ghl_language_id_list_count(const ghl_language_id_list* languages) { return languages == nullptr ? 0 : languages->ids.size(); }
ghl_status GHL_CALL ghl_language_id_list_at(const ghl_language_id_list* languages, size_t index, uint64_t* out_language_id) { if (languages == nullptr || out_language_id == nullptr) return GHL_STATUS_INVALID_ARGUMENT; *out_language_id = 0; if (index >= languages->ids.size()) return GHL_STATUS_NOT_FOUND; *out_language_id = languages->ids[index]; return GHL_STATUS_OK; }

ghl_status GHL_CALL ghl_error_status(const ghl_error* error) { return error == nullptr ? GHL_STATUS_INVALID_ARGUMENT : error->status; }
ghl_string_view GHL_CALL ghl_error_message(const ghl_error* error) { return error == nullptr ? empty_view() : view(error->message); }
ghl_string_view GHL_CALL ghl_error_ruby_class(const ghl_error* error) { return error == nullptr ? empty_view() : view(error->ruby_class); }
ghl_string_view GHL_CALL ghl_error_ruby_backtrace(const ghl_error* error) { return error == nullptr ? empty_view() : view(error->ruby_backtrace); }
void GHL_CALL ghl_error_release(ghl_error* error) { delete error; }

} // extern "C"
