# KKS DLL String Extractor · KKS 插件 DLL 文本提取器

[English](README.md) · [输出格式细节](docs/OUTPUT_FORMAT.md) · [贡献指南](CONTRIBUTING.md)

用于《恋活 Sunshine》（KKS）及 HF Patch 环境的 **BepInEx 插件 DLL 静态文本提取器**。
基于 **.NET 8 + Mono.Cecil**，不执行、不修改扫描的 DLL，也不附带翻译加载器。

> 非官方社区工具，与游戏开发商、BepInEx 及 XUnity.AutoTranslator 无官方关联。

## 功能

- 批量递归读取 `BepInEx/plugins` 下的托管 DLL。
- 提取 IL `ldstr` 字符串和部分内嵌资源文字。
- **所有语言**都保留：日文、英文、中文、韩文、俄文及其他文字系统。
- UI/配置项启发式识别，尽量排除 Unity 对象路径及技术标识符。
- 全局汇总**跨 DLL 精确去重**；按插件 TXT 则**每个 DLL 独立去重**。
- 只生成 TXT；分组 `// DLL / 类名`，原文行 `原文=`。
- 有换行、反斜杠、等号等特殊情况时，用独立档案无损保留，不伪造可匹配键。
- 特殊原文编号在所有导出文件中保持一致；UTF-8、带 BOM 的 UTF-16 均严格解码，异常编码记录错误。
- 文本与 `.resources` 资源在读取前限制为 32,000,000 字节，其他二进制资源直接跳过。
- 子目录访问失败时记录错误并继续扫描其他目录；跳过目录联接和符号链接，防止循环扫描。

## Windows 使用

### 直接使用发行版（Windows x64）

1. 下载并完整解压发行版 ZIP，无需安装 .NET。
2. 双击 `KKS_DLL_String_Extractor.exe`，输入或拖入 `BepInEx\plugins` 文件夹，然后按 Enter。
3. 输出目录留空，会在程序目录下自动创建新的 `Results_TXT/scan_日期时间_随机编号/`；也可以指定一个不存在或为空的目录。
4. 扫描完成或报错后窗口会保留，查看提示后按 Enter 关闭。第一个目录提示处直接按 Enter 可取消。

命令行扫描及 `--self-test`、`--help`、`--version` 不暂停，方便脚本和 CI 使用。
详细步骤见发行包中的 `README-win-x64.txt`。

### 从源码运行

1. 安装 [Microsoft .NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)；首次构建需要联网下载 Mono.Cecil。
2. 下载仓库 ZIP 并解压，或者使用 `git clone`。
3. 运行 `scripts/Run_SelfTest.bat` 进行自测。
4. 运行 `scripts/Run_Scan.bat`，输入 KKS 的 `BepInEx\plugins` 路径。
5. 结果位于 `Results_TXT/scan_日期时间/`，不会覆盖此前的结果。

也可以命令行扫描：

```powershell
dotnet run --project src/KksDllStringExtractor -- "D:\Games\KoikatsuSunshine\BepInEx\plugins" "D:\New-KKS-Results"
```

**输出目录必须不存在或为空**，以免覆盖人工修改过的翻译文件。

### 输出样式

```ini
// KKS_HLightControl.dll / KKS_HLightControl
General=
Shadow resolution target=
What resolution to apply when clicking 'Lower shadow resolution'=

// KKS_MakerSearch.dll / Tools
Search=
Reset=
```

### 输出文件

| 文件 | 用途 |
|---|---|
| `AllStrings.txt` | 所有提取到的原文，全局精确去重 |
| `Translatable_AllLanguages.txt` | 多语言翻译候选 |
| `UIStrings.txt` | 疑似 UI 文字 |
| `UI_HighConfidence.txt` | 高置信度 UI/配置 API 关联文字 |
| `ByPlugin/` | 每个 DLL 单独的全部文本 |
| `ByPlugin_UI/` | 每个 DLL 单独的 UI 候选 |
| `SpecialStrings_Exact.txt` | 特殊原文的无损 UTF-16LE Base64 存档 |
| `ByPlugin_Special/` | 特殊原文按 DLL 分档 |
| `ScanSummary.txt`、`Errors.txt` | 扫描统计和错误 |

### 特殊原文

普通单行字符串不转义，可以在 `原文=` 后填写翻译；但 `=`、反斜杠、真实换行、注释前缀或前后空格可能导致 TXT 解析歧义。程序把这些内容作为 `[SPECIAL]` 标记并以 UTF-16LE Base64 无损保存在档案中。**Base64 不是翻译键**，应参考 XUnity 实际捕获的原文和转义规则手工处理。

### 与 XUnity.AutoTranslator 配合

本工具只提取。你可以使用已有 XUnity.AutoTranslator 将真正被拦截到的游戏 UI 文本翻译；但 DLL 中的静态字符串不一定会出现在 UI 中，也不保证全部能被 XUnity 捕获。不能单靠把导出 TXT 放进游戏目录就保证翻译生效。

## 编译 Windows EXE

双击 `scripts/Build_Windows_EXE.bat`，或者执行：

```powershell
dotnet publish src/KksDllStringExtractor -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:PublishTrimmed=false -o dist/win-x64
```

GitHub Actions 自动构建 Windows 和 Linux，并可产出 Windows x64 构建产物；发布 Release 需要项目维护者自行完成。
发布流程会运行构建后的 EXE 自测并检查许可证文件。分发时请保留整个发布目录，
包括 EXE、`LICENSE`、`THIRD_PARTY_NOTICES.md` 和 `licenses/`，不要只复制 EXE。

## 开发与参与

- 架构说明：[docs/DEVELOPMENT.md](docs/DEVELOPMENT.md)
- 贡献指南：[CONTRIBUTING.md](CONTRIBUTING.md)
- 版本记录：[CHANGELOG.md](CHANGELOG.md)
- 开源许可证：[MIT](LICENSE)

**隐私提醒：** 不要把扫描生成的 TXT、HF Patch 插件 DLL、游戏资源或私人日志提交到 GitHub。本仓库默认通过 `.gitignore` 忽略 `Results_TXT/` 和构建目录。

**验证说明：** v2.6.2 已在 Windows / .NET SDK 8.0.425 下验证 Release 构建、自带回归测试和 win-x64 自包含单文件发布。已针对发布后的 EXE 验证无参数启动、中文路径输入、完成/报错后暂停、取消和命令行兼容性。自测会生成合成 DLL 并静态扫描，单文件 EXE 也执行同样的集成测试，不依赖或执行游戏 DLL。Linux 实际运行和真实 KKS 插件兼容性仍需 CI 或用户验证。
