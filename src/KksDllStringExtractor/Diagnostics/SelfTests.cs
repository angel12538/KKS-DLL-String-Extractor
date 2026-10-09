using System.Text;

namespace KksDllStringExtractor;

internal static class SelfTests
{
    public static void Run()
    {
        static void Check(bool ok, string message)
        {
            if (!ok) throw new InvalidOperationException("Self-test failed: " + message);
        }
        var knownUi = new RawRow("Example.dll", "IL", "Example.Window", "DrawGUI()", "IL_0001", "Open Settings", "DirectUI", "UnityEngine.GUILayout::Button");
        var config = new RawRow("Example.dll", "IL", "Example.Config", "Awake()", "IL_0002", "Enable this feature", "DirectConfig", "BepInEx.Configuration.ConfigFile::Bind");
        var internalPath = new RawRow("Example.dll", "IL", "Example.Window", "CreateUI()", "IL_0003", "Canvas Main Menu/04_System/Option/Viewport/Content", "DirectUI", "UnityEngine.GUILayout::Label");
        var internalName = new RawRow("Example.dll", "IL", "Example.Window", "OnGUI()", "IL_0004", "cvsEye01");
        var objPath = new RawRow("Example.dll", "IL", "Example.Window", "OnGUI()", "IL_0005", "imgTglCol/textTgl");
        var multilingual = new[] { "保存", "設定を開く", "Сохранить", "إعدادات", "설정", "Open", "Éditer", "Αποθήκευση" };
        Check(Classifier.Analyze(knownUi).IsUi && Classifier.Analyze(knownUi).UiConfidence == "High", "direct GUI call");
        Check(Classifier.Analyze(config).IsUi && Classifier.Analyze(config).UiConfidence == "High", "configuration call");
        Check(!Classifier.Analyze(internalPath).IsUi, "Unity hierarchy excluded");
        Check(!Classifier.Analyze(internalName).IsUi, "Unity identifier excluded");
        Check(!Classifier.Analyze(objPath).IsUi, "Unity object path excluded");
        foreach (string value in multilingual)
            Check(Classifier.Analyze(knownUi with { Text = value }).IsUi, "multilingual: " + value);
        string[] special = { "Line 1\r\nLine 2", "Literal \\n sequence", "Open=Settings", " trailing ", "a\tb", "\uD800", "//prefixed" };
        foreach (string value in special)
        {
            Check(TxtExporter.NeedsExactArchive(value), "special detection");
            byte[] bytes = TxtExporter.ExactUtf16Bytes(value);
            Check(bytes.Length == value.Length * 2, "UTF16 length");
            // Verify raw UTF-16 code units, including unmatched surrogates, without .NET fallback.
            for (int i = 0; i < value.Length; i++)
                Check((char)(bytes[i * 2] | (bytes[i * 2 + 1] << 8)) == value[i], "UTF16 round trip");
        }
        string[] safe = { "Save", "保存", "Open Settings", "Настройки", "設定を開く" };
        foreach (string value in safe)
            Check(!TxtExporter.NeedsExactArchive(value), "safe inline exact text: " + value);
        string temp = Path.Combine(Path.GetTempPath(), "KKS_TXT_v26_SelfTest_" + Guid.NewGuid().ToString("N"));
        try
        {
            var rows = new List<Analyzed> { Classifier.Analyze(knownUi), Classifier.Analyze(internalPath) };
            rows.AddRange(special.Select(x => Classifier.Analyze(knownUi with { Text = x })));
            // The exact same raw text occurs in another method and another class.
            rows.Add(Classifier.Analyze(knownUi with { Source = "Other.Namespace.OtherClass", Method = "OtherMethod()" }));
            rows.Add(Classifier.Analyze(knownUi with { Method = "Awake()" }));
            // Different case and different DLL must remain distinct in per-plugin output.
            rows.Add(Classifier.Analyze(knownUi with { Text = "open settings" }));
            rows.Add(Classifier.Analyze(knownUi with { Dll = "Second.dll", Source = "Another.Tool" }));
            TxtExporter.Export(rows, temp);
            string exported = File.ReadAllText(Path.Combine(temp, "AllStrings.txt"), Encoding.UTF8);
            string archive = File.ReadAllText(Path.Combine(temp, "SpecialStrings_Exact.txt"), Encoding.UTF8);
            Check(exported.Contains("Open Settings=", StringComparison.Ordinal), "original inline key");
            Check(!exported.Contains("//Open Settings=", StringComparison.Ordinal), "no // before key");
            Check(exported.Contains("// Example.dll / Window", StringComparison.Ordinal), "short DLL/class header");
            Check(!exported.Contains("DrawGUI()", StringComparison.Ordinal), "no methods in header");
            Check(exported.Split("Open Settings=", StringSplitOptions.None).Length - 1 == 1, "dedupe globally across methods/classes/DLLs");
            Check(exported.Contains("open settings=", StringComparison.Ordinal), "case-sensitive dedupe");
            string plugin1 = Directory.GetFiles(Path.Combine(temp, "ByPlugin"), "Example__*.txt").Select(x => File.ReadAllText(x, Encoding.UTF8)).Single();
            string plugin2 = Directory.GetFiles(Path.Combine(temp, "ByPlugin"), "Second__*.txt").Select(x => File.ReadAllText(x, Encoding.UTF8)).Single();
            Check(plugin1.Split("Open Settings=", StringSplitOptions.None).Length - 1 == 1, "dedupe within DLL");
            Check(plugin2.Contains("Open Settings=", StringComparison.Ordinal), "retain same string in other DLL");
            Check(exported.Contains("[SPECIAL:", StringComparison.Ordinal), "special placeholder");
            foreach (string s in special)
                Check(archive.Contains("// utf16le-base64=" + Convert.ToBase64String(TxtExporter.ExactUtf16Bytes(s)), StringComparison.Ordinal), "exact archive contains original");
        }
        finally
        {
            if (Directory.Exists(temp)) Directory.Delete(temp, true);
        }
        RegressionTests.Run();
        Console.WriteLine("PASS: UI/path, Unicode, TXT/dedup/archive IDs, resource limits/encoding, directory recovery, managed DLL scan.");
    }
}
