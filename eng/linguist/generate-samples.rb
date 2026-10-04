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

# Usage: generate-samples.rb lib/linguist/samples.bin ghlinguist/languages.bin, with lib/linguist/samples_data.rb
# already being the loader that reads samples.bin. Both layouts are described where they are read: samples.bin in
# src/MBW.GHLinguist.Native/ruby/linguist/samples_data.rb, languages.bin in src/MBW.GHLinguist/LinguistClassifier.cs.
samples_destination = ARGV.fetch(0)
languages_destination = ARGV.fetch(1)
data = Linguist::Samples.data
abort("Linguist's samples data has unexpected keys: #{data.keys.inspect}") unless
  data.keys == %w[extnames interpreters filenames vocabulary icf centroids sha256]

def u32(value) = [value].pack("L<")
def str(value) = u32(value.bytesize) + value.b
def optional_str(value) = value.nil? ? [0xffffffff].pack("L<") : str(value)
def strs(values) = u32(values.length) + values.map { |value| str(value) }.join

samples = +"GHLS".b << u32(1)
%w[extnames interpreters filenames].each do |key|
  samples << u32(data[key].length)
  data[key].each { |name, values| samples << str(name) << strs(values) }
end
samples << u32(data["vocabulary"].length)
data["vocabulary"].each { |term, index| samples << str(term) << u32(index) }
samples << u32(data["icf"].length) << data["icf"].pack("E*")
samples << u32(data["centroids"].length)
data["centroids"].each do |name, centroid|
  samples << str(name) << u32(centroid.length) << centroid.keys.pack("L<*") << centroid.values.pack("E*")
end
samples << str(data["sha256"])
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
abort("samples.bin does not load back as the trained samples data.") unless same?(data, Linguist::Samples.load_samples)

# LinguistClassifier reads the registry instead of starting Ruby: Linguist::Language.all in registry order, with the
# values the native bridge copies out of each language.
require "linguist/language"

types = { nil => 0, data: 1, markup: 2, programming: 3, prose: 4 }
languages = +"GHLL".b << u32(1) << u32(Linguist::Language.all.length)
Linguist::Language.all.each do |language|
  group_id = language.group.language_id
  languages << [language.language_id, group_id == language.language_id ? 0xffffffffffffffff : group_id].pack("Q<Q<")
  languages << [types.fetch(language.type), (language.popular? ? 1 : 0) | (language.wrap ? 2 : 0)].pack("CC")
  languages << str(language.name) << optional_str(language.fs_name) << optional_str(language.color)
  languages << str(language.tm_scope) << optional_str(language.ace_mode) << optional_str(language.codemirror_mode)
  languages << optional_str(language.codemirror_mime_type)
  languages << strs(language.aliases) << strs(language.extensions) << strs(language.interpreters)
  languages << strs(language.filenames)
end
File.binwrite(languages_destination, languages)
