# Changelog

## [2.6.2] - 2026-10-10

- Fix the apparent "flash and close" when double-clicking the Windows EXE without arguments: show Chinese interactive folder prompts instead of immediately exiting with usage code 2.
- Keep scan results and errors visible until Enter is pressed; allow cancellation at the first prompt.
- Create a fresh output directory beside the EXE for each interactive scan; accept quoted Chinese paths with spaces.
- Preserve noninteractive command-line scanning and diagnostic flags for scripts and CI.
- Add managed-DLL integration tests for interactive launch, error/completion pauses, cancellation, CLI compatibility and output protection.
- Include a Chinese usage file in published builds.
