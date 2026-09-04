# frozen_string_literal: true

require "linguist/samples"
require "pp"

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

destination = ARGV.fetch(0)
File.binwrite(destination, "# frozen_string_literal: true\nDATA = #{PP.pp(Linguist::Samples.data, +"")}")
