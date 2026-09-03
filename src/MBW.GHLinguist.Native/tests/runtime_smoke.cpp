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
    const ghl_capabilities required_capabilities = GHL_CAP_LANGUAGE_REGISTRY | GHL_CAP_STANDARD_DETECTION |
        GHL_CAP_CONTENT_CLASSIFIER | GHL_CAP_STRATEGY_TRACE;
    if ((ghl_runtime_capabilities(runtime) & required_capabilities) != required_capabilities || ghl_runtime_language_count(runtime) == 0) {
        std::fprintf(stderr, "Runtime did not project the required Linguist bridge capabilities.\n");
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

    const char source[] = "def hello\n  puts :ok\nend\n";
    const ghl_blob_input blob = {
        sizeof(ghl_blob_input), 0,
        {"src/sample.rb", 13}, {"sample.rb", 9},
        {reinterpret_cast<const uint8_t*>(source), sizeof(source) - 1},
        {0, 0, 0, 0}};
    const ghl_analysis_options analysis_options = {
        sizeof(ghl_analysis_options), GHL_ANALYSIS_INCLUDE_TRACE | GHL_ANALYSIS_INCLUDE_LINE_COUNTS,
        GHL_STRATEGY_MASK_DEFAULT, 0, {0, 0, 0, 0}};
    ghl_analysis* analysis = nullptr;
    if (ghl_runtime_analyze(runtime, &blob, &analysis_options, &analysis, &error) != GHL_STATUS_OK ||
        ghl_analysis_language_id(analysis) != ruby_id || ghl_analysis_strategy(analysis) != GHL_STRATEGY_EXTENSION ||
        ghl_analysis_loc(analysis) == 0 || ghl_analysis_trace_count(analysis) == 0) {
        std::fprintf(stderr, "Bridge analysis projection failed.\n");
        ghl_error_release(error);
        ghl_analysis_release(analysis);
        ghl_runtime_release(runtime);
        return 1;
    }
    ghl_strategy_trace_entry trace = {};
    trace.struct_size = sizeof(trace);
    if (ghl_analysis_trace_entry(analysis, 0, &trace) != GHL_STATUS_OK || trace.strategy != GHL_STRATEGY_MODELINE) {
        std::fprintf(stderr, "Analysis trace projection failed.\n");
        ghl_analysis_release(analysis);
        ghl_runtime_release(runtime);
        return 1;
    }
    ghl_analysis_release(analysis);

    const ghl_classify_options classify_options = {
        sizeof(ghl_classify_options), 0, GHL_LANGUAGE_MASK_ALL, 50 * 1024, &ruby_id, 1, {0, 0, 0, 0}};
    ghl_classification* classification = nullptr;
    if (ghl_runtime_classify(runtime, {reinterpret_cast<const uint8_t*>(source), sizeof(source) - 1}, &classify_options,
            &classification, &error) != GHL_STATUS_OK ||
        ghl_classification_considered_bytes(classification) != sizeof(source) - 1 || ghl_classification_count(classification) == 0) {
        std::fprintf(stderr, "Bridge classification projection failed.\n");
        ghl_error_release(error);
        ghl_classification_release(classification);
        ghl_runtime_release(runtime);
        return 1;
    }
    uint64_t classified_id = 0;
    double classified_score = 0;
    if (ghl_classification_result(classification, 0, &classified_id, &classified_score) != GHL_STATUS_OK || classified_id != ruby_id) {
        std::fprintf(stderr, "Classification result projection failed.\n");
        ghl_classification_release(classification);
        ghl_runtime_release(runtime);
        return 1;
    }
    ghl_classification_release(classification);

    std::printf("Ruby %.*s initialized; languages=%zu; capabilities=%llu\n", static_cast<int>(version.ruby_version.length),
        version.ruby_version.data, ghl_runtime_language_count(runtime), static_cast<unsigned long long>(ghl_runtime_capabilities(runtime)));
    ghl_runtime_release(runtime);
    return 0;
}
