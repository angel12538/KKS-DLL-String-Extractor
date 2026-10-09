using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace KksDllStringExtractor;

// All TXT outputs use UTF-8 (no BOM), Windows CRLF, // DLL / Class and original=.
// Each output file is deduplicated by exact original string (ordinal case-sensitive).
// Do not silently escape original translation keys: doing so changes the literal lookup key.
// Ambiguous keys are stored losslessly as UTF-16LE Base64 in a dedicated TXT archive.
internal static class TxtExporter
{
    static readonly UTF8Encoding Utf8 = new UTF8Encoding(false);
    public static bool NeedsExactArchive(string s)
    {
        if (s.StartsWith("//", StringComparison.Ordinal) ||
            s.StartsWith("r:", StringComparison.Ordinal) || s.StartsWith("sr:", StringComparison.Ordinal) ||
            s.StartsWith("#", StringComparison.Ordinal) || s.StartsWith(";", StringComparison.Ordinal) ||
            s != s.Trim() || s.Contains('=') || s.Contains('\\')) return true;
        return s.Any(c => char.IsControl(c) || char.IsSurrogate(c)) || s.Contains('\u2028') || s.Contains('\u2029') || s.Contains('\uFEFF');
    }

    public static void Export(List<Analyzed> data, string output)
    {
        Directory.CreateDirectory(output);
        WriteTxt(Path.Combine(output, "AllStrings.txt"), data.Select(x => x.Raw), "all scanned strings / all languages");
        WriteTxt(Path.Combine(output, "Translatable_AllLanguages.txt"), data.Where(x => x.IsCandidate).Select(x => x.Raw), "multilingual candidates");
        WriteTxt(Path.Combine(output, "UIStrings.txt"), data.Where(x => x.IsUi).Select(x => x.Raw), "UI candidates with path filtering");
        WriteTxt(Path.Combine(output, "UI_HighConfidence.txt"), data.Where(x => x.IsUi && x.UiConfidence == "High").Select(x => x.Raw), "direct UI/config API evidence");
        WriteExact(Path.Combine(output, "SpecialStrings_Exact.txt"), data.Select(x => x.Raw), "all exact archived special strings");

        string all = Path.Combine(output, "ByPlugin");
        string ui = Path.Combine(output, "ByPlugin_UI");
        string special = Path.Combine(output, "ByPlugin_Special");
        Directory.CreateDirectory(all);
        Directory.CreateDirectory(ui);
        Directory.CreateDirectory(special);
        foreach (var group in data.GroupBy(x => x.Raw.Dll, StringComparer.OrdinalIgnoreCase))
        {
            string safe = SafeName(group.Key);
            WriteTxt(Path.Combine(all, safe + ".txt"), group.Select(x => x.Raw), "all strings for " + group.Key);
            var uiRows = group.Where(x => x.IsUi).Select(x => x.Raw).ToList();
            if (uiRows.Count != 0)
                WriteTxt(Path.Combine(ui, safe + ".txt"), uiRows, "UI strings for " + group.Key);
            var specialRows = group.Where(x => NeedsExactArchive(x.Raw.Text)).Select(x => x.Raw).ToList();
            if (specialRows.Count != 0)
                WriteExact(Path.Combine(special, safe + ".txt"), specialRows, "exact special originals for " + group.Key);
        }
        File.WriteAllText(Path.Combine(output, "Readme_重要说明.txt"),
            "TXT v2.6.1：直接扫描 DLL，仅输出 TXT；不提供旧版转换、CSV/TSV、翻译加载或 DLL 修改。\r\n" +
            "普通单行文本采用 原文=（不再使用 //原文=），按 // DLL文件名 / 类名 分组。\r\n" +
            "AllStrings.txt 对扫描范围全局去重；ByPlugin、ByPlugin_UI 和 ByPlugin_Special 各按 DLL 去重。\r\n" +
            "每个输出文件以完整原文作为精确去重键，区分大小写；不修剪空格、不转换语言、不规范化 Unicode。\r\n" +
            "分组标题只显示文件名和类名，不显示方法/命名空间。对资源文字显示资源名而非类名。\r\n" +
            "特别注意：去重只保留首次出现的来源分组，因此同一条字符串出现在多个类时只显示首次来源。\r\n" +
            "原文含真实换行、制表符、反斜杠、等号、前后空格或注释前缀等特殊情况时，\r\n" +
            "主文件只标记 [SPECIAL]；SpecialStrings_Exact.txt / ByPlugin_Special 使用 UTF-16LE Base64 保存精确原文。\r\n" +
            "特殊原文 ID 仅由精确文本决定，因此全部文本、UI 子集与按插件档案中的编号一致。\r\n" +
            "可用 PowerShell 的 [Text.Encoding]::Unicode.GetString([Convert]::FromBase64String('BASE64')) 还原正常 UTF-16 字符串。\r\n" +
            "不要将 [SPECIAL] 标记或 Base64 当成 XUnity 翻译键。普通单行原文保留原样，不会自动生成译文。\r\n" +
            "不要把未填写译文的 原文= 文件直接投入 XUnity：先填入翻译，再按 XUnity 的文件目录和转义规则使用。\r\n" +
            "有些 DLL 字符串并不是运行时 UI，XUnity 不一定能捕获，因此并非所有条目都能通过翻译文件生效。\r\n" +
            "UI 识别仍是启发式；完整提取结果不按语言过滤。\r\n" +
            "内嵌资源同时保存全文和逐行内容，但会按精确原文执行去重。\r\n" +
            "命令行拒绝写入非空输出目录；再次扫描请使用新的目录，并备份人工翻译。\r\n", Utf8);
    }

    static string SafeName(string dll)
    {
        string filename = dll.Replace('\\', '/').Split('/').Last();
        string stem = Path.GetFileNameWithoutExtension(filename);
        string safe = Regex.Replace(stem, @"[^\p{L}\p{N}._-]+", "_").Trim('.', ' ', '_');
        if (safe.Length == 0) safe = "Plugin";
        if (safe.Length > 65) safe = safe[..65];
        string digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(dll.ToLowerInvariant())))[..8];
        return safe + "__" + digest;
    }
    static string Header(RawRow r)
    {
        // A short display title: DLL basename / declaring class (no namespace, no method).
        // For embedded resources, retain the resource name rather than inventing a class.
        string dll = Path.GetFileName(r.Dll.Replace('\\', '/'));
        string source = r.Source;
        string label;
        if (r.Kind == "IL")
        {
            // Cecil type names use '/' for nested types and '.' for namespaces.
            string leaf = source.Split('/').Last();
            label = leaf.Split('.').Last();
        }
        else
        {
            label = source.Replace('\\', '/').Split('/').Last();
        }
        return Preview(dll) + " / " + Preview(label);
    }
    static string Preview(string s)
    {
        var b = new StringBuilder(s.Length);
        foreach (char c in s)
        {
            switch (c)
            {
                case '\\': b.Append("\\\\"); break;
                case '\n': b.Append("\\n"); break;
                case '\r': b.Append("\\r"); break;
                case '\t': b.Append("\\t"); break;
                default:
                    if (char.IsControl(c) || char.IsSurrogate(c) || c == '\u2028' || c == '\u2029' || c == '\uFEFF') b.Append("\\u" + ((int)c).ToString("X4"));
                    else b.Append(c);
                    break;
            }
        }
        return b.ToString();
    }
    public static byte[] ExactUtf16Bytes(string s)
    {
        // Manual conversion retains even lone UTF-16 surrogate code units without replacement fallback.
        var bytes = new byte[s.Length * 2];
        for (int i = 0; i < s.Length; i++)
        {
            bytes[i * 2] = (byte)(s[i] & 0xFF);
            bytes[i * 2 + 1] = (byte)(s[i] >> 8);
        }
        return bytes;
    }
    static string ExactId(RawRow r)
    {
        // Deduplication is by original text, so identifiers must share that identity
        // across source locations, DLLs and filtered UI subsets.
        return Convert.ToHexString(SHA256.HashData(ExactUtf16Bytes(r.Text)));
    }
    // Important: uniqueness is per output file, NOT per method/class.
    // AllStrings.txt removes global duplicates; ByPlugin/*.txt removes duplicates
    // inside each DLL while allowing another DLL to contain the same original.
    // Do not trim, normalize Unicode, lowercase or replace characters in the key.
    static List<RawRow> DistinctByOriginal(IEnumerable<RawRow> rows)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<RawRow>();
        foreach (var row in rows)
        {
            if (row.Text.Length != 0 && seen.Add(row.Text)) result.Add(row);
        }
        return result;
    }
    public static int CountDistinctOriginals(IEnumerable<RawRow> rows) =>
        DistinctByOriginal(rows).Count;

    static SortedDictionary<string, List<RawRow>> Group(IEnumerable<RawRow> rows)
    {
        var groups = new SortedDictionary<string, List<RawRow>>(StringComparer.Ordinal);
        foreach (var r in DistinctByOriginal(rows))
        {
            string header = Header(r);
            if (!groups.TryGetValue(header, out var items))
            {
                items = new List<RawRow>();
                groups[header] = items;
            }
            items.Add(r);
        }
        return groups;
    }
    static void WriteTxt(string file, IEnumerable<RawRow> rows, string purpose)
    {
        using var w = new StreamWriter(file, false, Utf8) { NewLine = "\r\n" };
        // No extra introduction: match the reference file's layout exactly.
        foreach (var group in Group(rows))
        {
            w.WriteLine("// " + group.Key);
            foreach (var r in group.Value)
            {
                if (NeedsExactArchive(r.Text))
                    w.WriteLine("// [SPECIAL:" + ExactId(r) + "] exact original in SpecialStrings_Exact.txt / ByPlugin_Special");
                else
                    w.WriteLine(r.Text + "="); // Preserve original, no comment prefix.
            }
            w.WriteLine();
        }
    }
    static void WriteExact(string file, IEnumerable<RawRow> rows, string purpose)
    {
        using var w = new StreamWriter(file, false, Utf8) { NewLine = "\r\n" };
        w.WriteLine("// KKS DLL String Extractor v2.6.1 - " + purpose);
        w.WriteLine("// Lossless original archive. utf16le-base64 is the exact .NET string, NOT an XUnity key.");
        w.WriteLine();
        foreach (var group in Group(rows.Where(x => NeedsExactArchive(x.Text))))
        {
            w.WriteLine("// " + group.Key);
            foreach (var r in group.Value)
            {
                w.WriteLine("// id=" + ExactId(r));
                w.WriteLine("// location=" + Preview(r.Offset));
                w.WriteLine("// preview=" + Preview(r.Text));
                w.WriteLine("// utf16le-base64=" + Convert.ToBase64String(ExactUtf16Bytes(r.Text)));
                w.WriteLine();
            }
            w.WriteLine();
        }
    }
}
