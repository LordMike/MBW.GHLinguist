#pragma once

#include <cstddef>
#include <cstdint>
#include <string>
#include <unordered_map>
#include <utility>
#include <vector>

// Linguist's content classifier (Linguist::Classifier#classify) over the pinned classifier database, projected
// once from CRuby so Classify runs on the calling thread without the Ruby worker.
struct ClassifierModel {
    static constexpr uint32_t kNoCentroid = UINT32_MAX;

    struct Language {
        uint64_t id = 0;
        uint32_t type_mask = 0;
        uint32_t centroid = kNoCentroid;
    };

    std::unordered_map<std::string, uint32_t> vocabulary;
    std::vector<double> icf;
    // Centroid entries grouped by vocabulary index: term t owns entries [posting_starts[t], posting_starts[t + 1]).
    std::vector<uint32_t> posting_starts;
    std::vector<uint32_t> posting_centroids;
    std::vector<double> posting_weights;
    uint32_t centroid_count = 0;
    // Registry order, which is the order Linguist::Classifier scores languages in when no candidates are given.
    std::vector<Language> languages;
};

// Ranks the languages at `language_indices` (into model.languages; every language when null, otherwise at least one) exactly as
// Linguist::Classifier#classify ranks their names: limited to `allowed_types` and to languages with a centroid,
// without repeats, best first, keeping only positive scores.
std::vector<std::pair<uint64_t, double>> classify_content(const ClassifierModel& model, const char* data, size_t length,
    const size_t* language_indices, size_t language_count, uint32_t allowed_types);
