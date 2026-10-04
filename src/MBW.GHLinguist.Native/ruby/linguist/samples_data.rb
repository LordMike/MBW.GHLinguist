# frozen_string_literal: true

# MBW.GHLinguist ships Linguist's samples database as samples.json, the one copy that both Linguist's Ruby classifier
# (through this file, which Linguist::Samples.load_samples loads) and the .NET classifier read. JSON object keys are
# strings, so centroid term indexes become integers again; everything else, key order and float bits included, is
# the Hash Linguist::Samples.data built.
require "json"

DATA = JSON.parse(File.binread(File.expand_path("samples.json", __dir__))).tap do |data|
  data.fetch("centroids").each_value { |centroid| centroid.transform_keys!(&:to_i) }
end
