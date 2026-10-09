## What changed?


## Compatibility checklist

- [ ] TXT remains `// DLL / Class` + `Original=`
- [ ] Original strings are preserved without Unicode normalization
- [ ] Global and per-plugin deduplication rules remain correct
- [ ] Scanned DLLs are never executed
- [ ] Self-test passes locally (`dotnet run --project src/KksDllStringExtractor -- --self-test`)
- [ ] No game DLLs, generated TXT, private paths or personal data included
