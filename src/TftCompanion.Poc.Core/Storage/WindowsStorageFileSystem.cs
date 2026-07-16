using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace TftCompanion.Poc.Core.Storage;

/// <summary>
/// The Host only writes a pre-provisioned status file under the approved D:
/// root. It never creates directories or files at runtime.
/// </summary>
public sealed class WindowsStorageFileSystem : IStorageFileSystem
{
    private const string DVolumeRoot = @"D:\";
    private const string ParentPath = @"D:\AlifeData";
    private const string RootPath = @"D:\AlifeData\TFTCompanion";
    private const string StatusPath = @"D:\AlifeData\TFTCompanion\poc-status.json";
    private const string ParentFinalPath = @"\\?\D:\AlifeData";
    private const string RootFinalPath = @"\\?\D:\AlifeData\TFTCompanion";
    private const string StatusFinalPath = @"\\?\D:\AlifeData\TFTCompanion\poc-status.json";

    private const uint FileReadAttributes = 0x00000080;
    private const uint FileWriteData = 0x00000002;
    private const uint FileShareRead = 0x00000001;
    private const uint FileShareWrite = 0x00000002;
    private const uint FileShareDelete = 0x00000004;
    private const uint OpenExisting = 3;
    private const uint FileFlagWriteThrough = 0x80000000;
    private const uint FileFlagBackupSemantics = 0x02000000;
    private const uint FileFlagOpenReparsePoint = 0x00200000;
    private const uint FileAttributeDirectory = 0x00000010;
    private const uint FileAttributeReparsePoint = 0x00000400;
    private const int VolumeNameDos = 0;
    private const int VolumeNameGuid = 1;

    public StorageRootProbe ProbeCanonicalRoot()
    {
        string expectedVolume;
        try
        {
            expectedVolume = GetVolumeGuid(DVolumeRoot);
        }
        catch (Exception)
        {
            return FailProbe(StorageHealth.MemoryOnlyDegraded, "D_ROOT_UNAVAILABLE");
        }

        try
        {
            using SafeFileHandle parentHandle = OpenExistingDirectory(ParentPath);
            HandleInfo parent = Inspect(parentHandle);
            if (!IsApprovedDirectory(parent, ParentFinalPath, expectedVolume, out string parentCode))
                return FailProbe(StorageHealth.IntegrityDegraded, parentCode, parent.FinalPath);

            using SafeFileHandle rootHandle = OpenExistingDirectory(RootPath);
            HandleInfo root = Inspect(rootHandle);
            if (!IsApprovedDirectory(root, RootFinalPath, expectedVolume, out string rootCode))
                return FailProbe(StorageHealth.IntegrityDegraded, rootCode, root.FinalPath);

            return new StorageRootProbe(true, root.FinalPath, StorageHealth.Available, "NONE");
        }
        catch (Win32Exception exception) when (exception.NativeErrorCode is 2 or 3)
        {
            return FailProbe(StorageHealth.MemoryOnlyDegraded, "ROOT_NOT_PROVISIONED");
        }
        catch (Exception)
        {
            return FailProbe(StorageHealth.IntegrityDegraded, "ROOT_VALIDATION_FAILED");
        }
    }

    public StorageWriteResult TryWritePocStatus(ReadOnlyMemory<byte> utf8)
    {
        StorageRootProbe root = ProbeCanonicalRoot();
        if (!root.Success)
            return new StorageWriteResult(false, root.FailureHealth, root.FailureCode);

        SafeFileHandle handle;
        try
        {
            handle = OpenExistingStatusFile(StatusPath);
        }
        catch (Win32Exception exception) when (exception.NativeErrorCode is 2 or 3)
        {
            return new StorageWriteResult(false, StorageHealth.MemoryOnlyDegraded, "STATUS_NOT_PROVISIONED");
        }
        catch (Exception)
        {
            return new StorageWriteResult(false, StorageHealth.PersistUnavailable, "STATUS_OPEN_DENIED");
        }

        using (handle)
        {
            HandleInfo status;
            try
            {
                status = Inspect(handle);
            }
            catch (Exception)
            {
                return new StorageWriteResult(false, StorageHealth.IntegrityDegraded, "STATUS_HANDLE_UNVERIFIABLE");
            }

            if (!IsApprovedStatusFile(status, root.ResolvedRoot!.VolumeGuid, out string statusCode))
                return new StorageWriteResult(false, StorageHealth.IntegrityDegraded, statusCode);

            try
            {
                using FileStream stream = new(handle, FileAccess.Write);
                stream.SetLength(0);
                stream.Write(utf8.Span);
                stream.Flush(flushToDisk: true);
                return new StorageWriteResult(true, StorageHealth.Available, "NONE");
            }
            catch (Exception)
            {
                return new StorageWriteResult(false, StorageHealth.PersistUnavailable, "STATUS_WRITE_DENIED");
            }
        }
    }

    private static StorageRootProbe FailProbe(StorageHealth health, string code, FinalPath? path = null) =>
        new(false, path, health, code);

    private static SafeFileHandle OpenExistingDirectory(string path) => OpenExistingHandle(
        path,
        FileReadAttributes,
        FileShareRead | FileShareWrite | FileShareDelete,
        FileFlagBackupSemantics | FileFlagOpenReparsePoint);

    private static SafeFileHandle OpenExistingStatusFile(string path) => OpenExistingHandle(
        path,
        FileReadAttributes | FileWriteData,
        0,
        FileFlagWriteThrough | FileFlagOpenReparsePoint);

    private static SafeFileHandle OpenExistingHandle(string path, uint access, uint share, uint flags)
    {
        SafeFileHandle handle = CreateFileW(path, access, share, IntPtr.Zero, OpenExisting, flags, IntPtr.Zero);
        if (handle.IsInvalid)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "CreateFileW failed");
        return handle;
    }

    private static HandleInfo Inspect(SafeFileHandle handle)
    {
        if (!GetFileInformationByHandle(handle, out ByHandleFileInformation info))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "GetFileInformationByHandle failed");

        return new HandleInfo(
            IsDirectory: (info.FileAttributes & FileAttributeDirectory) != 0,
            IsReparsePoint: (info.FileAttributes & FileAttributeReparsePoint) != 0,
            NumberOfLinks: info.NumberOfLinks,
            FinalPath: ResolveFinalPath(handle));
    }

    private static bool IsApprovedDirectory(HandleInfo value, string expectedPath, string expectedVolume, out string code)
    {
        if (!value.IsDirectory)
        {
            code = "SEGMENT_NOT_DIRECTORY";
            return false;
        }

        if (value.IsReparsePoint)
        {
            code = "SEGMENT_IS_REPARSE_POINT";
            return false;
        }

        if (!value.FinalPath.CanonicalPath.Equals(expectedPath, StringComparison.OrdinalIgnoreCase))
        {
            code = "ROOT_OUTSIDE_D";
            return false;
        }

        if (!value.FinalPath.VolumeGuid.Equals(expectedVolume, StringComparison.OrdinalIgnoreCase))
        {
            code = "VOLUME_MISMATCH";
            return false;
        }

        code = "NONE";
        return true;
    }

    private static bool IsApprovedStatusFile(HandleInfo value, string expectedVolume, out string code)
    {
        if (value.IsDirectory)
        {
            code = "STATUS_IS_DIRECTORY";
            return false;
        }

        if (value.IsReparsePoint)
        {
            code = "FILE_IS_REPARSE_POINT";
            return false;
        }

        if (value.NumberOfLinks != 1)
        {
            code = "FILE_LINK_COUNT_INVALID";
            return false;
        }

        if (!value.FinalPath.CanonicalPath.Equals(StatusFinalPath, StringComparison.OrdinalIgnoreCase))
        {
            code = "FILE_PATH_ESCAPE";
            return false;
        }

        if (!value.FinalPath.VolumeGuid.Equals(expectedVolume, StringComparison.OrdinalIgnoreCase))
        {
            code = "FILE_VOLUME_MISMATCH";
            return false;
        }

        code = "NONE";
        return true;
    }

    private static FinalPath ResolveFinalPath(SafeFileHandle handle) => new(
        ExtractVolumeGuid(ReadFinalPath(handle, VolumeNameGuid)),
        ReadFinalPath(handle, VolumeNameDos));

    private static string ReadFinalPath(SafeFileHandle handle, int flags)
    {
        StringBuilder buffer = new(260);
        while (true)
        {
            uint length = GetFinalPathNameByHandleW(handle, buffer, buffer.Capacity, flags);
            if (length == 0)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "GetFinalPathNameByHandleW failed");
            if (length < buffer.Capacity)
                return buffer.ToString();
            buffer = new StringBuilder(checked((int)length + 1));
        }
    }

    private static string ExtractVolumeGuid(string path)
    {
        const string prefix = @"\\?\Volume{";
        int end = path.IndexOf("}\\", prefix.Length, StringComparison.Ordinal);
        if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || end < prefix.Length)
            throw new InvalidOperationException("Invalid volume GUID final path.");
        return path[..(end + 2)];
    }

    private static string GetVolumeGuid(string root)
    {
        StringBuilder buffer = new(64);
        if (!GetVolumeNameForVolumeMountPointW(root, buffer, buffer.Capacity))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "GetVolumeNameForVolumeMountPointW failed");
        return buffer.ToString();
    }

    private sealed record HandleInfo(bool IsDirectory, bool IsReparsePoint, uint NumberOfLinks, FinalPath FinalPath);

    [StructLayout(LayoutKind.Sequential)]
    private struct ByHandleFileInformation
    {
        public uint FileAttributes;
        public uint CreationTimeLow;
        public uint CreationTimeHigh;
        public uint LastAccessTimeLow;
        public uint LastAccessTimeHigh;
        public uint LastWriteTimeLow;
        public uint LastWriteTimeHigh;
        public uint VolumeSerialNumber;
        public uint FileSizeHigh;
        public uint FileSizeLow;
        public uint NumberOfLinks;
        public uint FileIndexHigh;
        public uint FileIndexLow;
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern SafeFileHandle CreateFileW(string path, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern uint GetFinalPathNameByHandleW(SafeFileHandle handle, StringBuilder buffer, int capacity, int flags);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle handle, out ByHandleFileInformation information);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetVolumeNameForVolumeMountPointW(string root, StringBuilder buffer, int capacity);
}
