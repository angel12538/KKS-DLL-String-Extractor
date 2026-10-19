using System.Buffers.Binary;
using System.Text;
using Mono.Cecil;

namespace KksDllStringExtractor;

internal static class UnityBundleTests
{
    const string Xml = "<?xml version=\"1.0\"?><Items><Item><Key>good</Key><Title>Visible</Title><Description>Visible &amp; readable.\r\nSecond line.</Description></Item><Item><Key>inactive</Key><Title>Quiet</Title></Item></Items>";

    public static void Run()
    {
        Check(Encoding.UTF8.GetString(UnityBundleText.Decode(new byte[] { 0x44, 97, 98, 99, 100, 4, 0, 0x10, 33 }, 13, 2, 64)) == "abcdabcdabcd!", "overlapping LZ4 match");
        Reject(() => UnityBundleText.Decode(new byte[] { 0x20, 65 }, 2, 2, 64), "truncated literals");
        Reject(() => UnityBundleText.Decode(new byte[] { 0x10, 65, 0, 0 }, 5, 2, 64), "zero-distance match");
        Reject(() => UnityBundleText.Decode(new byte[] { 0xF0 }, 15, 2, 64), "truncated extended length");
        Reject(() => UnityBundleText.Decode(Array.Empty<byte>(), 65, 2, 64), "decompression allocation limit");
        Reject(() => UnityBundleText.Read(Bundle(Xml, false, false), 64).ToList(), "resource text/metadata limit");
        var invalidSize = Bundle(Xml, false, false);
        invalidSize[26] = 0x7F;
        Reject(() => UnityBundleText.Read(invalidSize, 4096).ToList(), "bundle header size mismatch");

        foreach (bool atEnd in new[] { false, true })
        foreach (bool compressed in new[] { false, true })
        {
            var bundle = Bundle(Xml, compressed, atEnd);
            var texts = UnityBundleText.Read(bundle, 4096).ToList();
            Check(texts.Count == 1 && Encoding.UTF8.GetString(texts[0].Bytes) == Xml, "exact TextAsset bytes, directory placement and LZ4 mode");
            Check(texts[0].Path.Contains("labels#2", StringComparison.Ordinal), "TextAsset source identity");
        }

        string root = Path.Combine(Path.GetTempPath(), "KKS_Unity_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            using (var fixture = XmlProjectionTests.Fixture(overwriteName: false))
            {
                fixture.MainModule.Resources.Add(new EmbeddedResource("Example.labels", ManifestResourceAttributes.Public, Bundle(Xml, true, true)));
                fixture.Write(Path.Combine(root, "Example.dll"));
            }
            var report = Scanner.Scan(root);
            Check(report.Errors.Count == 0, "embedded UnityFS scan without loading Unity or the fixture");
            Check(report.Rows.Any(r => r.Kind == "RESOURCE_XML_VALUE" && r.Text == "Visible & readable.\nSecond line."), "XML decoded values match runtime XML parsing");
            Check(report.Rows.Any(r => r.Text == "[+] Visible") && !report.Rows.Any(r => r.Text == "[+] Quiet"), "prefix applies only to enum set members");
            string output = Path.Combine(root, "export");
            TxtExporter.Export(report.Rows.Select(Classifier.Analyze).ToList(), output);
            Check(File.ReadAllText(Path.Combine(output, "AllStrings.txt")).Contains("[+] Visible=", StringComparison.Ordinal), "full generated label exported");
            Check(Directory.GetFiles(Path.Combine(output, "ByPlugin")).Select(File.ReadAllText).Single().Contains("[+] Visible=", StringComparison.Ordinal), "per-plugin generated label exported");
            using (var fixture = XmlProjectionTests.Fixture(overwriteName: true))
            {
                fixture.MainModule.Resources.Add(new EmbeddedResource("Example.labels", ManifestResourceAttributes.Public, Bundle(Xml, false, false)));
                fixture.Write(Path.Combine(root, "Example.dll"));
            }
            Check(!Scanner.Scan(root).Rows.Any(r => r.Kind == "RESOURCE_COMPOSITE"), "constructor field overwrites invalidate prefix reconstruction");
            using (var fixture = XmlProjectionTests.Fixture(overwriteName: false))
            {
                fixture.MainModule.Resources.Add(new EmbeddedResource("Blocked.xml", ManifestResourceAttributes.Public,
                    Encoding.UTF8.GetBytes("<!DOCTYPE doc [<!ENTITY expanded 'must not expand'>]><doc>&expanded;</doc>")));
                fixture.Write(Path.Combine(root, "Example.dll"));
            }
            var blocked = Scanner.Scan(root);
            Check(blocked.Errors.Any(e => e.Contains("XML values skipped", StringComparison.Ordinal)) &&
                !blocked.Rows.Any(r => r.Kind == "RESOURCE_XML_VALUE"), "XML DTD/entities are prohibited");
        }
        finally { Directory.Delete(root, true); }
        Console.WriteLine("PASS: embedded UnityFS TextAssets, lazy block reads, LZ4/range budgets, XML leaf values and enum-filtered resource labels.");
    }

    static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("Unity resource regression failed: " + message);
    }

    static void Reject(Action action, string message)
    {
        try { action(); }
        catch (InvalidDataException) { return; }
        throw new InvalidOperationException("Unity resource boundary not enforced: " + message);
    }

    static byte[] Bundle(string text, bool compressed, bool atEnd)
    {
        using var payload = new MemoryStream();
        using (var writer = new BinaryWriter(payload, Encoding.UTF8, leaveOpen: true))
        {
            var name = Encoding.UTF8.GetBytes("labels");
            writer.Write(name.Length);
            writer.Write(name);
            Align(writer, 4);
            var script = Encoding.UTF8.GetBytes(text);
            writer.Write(script.Length);
            writer.Write(script);
        }
        byte[] asset = payload.ToArray();
        const int dataOffset = 512, skippedTextureSize = 1024;
        int fileSize = dataOffset + skippedTextureSize + asset.Length;
        using var metadata = new MemoryStream();
        using (var writer = new BinaryWriter(metadata, Encoding.UTF8, leaveOpen: true))
        {
            Z(writer, "5.6.2f1");
            writer.Write(19); // Windows player build, not Unity editor data.
            writer.Write(false); // No type tree.
            writer.Write(2);
            foreach (int classId in new[] { 49, 28 })
            {
                writer.Write(classId);
                writer.Write(false);
                writer.Write((short)-1);
                writer.Write(new byte[16]);
            }
            writer.Write(2);
            Align(writer, 4);
            writer.Write(1L);
            writer.Write(0U);
            writer.Write((uint)skippedTextureSize);
            writer.Write(1); // Texture object; must never be decoded as text.
            Align(writer, 4);
            writer.Write(2L);
            writer.Write((uint)skippedTextureSize);
            writer.Write((uint)asset.Length);
            writer.Write(0); // TextAsset.
        }
        using var head = new MemoryStream();
        using (var writer = new BinaryWriter(head, Encoding.UTF8, leaveOpen: true))
        {
            BE(writer, metadata.Length, 4);
            BE(writer, fileSize, 4);
            BE(writer, 17, 4);
            BE(writer, dataOffset, 4);
            writer.Write(new byte[4]);
            writer.Write(metadata.ToArray());
            writer.Write(new byte[dataOffset - head.Length]);
        }
        var blocks = new[] { head.ToArray(), new byte[] { 0 }, compressed ? LiteralLz4(asset) : asset };
        using var directory = new MemoryStream();
        using (var writer = new BinaryWriter(directory, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(new byte[16]);
            BE(writer, 3, 4);
            int[] sizes = { dataOffset, skippedTextureSize, asset.Length };
            for (int i = 0; i < 3; i++)
            {
                BE(writer, sizes[i], 4);
                BE(writer, blocks[i].Length, 4);
                // An unsupported codec in a texture-only block proves reads are lazy.
                BE(writer, i == 1 ? 1 : i == 2 && compressed ? 3 : 0, 2);
            }
            BE(writer, 1, 4);
            BE(writer, 0, 8);
            BE(writer, fileSize, 8);
            BE(writer, 4, 4);
            Z(writer, "CAB-generated");
        }
        byte[] info = compressed ? LiteralLz4(directory.ToArray()) : directory.ToArray();
        using var bundle = new MemoryStream();
        using (var writer = new BinaryWriter(bundle, Encoding.UTF8, leaveOpen: true))
        {
            Z(writer, "UnityFS");
            BE(writer, 7, 4);
            Z(writer, "5.x.x");
            Z(writer, "5.6.2f1");
            long sizePosition = bundle.Position;
            BE(writer, 0, 8);
            BE(writer, info.Length, 4);
            BE(writer, directory.Length, 4);
            BE(writer, 0x40 | 0x200 | (atEnd ? 0x80 : 0) | (compressed ? 2 : 0), 4);
            Align(writer, 16);
            if (!atEnd) writer.Write(info);
            Align(writer, 16);
            foreach (var block in blocks) writer.Write(block);
            if (atEnd) writer.Write(info);
            long length = bundle.Length;
            bundle.Position = sizePosition;
            BE(writer, length, 8);
        }
        return bundle.ToArray();
    }

    static byte[] LiteralLz4(byte[] value)
    {
        using var stream = new MemoryStream();
        stream.WriteByte((byte)(Math.Min(value.Length, 15) << 4));
        if (value.Length >= 15)
        {
            int length = value.Length - 15;
            while (length >= 255) { stream.WriteByte(255); length -= 255; }
            stream.WriteByte((byte)length);
        }
        stream.Write(value);
        return stream.ToArray();
    }

    static void Z(BinaryWriter writer, string value) { writer.Write(Encoding.UTF8.GetBytes(value)); writer.Write((byte)0); }
    static void Align(BinaryWriter writer, int boundary)
    { while (writer.BaseStream.Position % boundary != 0) writer.Write((byte)0); }
    static void BE(BinaryWriter writer, long value, int width)
    {
        Span<byte> bytes = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(bytes, value);
        writer.Write(bytes[(8 - width)..]);
    }
}
