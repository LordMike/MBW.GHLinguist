# MBW.GHLinguist

MBW.GHLinguist is a .NET 10 library for hosting GitHub Linguist behind a
managed API. The native runtime and public API are under active development.

## Build

```powershell
dotnet restore MBW.GHLinguist.slnx
dotnet build MBW.GHLinguist.slnx --configuration Release --no-restore --nologo
dotnet test --solution MBW.GHLinguist.slnx --configuration Release --no-build --minimum-expected-tests 1
```

## Native assets

RID-specific native builds stage package-ready files beneath
`.tmp/artifacts/native/<rid>`. The library project supports `win-x64` and
`linux-x64`, copies the selected RID assets during RID-specific builds, and
packages both RID trees under `runtimes/<rid>/native`.
