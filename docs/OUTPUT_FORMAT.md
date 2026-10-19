# Output specification / 输出格式

Each normal line is an original .NET string plus the literal `=` character.

```ini
// KKS_HLightControl.dll / KKS_HLightControl
General=
Shadow resolution target=
What resolution to apply when clicking 'Lower shadow resolution'=

// KKS_MakerSearch.dll / Tools
Search=
Reset=
```

- `// DLL / Class`: source comment, not a translation.
- `Original=`: untranslated source key. Fill it in yourself as `Original=译文`.
- UTF-8 without BOM, CRLF line endings.
- `AllStrings.txt`: **global** exact, case-sensitive deduplication. The first encountered source is shown.
- `ByPlugin/*.txt`: each DLL deduplicated separately; duplicates between different DLLs are allowed.
- `UIStrings.txt` and `UI_HighConfidence.txt`: heuristic subsets only. Technical data remain in `AllStrings.txt`.
- Finite labels built from a static string dictionary and a static string array are reconstructed when a UI `Select` lambda combines them with a literal separator. For example, `Bra`, `On`, and `: ` become `Bra: On` in the normal exports.
- Reconstruction follows known collection replacements and the latest captured-name assignment. A generated label must feed directly into the returned `GUIContent`. Ambiguous assignments and log-only concatenations are skipped instead of using stale values.
- Embedded UnityFS bundles are detected by signature even when their names have no text extension. Supported TextAssets retain full documents/lines; XML leaf values are also exported as decoded text without XML tags. Conditional prefixes are reconstructed only for recognized XML-to-field mappings backed by literal enum sets. Such resource-derived candidates are not marked as direct UI API evidence.

## Special originals / 特殊原文

Keys containing `=`, backslash, whitespace at the start/end, control characters,
line breaks, or comment prefixes could break or change meaning in a simple TXT
translation format. They are represented by an archive reference and a complete,
searchable `// preview=` comment in normal TXT,
and stored in `SpecialStrings_Exact.txt` / `ByPlugin_Special` as the **raw UTF-16
code units encoded with Base64**. This archive is lossless (even for isolated
surrogates) but it is NOT a ready-made XUnity translation key.

From v2.6.4, the preview appears immediately after each `[SPECIAL:...]` reference
in all main lists and per-plugin lists. Multiline configuration descriptions
therefore remain visible while browsing `UIStrings.txt` or `UI_HighConfidence.txt`.
Actual line breaks appear as literal `\n` / `\r`, and literal backslashes as `\\`.
Previews are comments for inspection, not editable translation keys; the exact
archive and its IDs are unchanged. No paragraphs are split into new source keys.

From v2.6.1, `[SPECIAL:...]` and archive `id` fields use the same full SHA-256
of the original UTF-16LE code units, independent of DLL, class or method.
The same original therefore resolves to the same ID in all/global/UI/plugin
outputs. These IDs differ from v2.6.0 source-dependent IDs; translation keys
and raw archived originals are unchanged.

Decoded text examples are purely for manual inspection. For actual in-game use,
follow XUnity.AutoTranslator's supported translation syntax and compare the
runtime-captured key. Never use a Base64 field itself as the translation key.

## Limitations

- Static extraction is not the same as UI localization.
- Other strings made at runtime, encrypted/obfuscated data, and some serialized
  resources may not be available. Runtime reconstruction currently covers only
  the finite static UI projection described above.
- Calling API proximity is a heuristic, not a full IL dataflow proof.
- The program does not modify game plugins or supply translation injection.
- Resource lines may be extracted separately from the whole resource document.
- UnityFS support covers versions 6–8, uncompressed/LZ4/LZ4HC blocks and player serialized-file versions 14–21. LZMA, editor layouts, other bundle formats and other serialized object classes are not decoded; encountered unsupported formats are recorded in `Errors.txt`.
- Bundles are read block by block. Compressed resources, each block, metadata and total extracted TextAsset bytes remain bounded; logical bundle length/decompression work is limited to 512 MiB. XML DTDs and external entity resolution are disabled.
