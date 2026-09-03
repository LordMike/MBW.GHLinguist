# frozen_string_literal: true

require "json"
require "linguist/version"
require "linguist/tokenizer"
require "cgi"
require "mini_mime"
require "charlock_holmes"
require "ghlinguist/bridge"

manifest_path = ENV.fetch("GHL_DEPENDENCY_MANIFEST")
manifest = JSON.parse(File.read(manifest_path))
manifest.fetch("gems").each do |gem|
  specification = Gem::Specification.find_by_name(gem.fetch("name"), "=#{gem.fetch("version")}")
  abort "Expected #{gem.fetch("name")} #{gem.fetch("version")}, found #{specification.version}" unless specification.version.to_s == gem.fetch("version")
end

expected_version = "9.6.0"
abort "Expected Linguist #{expected_version}, found #{Linguist::VERSION}" unless Linguist::VERSION == expected_version

tokens = Linguist::Tokenizer.tokenize("class Example\n  def value = 42\nend\n")
abort "The Linguist tokenizer returned no tokens" if tokens.empty?

abort "The staged mini_mime gem did not resolve a Ruby filename" unless MiniMime.lookup_by_filename("example.rb")
CharlockHolmes::EncodingDetector.detect("plain text")

analysis = GHLinguist::Bridge.analyze("sample.rb", "sample.rb", "puts :ok\n", 0, 0, 0xff)
abort "The staged GHLinguist bridge did not identify a Ruby source file" if analysis[0].zero?

puts "Validated Linguist #{Linguist::VERSION} tokenizer (#{tokens.length} tokens)"
