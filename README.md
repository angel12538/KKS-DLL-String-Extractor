# KKS DLL String Extractor

[简体中文](README.zh-CN.md) · [Output format](docs/OUTPUT_FORMAT.md) · [Development](docs/DEVELOPMENT.md) · [Contributing](CONTRIBUTING.md) · [Changelog](CHANGELOG.md)

A static, multilingual string extractor for **Koikatsu Sunshine (KKS) BepInEx plugin DLLs**, built with .NET 8 and Mono.Cecil. It analyzes managed assemblies without intentionally executing or modifying scanned DLLs. It helps translators discover text; it is **not** a translation injector or a DLL patcher.

> Unofficial community tool. Not affiliated with Illusion, BepInEx, or XUnity.AutoTranslator.

## Features

- Recursively scans plugin directories for managed DLLs.
- Extracts IL `ldstr` strings and selected embedded text/resources.
- Preserves multiple writing systems, including Latin, Kana, Han, Hangul, Cyrillic, and Arabic; it is not Japanese-only.
- Exports all extracted originals and heuristic multilingual, UI, and configuration-related candidates.
- Deduplicates the combined output globally and deduplicates each per-DLL output independently.
- Uses plain TXT files with `// DLL / Class` headings and original strings as keys.
- Preserves ambiguous strings losslessly in a separate archive, including strings with newlines or escape-sensitive characters.
- Keeps special-string IDs stable across global, UI, and per-plugin exports.
- Strictly decodes UTF-8 and BOM-marked UTF-16; limits text and .resources payloads to 32,000,000 bytes before reading.
- Logs directory-access errors while continuing through accessible directories, and skips directory links to avoid recursive loops.

## Download

Visit [GitHub Releases](https://github.com/angel12538/KKS-DLL-String-Extractor/releases) and download the ZIP for your platform:

- **Windows x64:** `KKS-DLL-String-Extractor-win-x64.zip`
- **Linux x64:** `KKS-DLL-String-Extractor-linux-x64.zip`

The GitHub Actions workflow builds and self-tests both platforms on every branch push. When both jobs pass, it publishes a GitHub **pre-release** with both ZIPs attached. These continuous builds may contain work in progress and are not stable, versioned releases. Each run uses a unique `build-...` tag. Actions artifacts are retained for 14 days; release assets remain until the release is deleted.

Both packages include the .NET runtime, so a separate .NET Runtime installation is not required. Linux builds still depend on compatible system libraries provided by the distribution.

## Quick start: Windows x64

1. Download and fully extract the Windows ZIP. Keep the license and notice files with the executable.
2. Double-click `KKS_DLL_String_Extractor.exe`.
3. Enter or drag in your KKS `BepInEx\\plugins` directory, then press Enter.
4. Leave the output prompt blank to create a new `Results_TXT/scan_<timestamp>_<id>/` folder beside the executable, or specify a new or empty output directory.
5. Read the result or error message and press Enter to close the window. Press Enter at the first directory prompt to cancel.

Command-line scans and diagnostic options such as `--self-test`, `--help`, and `--version` do not pause, so they can be used in scripts and CI. The publish output also includes `README-win-x64.txt`.

## Quick start: Linux x64

1. Download and extract the Linux ZIP.
2. Open a terminal in the extracted directory. If needed, make the executable runnable:

    chmod +x KKS_DLL_String_Extractor

3. Run a scan by providing the plugin directory and a new or empty output directory:

    ./KKS_DLL_String_Extractor "/path/to/BepInEx/plugins" "/path/to/KKS-Output"

4. Run the built-in self-test when needed:

    ./KKS_DLL_String_Extractor --self-test

The output directory must be new or empty. The program refuses to overwrite existing files so that edited translation work is not silently replaced. Linux is built and self-tested on a GitHub-hosted Ubuntu runner; compatibility with every Linux distribution and every game/plugin setup is not guaranteed.

## Build from source

Requirements: the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0). The first build needs internet access to restore Mono.Cecil from NuGet.

On Windows, run the existing self-test:

    scripts\Run_SelfTest.bat

On Windows, launch the interactive scanner:

    scripts\Run_Scan.bat

Or scan directly from a terminal on Windows or Linux:

    dotnet run --project src/KksDllStringExtractor -- "/path/to/BepInEx/plugins" "/path/to/KKS-Output"

Publish a self-contained single-file Windows x64 build:

    dotnet publish src/KksDllStringExtractor/KksDllStringExtractor.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:PublishTrimmed=false -o dist/win-x64

Publish a self-contained single-file Linux x64 build:

    dotnet publish src/KksDllStringExtractor/KksDllStringExtractor.csproj -c Release -r linux-x64 --self-contained true -p:PublishSingleFile=true -p:PublishTrimmed=false -o dist/linux-x64

When distributing a build, keep the full publish directory, including `LICENSE`, `THIRD_PARTY_NOTICES.md`, and `licenses/`. Do not distribute only the executable.

## Output files

| File or folder | Purpose |
| --- | --- |
| `AllStrings.txt` | All extracted originals, globally exact-deduplicated across scanned DLLs |
| `Translatable_AllLanguages.txt` | Multilingual translation candidates |
| `UIStrings.txt` | Heuristic UI-text candidates |
| `UI_HighConfidence.txt` | Higher-confidence UI/configuration-related candidates |
| `ByPlugin/` | All strings grouped by DLL, deduplicated independently per DLL |
| `ByPlugin_UI/` | UI candidates grouped by DLL |
| `SpecialStrings_Exact.txt` | Lossless Base64 archive of ambiguous originals, encoded from raw UTF-16LE code units |
| `ByPlugin_Special/` | Special-string archives grouped by DLL |
| `ScanSummary.txt`, `Errors.txt` | Scan summary and diagnostics |
| `Readme_重要说明.txt` | Notes about interpreting generated files |

Example:

    // KKS_HLightControl.dll / KKS_HLightControl
    General=
    Shadow resolution target=
    What resolution to apply when clicking 'Lower shadow resolution'=

    // KKS_MakerSearch.dll / Tools
    Search=
    Reset=

## Special strings and XUnity.AutoTranslator

Ordinary single-line originals are written as source keys followed by `=`. Strings containing `=`, backslashes, leading/trailing whitespace, control characters, newlines, or comment-like prefixes can be ambiguous in a simple TXT format. Such entries are marked in the normal TXT output and preserved losslessly in `SpecialStrings_Exact.txt` and `ByPlugin_Special/` as Base64-encoded raw UTF-16LE code units.

**The Base64 value is not a translation key.** This tool only extracts text. XUnity.AutoTranslator may translate text it actually intercepts at runtime, but a string found in a DLL is not necessarily displayed in the UI or intercepted by XUnity. Adding `Original=Translation` lines to a TXT file does not guarantee an in-game translation. See [output format and special-string notes](docs/OUTPUT_FORMAT.md).

## Safety and limitations

- Treat input DLLs as untrusted and run the scanner with normal user privileges. The tool parses files and does not intentionally execute them, but parsing untrusted files is not risk-free.
- UI/configuration classification is heuristic. It can miss visible text or classify technical identifiers as UI candidates.
- Dynamically generated strings, encrypted or obfuscated data, and some serialized resources may not be available to static extraction.
- The tool does not modify game plugins, inject translations, or rewrite DLLs.
- When reporting a false positive, share a redacted example rather than uploading proprietary game or plugin DLLs.

## License

MIT; see [LICENSE](LICENSE). Mono.Cecil and other third-party projects are independently maintained; see [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) and the `licenses/` folder.
