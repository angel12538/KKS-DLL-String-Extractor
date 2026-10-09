# Contributing / 贡献指南

Thanks for helping improve this tool! Pull requests are welcome.

1. Create a branch from `main`.
2. Target .NET 8. Keep scanning strictly **static**: never load or execute user DLLs.
3. Preserve exact original strings. Don't trim, normalize, translate, or case-fold deduplication keys.
4. Keep the default TXT output format (`// DLL / Class` followed by `Original=`).
5. Add self-test cases for behavior changes and run `dotnet run --project src/KksDllStringExtractor -- --self-test`.
6. Submit a PR explaining the use case, behavioral changes, and test output.

请勿提交游戏资源、第三方插件 DLL、个人翻译数据或包含私人信息的日志。
When reporting a bug, prefer a minimal artificial sample DLL or a redacted log.

## Development commands

```powershell
dotnet restore src/KksDllStringExtractor/KksDllStringExtractor.csproj
dotnet build src/KksDllStringExtractor/KksDllStringExtractor.csproj -c Release
dotnet run --project src/KksDllStringExtractor -- --self-test
```
