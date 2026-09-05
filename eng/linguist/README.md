# Linguist build scripts

`native-dependencies.json` is the checked-in, non-secret source of truth for
the CRuby 4.0.6 source artifacts, Linguist 9.6.0, gem, OS-package, ICU, build
recipe, and exclusion pins. It records the empty patch set explicitly and
excludes Rugged and libgit2 because this package does not expose repository
traversal.

The scripts create a complete RID-local closure beneath
`.tmp/artifacts/native/<rid>`. Each closure contains the `ghlinguist` bridge,
CRuby runtime and standard library, pinned gems, ICU libraries, Linguist Ruby
sources and data, the tokenizer extension, and a `provenance.json` containing
SHA-256 hashes for every staged file, the locked build configuration, and the
resolved external package identities. No native binaries are checked in.

`third-party-redistribution.json` is the checked-in redistribution inventory.
It identifies every component included in the current closures, its license
description, platform-specific license source locations, and exact required
`licenses/` outputs per RID. The inventory ships in the NuGet package, and
package verification rejects missing declared texts or source divergence. Both
build scripts fail when a required license text cannot be staged. Linux derives
a Debian package copyright file for every copied system ELF library; Windows
stages the RubyInstaller and MSYS2 license texts for Ruby, ICU, GCC/libstdc++,
and winpthreads.

## Linux x64

Requires Docker with Linux containers:

```powershell
./eng/linguist/build-docker.ps1
```

The image is pinned by digest in the manifest and installs only the compiler,
CMake, and ICU development files needed to build this closure.

The resulting package is exercised on Debian Bookworm. The closure contains CRuby,
ICU, and its other non-system native dependencies, but deliberately relies on the
target system's ELF loader and glibc family. It is not compatible with musl-based
distributions such as Alpine. The earlier glibc 2.35 observation covered only the
bridge and libruby, not every staged ELF dependency; a full closure audit is
required before declaring a minimum glibc version. Re-audit after changing the
base image, compiler, or native dependency set.

## Windows x64

Requires an existing RubyInstaller 4.0.6 x64-ucrt root with the MSYS2 Devkit,
the pinned gems, and ICU DLLs. By default the script uses `ruby` on `PATH` and
`extern/linguist`; pass explicit roots when staging a different installed asset
root:

```powershell
./eng/linguist/build-windows.ps1
./eng/linguist/build-windows.ps1 -RubyRoot C:\Ruby40-x64 -LinguistRoot C:\src\linguist
```

Both scripts stop before producing an asset when a pinned source, gem, ICU
library, tokenizer, or bridge binary is absent or has the wrong version.
