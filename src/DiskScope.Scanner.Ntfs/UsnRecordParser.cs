using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace DiskScope.Scanner.Ntfs;

/// <summary>Counters of one parsed FSCTL_ENUM_USN_DATA output buffer.</summary>
public readonly record struct UsnParseResult(ulong NextStartReference, int Accepted, int UnsupportedVersion, int Malformed, bool Truncated);

/// <summary>
/// Parses the output buffer of FSCTL_ENUM_USN_DATA: an 8-byte next start file reference, then
/// USN_RECORD_V2 or USN_RECORD_V3 records aligned on 64-bit boundaries.
/// Layouts from the Windows SDK documentation (winioctl.h), checked 2026-10-21:
///   common header: RecordLength u32 @0, MajorVersion u16 @4, MinorVersion u16 @6;
///   V2: FileReferenceNumber u64 @8, Parent u64 @16, FileAttributes u32 @52, FileNameLength u16 @56, FileNameOffset u16 @58;
///   V3: FileReferenceNumber FILE_ID_128 @8, Parent FILE_ID_128 @24, FileAttributes u32 @68, FileNameLength u16 @72, FileNameOffset u16 @74.
/// The name is located through FileNameOffset, never a fixed offset, as the docs require for minor-version growth.
/// Never trusts the buffer: a record whose length leaves the buffer stops the parse (Truncated);
/// a record whose name leaves the record is counted Malformed and skipped; other major versions are skipped and counted.
/// </summary>
public static class UsnRecordParser
{
    private const int HeaderSize = 8;
    // Offset of the FileName field: the smallest valid FileNameOffset and the smallest record carrying the fixed fields.
    private const int V2MinLength = 60;
    private const int V3MinLength = 76;

    public static UsnParseResult Parse(ReadOnlySpan<byte> buffer, UsnRecordStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        if (buffer.Length < sizeof(ulong))
        {
            return new UsnParseResult(0, 0, 0, 0, Truncated: buffer.Length > 0);
        }

        ulong next = BinaryPrimitives.ReadUInt64LittleEndian(buffer);
        int accepted = 0, unsupported = 0, malformed = 0;
        int offset = sizeof(ulong);

        while (offset < buffer.Length)
        {
            ReadOnlySpan<byte> rest = buffer[offset..];
            if (rest.Length < HeaderSize)
            {
                return new UsnParseResult(next, accepted, unsupported, malformed, Truncated: true);
            }

            uint length = BinaryPrimitives.ReadUInt32LittleEndian(rest);
            if (length < HeaderSize || length > rest.Length || (length & 7) != 0)
            {
                // Without a trustworthy length the next record cannot be located: stop here.
                return new UsnParseResult(next, accepted, unsupported, malformed, Truncated: true);
            }

            ReadOnlySpan<byte> record = rest[..(int)length];
            ushort major = BinaryPrimitives.ReadUInt16LittleEndian(record[4..]);
            switch (major)
            {
                case 2 when length >= V2MinLength:
                    if (TryAdd(record, store,
                            BinaryPrimitives.ReadUInt64LittleEndian(record[8..]),
                            BinaryPrimitives.ReadUInt64LittleEndian(record[16..]),
                            attrAt: 52, nameLengthAt: 56, nameOffsetAt: 58, minLength: V2MinLength))
                    {
                        accepted++;
                    }
                    else
                    {
                        malformed++;
                    }

                    break;
                case 3 when length >= V3MinLength:
                    if (TryAdd(record, store,
                            BinaryPrimitives.ReadUInt128LittleEndian(record[8..]),
                            BinaryPrimitives.ReadUInt128LittleEndian(record[24..]),
                            attrAt: 68, nameLengthAt: 72, nameOffsetAt: 74, minLength: V3MinLength))
                    {
                        accepted++;
                    }
                    else
                    {
                        malformed++;
                    }

                    break;
                case 2 or 3:
                    malformed++;
                    break;
                default:
                    unsupported++;
                    break;
            }

            offset += (int)length;
        }

        return new UsnParseResult(next, accepted, unsupported, malformed, Truncated: false);
    }

    private static bool TryAdd(
        ReadOnlySpan<byte> record,
        UsnRecordStore store,
        UInt128 fileRef,
        UInt128 parentRef,
        int attrAt,
        int nameLengthAt,
        int nameOffsetAt,
        int minLength)
    {
        uint attrs = BinaryPrimitives.ReadUInt32LittleEndian(record[attrAt..]);
        int nameBytes = BinaryPrimitives.ReadUInt16LittleEndian(record[nameLengthAt..]);
        int nameOffset = BinaryPrimitives.ReadUInt16LittleEndian(record[nameOffsetAt..]);

        // The name must sit after the fixed fields and inside the record; zero-length or odd byte counts are invalid.
        if (nameBytes == 0 || (nameBytes & 1) != 0 || nameOffset < minLength || nameOffset + nameBytes > record.Length)
        {
            return false;
        }

        ReadOnlySpan<char> name = MemoryMarshal.Cast<byte, char>(record.Slice(nameOffset, nameBytes));
        store.Add(fileRef, parentRef, attrs, name);
        return true;
    }
}
