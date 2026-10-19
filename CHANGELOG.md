# Changelog

## Unreleased

- Track the actual value assigned to static string collections, including array aliases, and invalidate prior values after unknown replacements.
- Respect the latest captured-name assignment and skip ambiguous writes after delegate creation.
- Require a reconstructed string to flow directly into the returned `GUIContent`; log-only concatenations no longer become high-confidence UI labels.
- Add generated managed-DLL regression cases for normal labels, collection replacements, captured-variable overwrites and logging.

## [2.6.5] - 2026-10-19

- Recover finite UI labels produced by static string-array projections, such as `Bra: On`, when plug-ins assemble them at runtime from separate label and state strings.
- Keep these reconstructed labels in the normal string exports and classify them as direct UI strings.
- Verify the new extraction against 303 plug-in DLLs; 32 clothing-kind/state labels were recovered from the ClothingStateMenu DLL.

## [2.6.4] - 2026-10-19

- Show a complete searchable preview beside every special-string reference in main and per-plugin TXT lists. Multiline configuration descriptions previously appeared only as IDs there, making them look missing.
- Preserve exact UTF-16LE archives, stable IDs, filtering and ordinary translation keys. Preview comments escape line breaks and do not create partial source keys.
- Add a managed-DLL regression for multiline configuration descriptions in all six global/per-plugin lists, and use the current application version in generated file headers.

- Fix release workflow validation by merging duplicate push event definitions; preserve main-branch, version-tag and manual triggers.

## [2.6.2] - 2026-10-10

- Fix the apparent "flash and close" when double-clicking the Windows EXE without arguments: show Chinese interactive folder prompts instead of immediately exiting with usage code 2.
- Keep scan results and errors visible until Enter is pressed; allow cancellation at the first prompt.
- Create a fresh output directory beside the EXE for each interactive scan; accept quoted Chinese paths with spaces.
- Preserve noninteractive command-line scanning and diagnostic flags for scripts and CI.
- Add managed-DLL integration tests for interactive launch, error/completion pauses, cancellation, CLI compatibility and output protection.
- Include a Chinese usage file in published builds.
