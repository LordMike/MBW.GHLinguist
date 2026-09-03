# frozen_string_literal: true

require "ghlinguist/bridge"

ruby = Linguist::Language["Ruby"]
source = "def hello\n  puts :ok\nend\n"
analysis = GHLinguist::Bridge.analyze("src/sample.rb", "sample.rb", source, 0, 6, 0xff)
raise "extension analysis failed: #{analysis.inspect}" unless analysis[0] == ruby.language_id && analysis[1] == 4

classification = GHLinguist::Bridge.classify(source, 50 * 1024, 0x0f, nil)
raise "classification returned no results" if classification[1].empty?

classified_language = Linguist::Language.find_by_id(classification[1][0][0])
puts "analysis=#{ruby.name} strategy=#{analysis[1]} classifier=#{classified_language.name} score=#{classification[1][0][1]}"
