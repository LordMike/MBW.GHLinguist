# frozen_string_literal: true

require "linguist/version"
require "linguist/language"
require "linguist/strategy/manpage"
require "linguist/strategy/xml"

module GHLinguist
  class InteropBlob
    include Linguist::BlobHelper

    attr_reader :path, :name, :data

    def initialize(path, name, data, symlink, lfs_tracked)
      @path = path || ""
      @name = name.nil? ? File.basename(@path) : name
      @data = data.b
      @symlink = symlink
      @lfs_tracked = lfs_tracked
    end

    def size = @data.bytesize
    def symlink? = @symlink
    def lfs_tracked? = @lfs_tracked

    # BlobHelper's ||= cache retries negative generated results. Preserve them too,
    # because analysis asks for generated status both as a result flag and for stats.
    def generated?
      return @_ghlinguist_generated if defined?(@_ghlinguist_generated)

      @_ghlinguist_generated = super
    end
  end

  module Bridge
    NO_LANGUAGE_ID = (1 << 64) - 1

    STRATEGIES = [
      [1, 1 << 0, Linguist::Strategy::Modeline],
      [2, 1 << 1, Linguist::Strategy::Filename],
      [3, 1 << 2, Linguist::Shebang],
      [4, 1 << 3, Linguist::Strategy::Extension],
      [5, 1 << 4, Linguist::Strategy::XML],
      [6, 1 << 5, Linguist::Strategy::Manpage],
      [7, 1 << 6, Linguist::Heuristics],
      [8, 1 << 7, Linguist::Classifier]
    ].freeze

    TYPE_MASKS = {
      data: 1 << 0,
      markup: 1 << 1,
      programming: 1 << 2,
      prose: 1 << 3
    }.freeze

    FLAG_METHODS = [
      [1 << 0, :likely_binary?],
      [1 << 1, :binary?],
      [1 << 2, :text?],
      [1 << 3, :image?],
      [1 << 4, :solid?],
      [1 << 5, :csv?],
      [1 << 6, :pdf?],
      [1 << 7, :large?],
      [1 << 8, :viewable?],
      [1 << 9, :safe_to_colorize?],
      [1 << 10, :high_ratio_of_long_lines?],
      [1 << 11, :lfs_pointer?],
      [1 << 12, :vendored?],
      [1 << 13, :documentation?],
      [1 << 14, :generated?]
    ].freeze

    module_function

    def analyze(path, name, data, input_flags, option_flags, strategy_mask)
      blob = InteropBlob.new(path, name, data, (input_flags & 1) != 0, (input_flags & 2) != 0)
      allow_empty = (option_flags & 1) != 0
      include_trace = (option_flags & 2) != 0
      include_lines = (option_flags & 4) != 0
      trace = []
      language = nil
      selecting_strategy = 0

      unless blob.likely_binary? || blob.binary? || (!allow_empty && blob.empty?)
        candidates = []
        STRATEGIES.each do |strategy_id, strategy_bit, strategy|
          next if (strategy_mask & strategy_bit).zero?

          selecting_strategy = strategy_id
          considered = strategy.call(blob, candidates)
          trace << [strategy_id, considered.map(&:language_id)] if include_trace
          candidates = considered if considered.any?
          break if considered.length == 1
        end
        language = candidates.first
        selecting_strategy = 0 unless language
      end

      flags = FLAG_METHODS.sum { |flag, method| blob.public_send(method) ? flag : 0 }
      detectable = !blob.likely_binary? && !blob.binary? && (allow_empty || !blob.empty?)
      flags |= 1 << 15 if detectable
      included = !blob.vendored? && !blob.documentation? && !blob.generated? &&
        !(blob.lfs_tracked? && blob.lfs_pointer?) && language && [:programming, :markup].include?(language.type)
      flags |= 1 << 16 if included

      [
        language&.language_id || NO_LANGUAGE_ID,
        selecting_strategy,
        flags,
        blob.mime_type,
        blob.content_type,
        blob.disposition,
        blob.encoding,
        blob.ruby_encoding,
        language&.tm_scope,
        include_lines ? blob.loc : 0,
        include_lines ? blob.sloc : 0,
        trace
      ]
    end

    def classify(data, maximum_bytes, allowed_types, candidate_ids)
      considered = [data.bytesize, maximum_bytes.zero? ? 50 * 1024 : maximum_bytes].min
      prefix = data.byteslice(0, considered)
      languages = candidate_ids&.map { |id| Linguist::Language.find_by_id(id) }
      raise ArgumentError, "candidate language ID does not exist" if languages&.any?(&:nil?)

      languages ||= Linguist::Language.all
      languages = languages.select { |language| (allowed_types & TYPE_MASKS.fetch(language.type)) != 0 }
      centroids = Linguist::Samples.cache.fetch("centroids")
      names = languages.filter_map do |language|
        key = language.fs_name || language.name
        language.name if centroids.key?(key)
      end
      results = Linguist::Classifier.classify(Linguist::Samples.cache, prefix, names)
      [considered, results.map { |name, score| [Linguist::Language[name].language_id, score] }]
    end
  end
end
