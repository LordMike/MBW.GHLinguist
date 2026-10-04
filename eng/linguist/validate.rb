# frozen_string_literal: true

require "json"
manifest_path = ENV.fetch("GHL_DEPENDENCY_MANIFEST")
manifest = JSON.parse(File.read(manifest_path))
psych = manifest.fetch("ruby").fetch("bundledComponents").find { |component| component.fetch("name") == "psych" }
zlib = manifest.fetch("gems").find { |gem| gem.fetch("name") == "zlib" }
resolv = manifest.fetch("gems").find { |gem| gem.fetch("name") == "resolv" }
abort "The runtime closure must pin psych and libyaml" unless psych&.dig("nativeDependency", "name") == "libyaml"
abort "The runtime closure must pin zlib" unless zlib
abort "The runtime closure must pin resolv" unless resolv

gem "zlib", "=#{zlib.fetch("version")}"
gem "resolv", "=#{resolv.fetch("version")}"
require "zlib"
require "resolv"
require "psych"
require "linguist/version"
require "linguist/tokenizer"
require "cgi"
require "mini_mime"
require "charlock_holmes"
require "ghlinguist/bridge"

manifest.fetch("gems").each do |gem|
  specification = Gem::Specification.find_by_name(gem.fetch("name"), "=#{gem.fetch("version")}")
  abort "Expected #{gem.fetch("name")} #{gem.fetch("version")}, found #{specification.version}" unless specification.version.to_s == gem.fetch("version")
end

abort "The staged zlib gem was not activated" unless Gem.loaded_specs.fetch("zlib").version.to_s == zlib.fetch("version")
abort "The staged resolv gem was not activated" unless Gem.loaded_specs.fetch("resolv").version.to_s == resolv.fetch("version")
abort "Expected Psych #{psych.fetch("version")}, found #{Psych::VERSION}" unless Psych::VERSION == psych.fetch("version")
expected_libyaml = psych.fetch("nativeDependency").fetch("version")
actual_libyaml = Psych.libyaml_version.join(".")
abort "Expected libyaml #{expected_libyaml}, found #{actual_libyaml}" unless actual_libyaml == expected_libyaml

expected_version = "9.6.0"
abort "Expected Linguist #{expected_version}, found #{Linguist::VERSION}" unless Linguist::VERSION == expected_version

tokens = Linguist::Tokenizer.tokenize("class Example\n  def value = 42\nend\n")
abort "The Linguist tokenizer returned no tokens" if tokens.empty?

abort "The staged mini_mime gem did not resolve a Ruby filename" unless MiniMime.lookup_by_filename("example.rb")
CharlockHolmes::EncodingDetector.detect("plain text")

analysis = GHLinguist::Bridge.analyze("sample.rb", "sample.rb", "puts :ok\n", 0, 0, 0xff)
abort "The staged GHLinguist bridge did not identify a Ruby source file" if analysis[0].zero?

# The bridge scores Classify calls with its own inverted index; it must reproduce Linguist's classifier exactly,
# including type filtering, candidate order and duplicates, empty candidate lists and inputs without known tokens.
classifier_samples = [
  "class Example\n  def value = 42\nend\n",
  "#include <stdio.h>\nint main(void) { printf(\"hi\\n\"); return 0; }\n",
  "<?xml version=\"1.0\"?>\n<root><item key=\"a\">1</item></root>\n",
  "{\n  \"name\": \"example\",\n  \"values\": [1, 2, 3]\n}\n",
  "# Title\n\nSome *prose* with a [link](https://example.com).\n",
  "zzqqxx",
  ""
]
centroids = Linguist::Samples.cache.fetch("centroids")
classifier_languages = Linguist::Language.all.select { |language| centroids.key?(language.fs_name || language.name) }
type_masks = { data: 1, markup: 2, programming: 4, prose: 8 }
candidate_lists = [
  nil,
  [],
  classifier_languages.first(40).map(&:language_id).reverse * 2,
  Linguist::Language.all.map(&:language_id).shuffle(random: Random.new(1))
]
classifier_samples.each do |sample|
  (0..15).each do |allowed_types|
    candidate_lists.each do |candidate_ids|
      languages = candidate_ids ? candidate_ids.map { |id| Linguist::Language.find_by_id(id) } : Linguist::Language.all
      names = languages.select { |language| (allowed_types & type_masks.fetch(language.type)) != 0 }
        .select { |language| centroids.key?(language.fs_name || language.name) }.map(&:name).uniq
      expected = Linguist::Classifier.classify(Linguist::Samples.cache, sample, names)
        .map { |name, score| [Linguist::Language[name].language_id, score] }
      actual = GHLinguist::Bridge.classify(sample, 0, allowed_types, candidate_ids)[1]
      next if actual == expected

      abort "The bridge classifier diverged from Linguist's classifier (types #{allowed_types}, " \
        "#{candidate_ids.nil? ? "all" : candidate_ids.length} candidates, #{sample.bytesize}-byte input)"
    end
  end
end

puts "Validated Linguist #{Linguist::VERSION} tokenizer (#{tokens.length} tokens)"
