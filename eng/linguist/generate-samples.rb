# frozen_string_literal: true

require "json"
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

# Usage: generate-samples.rb lib/linguist/samples.json ghlinguist/languages.json, with lib/linguist/samples_data.rb
# already being the loader that reads samples.json.
samples_destination = ARGV.fetch(0)
languages_destination = ARGV.fetch(1)
data = Linguist::Samples.data
File.binwrite(samples_destination, JSON.generate(data) + "\n")

# Ruby writes floats with their shortest round-trip digits, so both readers get back the same doubles. Prove it for
# this Ruby: the Hash Linguist will load must equal the one it trained, in key order and to the bit.
def same?(left, right)
  return false unless left.instance_of?(right.class)

  case left
  when Hash then left.keys == right.keys && left.all? { |key, value| same?(value, right[key]) }
  when Array then left.length == right.length && left.zip(right).all? { |a, b| same?(a, b) }
  when Float then [left].pack("E") == [right].pack("E")
  else left == right
  end
end
abort("samples.json does not load back as the trained samples data.") unless same?(data, Linguist::Samples.load_samples)

# LinguistClassifier reads the registry instead of starting Ruby: Linguist::Language.all in registry order, with the
# values the native bridge copies out of each language.
require "linguist/language"

languages = Linguist::Language.all.map do |language|
  group_id = language.group.language_id
  {
    "id" => language.language_id,
    "groupId" => group_id == language.language_id ? nil : group_id,
    "name" => language.name,
    "fsName" => language.fs_name,
    "type" => language.type&.to_s,
    "popular" => language.popular? ? true : false,
    "wrap" => language.wrap ? true : false,
    "color" => language.color,
    "tmScope" => language.tm_scope,
    "aceMode" => language.ace_mode,
    "codemirrorMode" => language.codemirror_mode,
    "codemirrorMimeType" => language.codemirror_mime_type,
    "aliases" => language.aliases,
    "extensions" => language.extensions,
    "interpreters" => language.interpreters,
    "filenames" => language.filenames
  }
end
File.binwrite(languages_destination, JSON.generate(languages) + "\n")
