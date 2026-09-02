# Linguist build scripts

These scripts compile GitHub Linguist's native tokenizer and stage it with the
Linguist Ruby sources under `.tmp/artifacts/linguist/<rid>`.

The build intentionally does not install Linguist's gem dependencies yet. It
validates the first native boundary needed by the embedded runtime without
bringing in the deferred Rugged dependency.

## Linux x64

Requires Docker with Linux containers:

```powershell
./eng/linguist/build-docker.ps1
```

## Windows x64

Requires RubyInstaller 4.0.1 with its MSYS2 Devkit. The script locates the
Devkit tools from the Ruby installation when they are not already on `PATH`:

```powershell
./eng/linguist/build-windows.ps1
```
