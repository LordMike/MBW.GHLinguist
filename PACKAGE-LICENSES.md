# Package licenses

The MBW.GHLinguist source code is licensed under the MIT License in `LICENSE`.

The runtime-specific packages are aggregate distributions that also contain
third-party software. Those components are not relicensed under the
MBW.GHLinguist MIT License. Their copyright notices, license texts, and
component-specific terms remain controlling for those components.

`THIRD-PARTY-NOTICES.md` summarizes the redistributed components, and
`THIRD-PARTY-REDISTRIBUTION.json` records the redistribution policy. Each
runtime package contains the complete collected license texts under
`nativeassets/<rid>/licenses/` and an exact file and dependency inventory in
`nativeassets/<rid>/provenance.json`.

The runtime closure includes software distributed under licenses including the
Ruby License, BSD-2-Clause, MIT, Expat, Unicode-3.0, Zlib,
GPL-3.0-with-GCC-exception-3.1, and package-specific Debian terms. Review the
included component license texts before redistributing a runtime package or an
application containing its native closure.

Source code and reproducible build inputs for MBW.GHLinguist are available from
the public source repository and the release matching the package version:

`https://github.com/LordMike/MBW.GHLinguist`

The native dependency manifest records the exact upstream versions, artifacts,
and hashes used to construct each runtime closure. If source corresponding to a
redistributed component cannot be obtained from the recorded upstream project
or distribution, open an issue in the source repository for assistance.
