#include "ghlinguist.h"

#include <cstdio>
#include <cstring>

int main(int argc, char** argv) {
    if (argc != 2) {
        std::fprintf(stderr, "Usage: ghlinguist_smoke <asset-root>\n");
        return 2;
    }

    const ghl_runtime_options options = {
        sizeof(ghl_runtime_options), 0,
        {argv[1], std::strlen(argv[1])},
        {0, 0, 0, 0}};
    ghl_runtime* runtime = nullptr;
    ghl_error* error = nullptr;
    const ghl_status status = ghl_runtime_create(&options, &runtime, &error);
    if (status != GHL_STATUS_OK) {
        const ghl_string_view message = ghl_error_message(error);
        std::fprintf(stderr, "Runtime creation failed (%d): %.*s\n", status,
            static_cast<int>(message.length), message.data == nullptr ? "" : message.data);
        ghl_error_release(error);
        return 1;
    }

    ghl_version_info version = {};
    version.struct_size = sizeof(version);
    if (ghl_runtime_version(runtime, &version) != GHL_STATUS_OK || version.ruby_version.length == 0) {
        std::fprintf(stderr, "Runtime did not report a Ruby version.\n");
        ghl_runtime_release(runtime);
        return 1;
    }
    if ((ghl_runtime_capabilities(runtime) & GHL_CAP_LANGUAGE_REGISTRY) == 0 || ghl_runtime_language_count(runtime) == 0) {
        std::fprintf(stderr, "Runtime did not project the Linguist language registry.\n");
        ghl_runtime_release(runtime);
        return 1;
    }

    const ghl_string_view ruby = {"Ruby", 4};
    ghl_language_id_list* matches = nullptr;
    if (ghl_runtime_lookup_languages(runtime, GHL_LOOKUP_NAME, ruby, &matches, &error) != GHL_STATUS_OK ||
        ghl_language_id_list_count(matches) != 1) {
        std::fprintf(stderr, "Language name lookup failed.\n");
        ghl_error_release(error);
        ghl_language_id_list_release(matches);
        ghl_runtime_release(runtime);
        return 1;
    }
    uint64_t ruby_id = 0;
    ghl_language_info ruby_info = {};
    ruby_info.struct_size = sizeof(ruby_info);
    if (ghl_language_id_list_at(matches, 0, &ruby_id) != GHL_STATUS_OK ||
        ghl_runtime_language_info(runtime, ruby_id, &ruby_info) != GHL_STATUS_OK ||
        ruby_info.name.length != ruby.length || std::memcmp(ruby_info.name.data, ruby.data, ruby.length) != 0) {
        std::fprintf(stderr, "Language metadata projection failed.\n");
        ghl_language_id_list_release(matches);
        ghl_runtime_release(runtime);
        return 1;
    }
    ghl_language_id_list_release(matches);

    std::printf("Ruby %.*s initialized; languages=%zu; capabilities=%llu\n", static_cast<int>(version.ruby_version.length),
        version.ruby_version.data, ghl_runtime_language_count(runtime), static_cast<unsigned long long>(ghl_runtime_capabilities(runtime)));
    ghl_runtime_release(runtime);
    return 0;
}
