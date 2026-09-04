#include "ghlinguist.h"

#include <cmath>
#include <cstdio>
#include <cstring>
#include <string>
#include <vector>

int main(int argc, char** argv) {
    if (argc != 2) {
        std::fprintf(stderr, "Usage: ghlinguist_smoke <asset-root>\n");
        return 2;
    }

    if (ghl_abi_version_major() != GHL_ABI_VERSION_MAJOR || ghl_abi_version_minor() != GHL_ABI_VERSION_MINOR) {
        std::fprintf(stderr, "Exported ABI version does not match the public header.\n");
        return 1;
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
        const ghl_string_view ruby_class = ghl_error_ruby_class(error);
        const ghl_string_view ruby_backtrace = ghl_error_ruby_backtrace(error);
        std::fprintf(stderr, "Runtime creation failed (%d): %.*s\n", status,
            static_cast<int>(message.length), message.data == nullptr ? "" : message.data);
        if (ruby_class.length != 0) std::fprintf(stderr, "Ruby exception: %.*s\n", static_cast<int>(ruby_class.length), ruby_class.data);
        if (ruby_backtrace.length != 0) std::fprintf(stderr, "%.*s\n", static_cast<int>(ruby_backtrace.length), ruby_backtrace.data);
        ghl_error_release(error);
        return 1;
    }

    ghl_version_info version = {};
    version.struct_size = sizeof(version);
    const auto view_equals = [](ghl_string_view value, const char* expected) {
        const size_t length = std::strlen(expected);
        return value.length == length && value.data != nullptr && std::memcmp(value.data, expected, length) == 0;
    };
    if (ghl_runtime_version(runtime, &version) != GHL_STATUS_OK ||
        version.abi_major != GHL_ABI_VERSION_MAJOR || version.abi_minor != GHL_ABI_VERSION_MINOR ||
        !view_equals(version.ruby_version, "4.0.6") || !view_equals(version.linguist_version, "9.6.0") ||
        !view_equals(version.linguist_revision, "196b2a14418cab005065c72c9759370934c184bc") ||
        !view_equals(version.classifier_sha256, "24af803786a1157cb36a59feb5b4f2f3341a034ef7b5edd5b762a6d6ccb5d95d")) {
        std::fprintf(stderr, "Runtime version projection did not match the locked release inputs.\n");
        ghl_runtime_release(runtime);
        return 1;
    }
    const ghl_capabilities required_capabilities = GHL_CAP_LANGUAGE_REGISTRY | GHL_CAP_STANDARD_DETECTION |
        GHL_CAP_CONTENT_CLASSIFIER | GHL_CAP_STRATEGY_TRACE | GHL_CAP_ENCODING_BINARY |
        GHL_CAP_GENERATED_DETECTION | GHL_CAP_PATH_CLASSIFICATION;
    if (ghl_runtime_capabilities(runtime) != required_capabilities || ghl_runtime_language_count(runtime) <= 700) {
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

    const ghl_string_view python = {"Python", 6};
    matches = nullptr;
    if (ghl_runtime_lookup_languages(runtime, GHL_LOOKUP_NAME, python, &matches, &error) != GHL_STATUS_OK ||
        ghl_language_id_list_count(matches) != 1) {
        std::fprintf(stderr, "Python language lookup failed.\n");
        ghl_error_release(error);
        ghl_language_id_list_release(matches);
        ghl_runtime_release(runtime);
        return 1;
    }
    uint64_t python_id = 0;
    if (ghl_language_id_list_at(matches, 0, &python_id) != GHL_STATUS_OK) {
        std::fprintf(stderr, "Python language ID projection failed.\n");
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
        ghl_analysis_loc(analysis) == 0 || ghl_analysis_trace_count(analysis) == 0 ||
        (ghl_analysis_flags(analysis) & (GHL_BLOB_TEXT | GHL_BLOB_DETECTABLE | GHL_BLOB_INCLUDE_IN_STATS)) !=
            (GHL_BLOB_TEXT | GHL_BLOB_DETECTABLE | GHL_BLOB_INCLUDE_IN_STATS)) {
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

    const ghl_blob_input vendored_blob = {
        sizeof(ghl_blob_input), 0,
        {"vendor/sample.rb", 16}, {"sample.rb", 9},
        {reinterpret_cast<const uint8_t*>(source), sizeof(source) - 1},
        {0, 0, 0, 0}};
    analysis = nullptr;
    if (ghl_runtime_analyze(runtime, &vendored_blob, &analysis_options, &analysis, &error) != GHL_STATUS_OK ||
        ghl_analysis_language_id(analysis) != ruby_id ||
        (ghl_analysis_flags(analysis) & GHL_BLOB_VENDORED) == 0 ||
        (ghl_analysis_flags(analysis) & GHL_BLOB_INCLUDE_IN_STATS) != 0) {
        std::fprintf(stderr, "Vendored path classification failed.\n");
        ghl_error_release(error);
        ghl_analysis_release(analysis);
        ghl_runtime_release(runtime);
        return 1;
    }
    ghl_analysis_release(analysis);

    const uint8_t binary_source[] = {'a', 'b', 'c', 0, 'd', 'e', 'f'};
    const ghl_blob_input binary_blob = {
        sizeof(ghl_blob_input), 0,
        {"data.bin", 8}, {"data.bin", 8},
        {binary_source, sizeof(binary_source)},
        {0, 0, 0, 0}};
    analysis = nullptr;
    if (ghl_runtime_analyze(runtime, &binary_blob, &analysis_options, &analysis, &error) != GHL_STATUS_OK ||
        ghl_analysis_language_id(analysis) != GHL_LANGUAGE_ID_NONE ||
        ghl_analysis_strategy(analysis) != GHL_STRATEGY_NONE ||
        (ghl_analysis_flags(analysis) & GHL_BLOB_BINARY) == 0 ||
        (ghl_analysis_flags(analysis) & GHL_BLOB_DETECTABLE) != 0) {
        std::fprintf(stderr, "Binary detection projection failed.\n");
        ghl_error_release(error);
        ghl_analysis_release(analysis);
        ghl_runtime_release(runtime);
        return 1;
    }
    ghl_analysis_release(analysis);

    std::string classifier_source;
    const char classifier_sample[] =
        "class Greeter\n  def initialize(name)\n    @name = name\n  end\n  def hello\n"
        "    puts \"Hello #{@name}\"\n  end\nend\n";
    for (int index = 0; index < 20; ++index) classifier_source += classifier_sample;
    const uint64_t classifier_candidates[] = {ruby_id, python_id};
    const ghl_classify_options classify_options = {
        sizeof(ghl_classify_options), 0, GHL_LANGUAGE_MASK_ALL, 50 * 1024,
        classifier_candidates, 2, {0, 0, 0, 0}};
    ghl_classification* classification = nullptr;
    if (ghl_runtime_classify(runtime,
            {reinterpret_cast<const uint8_t*>(classifier_source.data()), classifier_source.size()}, &classify_options,
            &classification, &error) != GHL_STATUS_OK ||
        ghl_classification_considered_bytes(classification) != classifier_source.size() ||
        ghl_classification_count(classification) != 2) {
        std::fprintf(stderr, "Bridge classification projection failed.\n");
        ghl_error_release(error);
        ghl_classification_release(classification);
        ghl_runtime_release(runtime);
        return 1;
    }
    uint64_t classified_id = 0;
    double classified_score = 0;
    if (ghl_classification_result(classification, 0, &classified_id, &classified_score) != GHL_STATUS_OK ||
        classified_id != ruby_id || std::abs(classified_score - 0.35462628853032) > 1e-12) {
        std::fprintf(stderr, "Classification result projection failed.\n");
        ghl_classification_release(classification);
        ghl_runtime_release(runtime);
        return 1;
    }
    if (ghl_classification_result(classification, 1, &classified_id, &classified_score) != GHL_STATUS_OK ||
        classified_id != python_id || std::abs(classified_score - 0.15510731503460715) > 1e-12) {
        std::fprintf(stderr, "Ranked classification result projection failed.\n");
        ghl_classification_release(classification);
        ghl_runtime_release(runtime);
        return 1;
    }
    ghl_classification_release(classification);

    std::string oversized_source;
    while (oversized_source.size() <= 60 * 1024) oversized_source += "puts 'Hello'\n";
    classification = nullptr;
    if (ghl_runtime_classify(runtime, {reinterpret_cast<const uint8_t*>(oversized_source.data()), oversized_source.size()}, &classify_options,
            &classification, &error) != GHL_STATUS_OK ||
        ghl_classification_considered_bytes(classification) != 50 * 1024) {
        std::fprintf(stderr, "Classification input cap failed.\n");
        ghl_error_release(error);
        ghl_classification_release(classification);
        ghl_runtime_release(runtime);
        return 1;
    }
    ghl_classification_release(classification);
    classification = nullptr;

    std::vector<uint64_t> too_many_candidates(4097, ruby_id);
    ghl_classify_options invalid_candidate_options = classify_options;
    invalid_candidate_options.candidate_language_ids = too_many_candidates.data();
    invalid_candidate_options.candidate_language_count = too_many_candidates.size();
    if (ghl_runtime_classify(runtime, {reinterpret_cast<const uint8_t*>(source), sizeof(source) - 1}, &invalid_candidate_options,
            &classification, &error) != GHL_STATUS_INVALID_ARGUMENT || error == nullptr ||
        ghl_error_status(error) != GHL_STATUS_INVALID_ARGUMENT || ghl_error_message(error).length == 0) {
        std::fprintf(stderr, "Classification candidate cap failed.\n");
        ghl_error_release(error);
        ghl_classification_release(classification);
        ghl_runtime_release(runtime);
        return 1;
    }
    ghl_error_release(error);
    error = nullptr;

    std::printf("Ruby %.*s initialized; languages=%zu; capabilities=%llu\n", static_cast<int>(version.ruby_version.length),
        version.ruby_version.data, ghl_runtime_language_count(runtime), static_cast<unsigned long long>(ghl_runtime_capabilities(runtime)));
    ghl_runtime_release(runtime);
    return 0;
}
