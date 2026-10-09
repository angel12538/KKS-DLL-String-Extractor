# KKS DLL String Extractor

[简体中文说明](README.zh-CN.md) · [Output format](docs/OUTPUT_FORMAT.md) · [Contributing](CONTRIBUTING.md)

A static, **multilingual** string extractor for **Koikatsu Sunshine (KKS)** BepInEx plugin DLLs, built with .NET 8 and Mono.Cecil. It does not run or modify scanned DLLs. Its purpose is to help translators locate text, not to inject translations into the game.

> Unofficial community tool. Not affiliated with Illusion, BepInEx or XUnity.AutoTranslator.

## Features

- Recursive DLL scan; IL `ldstr` and selected embedded text/resources.
- Full original-string output, plus heuristic translatable/UI subsets.
- Multi-script: Latin, Kana, Han, Hangul, Cyrillic, Arabic, etc. No Japanese-only filter.
- Deduplication: globally for combined TXT, and separately for each DLL.
- Plain `.txt` with `// DLL / Class` headings and `Original=` lines.
- Exact archive for multiline, escaped and other ambiguous originals.
- Stable special-string IDs across every export; strict UTF-8 / BOM-marked UTF-16 decoding.
- Text and `.resources` payloads limited to 32,000,000 bytes before reading; unrelated binary resources skipped.
- Directory errors are logged while accessible siblings continue; directory links are skipped.

## Quick start (Windows)

**Requirements:** Windows 10/11 and the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0). Internet is required on first build to restore `Mono.Cecil` through NuGet.

1. Clone or download this repository.
2. Double-click `scripts/Run_SelfTest.bat` to run self-tests.
3. Double-click `scripts/Run_Scan.bat` and enter your KKS `BepInEx\plugins` folder.
4. Find files under `Results_TXT/scan_<timestamp>/`.

Or run from a terminal:

```powershell
dotnet run --project src/KksDllStringExtractor -- "D:\Games\KoikatsuSunshine\BepInEx\plugins" "D:\KKS-Output-New"
```

The output directory must be new or empty; the program intentionally refuses to overwrite your edited translations.

## Example output

```ini
// KKS_HLightControl.dll / KKS_HLightControl
General=
Shadow resolution target=
What resolution to apply when clicking 'Lower shadow resolution'=

// KKS_MakerSearch.dll / Tools
Search=
Reset=
```

Files: `AllStrings.txt`, `Translatable_AllLanguages.txt`, `UIStrings.txt`, `UI_HighConfidence.txt`, `SpecialStrings_Exact.txt`, `ByPlugin/`, `ByPlugin_UI/`, `ByPlugin_Special/`, `ScanSummary.txt`, `Errors.txt` and `Readme_重要说明.txt`.

## Build a Windows executable

Run `scripts/Build_Windows_EXE.bat`, or:

```powershell
dotnet publish src/KksDllStringExtractor -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:PublishTrimmed=false -o dist/win-x64
```

The `Windows release build` GitHub Actions workflow also uploads a Windows x64 build artifact; it does **not** publish a GitHub Release automatically.
The workflow tests the published EXE and verifies its license notices. Distribute the
complete publish folder (EXE, `LICENSE`, `THIRD_PARTY_NOTICES.md`, and `licenses/`),
keeping all notices with the executable.

## Relationship with XUnity.AutoTranslator

Strings that are visible in the plugin's runtime UI may be translated with XUnity if that text framework is intercepted. Merely inserting `Original=Translation` into a TXT file does not guarantee the plugin displays a translation. No runtime hooking or DLL rewriting is included.

See [output format and special string notes](docs/OUTPUT_FORMAT.md). If you find false-positive UI candidates, please open an issue with a redacted example rather than uploading proprietary DLLs.

## Safety and accuracy

The scanner opens untrusted plugin files for parsing but does not intentionally execute them. Treat DLL files as untrusted and scan with normal user privileges. UI selection is heuristic; strings may be missed, and technical identifiers can be mistaken for UI text.

## Build verification

Version 2.6.1 was verified on Windows with .NET SDK 8.0.425: Release build,
built-in regression tests, and self-contained win-x64 single-file publish.
Self-tests generate artificial managed DLLs and run their extraction even in the
single-file EXE; no game DLLs are needed or executed. Linux execution and actual
KKS plugin compatibility still need CI/user validation.

## License

MIT (see [LICENSE](LICENSE)). Mono.Cecil and other third-party projects are independently maintained; see [third-party notices](THIRD_PARTY_NOTICES.md).
