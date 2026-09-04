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
| GitHub Linguist | 9.6.0, revision `196b2a14418cab005065c72c9759370934c184bc` | `licenses/linguist/LICENSE` |
| cgi | 0.4.2 | `licenses/gems/cgi-0.4.2/` |
| mini_mime | 1.1.5 | `licenses/gems/mini_mime-1.1.5/` |
| charlock_holmes | 0.7.9 | `licenses/gems/charlock_holmes-0.7.9/` |
| zlib Ruby gem | 3.2.3 | `licenses/gems/zlib-3.2.3/` |
| resolv | 0.7.2 | `licenses/gems/resolv-0.7.2/` |
| RubyInstaller distribution | Resolved in Windows provenance | `licenses/rubyinstaller/LICENSE` |
| Debian runtime libraries | Exact packages resolved in Linux provenance | `licenses/debian/` |
| MSYS2 ICU, GCC runtime, and winpthreads | Exact packages resolved in Windows provenance | `licenses/msys2/` |

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
