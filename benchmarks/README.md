# Linguist Benchmarks

`linguist_benchmark.rb` measures the public Ruby bridge used by the native runtime. It is intentionally a
repeatable microbenchmark rather than a replacement for application profiling. Fixtures are synthesized and
checked in; no source corpus content, paths, or package contents are recorded.

Current results are preliminary synthetic Ruby-only measurements. They do not
measure managed end-to-end performance or establish a trustworthy timing speedup.
For the sampled generated checks, allocation reductions are about 23%, 30%, and
33%; direct `Classify` allocation is unaffected. Treat these as investigation
data, not a portable performance claim.

Run against a packaged Windows closure (use a private copy if another build may replace its files):

```powershell
& <runtime-root>\bin\ruby.exe benchmarks\linguist_benchmark.rb --runtime-root <runtime-root> --rounds 9 --output benchmarks\results\baseline.json
```

To benchmark an un-staged bridge change against the closure's Ruby and Linguist
assets, add `--bridge src\MBW.GHLinguist.Native\ruby\ghlinguist\bridge.rb`.

Run against a Linguist checkout instead:

```powershell
ruby benchmarks\linguist_benchmark.rb --linguist-root <linguist-root> --rounds 9 --output benchmarks\results\local.json
```

The result records Ruby/platform metadata, SHA-256 hashes of the bridge and classifier, medians, min/max
spread, allocation counts, GC activity, and a deterministic equivalence digest. `--candidate-ids` accepts a
comma-separated language-ID cache to test candidate-filtered classifier calls without changing registry order.
`--warmup`, `--rounds`, and `--iterations` control sampling. Startup is measured by a child process before the
in-process tests. The normal suite covers unrestricted and filtered classification, normal/generated JS/generated
CSS analysis, line-count and trace toggles, tiny and large blobs, and registry lookup.

External corpus discovery is opt-in. It inspects extensionless ZIP/NuGet-like files without modifying the source:

```powershell
pwsh -File benchmarks\discover-corpus.ps1 -CorpusRoot D:\OriginaryTemp\Cache -OutputRoot .tmp\benchmark-corpus
```

It bounds inspected files, archives, entries, and uncompressed bytes; rejects traversal names and binary content; and selects a
stable, representative extension distribution by hash. The extracted fixtures and its manifest are local-only.
Pass `--fixture-root .tmp\benchmark-corpus\fixtures` to include them in the Ruby suite. Do not commit the output.

Compare two result files with `ruby benchmarks\compare_results.rb before.json after.json`. It checks fixture and
result digests; it does not compare bridge hashes or operation order.

For managed end-to-end validation, rebuild and stage the complete closure, then
copy it to an isolated asset layout before running the native-gated suite:

```powershell
$env:GHL_RUN_NATIVE_INTEGRATION = "true"
dotnet test --project tests/MBW.GHLinguist.Tests/MBW.GHLinguist.Tests.csproj --configuration Release --runtime win-x64 -p:RunNativeIntegrationTests=true --minimum-expected-tests 1
```

On Linux, set `GHL_RUN_NATIVE_INTEGRATION=true` and use `--runtime linux-x64`.
A raw checkout also needs generated samples and gem setup, so use the complete
native-build closure instead of assuming a standalone command is sufficient.

Keep the machine otherwise quiet during baseline rounds. Results are measurements for the exact recorded machine,
runtime, classifier, and fixture hashes, not portable performance claims.
