# Classifier parity bench

Compares Linguist's Ruby classifier (`LinguistRuntime.Classify`) with the managed port
(`LinguistRuntime.ClassifyDotNet`) on the same inputs and options. Every case must produce the same full ranked
list: the same language IDs in the same order, bit-identical scores, and the same `ConsideredBytes`. It then times
both paths single-threaded and in parallel. The exit code is 1 when any case differs.

Inputs:

- `snippets/`: short hand-written samples for common languages, plus generated edge cases (empty, NUL, invalid
  UTF-8, unterminated comments and strings, shebangs, a long line, a repeated token). Each runs with default,
  production (Programming|Data|Markup, 50 KiB), 64-byte and Programming-only options.
- Linguist's own `samples/` from the `extern/linguist` submodule, which is its classifier training set. Each file
  runs as its first 256 B, 2 KiB and 16 KiB and in full. Files whose extension maps to several languages also run
  restricted to those candidates, as in Linguist's `test_classify_ambiguous_languages`.
- Optionally, an external corpus of concatenated prefixes plus a JSONL manifest of `id`, `offset` and `length`
  (`--corpus` and `--manifest`). Keep such corpora out of the repository.

Build the native closure from source first (`eng/linguist/build-docker.ps1`, which stages
`.tmp/artifacts/native/<rid>`), then run:

```sh
git submodule update --init extern/linguist
dotnet run -c Release --project benchmarks/ClassifierParity -- --rounds 3
```

Options: `--trims 256,2048,16384,full`, `--stride <n>` (every nth sample, for a quick run), `--threads <n>`,
`--no-samples`, `--samples <dir>`, `--snippets <dir>` and `--output <file.json>` for a machine-readable report.
