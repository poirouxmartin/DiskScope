# NTFS engine risks

Scope: the planned NTFS engine (`DiskScope.Scanner.Ntfs`). Standard engine behaviour and counting rules: see `docs/ARCHITECTURE.md`.

## 1. Access

`FSCTL_ENUM_USN_DATA` through `DeviceIoControl` needs a handle on the volume (`\\.\C:`). Opening that handle requires administrator rights. The dev process was measured non-elevated on 2026-10-19, so the handle open fails there.

The NTFS engine must report itself unavailable (`CheckAvailability` false, with the reason). The orchestrator then falls back to the standard engine. No kernel driver.

## 2. Input and output

Input: `MFT_ENUM_DATA_V0` (StartFileReferenceNumber, LowUsn, HighUsn), or `MFT_ENUM_DATA_V1` (adds MinMajorVersion and MaxMajorVersion). V1 is needed for `USN_RECORD_V3` with 128-bit `FILE_ID_128`, which ReFS uses.

Output buffer: the first 8 bytes are the next start reference, followed by `USN_RECORD_V2` or `USN_RECORD_V3` records.

Check `MajorVersion` of every record. Reject unknown versions. Verify struct offsets against the Windows SDK headers before use (to verify in prototype).

## 3. What USN records give and do not give

Given: FileReferenceNumber, ParentFileReferenceNumber, FileAttributes, one FileName.

Not given: logical size, allocated size, reliable timestamps (TimeStamp is the USN change time, not last write), the other names of hard links, alternate data streams.

Enumeration alone cannot build a sized tree.

## 4. Options for sizes

(a) `OpenFileById` per file: one handle per file. Probably slower than the standard engine (to measure).

(b) Per-directory `GetFileInformationByHandleEx(FileIdBothDirectoryInfo)`: this is already the standard engine. Not a new option.

(c) Read raw MFT FILE records (`FSCTL_GET_NTFS_FILE_RECORD` per record, or read the `$MFT` data runs through the volume handle) and parse them:
- update sequence fixups;
- `$STANDARD_INFORMATION`;
- `$FILE_NAME`, several per file (DOS, Win32, POSIX namespaces, hard links);
- `$DATA` resident vs non-resident (allocated size from the non-resident header);
- `$ATTRIBUTE_LIST` for records spread over several FILE records;
- named streams.

This is how WizTree-class tools get their speed. The on-disk format is not fully documented by Microsoft. Community reference: linux-ntfs documentation. This is the highest risk option. It needs tests on corrupted and inconsistent records.

## 5. Tree reconstruction

FRN = 48-bit record number + 16-bit sequence number. Map FRN to an internal id with a hash map. The root directory is record 5.

Parents can arrive after their children. Reorder breadth-first from the root to restore the parent < child invariant of `NodeIndexBuilder`.

Orphans (missing parent, or sequence mismatch after record reuse) go under a synthetic `<orphans>` node and are reported. They are never dropped.

The snapshot is not atomic: the volume changes during the scan. Record this in the scan result, not as an error.

## 6. Hard links

NTFS engine: count once per file reference (decision to confirm).
Standard engine: count once per path (current rule in `docs/ARCHITECTURE.md`).

`compare` must explain the delta between the two engines on any dataset with hard links.

## 7. Unsupported or unverified

- FAT and exFAT: not supported.
- Network shares: not supported.
- ReFS: not supported until V3 records are handled.
- BitLocker-locked volumes: unverified.
- Read-only mounted VHDs: unverified (to verify).

## 8. Prototype plan

1. Elevated console probe enumerating USN records on a small NTFS VHDX (`New-VHD` / `diskpart`, administrator).
2. Compare record count with the standard engine on the same tree.
3. Enumeration time on `C:\`.
4. Cost of option (a) and option (c).
5. Go / no-go decision.

Each step adds a row to `docs/benchmarks/RESULTS.md`, produced by `docs/BENCHMARK-PROTOCOL.md`.

## 9. Fallback guarantee

Any NTFS engine error gives `Outcome = Failed` with a message. The UI offers a standard scan. Partial NTFS data is never mixed with standard data in the same index.
