#!/usr/bin/env ruby
# frozen_string_literal: true

require "digest"
require "fileutils"
require "json"
require "open3"
require "optparse"
require "rbconfig"
require "time"

options = { warmup: 3, rounds: 9, iterations: 100, output: nil }
OptionParser.new do |parser|
  parser.banner = "Usage: ruby benchmarks/linguist_benchmark.rb [options]"
  parser.on("--runtime-root PATH", "Packaged runtime root containing ghlinguist/bridge.rb") { |v| options[:runtime_root] = v }
  parser.on("--linguist-root PATH", "Linguist checkout or lib directory") { |v| options[:linguist_root] = v }
  parser.on("--fixture-root PATH", "Locally extracted corpus fixtures (opt-in)") { |v| options[:fixture_root] = v }
  parser.on("--candidate-ids IDS", "Comma-separated cached language IDs") { |v| options[:candidate_ids] = v.split(",").map(&:to_i) }
  parser.on("--warmup N", Integer) { |v| options[:warmup] = v }
  parser.on("--rounds N", Integer) { |v| options[:rounds] = v }
  parser.on("--iterations N", Integer) { |v| options[:iterations] = v }
  parser.on("--output PATH", "JSON output path") { |v| options[:output] = v }
end.parse!

abort "--rounds and --iterations must be positive" unless options[:rounds].positive? && options[:iterations].positive?
root = options[:runtime_root] && File.expand_path(options[:runtime_root])
bridge = root ? File.join(root, "ghlinguist", "bridge.rb") : File.expand_path("../src/MBW.GHLinguist.Native/ruby/ghlinguist/bridge.rb", __dir__)
abort "bridge not found: #{bridge}" unless File.file?(bridge)
$LOAD_PATH.unshift(File.join(root, "lib")) if root && File.directory?(File.join(root, "lib"))
if options[:linguist_root]
  linguist = File.expand_path(options[:linguist_root])
  $LOAD_PATH.unshift(File.directory?(File.join(linguist, "lib")) ? File.join(linguist, "lib") : linguist)
end
require bridge

def hash_file(path)
  File.file?(path) ? Digest::SHA256.file(path).hexdigest : nil
end

def fixtures(root)
  result = {
    "tiny.rb" => "puts 'hello'\n",
    "normal.cs" => ("using System;\npublic class Example { static void Main() { Console.WriteLine(\"hello\"); } }\n" * 8),
    "generated.js" => ("/*! generated */(function(){var x=1;function f(a){return a+x}}window.f=f})();\n" * 200),
    "generated.css" => ("/* generated */.a{color:#123;margin:0;padding:0}.b{display:flex}\n" * 200),
    "large.py" => ("def example(value):\n    return value + 1\n\n" * 4_000)
  }
  return result unless root

  Dir.glob(File.join(root, "**", "*")).sort.each do |path|
    next unless File.file?(path) && File.size(path) <= 512_000
    data = File.binread(path)
    next if data.include?("\0")
    relative = path.delete_prefix(root).tr("\\", "/").sub(%r{^/}, "")
    result["corpus/#{relative}"] = data
  end
  result
end

def measure(name, warmup, rounds, iterations)
  warmup.times { iterations.times { yield } }
  samples = rounds.times.map do
    GC.start
    before_allocations = GC.stat(:total_allocated_objects)
    before_gc = GC.stat(:count)
    started = Process.clock_gettime(Process::CLOCK_MONOTONIC)
    digest = iterations.times.map { yield }.hash
    elapsed = Process.clock_gettime(Process::CLOCK_MONOTONIC) - started
    { "ns_per_call" => (elapsed * 1_000_000_000 / iterations).round(1), "allocated_objects_per_call" => ((GC.stat(:total_allocated_objects) - before_allocations).to_f / iterations).round(2), "gc_runs" => GC.stat(:count) - before_gc, "digest" => digest }
  end
  ordered = samples.map { |s| s["ns_per_call"] }.sort
  { "name" => name, "median_ns_per_call" => ordered[ordered.length / 2], "min_ns_per_call" => ordered.first, "max_ns_per_call" => ordered.last, "spread_percent" => ((ordered.last - ordered.first) * 100 / ordered[ordered.length / 2]).round(2), "median_allocated_objects_per_call" => samples.map { |s| s["allocated_objects_per_call"] }.sort[samples.length / 2], "gc_runs" => samples.sum { |s| s["gc_runs"] }, "equivalence_digest" => Digest::SHA256.hexdigest(samples.map { |s| s["digest"] }.join(",")) }
end

all_types = GHLinguist::Bridge::TYPE_MASKS.values.sum
data = fixtures(options[:fixture_root])
normal = data.fetch("normal.cs")
candidate_ids = options[:candidate_ids] || [GHLinguist::Bridge.analyze("a.cs", "a.cs", normal, 0, 0, 0xff).first]
operations = [
  ["classify.unrestricted", -> { GHLinguist::Bridge.classify(normal, 0, all_types, nil) }],
  ["classify.filtered", -> { GHLinguist::Bridge.classify(normal, 16 * 1024, all_types, candidate_ids) }],
  ["analyze.normal", -> { GHLinguist::Bridge.analyze("src/Example.cs", "Example.cs", normal, 0, 0, 0xff) }],
  ["analyze.generated_js", -> { GHLinguist::Bridge.analyze("dist/app.min.js", "app.min.js", data.fetch("generated.js"), 0, 0, 0xff) }],
  ["analyze.generated_css", -> { GHLinguist::Bridge.analyze("dist/site.min.css", "site.min.css", data.fetch("generated.css"), 0, 0, 0xff) }],
  ["analyze.line_counts", -> { GHLinguist::Bridge.analyze("src/Example.cs", "Example.cs", normal, 0, 4, 0xff) }],
  ["analyze.trace", -> { GHLinguist::Bridge.analyze("src/Example.cs", "Example.cs", normal, 0, 2, 0xff) }],
  ["analyze.tiny", -> { GHLinguist::Bridge.analyze("tiny.rb", "tiny.rb", data.fetch("tiny.rb"), 0, 0, 0xff) }],
  ["analyze.large", -> { GHLinguist::Bridge.analyze("large.py", "large.py", data.fetch("large.py"), 0, 0, 0xff) }],
  ["registry.lookup", -> { Linguist::Language.find_by_name("C#") }]
]
operations.concat(data.filter { |name, _| name.start_with?("corpus/") }.map { |name, bytes| ["analyze.#{name}", -> { GHLinguist::Bridge.analyze(name, File.basename(name), bytes, 0, 0, 0xff) }] })

startup_load_path = root && File.join(root, "lib")
startup_code = "$LOAD_PATH.unshift(#{startup_load_path.inspect}) if #{(!startup_load_path.nil?).inspect}; require #{bridge.inspect}; GHLinguist::Bridge.classify('class X {}'.b, 0, 15, nil)"
startup = Process.clock_gettime(Process::CLOCK_MONOTONIC)
_, status = Open3.capture2e(RbConfig.ruby, "-e", startup_code)
abort "startup child failed" unless status.success?
result = {
  "schema" => 1,
  "timestamp_utc" => Time.now.utc.iso8601,
  "environment" => { "ruby" => RUBY_DESCRIPTION, "ruby_platform" => RUBY_PLATFORM, "host_os" => RbConfig::CONFIG["host_os"], "bridge_sha256" => hash_file(bridge), "classifier_sha256" => hash_file(Dir.glob(File.join(root || options[:linguist_root].to_s, "**", "classifier.rb")).first) },
  "fixture_sha256" => Digest::SHA256.hexdigest(data.sort.map { |name, bytes| "#{name}\0#{Digest::SHA256.hexdigest(bytes)}" }.join("\n")),
  "startup_ms" => ((Process.clock_gettime(Process::CLOCK_MONOTONIC) - startup) * 1000).round(1),
  "rounds" => options[:rounds], "warmup" => options[:warmup], "iterations" => options[:iterations],
  "operations" => operations.map { |name, operation| measure(name, options[:warmup], options[:rounds], options[:iterations], &operation) }
}
json = JSON.pretty_generate(result) + "\n"
if options[:output]
  FileUtils.mkdir_p(File.dirname(File.expand_path(options[:output])))
  File.write(options[:output], json)
else
  puts(json)
end
