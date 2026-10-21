namespace DiskScope.Scanner.Ntfs;

/// <summary>
/// Staging area for enumerated USN records, struct-of-arrays with one char pool (no string per record).
/// Records arrive in MFT order, so parents can come after their children; <see cref="UsnTreeBuilder"/> reorders them.
/// File references are kept as 128-bit values so USN_RECORD_V3 (FILE_ID_128) fits without truncation.
/// </summary>
public sealed class UsnRecordStore
{
    private UInt128[] _fileRef = new UInt128[1024];
    private UInt128[] _parentRef = new UInt128[1024];
    private uint[] _attributes = new uint[1024];
    private int[] _nameOffset = new int[1024];
    private int[] _nameLength = new int[1024];
    private char[] _names = new char[16 * 1024];
    private int _nameEnd;

    public int Count { get; private set; }

    public void Add(UInt128 fileReference, UInt128 parentReference, uint attributes, ReadOnlySpan<char> name)
    {
        int i = Count;
        if (i == _fileRef.Length)
        {
            int n = i * 2;
            Array.Resize(ref _fileRef, n);
            Array.Resize(ref _parentRef, n);
            Array.Resize(ref _attributes, n);
            Array.Resize(ref _nameOffset, n);
            Array.Resize(ref _nameLength, n);
        }

        if (_nameEnd + name.Length > _names.Length)
        {
            Array.Resize(ref _names, Math.Max(_nameEnd + name.Length, _names.Length * 2));
        }

        name.CopyTo(_names.AsSpan(_nameEnd));
        _fileRef[i] = fileReference;
        _parentRef[i] = parentReference;
        _attributes[i] = attributes;
        _nameOffset[i] = _nameEnd;
        _nameLength[i] = name.Length;
        _nameEnd += name.Length;
        Count++;
    }

    public UInt128 GetFileReference(int i) => _fileRef[Check(i)];

    public UInt128 GetParentReference(int i) => _parentRef[Check(i)];

    public FileAttributes GetAttributes(int i) => (FileAttributes)_attributes[Check(i)];

    public ReadOnlySpan<char> GetName(int i) => _names.AsSpan(_nameOffset[Check(i)], _nameLength[i]);

    private int Check(int i) => (uint)i < (uint)Count ? i : throw new ArgumentOutOfRangeException(nameof(i));
}
