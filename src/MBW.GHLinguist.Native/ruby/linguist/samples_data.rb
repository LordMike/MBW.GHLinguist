# frozen_string_literal: true

# MBW.GHLinguist ships Linguist's samples database as samples.bin, the one copy that both Linguist's Ruby classifier
# (through this file, which Linguist::Samples.load_samples loads) and the .NET classifier read. The layout, all
# little-endian, is written by eng/linguist/generate-samples.rb:
#   "GHLS" u32 version
#   extnames, interpreters, filenames: u32 count, then per entry: str name, u32 n, n strs
#   vocabulary: u32 count, then per term: str term, u32 index
#   icf: u32 count, then count f64
#   centroids: u32 count, then per centroid: str name, u32 n, n u32 term indexes, n f64 values
#   sha256: str
# where str is u32 byte length followed by UTF-8 bytes.
DATA = begin
  bytes = File.binread(File.expand_path("samples.bin", __dir__))
  raise "samples.bin is not a version 1 samples database" unless bytes.byteslice(0, 8) == "GHLS\x01\x00\x00\x00".b

  position = 8
  u32 = lambda do
    value = bytes.unpack1("L<", offset: position)
    position += 4
    value
  end
  str = lambda do
    length = u32.call
    value = bytes.byteslice(position, length).force_encoding(Encoding::UTF_8)
    position += length
    value
  end
  array = lambda do |directive, count, size|
    values = count.zero? ? [] : bytes.unpack("#{directive}#{count}", offset: position)
    position += count * size
    values
  end

  data = {}
  %w[extnames interpreters filenames].each do |key|
    data[key] = Array.new(u32.call) { [str.call, Array.new(u32.call) { str.call }] }.to_h
  end
  data["vocabulary"] = Array.new(u32.call) { [str.call, u32.call] }.to_h
  data["icf"] = array.call("E", u32.call, 8)
  data["centroids"] = Array.new(u32.call) do
    name = str.call
    count = u32.call
    terms = array.call("L<", count, 4)
    [name, terms.zip(array.call("E", count, 8)).to_h]
  end.to_h
  data["sha256"] = str.call
  raise "samples.bin has trailing bytes" unless position == bytes.bytesize

  data
end
