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

puts "Validated Linguist #{Linguist::VERSION} tokenizer (#{tokens.length} tokens)"
