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

    std::printf("Ruby %.*s initialized; capabilities=%llu\n", static_cast<int>(version.ruby_version.length),
        version.ruby_version.data, static_cast<unsigned long long>(ghl_runtime_capabilities(runtime)));
    ghl_runtime_release(runtime);
    return 0;
}
