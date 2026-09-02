# frozen_string_literal: true

require "linguist/version"
require "linguist/tokenizer"

expected_version = "9.6.0"
abort "Expected Linguist #{expected_version}, found #{Linguist::VERSION}" unless Linguist::VERSION == expected_version

tokens = Linguist::Tokenizer.tokenize("class Example\n  def value = 42\nend\n")
abort "The Linguist tokenizer returned no tokens" if tokens.empty?

puts "Validated Linguist #{Linguist::VERSION} tokenizer (#{tokens.length} tokens)"
