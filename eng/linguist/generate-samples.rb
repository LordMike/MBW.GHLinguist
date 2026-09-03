# frozen_string_literal: true

require "linguist/samples"
require "pp"

destination = ARGV.fetch(0)
File.write(destination, "# frozen_string_literal: true\nDATA = #{PP.pp(Linguist::Samples.data, +"")}")
