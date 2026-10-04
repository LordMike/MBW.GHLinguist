# frozen_string_literal: true

require "ghlinguist/bridge"

ruby = Linguist::Language["Ruby"]
source = "def hello\n  puts :ok\nend\n"
analysis = GHLinguist::Bridge.analyze("src/sample.rb", "sample.rb", source, 0, 6, 0xff)
raise "extension analysis failed: #{analysis.inspect}" unless analysis[0] == ruby.language_id && analysis[1] == 4

puts "analysis=#{ruby.name} strategy=#{analysis[1]}"

blob = GHLinguist::InteropBlob.new("src/sample.rb", "sample.rb", source, false, false)
raise "negative generated result was not cached" if blob.generated? || !blob.instance_variable_defined?(:@_ghlinguist_generated)
