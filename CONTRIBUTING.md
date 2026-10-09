# 贡献指南 / Contributing

感谢你帮助改进这个工具！欢迎提交 Pull Request。

1. 从 `main` 创建分支。
2. 目标框架为 .NET 8。扫描逻辑必须严格保持为 静态 分析：绝不能加载或执行用户 DLL。
3. 保留原始字符串的原样，不要修剪、规范化、翻译或对去重键执行大小写无关处理。
4. 保持默认 TXT 输出格式（`// DLL / Class` 后跟 `Original=`）。
5. 为行为变更添加自测用例，并运行 `dotnet run --project src/KksDllStringExtractor -- --self-test`。
6. 提交 PR 时，请说明使用场景、行为变更和测试输出。

请勿提交游戏资源、第三方插件 DLL、个人翻译数据或包含私人信息的日志。
如需报告 Bug，请优先使用最小化的人工样例 DLL 或经过脱敏处理的日志。

## 开发命令

```powershell
dotnet restore src/KksDllStringExtractor/KksDllStringExtractor.csproj
dotnet build src/KksDllStringExtractor/KksDllStringExtractor.csproj -c Release
dotnet run --project src/KksDllStringExtractor -- --self-test
