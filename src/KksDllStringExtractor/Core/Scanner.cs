using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Resources;
using System.Text;
using Mono.Cecil;
using Mono.Cecil.Cil;
using AssemblyDefinition = Mono.Cecil.AssemblyDefinition;
using TypeDefinition = Mono.Cecil.TypeDefinition;

namespace KksDllStringExtractor;

internal static class Scanner
{
    internal const int MaxResourceBytes = 32_000_000;
    static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static readonly string[] TextExt = { ".txt", ".json", ".jsonl", ".xml", ".csv", ".tsv", ".ini", ".yaml", ".yml", ".po", ".md", ".resx", ".html", ".htm", ".properties", ".cfg", ".config" };
    public static Report Scan(string folder, int maxResourceBytes = MaxResourceBytes)
    {
        if (maxResourceBytes <= 0 || maxResourceBytes > MaxResourceBytes)
            throw new ArgumentOutOfRangeException(nameof(maxResourceBytes));
        var rows = new List<RawRow>();
        var errors = new List<string>();
        int dlls = 0;
        foreach (var file in EnumerateDlls(folder, errors))
        {
            dlls++;
            string relative = Path.GetRelativePath(folder, file);
            try
            {
                using var asm = AssemblyDefinition.ReadAssembly(file, new ReaderParameters { ReadSymbols = false, InMemory = false });
                foreach (var type in Flatten(asm.MainModule.Types))
                foreach (var method in type.Methods)
                {
                    if (!method.HasBody) continue;
                    var instructions = method.Body.Instructions;
                    for (int i = 0; i < instructions.Count; i++)
                    {
                        var ins = instructions[i];
                        if (ins.OpCode != OpCodes.Ldstr || ins.Operand is not string s || s.Length == 0)
                            continue;
                        var (usage, target) = CallInspector.Inspect(instructions, i);
                        rows.Add(new RawRow(relative, "IL", type.FullName, method.FullName, $"IL_{ins.Offset:X4}", s, usage, target));
                    }
                }
                ReadResources(file, relative, rows, errors, maxResourceBytes);
                if (dlls % 20 == 0) Console.WriteLine($"Scanned {dlls} DLLs...");
            }
            catch (Exception ex)
            {
                errors.Add($"{relative} | {ex.GetType().Name}: {ex.Message}");
                Console.WriteLine("[SKIP] " + relative);
            }
        }
        return new Report(rows, errors, dlls, "DLL scan");
    }

    internal static IEnumerable<string> EnumerateDlls(string folder, List<string> errors,
        Func<string, IEnumerable<string>>? enumerateDirectory = null)
    {
        enumerateDirectory ??= path => Directory.EnumerateFileSystemEntries(path, "*", new EnumerationOptions
        {
            RecurseSubdirectories = false, IgnoreInaccessible = false, AttributesToSkip = 0
        });
        var pending = new Stack<string>();
        pending.Push(folder);
        while (pending.Count != 0)
        {
            string directory = pending.Pop();
            string[] entries;
            try
            {
                // Materialize within the try: enumeration can fail on MoveNext as well as creation.
                entries = enumerateDirectory(directory).OrderBy(x => x, StringComparer.Ordinal).ToArray();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                errors.Add($"{Path.GetRelativePath(folder, directory)} | directory | {ex.GetType().Name}: {ex.Message}");
                continue;
            }
            var children = new List<string>();
            foreach (string entry in entries)
            {
                FileAttributes attributes;
                try { attributes = File.GetAttributes(entry); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    errors.Add($"{Path.GetRelativePath(folder, entry)} | {ex.GetType().Name}: {ex.Message}");
                    continue;
                }
                if ((attributes & FileAttributes.Directory) != 0)
                {
                    if ((attributes & FileAttributes.ReparsePoint) != 0)
                    {
                        errors.Add($"{Path.GetRelativePath(folder, entry)} | directory link skipped");
                        continue;
                    }
                    children.Add(entry);
                }
                else if (entry.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)) yield return entry;
            }
            for (int i = children.Count - 1; i >= 0; i--) pending.Push(children[i]);
        }
    }

    static void ReadResources(string file, string dll, List<RawRow> rows, List<string> errors, int maxResourceBytes)
    {
        // Cecil's GetResourceStream allocates the whole payload before returning.
        // Read the resource table and length prefix directly so limits apply BEFORE allocation.
        using var stream = File.OpenRead(file);
        using var pe = new PEReader(stream, PEStreamOptions.PrefetchMetadata | PEStreamOptions.LeaveOpen);
        var metadata = pe.GetMetadataReader();
        var directory = pe.PEHeaders.CorHeader?.ResourcesDirectory ?? default;
        using var binary = new BinaryReader(stream, StrictUtf8, leaveOpen: true);
        foreach (var handle in metadata.ManifestResources)
        {
            var resource = metadata.GetManifestResource(handle);
            if (!resource.Implementation.IsNil) continue; // Only embedded resources.
            string name = metadata.GetString(resource.Name);
            bool dotResources = name.EndsWith(".resources", StringComparison.OrdinalIgnoreCase);
            if (!dotResources && !TextExt.Any(ext => name.EndsWith(ext, StringComparison.OrdinalIgnoreCase))) continue;
            try
            {
                if (!pe.PEHeaders.TryGetDirectoryOffset(directory, out int directoryOffset) ||
                    resource.Offset < 0 || resource.Offset > (long)directory.Size - sizeof(uint))
                    throw new BadImageFormatException("Invalid embedded resource offset.");
                stream.Position = checked(directoryOffset + resource.Offset);
                uint length = binary.ReadUInt32();
                if (length > (long)directory.Size - resource.Offset - sizeof(uint) || length > stream.Length - stream.Position)
                    throw new BadImageFormatException("Embedded resource extends beyond the DLL resource directory.");
                if (length > maxResourceBytes)
                {
                    errors.Add($"{dll} | {name} | resource too large ({length} bytes; limit {maxResourceBytes})");
                    continue;
                }
                byte[] bytes = new byte[(int)length];
                stream.ReadExactly(bytes);
                if (dotResources) ReadDotResources(dll, name, bytes, rows, errors);
                else
                {
                    var decoded = DecodeText(bytes);
                    if (decoded == null) { errors.Add($"{dll} | {name} | text encoding unsupported or invalid"); continue; }
                    if (decoded.Length != 0)
                        rows.Add(new RawRow(dll, "RESOURCE_DOCUMENT", name, "full original resource", "", decoded));
                    using var lineReader = new StringReader(decoded);
                    string? line;
                    int lineNumber = 0;
                    while ((line = lineReader.ReadLine()) != null)
                    {
                        lineNumber++;
                        if (!string.IsNullOrWhiteSpace(line))
                            rows.Add(new RawRow(dll, "RESOURCE_LINE", name, "", $"Line {lineNumber}", line));
                    }
                }
            }
            catch (Exception ex) { errors.Add($"{dll} | {name} | {ex.GetType().Name}: {ex.Message}"); }
        }
    }
    static IEnumerable<TypeDefinition> Flatten(IEnumerable<TypeDefinition> types)
    {
        foreach (var type in types)
        {
            yield return type;
            foreach (var child in Flatten(type.NestedTypes)) yield return child;
        }
    }
    static void ReadDotResources(string dll, string name, byte[] bytes, List<RawRow> rows, List<string> errors)
    {
        using var reader = new ResourceReader(new MemoryStream(bytes));
        var it = reader.GetEnumerator();
        // GetResourceData inspects the raw payload without deserializing custom .NET objects.
        while (it.MoveNext())
        {
            string key = it.Key?.ToString() ?? "";
            try
            {
                reader.GetResourceData(key, out string typeName, out byte[] data);
                if (typeName == "ResourceTypeCode.String" || typeName == "System.String")
                {
                    using var binary = new BinaryReader(new MemoryStream(data), StrictUtf8);
                    string value = binary.ReadString();
                    if (value.Length != 0) rows.Add(new RawRow(dll, "RESOURCE", name, key, "", value));
                }
                // Intentionally skip serialized object payloads; they are unsafe to deserialize.
            }
            catch (Exception ex) { errors.Add($"{dll} | {name} | key={key} | {ex.GetType().Name}: {ex.Message}"); }
        }
    }
    internal static string? DecodeText(byte[] bytes)
    {
        if (bytes.Length > MaxResourceBytes) return null;
        try
        {
            // UTF-32 is unsupported; do not mistake its LE BOM for UTF-16.
            if (bytes.Length >= 4 &&
                (bytes[0] == 0xFF && bytes[1] == 0xFE && bytes[2] == 0 && bytes[3] == 0 ||
                 bytes[0] == 0 && bytes[1] == 0 && bytes[2] == 0xFE && bytes[3] == 0xFF)) return null;
            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
                return StrictUtf8.GetString(bytes, 3, bytes.Length - 3);
            if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE) return new UnicodeEncoding(false, false, true).GetString(bytes, 2, bytes.Length - 2);
            if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF) return new UnicodeEncoding(true, false, true).GetString(bytes, 2, bytes.Length - 2);
            if (bytes.Contains((byte)0)) return null;
            return StrictUtf8.GetString(bytes);
        }
        catch (DecoderFallbackException) { return null; }
    }
}
