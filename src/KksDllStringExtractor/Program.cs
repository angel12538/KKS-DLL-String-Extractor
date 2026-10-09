using System.Text;
using KksDllStringExtractor;

Console.OutputEncoding = Encoding.UTF8;

if (args.Length == 1 && args[0] == "--self-test")
{
    SelfTests.Run();
    return;
}

if (args.Length == 1 && args[0] == "--version")
{
    Console.WriteLine("KKS DLL String Extractor v2.6.1");
    return;
}

if (args.Length == 1 && args[0] is "--help" or "-h")
{
    PrintUsage();
    return;
}

if (args.Length < 1 || args.Length > 2 || args[0].StartsWith("--", StringComparison.Ordinal))
{
    PrintUsage();
    Environment.ExitCode = 2;
    return;
}

try
{
    string input = Path.GetFullPath(args[0].Trim('"'));
    string output = Path.GetFullPath(args.Length > 1
        ? args[1].Trim('"')
        : Path.Combine(Directory.GetCurrentDirectory(), "Results_TXT"));

    if (!Directory.Exists(input))
        throw new ArgumentException("DLL folder does not exist: " + input);

    // Never overwrite previous extractions or hand-edited translations.
    if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any())
        throw new ArgumentException("Output folder is not empty. Choose a NEW folder: " + output);

    var report = Scanner.Scan(input);
    Console.WriteLine($"Scanned {report.DllCount:N0} DLL(s); read {report.Rows.Count:N0} strings.");
    var data = report.Rows.Select(Classifier.Analyze).ToList();
    TxtExporter.Export(data, output);

    var utf8 = new UTF8Encoding(false);
    File.WriteAllLines(Path.Combine(output, "Errors.txt"), report.Errors.Count == 0
        ? new[] { "No errors." } : report.Errors, utf8);
    var summary = new[]
    {
        "KKS DLL String Extractor v2.6.1 - direct DLL scan",
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
    Console.WriteLine("TXT output: " + output);
    Console.WriteLine($"Errors: {report.Errors.Count:N0}");
}
catch (ArgumentException ex)
{
    Console.Error.WriteLine("Invalid input: " + ex.Message);
    Environment.ExitCode = 2;
}
catch (Exception ex)
{
    Console.Error.WriteLine("Scan failed: " + ex);
    Environment.ExitCode = 1;
}

static void PrintUsage()
{
    Console.WriteLine("KKS DLL String Extractor v2.6.1");
    Console.WriteLine("Usage: KKS_DLL_String_Extractor <plugins-folder> [new-output-folder]");
    Console.WriteLine("       KKS_DLL_String_Extractor --self-test | --help | --version");
    Console.WriteLine("Recursively scans DLLs without executing them. Writes TXT only.");
    Console.WriteLine("Will NOT overwrite a nonempty output folder.");
}
