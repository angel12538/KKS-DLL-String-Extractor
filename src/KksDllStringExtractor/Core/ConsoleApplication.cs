using System.Text;

namespace KksDllStringExtractor;

internal static class ConsoleApplication
{
    public const string Version = "2.6.2";

    public static int Run(string[] args, TextReader input, TextWriter output, TextWriter error, string baseDirectory)
    {
        if (args.Length == 0)
            return RunInteractive(input, output, error, baseDirectory);
        if (args.Length == 1 && args[0] == "--self-test")
        {
            SelfTests.Run();
            return 0;
        }
        if (args.Length == 1 && args[0] == "--version")
        {
            output.WriteLine($"KKS DLL String Extractor v{Version}");
            return 0;
        }
        if (args.Length == 1 && args[0] is "--help" or "-h")
        {
            PrintUsage(output);
            return 0;
        }
        if (args.Length > 2 || args[0].StartsWith("--", StringComparison.Ordinal))
        {
            PrintUsage(output);
            return 2;
        }
        try
        {
            Scan(args[0], args.Length > 1 ? args[1] : Path.Combine(Directory.GetCurrentDirectory(), "Results_TXT"), output);
            return 0;
        }
        catch (ArgumentException ex)
        {
            error.WriteLine("Invalid input: " + ex.Message);
            return 2;
        }
        catch (Exception ex)
        {
            error.WriteLine("Scan failed: " + ex);
            return 1;
        }
    }

    static int RunInteractive(TextReader input, TextWriter output, TextWriter error, string baseDirectory)
    {
        bool waitBeforeExit = false;
        try
        {
            output.WriteLine($"KKS DLL 文本提取工具 v{Version}");
            output.WriteLine("静态提取插件 DLL 中的文本，不执行或修改 DLL。");
            output.WriteLine();
            output.WriteLine("请输入或拖入 BepInEx\\plugins 文件夹，然后按 Enter。");
            output.Write("插件目录（直接按 Enter 退出）：");
            output.Flush();
            string? inputLine = input.ReadLine();
            if (string.IsNullOrWhiteSpace(inputLine))
            {
                output.WriteLine("已取消，未扫描文件。");
                return 0;
            }
            // Keep scan results and errors visible until Enter is pressed.
            waitBeforeExit = true;
            string sourceDirectory = NormalizePath(inputLine);
            if (!Directory.Exists(sourceDirectory))
                throw new ArgumentException("插件目录不存在：" + sourceDirectory);
            output.WriteLine("输出目录必须不存在或为空，已有翻译文件不会被覆盖。");
            output.Write("输出目录（留空自动创建新的结果目录）：");
            output.Flush();
            string? outputLine = input.ReadLine();
            if (outputLine is null)
            {
                output.WriteLine("输入已结束，取消扫描。");
                return 0;
            }
            string destination = string.IsNullOrWhiteSpace(outputLine)
                ? Path.Combine(baseDirectory, "Results_TXT", $"scan_{DateTime.Now:yyyyMMdd_HHmmss_fff}_{Guid.NewGuid().ToString("N")[..8]}")
                : NormalizePath(outputLine);
            if (Directory.Exists(destination) && Directory.EnumerateFileSystemEntries(destination).Any())
                throw new ArgumentException("输出目录不是空目录，请选择一个新目录：" + destination);
            output.WriteLine();
            output.WriteLine("正在扫描，请稍候……");
            output.Flush();
            Scan(sourceDirectory, destination, output);
            output.WriteLine();
            output.WriteLine("扫描完成。结果目录：" + Path.GetFullPath(destination));
            output.WriteLine("详细统计和跳过的文件请查看 ScanSummary.txt、Errors.txt。");
            return 0;
        }
        catch (ArgumentException ex)
        {
            error.WriteLine("输入有误：" + ex.Message);
            return 2;
        }
        catch (Exception ex)
        {
            error.WriteLine("扫描失败：" + ex.Message);
            return 1;
        }
        finally
        {
            if (waitBeforeExit)
            {
                output.WriteLine();
                output.Write("按 Enter 键关闭窗口……");
                output.Flush();
                input.ReadLine();
            }
        }
    }

    static string NormalizePath(string value) => Path.GetFullPath(value.Trim().Trim('"'));

    static void Scan(string source, string destination, TextWriter log)
    {
        string input = NormalizePath(source);
        string output = NormalizePath(destination);
        if (!Directory.Exists(input))
            throw new ArgumentException("DLL folder does not exist: " + input);
    
        // Never overwrite previous extractions or hand-edited translations.
        if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any())
            throw new ArgumentException("Output folder is not empty. Choose a NEW folder: " + output);
    
        var report = Scanner.Scan(input);
        log.WriteLine($"Scanned {report.DllCount:N0} DLL(s); read {report.Rows.Count:N0} strings.");
        var data = report.Rows.Select(Classifier.Analyze).ToList();
        TxtExporter.Export(data, output);
    
        var utf8 = new UTF8Encoding(false);
        File.WriteAllLines(Path.Combine(output, "Errors.txt"), report.Errors.Count == 0
            ? new[] { "No errors." } : report.Errors, utf8);
        var summary = new[]
        {
            $"KKS DLL String Extractor v{Version} - direct DLL scan",
            $"DLL files visited: {report.DllCount:N0}",
            $"All extracted occurrences: {report.Rows.Count:N0}",
            $"All unique originals (global): {TxtExporter.CountDistinctOriginals(data.Select(x => x.Raw)):N0}",
            $"Safe inline originals (global): {TxtExporter.CountDistinctOriginals(data.Select(x => x.Raw).Where(x => !TxtExporter.NeedsExactArchive(x.Text))):N0}",
            $"Safe inline originals (per-DLL dedup sum): {data.GroupBy(x => x.Raw.Dll, StringComparer.OrdinalIgnoreCase).Sum(g => TxtExporter.CountDistinctOriginals(g.Select(x => x.Raw).Where(x => !TxtExporter.NeedsExactArchive(x.Text)))):N0}",
            $"Translation candidate occurrences: {data.Count(x => x.IsCandidate):N0}",
            $"Possible UI occurrences: {data.Count(x => x.IsUi):N0}",
            $"High confidence UI occurrences: {data.Count(x => x.IsUi && x.UiConfidence == "High"):N0}",
            $"Potentially unsafe inline keys (exact archive required): {data.Count(x => TxtExporter.NeedsExactArchive(x.Raw.Text)):N0}",
            $"Direct UI API strings: {data.Count(x => x.Raw.Usage == "DirectUI"):N0}",
            $"Direct configuration API strings: {data.Count(x => x.Raw.Usage == "DirectConfig"):N0}",
            $"Errors and skipped resources: {report.Errors.Count:N0}",
            "AllStrings.txt contains unique nonempty originals across DLLs; ByPlugin contains unique originals per DLL.",
            "UI classification is heuristic. This tool does not inject translations or modify DLLs."
        };
        File.WriteAllLines(Path.Combine(output, "ScanSummary.txt"), summary, utf8);
        log.WriteLine("TXT output: " + output);
        log.WriteLine($"Errors: {report.Errors.Count:N0}");
    }

    static void PrintUsage(TextWriter output)
    {
        output.WriteLine($"KKS DLL String Extractor v{Version}");
        output.WriteLine("Double-click or run without arguments for interactive Chinese prompts.");
        output.WriteLine("Usage: KKS_DLL_String_Extractor <plugins-folder> [new-output-folder]");
        output.WriteLine("       KKS_DLL_String_Extractor --self-test | --help | --version");
        output.WriteLine("Recursively scans DLLs without executing them. Writes TXT only.");
        output.WriteLine("Will NOT overwrite a nonempty output folder.");
    }
}
