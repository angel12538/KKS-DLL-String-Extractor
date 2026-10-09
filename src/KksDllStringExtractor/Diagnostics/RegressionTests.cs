using System.Resources;
using System.Text;
using System.Text.RegularExpressions;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace KksDllStringExtractor;

internal static class RegressionTests
{
    const string SpecialText = "Line 1\nLine 2";
    const string UnicodeText = "保存😀\r\nText";

    static void Check(bool ok, string message)
    {
        if (!ok) throw new InvalidOperationException("Regression test failed: " + message);
    }

    public static void Run()
    {
        string root = Path.Combine(Path.GetTempPath(), "KKS_Regression_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string fixtures = Path.Combine(root, "fixtures");
            Directory.CreateDirectory(fixtures);
            string fixture = Path.Combine(fixtures, "RegressionFixture.dll");
            WriteFixture(fixture);
            // Use a smaller resource budget to exercise the same pre-allocation boundary cheaply.
            var report = Scanner.Scan(fixtures, maxResourceBytes: 1024);
            Check(report.DllCount == 1 && report.Rows.Any(r => r.Text == "KKS_TEST_こんにちは_Hello"), "managed IL extraction (including single-file EXE)");
            Check(report.Rows.Any(r => r.Text == SpecialText && r.Usage == "DirectUI"), "real IL GUI call detection");
            Check(report.Errors.Count(e => e.Contains("resource too large", StringComparison.Ordinal)) == 2, "text AND .resources size limits");
            Check(!report.Rows.Any(r => r.Source is "Oversized.txt" or "Oversized.resources" or "Ignored.png"), "oversized and binary payloads not extracted");
            Check(!report.Errors.Any(e => e.Contains("Ignored.png", StringComparison.Ordinal)), "unsupported resource ignored before decoding");
            foreach (string invalid in new[] { "InvalidLE.txt", "InvalidBE.txt", "TruncatedLE.txt", "TruncatedBE.txt", "InvalidUtf8.txt", "Utf32.txt" })
            {
                Check(report.Errors.Any(e => e.Contains(invalid, StringComparison.Ordinal)), "invalid encoding reported: " + invalid);
                Check(!report.Rows.Any(r => r.Source == invalid), "invalid encoding not silently replaced: " + invalid);
            }
            foreach (string valid in new[] { "ValidLE.txt", "ValidBE.txt", "ValidUtf8.txt" })
                Check(report.Rows.Any(r => r.Kind == "RESOURCE_DOCUMENT" && r.Source == valid && r.Text == UnicodeText), "exact text decoding: " + valid);
            Check(!report.Errors.Any(e => e.Contains("Empty.txt", StringComparison.Ordinal)), "empty text resource is valid");
            Check(report.Rows.Any(r => r.Kind == "RESOURCE" && r.Text == "Resource string 保存"), ".resources strings still decoded");
            var data = report.Rows.Select(Classifier.Analyze).ToList();
            foreach (var row in data.Where(x => x.Raw.Text is "alice@example.org" or "画像.jpg"))
                Check(row.Category == "Technical" && !row.IsUi && !row.IsCandidate, "technical token filtering: " + row.Raw.Text);
            Check(data.Count(x => x.Raw.Text is "alice@example.org" or "画像.jpg") == 2, "technical token fixture present");
            // A second DLL has the same special text at another source location.
            data.Add(Classifier.Analyze(new RawRow("Second.dll", "IL", "Other.Window", "DrawGUI()", "IL_0010", SpecialText, "DirectUI", "UnityEngine.GUILayout::Label")));
            string output = Path.Combine(root, "export");
            TxtExporter.Export(data, output);
            CheckArchiveReferences(output);
            CheckDirectoryRecovery(root, fixture);
        }
        finally { Directory.Delete(root, true); }
    }

    static void CheckArchiveReferences(string output)
    {
        string global = File.ReadAllText(Path.Combine(output, "SpecialStrings_Exact.txt"));
        string[] pluginArchives = Directory.GetFiles(Path.Combine(output, "ByPlugin_Special")).Select(File.ReadAllText).ToArray();
        string[] lists = Directory.GetFiles(output, "*.txt")
            .Concat(Directory.GetFiles(Path.Combine(output, "ByPlugin")))
            .Concat(Directory.GetFiles(Path.Combine(output, "ByPlugin_UI"))).ToArray();
        int placeholders = 0;
        foreach (string list in lists)
        foreach (Match match in Regex.Matches(File.ReadAllText(list), @"\[SPECIAL:([0-9A-F]+)\]"))
        {
            placeholders++;
            string marker = "// id=" + match.Groups[1].Value;
            Check(global.Contains(marker, StringComparison.Ordinal), "every subset placeholder resolves in global archive");
            Check(pluginArchives.Any(a => a.Contains(marker, StringComparison.Ordinal)), "every placeholder resolves in a plugin archive");
        }
        Check(placeholders > 0, "special placeholder fixture present");
        string expectedId = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(TxtExporter.ExactUtf16Bytes(SpecialText)));
        Check(pluginArchives.Length == 2 && pluginArchives.All(a => a.Contains("// id=" + expectedId, StringComparison.Ordinal)), "same original ID across DLLs and methods");
    }

    static void CheckDirectoryRecovery(string root, string fixture)
    {
        string tree = Path.Combine(root, "tree");
        string denied = Path.Combine(tree, "a-denied");
        string removed = Path.Combine(tree, "b-removed");
        string good = Path.Combine(tree, "c-good");
        Directory.CreateDirectory(denied);
        Directory.CreateDirectory(removed);
        Directory.CreateDirectory(good);
        string expected = Path.Combine(good, "Good.DLL");
        File.Copy(fixture, expected);
        IEnumerable<string> Enumerate(string path)
        {
            // Deferred exceptions exercise failures from MoveNext, not just from creating the iterator.
            if (path == denied) return Enumerable.Range(0, 1).Select<int, string>(_ => throw new UnauthorizedAccessException("test denied directory"));
            if (path == removed) return Enumerable.Range(0, 1).Select<int, string>(_ => throw new DirectoryNotFoundException("test removed directory"));
            return Directory.EnumerateFileSystemEntries(path);
        }
        var errors = new List<string>();
        var files = Scanner.EnumerateDlls(tree, errors, Enumerate).ToList();
        Check(files.Count == 1 && files[0] == expected, "accessible siblings survive directory errors");
        Check(errors.Count == 2 && errors.Any(e => e.Contains("UnauthorizedAccessException", StringComparison.Ordinal)) && errors.Any(e => e.Contains("DirectoryNotFoundException", StringComparison.Ordinal)), "directory failures logged");
    }

    static void WriteFixture(string path)
    {
        using var asm = AssemblyDefinition.CreateAssembly(new AssemblyNameDefinition("RegressionFixture", new Version(1, 0)), "RegressionFixture", ModuleKind.Dll);
        var module = asm.MainModule;
        var plain = new TypeDefinition("Example", "Plain", TypeAttributes.Public | TypeAttributes.Class, module.TypeSystem.Object);
        module.Types.Add(plain);
        foreach (string text in new[] { SpecialText, "KKS_TEST_こんにちは_Hello" })
        {
            var get = new MethodDefinition("Get" + plain.Methods.Count, MethodAttributes.Public | MethodAttributes.Static, module.TypeSystem.String);
            plain.Methods.Add(get);
            get.Body.Instructions.Add(Instruction.Create(OpCodes.Ldstr, text));
            get.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        }
        var gui = new TypeDefinition("UnityEngine", "GUILayout", TypeAttributes.Public | TypeAttributes.Class, module.TypeSystem.Object);
        module.Types.Add(gui);
        var label = new MethodDefinition("Label", MethodAttributes.Public | MethodAttributes.Static, module.TypeSystem.Void);
        label.Parameters.Add(new ParameterDefinition(module.TypeSystem.String));
        label.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        gui.Methods.Add(label);
        var window = new TypeDefinition("Example", "Window", TypeAttributes.Public | TypeAttributes.Class, module.TypeSystem.Object);
        module.Types.Add(window);
        var draw = new MethodDefinition("DrawGUI", MethodAttributes.Public | MethodAttributes.Static, module.TypeSystem.Void);
        window.Methods.Add(draw);
        foreach (string text in new[] { SpecialText, "alice@example.org", "画像.jpg" })
        {
            draw.Body.Instructions.Add(Instruction.Create(OpCodes.Ldstr, text));
            draw.Body.Instructions.Add(Instruction.Create(OpCodes.Call, label));
        }
        draw.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        void Add(string name, byte[] bytes) => module.Resources.Add(new EmbeddedResource(name, ManifestResourceAttributes.Public, bytes));
        Add("Oversized.txt", new byte[2048]);
        Add("Oversized.resources", new byte[2048]);
        Add("Ignored.png", new byte[2048]);
        Add("InvalidLE.txt", new byte[] { 0xFF, 0xFE, 0, 0xD8 });
        Add("InvalidBE.txt", new byte[] { 0xFE, 0xFF, 0xD8, 0 });
        Add("TruncatedLE.txt", new byte[] { 0xFF, 0xFE, 0x41 });
        Add("TruncatedBE.txt", new byte[] { 0xFE, 0xFF, 0x41 });
        Add("InvalidUtf8.txt", new byte[] { 0xC3, 0x28 });
        Add("Utf32.txt", new byte[] { 0xFF, 0xFE, 0, 0, 0x41, 0, 0, 0 });
        Add("Empty.txt", Array.Empty<byte>());
        foreach (var pair in new[] { ("ValidLE.txt", Encoding.Unicode), ("ValidBE.txt", Encoding.BigEndianUnicode), ("ValidUtf8.txt", Encoding.UTF8) })
            Add(pair.Item1, pair.Item2.GetPreamble().Concat(pair.Item2.GetBytes(UnicodeText)).ToArray());
        using (var buffer = new MemoryStream())
        {
            using (var writer = new ResourceWriter(buffer))
            {
                writer.AddResource("Greeting", "Resource string 保存");
                writer.AddResource("Binary", new byte[] { 1, 2, 3 });
                writer.Generate();
                Add("Strings.resources", buffer.ToArray());
            }
        }
        asm.Write(path);
    }
}
