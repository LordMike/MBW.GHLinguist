# Third-party notices

MBW.GHLinguist includes third-party software in its runtime-specific packages.
The notices below apply to the identified components and do not replace the
license for MBW.GHLinguist itself.

Each native closure contains the complete license texts under
`nativeassets/<rid>/licenses/` and records the exact files, hashes, downloaded
gem artifacts, and resolved platform package identities in
`nativeassets/<rid>/provenance.json`.

## Redistributed components

| Component | Version or identity | License location |
| --- | --- | --- |
| CRuby and standard library | 4.0.6 | `licenses/ruby/COPYING`, `BSDL`, and `LEGAL` |
| Psych and libyaml | Psych 5.3.1, libyaml 0.2.5 | `licenses/ruby/LEGAL`; Linux also includes `licenses/debian/libyaml-0-2/copyright` |
| GitHub Linguist | 9.6.0, revision `196b2a14418cab005065c72c9759370934c184bc` | `licenses/linguist/LICENSE` |
| cgi | 0.4.2 | `licenses/gems/cgi-0.4.2/` |
| mini_mime | 1.1.5 | `licenses/gems/mini_mime-1.1.5/` |
| charlock_holmes | 0.7.9 | `licenses/gems/charlock_holmes-0.7.9/` |
| zlib Ruby gem | 3.2.3 | `licenses/gems/zlib-3.2.3/` |
| resolv | 0.7.2 | `licenses/gems/resolv-0.7.2/` |
| RubyInstaller distribution | Resolved in Windows provenance | `licenses/rubyinstaller/LICENSE` |
| GNU MP (GMP), Windows | `libgmp-10.dll` from the pinned RubyInstaller distribution | `licenses/gmp/` |
| Debian runtime libraries | Exact packages resolved in Linux provenance | `licenses/debian/` |
| MSYS2 ICU, GCC runtime, and winpthreads | Exact packages resolved in Windows provenance | `licenses/msys2/` |

The Linux closure copies these Debian Bookworm libraries: GMP (`libgmp10`,
LGPL-3.0-or-later or GPL-2.0-or-later), libxcrypt (`libcrypt1`,
LGPL-2.1-or-later), OpenSSL 3 (`libssl3`, Apache-2.0), ICU (`libicu72`),
libyaml (`libyaml-0-2`), zlib (`zlib1g`), and the GCC runtime libraries
(`libgcc-s1` and `libstdc++6`, GPL-3.0 with the GCC Runtime Library Exception).
Their Debian copyright files are under `licenses/debian/<package>/`.

## LGPL-licensed libraries

GMP and libxcrypt are licensed under the GNU LGPL. They are separate shared
libraries that CRuby loads dynamically, and you may replace them with
compatible builds. On Linux, the build changes only the ELF `RUNPATH` of every
copied shared library, including these two, so that they resolve dependencies
inside the closure; no code is changed. The Windows `libgmp-10.dll` is
redistributed unmodified.

Corresponding source for the exact versions is available from:

- GMP 6.2.1 (Debian `2:6.2.1+dfsg1-1.1`):
  <https://snapshot.debian.org/package/gmp/2%3A6.2.1%2Bdfsg1-1.1/>
- libxcrypt 4.4.33 (Debian `1:4.4.33-2`):
  <https://snapshot.debian.org/package/libxcrypt/1%3A4.4.33-2/>
- GMP for Windows: the GMP project at <https://gmplib.org/> and the MSYS2
  `mingw-w64-gmp` source packages used by RubyInstaller at
  <https://repo.msys2.org/mingw/sources/>

The build removes inherited RubyGems content before staging the five locked
gems above. Repository traversal support and its Rugged/libgit2 dependencies
are intentionally excluded.

## GitHub Linguist

Project: GitHub Linguist

Version: 9.6.0

Revision: 196b2a14418cab005065c72c9759370934c184bc

License: MIT

Copyright (c) 2017 GitHub, Inc.

Permission is hereby granted, free of charge, to any person obtaining a copy of
this software and associated documentation files (the "Software"), to deal in
the Software without restriction, including without limitation the rights to
use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of
the Software, and to permit persons to whom the Software is furnished to do so,
subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
