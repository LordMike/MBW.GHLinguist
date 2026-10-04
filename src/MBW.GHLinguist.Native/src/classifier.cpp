#include "classifier.h"

#include <algorithm>
#include <cmath>
#include <new>

// Linguist's tokenizer extension (ext/linguist/lex.linguist_yy.c), compiled against tokenizer/ruby.h.
extern "C" {
typedef void* yyscan_t;
int linguist_yylex_init_extra(uintptr_t* user_defined, yyscan_t* scanner);
void* linguist_yy_scan_bytes(const char* bytes, int length, yyscan_t scanner);
int linguist_yylex(yyscan_t scanner);
void linguist_yy_delete_buffer(void* buffer, yyscan_t scanner);
int linguist_yylex_destroy(yyscan_t scanner);
}

namespace {

// The token the current scanner action fed; a scanner runs on one thread at a time.
thread_local std::string fed_token;

struct Scratch {
    std::vector<int32_t> term_positions;
    std::vector<uint32_t> terms;
    std::vector<double> weights;
    std::vector<double> scores;
    std::vector<uint8_t> seen_languages;
};

thread_local Scratch scratch;

class Scanner {
public:
    Scanner(const char* data, size_t length) {
        if (linguist_yylex_init_extra(&extra_, &scanner_) != 0) throw std::bad_alloc();
        // Linguist's extension tokenizes at most the first 100,000 bytes.
        buffer_ = linguist_yy_scan_bytes(data, static_cast<int>(std::min<size_t>(length, 100000)), scanner_);
    }

    ~Scanner() {
        linguist_yy_delete_buffer(buffer_, scanner_);
        linguist_yylex_destroy(scanner_);
    }

    Scanner(const Scanner&) = delete;
    Scanner& operator=(const Scanner&) = delete;

    // Mirrors rb_tokenizer_extract_tokens: a token is kept when the scanner fed one during this step.
    template <typename F>
    void each_token(F&& on_token) {
        int more;
        do {
            extra_ = 0;
            more = linguist_yylex(scanner_);
            if (extra_ != 0) on_token(fed_token);
        } while (more != 0);
    }

private:
    uintptr_t extra_ = 0;
    yyscan_t scanner_ = nullptr;
    void* buffer_ = nullptr;
};

} // namespace

// Each scanner reads one buffer, as in Linguist's extension.
extern "C" int linguist_yywrap(yyscan_t) { return 1; }

extern "C" uintptr_t ghl_tokenizer_token(const char* text, long length) {
    fed_token.assign(text, static_cast<size_t>(length));
    return 1;
}

extern "C" uintptr_t ghl_tokenizer_append(uintptr_t token, const char* text, long length) {
    fed_token.append(text, static_cast<size_t>(length));
    return token;
}

// Every floating-point step repeats Linguist::Classifier#classify and its helpers operation for operation, so
// scores are bit-identical to the Ruby classifier's on the same platform:
// - term weights in first-occurrence order: tf = 1.0 + log(count), then tf * icf (Math.log is C log);
// - the L2 norm sums weight * weight (Float#** 2) in that order, then sqrt (Math.sqrt), and divides each weight;
// - a language's score adds weight * centroid weight over the query terms its centroid contains, in query term
//   order. Walking each term's centroid postings in that order performs the same additions per language while
//   touching only centroid entries the query contains.
// Results are sorted by descending score, keeping language order for equal scores, as Linguist's sort_by does
// with glibc's stable qsort_r.
std::vector<std::pair<uint64_t, double>> classify_content(const ClassifierModel& model, const char* data, size_t length,
    const size_t* language_indices, size_t language_count, uint32_t allowed_types) {
    std::vector<std::pair<uint64_t, double>> results;

    Scratch& state = scratch;
    state.term_positions.resize(model.icf.size(), -1);
    state.terms.clear();
    state.weights.clear();
    {
        Scanner scanner(data, length);
        scanner.each_token([&model, &state](const std::string& token) {
            const auto term = model.vocabulary.find(token);
            if (term == model.vocabulary.end()) return;
            int32_t& position = state.term_positions[term->second];
            if (position < 0) {
                position = static_cast<int32_t>(state.terms.size());
                state.terms.push_back(term->second);
                state.weights.push_back(1.0);
            } else {
                state.weights[static_cast<size_t>(position)] += 1.0;
            }
        });
    }
    for (uint32_t term : state.terms) state.term_positions[term] = -1;
    if (state.terms.empty()) return results;

    double norm = 0.0;
    for (size_t i = 0; i < state.terms.size(); ++i) {
        const double tf = 1.0 + std::log(state.weights[i]);
        const double weight = tf * model.icf[state.terms[i]];
        state.weights[i] = weight;
        norm = norm + weight * weight;
    }
    norm = std::sqrt(norm);

    state.scores.assign(model.centroid_count, 0.0);
    for (size_t i = 0; i < state.terms.size(); ++i) {
        const double weight = state.weights[i] / norm;
        const uint32_t term = state.terms[i];
        for (uint32_t posting = model.posting_starts[term]; posting < model.posting_starts[term + 1]; ++posting) {
            double& score = state.scores[model.posting_centroids[posting]];
            score = score + weight * model.posting_weights[posting];
        }
    }

    const size_t count = language_indices == nullptr ? model.languages.size() : language_count;
    state.seen_languages.assign(model.languages.size(), 0);
    results.reserve(model.centroid_count);
    for (size_t i = 0; i < count; ++i) {
        const size_t index = language_indices == nullptr ? i : language_indices[i];
        const ClassifierModel::Language& language = model.languages[index];
        if ((language.type_mask & allowed_types) == 0 || language.centroid == ClassifierModel::kNoCentroid ||
            state.seen_languages[index] != 0) continue;
        state.seen_languages[index] = 1;
        const double score = state.scores[language.centroid];
        if (score > 0.0) results.emplace_back(language.id, score);
    }
    std::stable_sort(results.begin(), results.end(),
        [](const std::pair<uint64_t, double>& left, const std::pair<uint64_t, double>& right) { return left.second > right.second; });
    return results;
}
