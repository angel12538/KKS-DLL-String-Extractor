using Mono.Cecil;
using Mono.Cecil.Cil;

namespace KksDllStringExtractor;

internal static class ConsoleLaunchTests
{
    static void Check(bool ok, string message)
    {
        if (!ok) throw new InvalidOperationException("Console launch test failed: " + message);
    }

    public static void Run()
    {
        string root = Path.Combine(Path.GetTempPath(), "KKS_Console_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string plugins = Path.Combine(root, "插件 目录");
            string app = Path.Combine(root, "程序 目录");
            Directory.CreateDirectory(plugins);
            Directory.CreateDirectory(app);
            WriteFixture(Path.Combine(plugins, "ConsoleFixture.dll"));

            var input = new CountingReader($"\"{plugins}\"\n\n\n");
            var output = new StringWriter();
            var error = new StringWriter();
            int code = ConsoleApplication.Run(Array.Empty<string>(), input, output, error, app);
            Check(code == 0 && error.ToString() == "", "no-argument launch accepts quoted Chinese paths");
            Check(input.ReadCount == 3 && output.ToString().Contains("按 Enter 键关闭窗口"), "scan waits for Enter before exit");
            string result = Directory.GetDirectories(Path.Combine(app, "Results_TXT")).Single();
            Check(File.ReadAllText(Path.Combine(result, "AllStrings.txt")).Contains("Console fixture 保存="), "interactive launch extracts a real managed DLL");
            Check(File.ReadAllText(Path.Combine(result, "ScanSummary.txt")).Contains("v" + ConsoleApplication.Version), "interactive scan summary version");

            code = ConsoleApplication.Run(Array.Empty<string>(), new StringReader(plugins + "\n\n\n"), new StringWriter(), new StringWriter(), app);
            Check(code == 0 && Directory.GetDirectories(Path.Combine(app, "Results_TXT")).Length == 2, "repeated interactive launches keep previous results");

            string occupied = Path.Combine(root, "已有翻译");
            Directory.CreateDirectory(occupied);
            string translation = Path.Combine(occupied, "translation.txt");
            File.WriteAllText(translation, "keep edited translation");
            input = new CountingReader(plugins + "\n" + occupied + "\n\n");
            error = new StringWriter();
            code = ConsoleApplication.Run(Array.Empty<string>(), input, new StringWriter(), error, app);
            Check(code == 2 && input.ReadCount == 3 && error.ToString().Contains("输出目录不是空目录"), "output error remains visible until Enter");
            Check(File.ReadAllText(translation) == "keep edited translation" && Directory.GetFiles(occupied).Length == 1, "existing translations preserved");

            input = new CountingReader(Path.Combine(root, "missing") + "\n\n");
            error = new StringWriter();
            code = ConsoleApplication.Run(Array.Empty<string>(), input, new StringWriter(), error, app);
            Check(code == 2 && input.ReadCount == 2 && error.ToString().Contains("插件目录不存在"), "invalid input remains visible until Enter");

            input = new CountingReader(plugins + "\n" + translation + "\n\n");
            error = new StringWriter();
            code = ConsoleApplication.Run(Array.Empty<string>(), input, new StringWriter(), error, app);
            Check(code == 1 && input.ReadCount == 3 && error.ToString().Contains("扫描失败"), "filesystem failure remains visible until Enter");
            Check(File.ReadAllText(translation) == "keep edited translation", "failed export does not overwrite a file");

            foreach (string cancel in new[] { "\n", "" })
            {
                input = new CountingReader(cancel);
                code = ConsoleApplication.Run(Array.Empty<string>(), input, new StringWriter(), new StringWriter(), app);
                Check(code == 0 && input.ReadCount == 1, "blank input and closed stdin exit cleanly");
            }
            input = new CountingReader(plugins + "\n");
            code = ConsoleApplication.Run(Array.Empty<string>(), input, new StringWriter(), new StringWriter(), app);
            Check(code == 0 && Directory.GetDirectories(Path.Combine(app, "Results_TXT")).Length == 2, "EOF at output prompt cancels without scanning");

            foreach (string flag in new[] { "--help", "-h", "--version", "--unknown" })
            {
                input = new CountingReader("");
                code = ConsoleApplication.Run(new[] { flag }, input, new StringWriter(), new StringWriter(), app);
                Check(code == (flag == "--unknown" ? 2 : 0) && input.ReadCount == 0, "CLI flag never pauses: " + flag);
            }
            string cliOutput = Path.Combine(root, "命令行 结果");
            input = new CountingReader("");
            code = ConsoleApplication.Run(new[] { plugins, cliOutput }, input, new StringWriter(), new StringWriter(), app);
            Check(code == 0 && input.ReadCount == 0 && File.Exists(Path.Combine(cliOutput, "AllStrings.txt")), "CLI scan remains noninteractive");
        }
        finally { Directory.Delete(root, true); }
    }

    sealed class CountingReader(string value) : StringReader(value)
    {
        public int ReadCount { get; private set; }
        public override string? ReadLine()
        {
            ReadCount++;
            return base.ReadLine();
        }
    }

    static void WriteFixture(string path)
    {
        using var assembly = AssemblyDefinition.CreateAssembly(new AssemblyNameDefinition("ConsoleFixture", new Version(1, 0)), "ConsoleFixture", ModuleKind.Dll);
        var module = assembly.MainModule;
        var type = new TypeDefinition("Fixture", "Strings", TypeAttributes.Public, module.TypeSystem.Object);
        module.Types.Add(type);
        var method = new MethodDefinition("GetText", MethodAttributes.Public | MethodAttributes.Static, module.TypeSystem.String);
        type.Methods.Add(method);
        method.Body.Instructions.Add(Instruction.Create(OpCodes.Ldstr, "Console fixture 保存"));
        method.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        assembly.Write(path);
    }
}
