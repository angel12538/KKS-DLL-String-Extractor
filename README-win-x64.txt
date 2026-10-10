KKS DLL 文本提取工具 v2.6.2（Windows x64）

使用方法
1. 完整解压此 ZIP，无需另外安装 .NET，也无需管理员权限。
2. 双击 KKS_DLL_String_Extractor.exe。
3. 输入或拖入游戏的 BepInEx\plugins 文件夹，然后按 Enter。
4. 输出目录留空并按 Enter，会在程序目录下自动创建
   Results_TXT\scan_日期时间_随机编号；也可以指定一个不存在或为空的目录。
5. 扫描完成或报错后，窗口会保留。查看提示后按 Enter 关闭。
6. 查看结果目录中的 AllStrings.txt、ScanSummary.txt 和 Errors.txt。

第一步输入插件目录时直接按 Enter，可以取消并退出。
如果程序目录没有写入权限，请在输出目录提示处填写有权限的新目录。
重复扫描会创建不同的默认结果目录，不会覆盖已有提取结果或翻译。

命令行方式（自动化调用不暂停）
KKS_DLL_String_Extractor.exe "D:\Games\KKS\BepInEx\plugins" "D:\KKS-New-Results"
KKS_DLL_String_Extractor.exe --self-test
KKS_DLL_String_Extractor.exe --help
KKS_DLL_String_Extractor.exe --version

本工具静态读取托管 DLL，不执行或修改插件文件。提取结果仍需人工筛选。
分发时请保留 LICENSE、THIRD_PARTY_NOTICES.md 和 licenses 文件夹。
