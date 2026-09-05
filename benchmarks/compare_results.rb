#!/usr/bin/env ruby
# frozen_string_literal: true

require "json"
before, after = ARGV.map { |path| JSON.parse(File.read(path)) }
abort "usage: ruby benchmarks/compare_results.rb before.json after.json" unless after
%w[fixture_sha256].each { |key| abort "incomparable #{key}" unless before[key] == after[key] }
before_ops = before.fetch("operations").to_h { |op| [op.fetch("name"), op] }
after.fetch("operations").each do |op|
  prior = before_ops.fetch(op.fetch("name"))
  abort "non-equivalent result for #{op.fetch("name")}" unless prior.fetch("equivalence_digest") == op.fetch("equivalence_digest")
  delta = ((op.fetch("median_ns_per_call") / prior.fetch("median_ns_per_call") - 1) * 100).round(2)
  puts "%s: %+.2f%% (%s -> %s ns/call)" % [op.fetch("name"), delta, prior.fetch("median_ns_per_call"), op.fetch("median_ns_per_call")]
end
