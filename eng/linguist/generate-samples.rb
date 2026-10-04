# frozen_string_literal: true

require "linguist/samples"

module DeterministicSampleInputs
  SAMPLE_ROOT = File.expand_path(Linguist::Samples::ROOT) + File::SEPARATOR

  def read(path, *arguments, **options)
    content = super
    expanded_path = File.expand_path(path.to_s)
    return content unless expanded_path.start_with?(SAMPLE_ROOT)

    content.gsub("\r\n", "\n").gsub("\r", "\n")
  end
end

module DeterministicSampleOrder
  SAMPLE_ROOT = File.expand_path(Linguist::Samples::ROOT)

  def entries(path, *arguments, **options)
    entries = super
    expanded_path = File.expand_path(path.to_s)
    expanded_path == SAMPLE_ROOT || expanded_path.start_with?(SAMPLE_ROOT + File::SEPARATOR) ? entries.sort : entries
  end
end

File.singleton_class.prepend(DeterministicSampleInputs)
Dir.singleton_class.prepend(DeterministicSampleOrder)

# Usage: generate-samples.rb lib/linguist/samples.tsv ghlinguist/languages.tsv, with lib/linguist/samples_data.rb
# already being the loader that reads samples.tsv. Both layouts are described where they are read: samples.tsv in
# src/MBW.GHLinguist.Native/ruby/linguist/samples_data.rb, languages.tsv in src/MBW.GHLinguist/LinguistClassifier.cs.
samples_destination = ARGV.fetch(0)
languages_destination = ARGV.fetch(1)
data = Linguist::Samples.data
abort("Linguist's samples data has unexpected keys: #{data.keys.inspect}") unless
  data.keys == %w[extnames interpreters filenames vocabulary icf centroids sha256]

# Tab-separated fields; an empty field means nil, so real values must be non-empty and free of tabs and newlines.
def line(*fields)
  fields.map do |field|
    next "" if field.nil?

    text = field.to_s
    abort("Cannot write #{field.inspect} as a tab-separated field.") if text.empty? || text.match?(/[\t\r\n]/)
    text
  end.join("\t") + "\n"
end

samples = +""
%w[extnames interpreters filenames].each do |key|
  data[key].each { |name, values| samples << line(key, name, *values) }
end
data["vocabulary"].each { |term, index| samples << line("vocabulary", term, index) }
samples << line("icf", *data["icf"])
data["centroids"].each { |name, centroid| samples << line("centroid", name, *centroid.flatten) }
samples << line("sha256", data["sha256"])
File.binwrite(samples_destination, samples)

# The Hash Linguist will load must equal the one it trained, in key order and to the bit.
def same?(left, right)
  return false unless left.instance_of?(right.class)

  case left
  when Hash then left.keys == right.keys && left.all? { |key, value| same?(value, right[key]) }
  when Array then left.length == right.length && left.zip(right).all? { |a, b| same?(a, b) }
  when Float then [left].pack("E") == [right].pack("E")
  else left == right
  end
end
abort("samples.tsv does not load back as the trained samples data.") unless same?(data, Linguist::Samples.load_samples)

# LinguistClassifier reads the registry instead of starting Ruby: Linguist::Language.all in registry order, with the
# values the native bridge copies out of each language.
require "linguist/language"

languages = +""
Linguist::Language.all.each do |language|
  group_id = language.group.language_id
  languages << line(
    "language", language.language_id, group_id == language.language_id ? nil : group_id, language.type,
    language.popular? ? 1 : 0, language.wrap ? 1 : 0, language.name, language.fs_name, language.color,
    language.tm_scope, language.ace_mode, language.codemirror_mode, language.codemirror_mime_type
  )
  languages << line("aliases", *language.aliases) << line("extensions", *language.extensions)
  languages << line("interpreters", *language.interpreters) << line("filenames", *language.filenames)
end
File.binwrite(languages_destination, languages)
