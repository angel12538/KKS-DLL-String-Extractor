# KKS DLL String Extractor · KKS 插件 DLL 文本提取器

[English](README.md) · [输出格式细节](docs/OUTPUT_FORMAT.md) · [贡献指南](CONTRIBUTING.md) · [版本记录](CHANGELOG.md)

用于《恋活 Sunshine》（Koikatsu Sunshine，KKS）及相关 HF Patch 环境的 **BepInEx 插件 DLL 静态文本提取工具**。基于 **.NET 8 + Mono.Cecil**，读取托管程序集中的字符串，不会主动执行或修改被扫描的 DLL。它用于帮助整理翻译候选，**不是翻译注入器，也不会改写 DLL**。

> 非官方社区工具，与 Illusion、BepInEx 及 XUnity.AutoTranslator 均无官方关联。

## 功能

- 递归扫描插件目录中的托管 DLL。
- 提取 IL `ldstr` 字符串及部分内嵌文本/资源。
- 保留多种文字系统，包括日文、英文、中文、韩文、俄文、阿拉伯文等，不限于日文。
- 同时导出全部原文，以及通过启发式规则识别的翻译候选和 UI/配置文本候选。
- 汇总 TXT 在所有 DLL 之间进行全局精确去重；按 DLL 导出时各自独立去重。
- 使用纯 TXT 格式，以 `// DLL / 类名` 分组，并将原文作为待翻译键。
- 对换行、转义字符等可能产生歧义的特殊原文进行无损存档。
- 特殊字符串 ID 在全局、UI 和按插件导出文件中保持一致；严格解码 UTF-8 和带 BOM 的 UTF-16。
- 文本与 `.resources` 资源在读取前限制为 32,000,000 字节，并跳过无关二进制资源。
- 子目录访问失败时记录错误并继续扫描可访问的其他目录；跳过目录联接和符号链接，避免循环扫描。

## 下载与自动构建

打开 [GitHub Releases 页面](https://github.com/angel12538/KKS-DLL-String-Extractor/releases)，按平台下载对应 ZIP：

- `KKS-DLL-String-Extractor-win-x64.zip` —— Windows x64。
- `KKS-DLL-String-Extractor-linux-x64.zip` —— Linux x64。

GitHub Actions 工作流会在**每次推送分支代码**时构建并自测 Windows x64 和 Linux x64。两个平台都通过后，会自动创建一个**预发布版本（Pre-release）**，并附上两个 ZIP。持续构建可能包含开发中的改动，不等同于稳定正式版；每次运行都会使用唯一的 `build-...` 标签。Actions 临时构建产物保留 14 天，Release 附件会一直保留，直到对应 Release 被删除。

两个包都包含自包含的 .NET 运行时，因此无需另行安装 .NET Runtime。Linux 版本仍需要发行版提供兼容的系统库。

## 快速开始：Windows x64

1. 下载并完整解压 `KKS-DLL-String-Extractor-win-x64.zip`。请将许可证和第三方声明文件与 EXE 一起保留。
2. 双击 `KKS_DLL_String_Extractor.exe`。
3. 输入或拖入 KKS 的 `BepInEx\plugins` 目录，然后按 Enter。
4. 输出目录留空时，会在 EXE 所在目录旁创建新的 `Results_TXT/scan_<timestamp>_<id>/` 文件夹；也可以指定一个不存在或为空的目录。
5. 扫描完成或发生错误后，先查看提示，再按 Enter 关闭窗口。在第一个目录提示处直接按 Enter 可取消。

命令行扫描，以及 `--self-test`、`--help`、`--version` 等诊断选项不会暂停，适合脚本和 CI 使用。发布目录内的 `README-win-x64.txt` 也包含 Windows 使用说明。

## 快速开始：Linux x64

1. 下载并解压 `KKS-DLL-String-Extractor-linux-x64.zip`。
2. 在解压目录打开终端；必要时为程序添加执行权限：

   ```bash
   chmod +x KKS_DLL_String_Extractor
   ```

3. 传入插件目录和一个全新或空的输出目录，开始扫描：

   ```bash
   ./KKS_DLL_String_Extractor "/path/to/BepInEx/plugins" "/path/to/KKS-Output"
   ```

4. 单独运行内置自测：

   ```bash
   ./KKS_DLL_String_Extractor --self-test
   ```

输出目录必须不存在或为空。程序会拒绝覆盖已有文件，避免误删或覆盖人工编辑的翻译。Linux 构建和自测在 GitHub 托管的 Ubuntu Runner 上进行，但这不代表已验证所有 Linux 发行版或所有游戏/插件环境。

## 从源码运行与编译

需要安装 [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)。首次构建需要联网从 NuGet 还原 `Mono.Cecil`。

Windows 下运行现有自测脚本：

```bat
scripts\Run_SelfTest.bat
```

Windows 下运行交互式扫描脚本：

```bat
scripts\Run_Scan.bat
```

也可以在 Windows 或 Linux 终端中直接调用：

```text
dotnet run --project src/KksDllStringExtractor -- "/path/to/BepInEx/plugins" "/path/to/KKS-Output"
```

编译 Windows x64 自包含单文件程序：

```powershell
dotnet publish src/KksDllStringExtractor/KksDllStringExtractor.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:PublishTrimmed=false -o dist/win-x64
```

编译 Linux x64 自包含单文件程序：

```bash
dotnet publish src/KksDllStringExtractor/KksDllStringExtractor.csproj -c Release -r linux-x64 --self-contained true -p:PublishSingleFile=true -p:PublishTrimmed=false -o dist/linux-x64
```

分发时请保留整个发布目录，包括 `LICENSE`、`THIRD_PARTY_NOTICES.md` 和 `licenses/`，不可只复制可执行文件。

## 输出文件

通常会生成以下文件和目录（若没有匹配项，部分文件可能为空）：

| 文件或目录 | 用途 |
|---|---|
| `AllStrings.txt` | 提取到的全部原文，跨 DLL 全局精确去重 |
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

```ini
// KKS_HLightControl.dll / KKS_HLightControl
General=
Shadow resolution target=
What resolution to apply when clicking 'Lower shadow resolution'=

// KKS_MakerSearch.dll / Tools
Search=
Reset=
```

## 特殊原文与输出格式

普通单行原文会以“原文=`”的形式输出。包含 `=`、反斜杠、首尾空白、控制字符、真实换行或类似注释前缀的字符串，可能无法在简单 TXT 格式中无歧义表示。此类内容会在普通 TXT 中标记为特殊项，并以原始 UTF-16LE 代码单元的 Base64 形式无损保存到 `SpecialStrings_Exact.txt` 和 `ByPlugin_Special/`。

**Base64 内容不是可直接用于翻译的键。** 实际游戏翻译请以 XUnity.AutoTranslator 运行时捕获到的键为准，并遵循它支持的格式。详见[输出格式与特殊字符串说明](docs/OUTPUT_FORMAT.md)。

## 与 XUnity.AutoTranslator 的关系

本工具只负责提取文本。XUnity.AutoTranslator 只能翻译运行时实际捕获到的文本；DLL 中存在的字符串不一定显示在 UI，也不保证会被 XUnity 拦截。仅将 `原文=译文` 写入 TXT 并不能保证游戏内翻译生效。本项目不包含运行时 Hook、翻译注入或 DLL 改写功能。

## 安全性与限制

- 请将输入 DLL 视为不可信文件，并以普通用户权限运行扫描器。程序不会主动执行 DLL，但解析不可信文件仍存在风险。
- UI/配置项分类基于启发式规则，可能漏掉可见文本，也可能把技术标识符归类为 UI 候选。
- 运行时生成、加密或混淆的字符串，以及部分序列化资源，可能无法通过静态分析提取。
- 资源中的文本可能会与完整资源文档分开输出。
- 报告误判时，请提供脱敏示例，不要上传专有游戏资源或插件 DLL。

## 开发与许可证

- [开发说明](docs/DEVELOPMENT.md)
- [贡献指南](CONTRIBUTING.md)
- [版本记录](CHANGELOG.md)
- [MIT 许可证](LICENSE) 与[第三方声明](THIRD_PARTY_NOTICES.md)

请勿将 `Results_TXT/` 生成结果、专有游戏资源、插件 DLL 或私人日志提交到仓库。
