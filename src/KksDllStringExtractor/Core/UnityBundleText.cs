using System.Buffers.Binary;
using System.Text;

namespace KksDllStringExtractor;

// A bounded, read-only UnityFS/TextAsset reader. Only requested metadata and text
// blocks are decompressed; texture payloads are never materialized as a whole.
internal static class UnityBundleText
{
    internal record TextAsset(string Path, byte[] Bytes);
    const long MaxLogicalBytes = 512L * 1024 * 1024;
    static readonly UTF8Encoding Utf8 = new(false, true);

    public static bool IsBundle(ReadOnlySpan<byte> bytes) => bytes.StartsWith("UnityFS\0"u8);

    public static IEnumerable<TextAsset> Read(byte[] bytes, int limit)
    {
        var header = new Cursor(bytes);
        if (header.ZeroString() != "UnityFS") throw new InvalidDataException("Invalid UnityFS signature.");
        int version = header.Int32();
        if (version is < 6 or > 8) throw new NotSupportedException("Unsupported UnityFS version: " + version);
        header.ZeroString();
        header.ZeroString();
        long bundleSize = header.Int64();
        if (bundleSize != bytes.Length) throw new InvalidDataException("UnityFS size does not match resource length.");
        int packedInfoSize = header.Size(limit);
        int infoSize = header.Size(limit);
        uint flags = header.UInt32();
        if ((flags & 0x40) == 0) throw new NotSupportedException("UnityFS separate directory data is unsupported.");
        if (version >= 7) header.Align(16);
        int infoStart = (flags & 0x80) != 0 ? checked(bytes.Length - packedInfoSize) : header.Position;
        if (infoStart < header.Position) throw new InvalidDataException("Invalid UnityFS metadata offset.");
        var info = new Cursor(Decode(Slice(bytes, infoStart, packedInfoSize), infoSize, (int)(flags & 63), limit));
        info.Skip(16);
        int count = info.Count(10);
        var blocks = new List<Block>(count);
        long logicalOffset = 0;
        int packedOffset = (flags & 0x80) != 0 ? header.Position : checked(infoStart + packedInfoSize);
        if ((flags & 0x200) != 0) packedOffset = checked((packedOffset + 15) & ~15);
        int dataEnd = (flags & 0x80) != 0 ? infoStart : bytes.Length;
        for (int i = 0; i < count; i++)
        {
            int size = info.Size(limit);
            int packed = info.Size(limit);
            int mode = info.UInt16() & 63;
            if (size == 0 || packed == 0 || packedOffset > dataEnd - packed || logicalOffset > MaxLogicalBytes - size)
                throw new InvalidDataException("Invalid or oversized UnityFS block.");
            blocks.Add(new Block(logicalOffset, size, packedOffset, packed, mode));
            logicalOffset += size;
            packedOffset += packed;
        }
        var data = new BlockData(bytes, blocks, logicalOffset, limit);
        int files = info.Count(21);
        int textBytes = 0;
        for (int i = 0; i < files; i++)
        {
            long offset = info.Int64();
            long length = info.Int64();
            uint nodeFlags = info.UInt32();
            string name = info.ZeroString();
            if (offset < 0 || length < 0 || offset > logicalOffset - length)
                throw new InvalidDataException("UnityFS node extends beyond the block data.");
            if ((nodeFlags & 4) == 0) continue; // Serialized files only, not .resS texture streams.
            foreach (var text in ReadSerialized(data, offset, length, name, limit))
            {
                textBytes = checked(textBytes + text.Bytes.Length);
                if (textBytes > limit) throw new InvalidDataException("UnityFS text budget exceeded.");
                yield return text;
            }
        }
    }

    static IEnumerable<TextAsset> ReadSerialized(BlockData data, long start, long length, string name, int limit)
    {
        if (length < 20) throw new InvalidDataException("Truncated serialized Unity file.");
        var first = new Cursor(data.Read(start, 20));
        int metadataSize = first.Size(limit);
        long fileSize = first.UInt32();
        int version = first.Int32();
        long dataOffset = first.UInt32();
        byte endian = first.Byte();
        if (version is < 14 or > 21) throw new NotSupportedException("Unsupported Unity serialized-file version: " + version);
        if (fileSize != length || metadataSize > dataOffset - 20 || dataOffset > length || endian > 1)
            throw new InvalidDataException("Invalid Unity serialized-file header.");
        var meta = new Cursor(data.Read(start + 20, metadataSize), little: endian == 0);
        meta.ZeroString(); // Unity version.
        int platform = meta.Int32();
        if (platform == -2) throw new NotSupportedException("Unity editor TextAsset layout is unsupported.");
        bool typeTree = meta.Byte() != 0;
        int typeCount = meta.Count(20);
        var classes = new int[typeCount];
        for (int i = 0; i < typeCount; i++)
        {
            int classId = classes[i] = meta.Int32();
            if (version >= 16) meta.Skip(1);
            if (version >= 17) meta.Skip(2);
            if (version < 16 && classId < 0 || version >= 16 && classId == 114) meta.Skip(16);
            meta.Skip(16);
            if (typeTree)
            {
                int nodes = meta.Count(version >= 19 ? 32 : 24);
                int strings = meta.Size(limit);
                meta.Skip(checked(nodes * (version >= 19 ? 32 : 24)));
                meta.Skip(strings);
                if (version >= 21) meta.Skip(checked(meta.Count(4) * 4));
            }
        }
        int objectCount = meta.Count(20);
        for (int i = 0; i < objectCount; i++)
        {
            meta.Align(4);
            long id = meta.Int64();
            long offset = meta.UInt32() + dataOffset;
            long size = meta.UInt32();
            int type = meta.Int32();
            int classId;
            if (version < 16) classId = meta.UInt16();
            else
            {
                if ((uint)type >= classes.Length) throw new InvalidDataException("Invalid Unity object type index.");
                classId = classes[type];
            }
            if (version < 17) meta.Skip(2);
            if (version is 15 or 16) meta.Skip(1);
            if (offset < dataOffset || size < 0 || offset > length - size)
                throw new InvalidDataException("Unity object extends beyond the serialized file.");
            if (classId != 49) continue; // TextAsset.
            if (size > limit) throw new InvalidDataException("Unity TextAsset too large: " + size);
            var text = new Cursor(data.Read(start + offset, (int)size), little: endian == 0);
            string assetName = Utf8.GetString(text.LengthBytes(limit));
            text.Align(4);
            byte[] contents = text.LengthBytes(limit).ToArray();
            yield return new TextAsset(name + "/" + assetName + "#" + id, contents);
        }
    }

    internal static byte[] Decode(ReadOnlySpan<byte> input, int size, int mode, int limit)
    {
        if (size < 0 || size > limit || input.Length > limit)
            throw new InvalidDataException("UnityFS decompression size limit exceeded.");
        if (mode == 0)
        {
            if (input.Length != size) throw new InvalidDataException("UnityFS uncompressed block size mismatch.");
            return input.ToArray();
        }
        if (mode is not (2 or 3)) throw new NotSupportedException("Unsupported UnityFS compression: " + mode);
        var output = new byte[size];
        int source = 0, destination = 0;
        while (source < input.Length)
        {
            byte token = input[source++];
            int literals = ExtendedLength(input, ref source, token >> 4);
            if (literals > input.Length - source || literals > size - destination)
                throw new InvalidDataException("Invalid LZ4 literal length.");
            input.Slice(source, literals).CopyTo(output.AsSpan(destination));
            source += literals;
            destination += literals;
            if (source == input.Length) break;
            if (source > input.Length - 2) throw new InvalidDataException("Truncated LZ4 match.");
            int distance = BinaryPrimitives.ReadUInt16LittleEndian(input.Slice(source, 2));
            source += 2;
            int match = checked(ExtendedLength(input, ref source, token & 15) + 4);
            if (distance == 0 || distance > destination || match > size - destination)
                throw new InvalidDataException("Invalid LZ4 back-reference.");
            for (int j = 0; j < match; j++) output[destination + j] = output[destination + j - distance];
            destination += match;
        }
        if (destination != size) throw new InvalidDataException("LZ4 decompressed size mismatch.");
        return output;
    }

    static int ExtendedLength(ReadOnlySpan<byte> input, ref int source, int value)
    {
        if (value != 15) return value;
        byte next;
        do
        {
            if (source >= input.Length) throw new InvalidDataException("Truncated LZ4 length.");
            next = input[source++];
            value = checked(value + next);
        } while (next == 255);
        return value;
    }

    static ReadOnlySpan<byte> Slice(byte[] bytes, int start, int size)
    {
        if (start < 0 || size < 0 || start > bytes.Length - size) throw new InvalidDataException("Invalid UnityFS byte range.");
        return bytes.AsSpan(start, size);
    }

    sealed record Block(long Offset, int Size, int PackedOffset, int PackedSize, int Mode);

    sealed class BlockData(byte[] bytes, List<Block> blocks, long length, int limit)
    {
        Block? cached;
        byte[] cache = Array.Empty<byte>();
        long work;
        public byte[] Read(long offset, int size)
        {
            if (offset < 0 || size < 0 || size > limit || offset > length - size)
                throw new InvalidDataException("Invalid UnityFS logical read.");
            var result = new byte[size];
            int copied = 0;
            foreach (var block in blocks)
            {
                if (block.Offset + block.Size <= offset) continue;
                if (copied == size) break;
                if (cached != block)
                {
                    work += block.Size;
                    if (work > MaxLogicalBytes) throw new InvalidDataException("UnityFS decompression work budget exceeded.");
                    cache = Decode(Slice(bytes, block.PackedOffset, block.PackedSize), block.Size, block.Mode, limit);
                    cached = block;
                }
                int within = checked((int)(offset - block.Offset));
                int count = Math.Min(size - copied, block.Size - within);
                cache.AsSpan(within, count).CopyTo(result.AsSpan(copied));
                copied += count;
                offset += count;
            }
            if (copied != size) throw new InvalidDataException("Truncated UnityFS logical read.");
            return result;
        }
    }

    sealed class Cursor(byte[] bytes, bool little = false)
    {
        public int Position { get; private set; }
        public void Skip(int count) { Take(count); }
        public void Align(int boundary) => Skip((boundary - Position % boundary) % boundary);
        ReadOnlySpan<byte> Take(int count)
        {
            var result = Slice(bytes, Position, count);
            Position += count;
            return result;
        }
        public byte Byte() => Take(1)[0];
        public ushort UInt16() => little ? BinaryPrimitives.ReadUInt16LittleEndian(Take(2)) : BinaryPrimitives.ReadUInt16BigEndian(Take(2));
        public uint UInt32() => little ? BinaryPrimitives.ReadUInt32LittleEndian(Take(4)) : BinaryPrimitives.ReadUInt32BigEndian(Take(4));
        public int Int32() => unchecked((int)UInt32());
        public long Int64() => little ? BinaryPrimitives.ReadInt64LittleEndian(Take(8)) : BinaryPrimitives.ReadInt64BigEndian(Take(8));
        public int Size(int limit)
        {
            uint size = UInt32();
            if (size > limit) throw new InvalidDataException("UnityFS size limit exceeded: " + size);
            return (int)size;
        }
        public int Count(int minimumBytes)
        {
            int count = Int32();
            if (count < 0 || count > 100_000 || count > (bytes.Length - Position) / minimumBytes)
                throw new InvalidDataException("Invalid UnityFS table count.");
            return count;
        }
        public string ZeroString()
        {
            int end = Array.IndexOf(bytes, (byte)0, Position);
            if (end < 0 || end - Position > 4096) throw new InvalidDataException("Invalid UnityFS name.");
            string result = Utf8.GetString(Take(end - Position));
            Skip(1);
            return result;
        }
        public ReadOnlySpan<byte> LengthBytes(int limit) => Take(Size(limit));
    }
}
