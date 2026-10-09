# 第三方依赖

本项目通过 NuGet 依赖 [Mono.Cecil 0.11.6](https://github.com/jbevain/cecil/tree/0.11.6)。版权归 Jb Evain（2008 - 2015）和 Novell, Inc.（2008 - 2011）所有。
其完整的 MIT/X11 许可证已包含在 [licenses/Mono.Cecil-LICENSE.txt](licenses/Mono.Cecil-LICENSE.txt) 中。
项目许可证、此声明以及 Mono.Cecil 许可证都会复制到所有构建和发布输出中，作为外部文件，包括单文件构建。

自包含的 EXE 包还会包含 .NET 运行时。发布目标会从实际解析到的 Microsoft.NETCore.App 运行时包中复制
`licenses/dotnet-LICENSE.txt` 和 `licenses/dotnet-THIRD-PARTY-NOTICES.txt`。如果这些文件找不到，发布会失败。
在重新分发 EXE 时，请保留完整的发布目录；不要只分发可执行文件。


