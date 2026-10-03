# Security policy

## Reporting a vulnerability

Report suspected vulnerabilities privately through GitHub's
[security advisory form](https://github.com/LordMike/MBW.GHLinguist/security/advisories/new)
for this repository. Do not open a public issue for a security problem.
Include the package versions involved, the runtime identifier, and a
reproduction when you have one.

## Supported versions

Only the latest published version of `MBW.GHLinguist` and its runtime
packages receives fixes. Fixes ship as a new package version; packages that
are already published are never modified.

## What the packages contain

The runtime packages redistribute a complete native closure: CRuby, its
standard library, a pinned set of Ruby gems, GitHub Linguist, ICU, and the
other shared libraries the bridge needs. Every component and its version is
listed in `eng/linguist/native-dependencies.json`, and each published closure
ships a `provenance.json` with the SHA-256 of every file. See
`THIRD-PARTY-NOTICES.md` and `PACKAGE-LICENSES.md` for the license inventory.

Because the closure is embedded, an advisory against CRuby, a bundled gem, or
ICU applies to this package even though consumers never install those
components themselves.

## How advisories against native components are handled

`eng/security/security-baseline.json` records the minimum patched version for
each advisory that has affected a bundled component. CI runs
`eng/security/verify-security-baseline.ps1`, which fails the build when
`native-dependencies.json` pins a version below any recorded minimum, and
performs a NuGet vulnerability audit of the managed packages.

Bumping a component after an advisory means:

1. Raise the pinned version and artifact hash in
   `eng/linguist/native-dependencies.json`.
2. Add the advisory and its minimum version to
   `eng/security/security-baseline.json`.
3. Rebuild the native closures through CI so a new `provenance.json` is
   generated, then publish new package versions.

## What the integrity check is and is not

`LinguistRuntime.Create()` validates the deployed closure against its
`provenance.json` before loading it. This catches incomplete or corrupted
deployments. It is not a signature: the manifest ships beside the files it
describes, so it does not defend against an attacker who can already write to
the application directory. Protect the application directory with the same
controls as the rest of the deployment.
