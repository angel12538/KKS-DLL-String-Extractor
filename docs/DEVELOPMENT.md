# Architecture / 架构

`src/KksDllStringExtractor/`

| Module | Responsibility |
|---|---|
| `Program.cs` | CLI argument handling, output protection, summary, exit codes |
| `Models/ScanModels.cs` | Source records and classification results |
| `Core/Scanner.cs` | Mono.Cecil IL string extraction and embedded resource handling |
| `Core/CallInspector.cs` | Conservative call-site classification (GUI/config/technical) |
| `Core/Classifier.cs` | Script classification and UI candidate scoring |
| `Core/TxtExporter.cs` | Exact-key deduplication, TXT groups, special-text archive |
| `Diagnostics/SelfTests.cs` | Fast self-tests and managed-assembly scan smoke test |
| `Diagnostics/RegressionTests.cs` | Generated DLL fixtures, resource boundaries, archive references and directory recovery |

Workflow: `Scanner.Scan` → `Classifier.Analyze` → `TxtExporter.Export`.

Resource payloads are read via the built-in PE metadata reader and explicit
length-prefix checks. Mono.Cecil's `GetResourceStream()` already allocates the
whole payload, so it must not be used to enforce a pre-allocation size limit.
Unsupported binary resources are never opened, and both text and `.resources`
are limited to 32,000,000 bytes. Directory traversal isolates errors per directory
and skips directory links; text decoding is strict UTF-8 or BOM-marked UTF-16.

## Principles

- Keep the full list even if UI classification filters a technical-looking key.
- Use `StringComparer.Ordinal` for text identity. No linguistic normalization.
- Never dynamically load, reflectively instantiate, or execute scanned code.
- Do not silently change string bytes when translating `Original=` into TXT.
- Prefer a new timestamped output directory per scan.

## Known technical debt

- `CallInspector` examines nearby IL calls instead of complete operand stack flow.
- Classification is heuristic and can still produce false positives/negatives.
- `.resources` binary entries and serialized custom objects are not all decoded.
- `Scanner.Scan` keeps rows in memory, which is fine for typical plugin folders,
  but large scans would benefit from streaming.
- Windows .NET 8 builds and both ordinary/single-file self-tests have been verified.
  Linux execution and actual game-plugin compatibility still require CI/user validation.
