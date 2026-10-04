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
data = Linguist::Samples.data
File.binwrite(destination, "# frozen_string_literal: true\nDATA = #{PP.pp(data, +"")}")

# LinguistClassifier and ClassifyDotNet read these instead of starting Ruby: Linguist::Language.all in registry order,
# with the values the native bridge copies out of each language, and the classifier database in JSON. Ruby writes
# floats with their shortest round-trip digits, so .NET parses back the same doubles.
dotnet_destination = ARGV[1]
if dotnet_destination
  require "json"
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
  File.binwrite(File.join(dotnet_destination, "languages.json"), JSON.generate(languages) + "\n")
  File.binwrite(File.join(dotnet_destination, "classifier.json"), JSON.generate(data.slice("vocabulary", "icf", "centroids")) + "\n")
end
