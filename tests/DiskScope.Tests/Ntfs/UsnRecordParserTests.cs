using System.Buffers.Binary;
using System.Text;
using DiskScope.Scanner.Ntfs;

namespace DiskScope.Tests.Ntfs;

public sealed class UsnRecordParserTests
{
    [Fact]
    public void V2AndV3Records_AreParsed()
    {
        var buf = new UsnBuffer(nextStart: 0x1234);
        buf.V2(fileRef: 100, parentRef: 5, attrs: 0x10, name: "dir");
        buf.V3(fileRef: (UInt128)ulong.MaxValue + 7, parentRef: 100, attrs: 0x20, name: "fé.txt");
        var store = new UsnRecordStore();

        UsnParseResult r = UsnRecordParser.Parse(buf.ToArray(), store);

        Assert.Equal(new UsnParseResult(0x1234, 2, 0, 0, false), r);
        Assert.Equal((UInt128)100, store.GetFileReference(0));
        Assert.Equal((UInt128)5, store.GetParentReference(0));
        Assert.Equal(FileAttributes.Directory, store.GetAttributes(0));
        Assert.Equal("dir", store.GetName(0).ToString());
        Assert.Equal((UInt128)ulong.MaxValue + 7, store.GetFileReference(1));
        Assert.Equal("fé.txt", store.GetName(1).ToString());
    }

    [Fact]
    public void UnknownMajorVersion_IsSkippedAndCounted_NextRecordStillRead()
    {
        var buf = new UsnBuffer(1);
        buf.V2(1, 5, 0, "a", majorOverride: 4);
        buf.V2(2, 5, 0, "b");
        var store = new UsnRecordStore();

        UsnParseResult r = UsnRecordParser.Parse(buf.ToArray(), store);

        Assert.Equal(1, r.UnsupportedVersion);
        Assert.Equal(1, r.Accepted);
        Assert.Equal("b", store.GetName(0).ToString());
    }

    [Fact]
    public void NameOutsideRecord_IsMalformed_NextRecordStillRead()
    {
        var buf = new UsnBuffer(1);
        buf.V2(1, 5, 0, "a", nameOffsetOverride: 250);
        buf.V2(2, 5, 0, "b");
        var store = new UsnRecordStore();

        UsnParseResult r = UsnRecordParser.Parse(buf.ToArray(), store);

        Assert.Equal(1, r.Malformed);
        Assert.Equal(1, store.Count);
    }

    [Theory]
    [InlineData(0)] // zero length would loop forever
    [InlineData(4)] // shorter than the header
    [InlineData(66)] // not 8-byte aligned
    [InlineData(4096)] // past the buffer end
    public void BadRecordLength_StopsTheParse(int length)
    {
        var buf = new UsnBuffer(1);
        buf.V2(1, 5, 0, "a", lengthOverride: length);
        buf.V2(2, 5, 0, "b");
        var store = new UsnRecordStore();

        UsnParseResult r = UsnRecordParser.Parse(buf.ToArray(), store);

        Assert.True(r.Truncated);
        Assert.Equal(0, store.Count);
    }

    [Fact]
    public void EmptyAndTinyBuffers_DoNotThrow()
    {
        var store = new UsnRecordStore();

        Assert.Equal(default, UsnRecordParser.Parse(ReadOnlySpan<byte>.Empty, store));
        Assert.True(UsnRecordParser.Parse(new byte[3], store).Truncated);
        Assert.Equal(new UsnParseResult(0, 0, 0, 0, false), UsnRecordParser.Parse(new byte[8], store));
    }
}

/// <summary>Builds FSCTL_ENUM_USN_DATA output buffers with the documented layouts.</summary>
internal sealed class UsnBuffer
{
    private readonly MemoryStream _stream = new();

    public UsnBuffer(ulong nextStart)
    {
        Span<byte> b = stackalloc byte[8];
        BinaryPrimitives.WriteUInt64LittleEndian(b, nextStart);
        _stream.Write(b);
    }

    public void V2(ulong fileRef, ulong parentRef, uint attrs, string name,
        ushort majorOverride = 2, int? nameOffsetOverride = null, int? lengthOverride = null)
    {
        byte[] nameBytes = Encoding.Unicode.GetBytes(name);
        int length = Align8(60 + nameBytes.Length);
        var r = new byte[length];
        BinaryPrimitives.WriteUInt32LittleEndian(r, (uint)(lengthOverride ?? length));
        BinaryPrimitives.WriteUInt16LittleEndian(r.AsSpan(4), majorOverride);
        BinaryPrimitives.WriteUInt64LittleEndian(r.AsSpan(8), fileRef);
        BinaryPrimitives.WriteUInt64LittleEndian(r.AsSpan(16), parentRef);
        BinaryPrimitives.WriteUInt32LittleEndian(r.AsSpan(52), attrs);
        BinaryPrimitives.WriteUInt16LittleEndian(r.AsSpan(56), (ushort)nameBytes.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(r.AsSpan(58), (ushort)(nameOffsetOverride ?? 60));
        nameBytes.CopyTo(r, 60);
        _stream.Write(r);
    }

    public void V3(UInt128 fileRef, UInt128 parentRef, uint attrs, string name)
    {
        byte[] nameBytes = Encoding.Unicode.GetBytes(name);
        int length = Align8(76 + nameBytes.Length);
        var r = new byte[length];
        BinaryPrimitives.WriteUInt32LittleEndian(r, (uint)length);
        BinaryPrimitives.WriteUInt16LittleEndian(r.AsSpan(4), 3);
        BinaryPrimitives.WriteUInt128LittleEndian(r.AsSpan(8), fileRef);
        BinaryPrimitives.WriteUInt128LittleEndian(r.AsSpan(24), parentRef);
        BinaryPrimitives.WriteUInt32LittleEndian(r.AsSpan(68), attrs);
        BinaryPrimitives.WriteUInt16LittleEndian(r.AsSpan(72), (ushort)nameBytes.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(r.AsSpan(74), 76);
        nameBytes.CopyTo(r, 76);
        _stream.Write(r);
    }

    public byte[] ToArray() => _stream.ToArray();

    private static int Align8(int n) => (n + 7) & ~7;
}
