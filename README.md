# KKS DLL String Extractor

[简体中文说明](README.zh-CN.md) · [Output format](docs/OUTPUT_FORMAT.md) · [Contributing](CONTRIBUTING.md) · [Changelog](CHANGELOG.md)

A static, **multilingual** string extractor for **Koikatsu Sunshine (KKS) BepInEx plugin DLLs**, built with .NET 8 and Mono.Cecil. It reads managed assemblies without intentionally executing or modifying the scanned DLLs. It helps translators discover text; it is **not** a translation injector or a DLL patcher.

> Unofficial community tool. Not affiliated with Illusion, BepInEx, or XUnity.AutoTranslator.

## Features

- Recursively scans plugin directories for managed DLLs.
- Extracts IL `ldstr` strings and selected embedded text/resources.
- Keeps multiple writing systems, including Latin, Kana, Han, Hangul, Cyrillic, and Arabic; it is not limited to Japanese.
- Exports all extracted strings as well as heuristic translation and UI/configuration candidates.
- Deduplicates the combined output globally and deduplicates each per-DLL output independently.
- Uses plain TXT files with `// DLL / Class` headings and source strings as keys.
- Archives ambiguous strings losslessly, including strings containing newlines or escape-sensitive characters.
- Keeps special-string IDs stable across global, UI, and per-plugin exports. Supports strict UTF-8 and BOM-marked UTF-16 decoding.
- Limits text and `.resources` payloads to 32,000,000 bytes before reading and skips unrelated binary resources.
- Logs directory-access errors while continuing with accessible sibling directories; skips directory links to avoid recursive loops.

## Download and automatic builds

Open the [GitHub Releases page](https://github.com/angel12538/KKS-DLL-String-Extractor/releases) and download the ZIP for your platform:

- `KKS-DLL-String-Extractor-win-x64.zip` — Windows x64.
- `KKS-DLL-String-Extractor-linux-x64.zip` — Linux x64.

The GitHub Actions workflow is configured to build and self-test both platforms on every branch push. If both jobs pass, it creates a **prerelease** with both ZIP files. These continuous builds are for testing and may contain work in progress; they are not stable, versioned releases. Each run gets a unique `build-...` tag. The standalone Actions artifacts are retained for 14 days; Release assets remain available until the Release is removed.

Both packages are self-contained with respect to the .NET runtime, so a separate .NET Runtime installation is not required. Linux builds still rely on compatible system libraries provided by the distribution.

## Quick start: Windows x64

1. Download and fully extract `KKS-DLL-String-Extractor-win-x64.zip`. Keep the license and notice files beside the executable.
2. Double-click `KKS_DLL_String_Extractor.exe`.
3. Enter or drag in your KKS `BepInEx\plugins` directory, then press Enter.
4. Leave the output prompt blank to create a new `Results_TXT/scan_<timestamp>_<id>/` folder beside the executable, or specify a new or empty directory.
5. When scanning finishes or an error occurs, read the message and press Enter to close the window. Press Enter at the first directory prompt to cancel.

Command-line scans and diagnostic options such as `--self-test`, `--help`, and `--version` do not pause, making them suitable for scripts and CI. More details are included in `README-win-x64.txt` in the publish output.

## Quick start: Linux x64

1. Download and extract `KKS-DLL-String-Extractor-linux-x64.zip`.
2. Open a terminal in the extracted directory and make the executable runnable if necessary:

   ```bash
   chmod +x KKS_DLL_String_Extractor
   ```

3. Run a scan by providing the plugin directory and a new or empty output directory:

   ```bash
   ./KKS_DLL_String_Extractor "/path/to/BepInEx/plugins" "/path/to/KKS-Output"
   ```

4. To run the built-in self-test instead:

   ```bash
   ./KKS_DLL_String_Extractor --self-test
   ```

The output directory must be new or empty. The program refuses to overwrite existing files so that edited translation work is not silently replaced. Linux is built and self-tested on a GitHub-hosted Ubuntu runner; this does not guarantee compatibility with every Linux distribution or every game/plugin setup.

## Build from source

Requirements: the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0). The first build needs internet access to restore `Mono.Cecil` from NuGet.

Run the existing self-test on Windows:

```bat
scripts\Run_SelfTest.bat
```

On Windows, run the interactive scan helper:

```bat
scripts\Run_Scan.bat
```

Or invoke the application directly on Windows or Linux:

```text
dotnet run --project src/KksDllStringExtractor -- "/path/to/BepInEx/plugins" "/path/to/KKS-Output"
```

Publish a self-contained single-file application for Windows x64:

```powershell
dotnet publish src/KksDllStringExtractor/KksDllStringExtractor.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:PublishTrimmed=false -o dist/win-x64
```

Or for Linux x64:

```bash
dotnet publish src/KksDllStringExtractor/KksDllStringExtractor.csproj -c Release -r linux-x64 --self-contained true -p:PublishSingleFile=true -p:PublishTrimmed=false -o dist/linux-x64
```

Keep the entire publish directory when distributing a build, including `LICENSE`, `THIRD_PARTY_NOTICES.md`, and `licenses/`. These notices must accompany the executable.

## Output files

A normal output directory contains the following files and folders (some files may be empty when no matching strings are found):

| File or folder | Purpose |
| --- | --- |
| `AllStrings.txt` | All extracted originals, exact-deduplicated across scanned DLLs |
| `Translatable_AllLanguages.txt` | Multilingual translation candidates |
| `UIStrings.txt` | Heuristic UI-text candidates |
| `UI_HighConfidence.txt` | Higher-confidence UI/configuration-related candidates |
| `ByPlugin/` | All extracted strings, deduplicated separately for each DLL |
| `ByPlugin_UI/` | UI candidates grouped by DLL |
| `SpecialStrings_Exact.txt` | Lossless UTF-16LE Base64 archive of ambiguous originals |
| `ByPlugin_Special/` | Special-string archives grouped by DLL |
| `ScanSummary.txt`, `Errors.txt` | Scan summary and diagnostics |
| `Readme_重要说明.txt` | Notes about interpreting the generated files |

Example:

```ini
// KKS_HLightControl.dll / KKS_HLightControl
General=
Shadow resolution target=
What resolution to apply when clicking 'Lower shadow resolution'=

// KKS_MakerSearch.dll / Tools
Search=
Reset=
```

## Special strings and output format

Ordinary single-line originals are written as source keys followed by `=`. Strings containing `=`, backslashes, leading/trailing whitespace, control characters, newlines, or comment-like prefixes can be ambiguous in a simple TXT format. Such entries are marked as special and preserved losslessly in `SpecialStrings_Exact.txt` and `ByPlugin_Special/` as Base64-encoded raw UTF-16LE code units.

**The Base64 value is not a translation key.** For in-game translation, use the key captured by XUnity.AutoTranslator at runtime and follow its supported syntax. See [output format and special-string notes](docs/OUTPUT_FORMAT.md).

## Relationship with XUnity.AutoTranslator

This tool only extracts text. XUnity.AutoTranslator may translate text that it actually intercepts at runtime, but a string found in a DLL is not necessarily displayed in the UI or intercepted by XUnity. Adding `Original=Translation` lines to a TXT file does not guarantee that the game will use them. This project does not include runtime hooks, translation injection, or DLL rewriting.

## Safety and limitations

- Treat input DLLs as untrusted and run the scanner with normal user privileges. The tool parses files and does not intentionally execute them, but parsing untrusted files is not risk-free.
- UI/configuration classification is heuristic. It can miss visible text and can classify technical identifiers as UI candidates.
- Dynamically generated strings, encrypted or obfuscated data, and some serialized resources may not be available to static extraction.
- Resource text can be emitted separately from the full resource document.
- If reporting a false positive, share a redacted example rather than uploading proprietary game or plugin DLLs.

## Development and license

- [Development notes](docs/DEVELOPMENT.md)
- [Contributing guide](CONTRIBUTING.md)
- [Changelog](CHANGELOG.md)
- [MIT license](LICENSE) and [third-party notices](THIRD_PARTY_NOTICES.md)

Do not commit generated `Results_TXT/` output, proprietary game assets, plugin DLLs, or private logs to the repository.
