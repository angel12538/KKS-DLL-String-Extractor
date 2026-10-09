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

## Special originals / 特殊原文

Keys containing `=`, backslash, whitespace at the start/end, control characters,
line breaks, or comment prefixes could break or change meaning in a simple TXT
translation format. They are replaced by an explanatory comment in normal TXT,
and stored in `SpecialStrings_Exact.txt` / `ByPlugin_Special` as the **raw UTF-16
code units encoded with Base64**. This archive is lossless (even for isolated
surrogates) but it is NOT a ready-made XUnity translation key.

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
- Strings made at runtime, encrypted/obfuscated data, and some serialized
  resources may not be available.
- Calling API proximity is a heuristic, not a full IL dataflow proof.
- The program does not modify game plugins or supply translation injection.
- Resource lines may be extracted separately from the whole resource document.
