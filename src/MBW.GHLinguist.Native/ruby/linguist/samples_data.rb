# frozen_string_literal: true

# MBW.GHLinguist ships Linguist's samples database as samples.tsv, the one copy that both Linguist's Ruby classifier
# (through this file, which Linguist::Samples.load_samples loads) and the .NET classifier read. Each line is
# tab-separated, starts with its kind, and eng/linguist/generate-samples.rb writes them in this order:
#   extnames|interpreters|filenames  name  value...
#   vocabulary  term  index
#   icf  value...                         (one line, in vocabulary index order)
#   centroid  name  index  value  index  value...
#   sha256  digest
# Floats are Ruby's shortest round-trip digits, so they parse back to the same doubles.
DATA = begin
  data = { "extnames" => {}, "interpreters" => {}, "filenames" => {}, "vocabulary" => {}, "icf" => [],
           "centroids" => {}, "sha256" => nil }
  File.foreach(File.expand_path("samples.tsv", __dir__), chomp: true, encoding: Encoding::UTF_8) do |line|
    kind, *fields = line.split("\t", -1)
    case kind
    when "extnames", "interpreters", "filenames" then data[kind][fields.shift] = fields
    when "vocabulary" then data["vocabulary"][fields.fetch(0)] = Integer(fields.fetch(1), 10)
    when "icf" then data["icf"] = fields.map { |value| Float(value) }
    when "centroid" then data["centroids"][fields.shift] = fields.each_slice(2).to_h { |index, value| [Integer(index, 10), Float(value)] }
    when "sha256" then data["sha256"] = fields.fetch(0)
    else raise "samples.tsv has an unknown line kind #{kind.inspect}"
    end
  end
  data
end
