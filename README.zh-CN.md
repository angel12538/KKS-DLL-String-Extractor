# KKS DLL String Extractor · KKS 插件 DLL 文本提取器

[English](README.md) · [输出格式细节](docs/OUTPUT_FORMAT.md) · [开发说明](docs/DEVELOPMENT.md) · [贡献指南](CONTRIBUTING.md) · [版本记录](CHANGELOG.md)

用于《恋活 Sunshine》（Koikatsu Sunshine，KKS）BepInEx 插件 DLL 的**静态文本提取工具**，基于 .NET 8 和 Mono.Cecil。工具分析托管程序集，不会主动执行或修改被扫描的 DLL。它帮助翻译人员发现文本，**不是翻译注入器，也不会改写 DLL**。

> 非官方社区工具，与 Illusion、BepInEx 及 XUnity.AutoTranslator 均无官方关联。

## 功能

- 递归扫描插件目录中的托管 DLL。
- 提取 IL 的 `ldstr` 字符串及部分内嵌文本/资源。
- 保留多种文字系统，包括日文、英文、中文、韩文、俄文、阿拉伯文等，不限于日文。
- 导出全部原文，以及通过启发式规则识别的多语言翻译候选、UI 和配置相关候选。
- 汇总文件在所有 DLL 之间全局精确去重；按 DLL 导出时各自独立去重。
- 使用纯 TXT 格式，以 `// DLL / 类名` 分组，并将原文作为待翻译键。
- 对换行、转义字符等可能产生歧义的原文进行无损存档。
- 特殊字符串 ID 在全局、UI 和按插件导出中保持稳定。
- 严格解码 UTF-8 和带 BOM 的 UTF-16；文本与 .resources 资源在读取前限制为 32,000,000 字节。
- 子目录访问失败时记录错误并继续扫描可访问目录；跳过目录链接，避免循环扫描。

## 下载

打开 [GitHub Releases 页面](https://github.com/angel12538/KKS-DLL-String-Extractor/releases)，按平台下载 ZIP：

- **Windows x64：** `KKS-DLL-String-Extractor-win-x64.zip`
- **Linux x64：** `KKS-DLL-String-Extractor-linux-x64.zip`

GitHub Actions 会在**每次推送分支代码**时构建并自测两个平台。两个构建任务都通过后，会自动创建 GitHub **预发布版本（Pre-release）**，并附上两个 ZIP。持续构建可能包含开发中的改动，不等同于稳定正式版。每次运行使用唯一的 `build-...` 标签。Actions 临时构建产物保留 14 天；Release 附件会一直保留，直到对应 Release 被删除。

两个包都包含 .NET 运行时，因此无需另行安装 .NET Runtime。Linux 版本仍依赖发行版提供的兼容系统库。

## 快速开始：Windows x64

1. 下载并完整解压 Windows ZIP。请将许可证和第三方声明文件与可执行文件一起保留。
2. 双击 `KKS_DLL_String_Extractor.exe`。
3. 输入或拖入 KKS 的 `BepInEx\\plugins` 目录，然后按 Enter。
4. 输出目录留空时，会在程序旁创建新的 `Results_TXT/scan_<timestamp>_<id>/` 文件夹；也可以指定一个不存在或为空的目录。
5. 扫描完成或发生错误后，查看提示并按 Enter 关闭窗口。在第一个目录提示处直接按 Enter 可取消。

命令行扫描及 `--self-test`、`--help`、`--version` 等诊断选项不会暂停，适合脚本和 CI 使用。发布目录中还包含 `README-win-x64.txt`。

## 快速开始：Linux x64

1. 下载并解压 Linux ZIP。
2. 在解压目录打开终端；必要时添加可执行权限：

    chmod +x KKS_DLL_String_Extractor

3. 指定插件目录以及一个全新或空的输出目录开始扫描：

    ./KKS_DLL_String_Extractor "/path/to/BepInEx/plugins" "/path/to/KKS-Output"

4. 运行内置自测：

    ./KKS_DLL_String_Extractor --self-test

输出目录必须不存在或为空。程序拒绝覆盖已有文件，避免人工编辑的翻译被静默替换。Linux 构建和自测在 GitHub 托管的 Ubuntu Runner 上执行，但不保证兼容所有 Linux 发行版或所有游戏/插件环境。

## 从源码运行与编译

需要安装 [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)。首次构建需要联网从 NuGet 还原 Mono.Cecil。

Windows 下运行自测脚本：

    scripts\Run_SelfTest.bat

Windows 下运行交互式扫描脚本：

    scripts\Run_Scan.bat

也可以在 Windows 或 Linux 终端中直接扫描：

    dotnet run --project src/KksDllStringExtractor -- "/path/to/BepInEx/plugins" "/path/to/KKS-Output"

编译 Windows x64 自包含单文件程序：

    dotnet publish src/KksDllStringExtractor/KksDllStringExtractor.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:PublishTrimmed=false -o dist/win-x64

编译 Linux x64 自包含单文件程序：

    dotnet publish src/KksDllStringExtractor/KksDllStringExtractor.csproj -c Release -r linux-x64 --self-contained true -p:PublishSingleFile=true -p:PublishTrimmed=false -o dist/linux-x64

分发时请保留整个发布目录，包括 `LICENSE`、`THIRD_PARTY_NOTICES.md` 和 `licenses/`，不要只复制可执行文件。

## 输出文件

| 文件或目录 | 用途 |
| --- | --- |
| `AllStrings.txt` | 所有提取到的原文，跨 DLL 全局精确去重 |
| `Translatable_AllLanguages.txt` | 多语言翻译候选 |
| `UIStrings.txt` | 启发式识别的 UI 文本候选 |
| `UI_HighConfidence.txt` | 置信度较高的 UI/配置相关候选 |
| `ByPlugin/` | 按 DLL 分组的全部文本，每个 DLL 单独去重 |
| `ByPlugin_UI/` | 按 DLL 分组的 UI 候选 |
| `SpecialStrings_Exact.txt` | 特殊原文的 UTF-16LE Base64 无损存档 |
| `ByPlugin_Special/` | 按 DLL 分组的特殊原文存档 |
| `ScanSummary.txt`、`Errors.txt` | 扫描汇总与诊断信息 |
| `Readme_重要说明.txt` | 生成文件的阅读说明 |

输出示例：

    // KKS_HLightControl.dll / KKS_HLightControl
    General=
    Shadow resolution target=
    What resolution to apply when clicking 'Lower shadow resolution'=

    // KKS_MakerSearch.dll / Tools
    Search=
    Reset=

## 特殊原文与 XUnity.AutoTranslator

普通单行原文会以“原文=”的形式输出。包含 `=`、反斜杠、首尾空白、控制字符、真实换行或类似注释前缀的字符串，可能无法在简单 TXT 格式中无歧义表示。此类内容会在普通 TXT 中标记，并以原始 UTF-16LE 代码单元的 Base64 形式无损保存到 `SpecialStrings_Exact.txt` 和 `ByPlugin_Special/`。

**Base64 内容不是翻译键。** 本工具只负责提取文本。XUnity.AutoTranslator 只能翻译运行时实际捕获到的文本；DLL 中存在的字符串不一定显示在 UI，也不保证会被 XUnity 拦截。仅将 `原文=译文` 写入 TXT 并不能保证游戏内翻译生效。详见[输出格式与特殊字符串说明](docs/OUTPUT_FORMAT.md)。

## 安全性与限制

- 请将输入 DLL 视为不可信文件，并以普通用户权限运行扫描器。程序不会主动执行 DLL，但解析不可信文件仍存在风险。
- UI/配置项分类基于启发式规则，可能漏掉可见文本，也可能把技术标识符归类为 UI 候选。
- 运行时生成、加密或混淆的字符串，以及部分序列化资源，可能无法通过静态分析提取。
- 本工具不修改游戏插件、不注入翻译，也不改写 DLL。
- 报告误判时，请提供脱敏示例，不要上传专有游戏资源或插件 DLL。

## 许可证

本项目采用 MIT 许可证，详见 [LICENSE](LICENSE)。Mono.Cecil 等第三方项目由各自维护者独立维护；请同时阅读 [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) 和 `licenses/` 文件夹中的声明。
