using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace TftCompanion.SecondScreen.Recovery;

public sealed class WindowsManualSessionRecoveryFileSystem : IManualSessionRecoveryFileSystem
{
    private const string DVolumeRoot = @"D:\";
    private const string ParentPath = @"D:\AlifeData";
    private const string RootPath = @"D:\AlifeData\TFTCompanion";
    private const string RecoveryPath = @"D:\AlifeData\TFTCompanion\manual-session-v1.json";
    private const string ParentFinalPath = @"\\?\D:\AlifeData";
    private const string RootFinalPath = @"\\?\D:\AlifeData\TFTCompanion";
    private const string RecoveryFinalPath = @"\\?\D:\AlifeData\TFTCompanion\manual-session-v1.json";

    private const uint FileReadData = 0x00000001;
    private const uint FileWriteData = 0x00000002;
    private const uint FileReadAttributes = 0x00000080;
    private const uint FileShareRead = 0x00000001;
    private const uint FileShareWrite = 0x00000002;
    private const uint FileShareDelete = 0x00000004;
    private const uint OpenExisting = 3;
    private const uint FileFlagWriteThrough = 0x80000000;
    private const uint FileFlagBackupSemantics = 0x02000000;
    private const uint FileFlagOpenReparsePoint = 0x00200000;
    private const uint FileAttributeDirectory = 0x00000010;
    private const uint FileAttributeReparsePoint = 0x00000400;
    private const uint VolumeNameDos = 0;
    private const uint VolumeNameGuid = 1;
    private const int MaximumFinalPathCharacters = 32_768;

    public ManualRecoveryFileResult TryReadExisting(int maximumBytes)
    {
        if (maximumBytes <= 0 || maximumBytes > ManualSessionRecoveryContract.MaximumSnapshotBytes)
            return Rejected("RECOVERY_READ_LIMIT_INVALID");

        try
        {
            string expectedVolume = GetCurrentDVolumeGuid();
            using SafeFileHandle fileHandle = OpenValidatedRecoveryFile(
                expectedVolume,
                FileReadData | FileReadAttributes,
                FileFlagBackupSemantics | FileFlagOpenReparsePoint);

            if (!GetFileSizeEx(fileHandle, out long length))
                throw LastWin32Exception();

            if (length < 0)
                return Rejected("RECOVERY_FILE_SIZE_INVALID");

            if (length > maximumBytes)
                return Rejected("RECOVERY_FILE_SIZE_EXCEEDS_LIMIT");

            byte[] bytes = new byte[(int)length];
            using FileStream stream = new(fileHandle, FileAccess.Read, bufferSize: 4096, isAsync: false);
            if (!TryReadExactly(stream, bytes))
                return Degraded("RECOVERY_READ_INCOMPLETE");

            return Available(bytes);
        }
        catch (Exception exception)
        {
            return ClassifyFailure(exception, "RECOVERY_READ_UNAVAILABLE");
        }
    }

    public ManualRecoveryFileResult TryWriteExisting(ReadOnlyMemory<byte> utf8)
    {
        if (utf8.IsEmpty || utf8.Length > ManualSessionRecoveryContract.MaximumSnapshotBytes)
            return Rejected("RECOVERY_WRITE_ARGUMENT_INVALID");

        try
        {
            string expectedVolume = GetCurrentDVolumeGuid();
            using SafeFileHandle fileHandle = OpenValidatedRecoveryFile(
                expectedVolume,
                FileWriteData | FileReadAttributes,
                FileFlagWriteThrough | FileFlagBackupSemantics | FileFlagOpenReparsePoint);

            using FileStream stream = new(fileHandle, FileAccess.Write, bufferSize: 4096, isAsync: false);
            stream.SetLength(0);
            stream.Position = 0;
            stream.Write(utf8.Span);
            stream.Flush(flushToDisk: true);
            return Available();
        }
        catch (Exception exception)
        {
            return ClassifyFailure(exception, "RECOVERY_WRITE_UNAVAILABLE");
        }
    }

    public ManualRecoveryFileResult TryProvisionFixedFile()
    {
        try
        {
            string expectedVolume = GetCurrentDVolumeGuid();
            using SafeFileHandle fileHandle = OpenValidatedRecoveryFile(
                expectedVolume,
                FileReadAttributes,
                FileFlagBackupSemantics | FileFlagOpenReparsePoint);

            return Available();
        }
        catch (Exception exception)
        {
            return ClassifyFailure(exception, "RECOVERY_PROVISION_UNAVAILABLE");
        }
    }

    private static ManualRecoveryFileResult Available(byte[]? utf8Document = null) => new(
        Success: true,
        Status: ManualRecoveryStatus.RecoveryAvailable,
        FailureCode: "NONE",
        Utf8Document: utf8Document);

    private static ManualRecoveryFileResult Degraded(string failureCode) => new(
        Success: false,
        Status: ManualRecoveryStatus.MemoryOnlyDegraded,
        FailureCode: failureCode,
        Utf8Document: null);

    private static ManualRecoveryFileResult Rejected(string failureCode) => new(
        Success: false,
        Status: ManualRecoveryStatus.RecoveryRejected,
        FailureCode: failureCode,
        Utf8Document: null);

    private static ManualRecoveryFileResult ClassifyFailure(Exception exception, string unavailableCode)
    {
        if (exception is RecoveryRejectedException rejected)
            return Rejected(rejected.FailureCode);

        if (exception is Win32Exception win32 && win32.NativeErrorCode is 2 or 3 or 15)
            return Degraded("RECOVERY_NOT_PROVISIONED");

        return Degraded(unavailableCode);
    }

    private static SafeFileHandle OpenValidatedRecoveryFile(string expectedVolume, uint desiredAccess, uint flags)
    {
        using SafeFileHandle parentHandle = OpenExistingDirectory(ParentPath);
        ValidateDirectory(
            parentHandle,
            ParentFinalPath,
            expectedVolume,
            "RECOVERY_PARENT_NOT_DIRECTORY",
            "RECOVERY_PARENT_REPARSE_POINT",
            "RECOVERY_PARENT_PATH_MISMATCH",
            "RECOVERY_PARENT_VOLUME_MISMATCH");

        using SafeFileHandle rootHandle = OpenExistingDirectory(RootPath);
        ValidateDirectory(
            rootHandle,
            RootFinalPath,
            expectedVolume,
            "RECOVERY_ROOT_NOT_DIRECTORY",
            "RECOVERY_ROOT_REPARSE_POINT",
            "RECOVERY_ROOT_PATH_MISMATCH",
            "RECOVERY_ROOT_VOLUME_MISMATCH");

        SafeFileHandle fileHandle = OpenExistingHandle(RecoveryPath, desiredAccess, 0, flags);
        try
        {
            ValidateRecoveryFile(fileHandle, expectedVolume);
            return fileHandle;
        }
        catch
        {
            fileHandle.Dispose();
            throw;
        }
    }

    private static SafeFileHandle OpenExistingDirectory(string path) => OpenExistingHandle(
        path,
        FileReadAttributes,
        FileShareRead | FileShareWrite | FileShareDelete,
        FileFlagBackupSemantics | FileFlagOpenReparsePoint);

    private static SafeFileHandle OpenExistingHandle(string path, uint desiredAccess, uint shareMode, uint flags)
    {
        SafeFileHandle handle = CreateFileW(
            path,
            desiredAccess,
            shareMode,
            IntPtr.Zero,
            OpenExisting,
            flags,
            IntPtr.Zero);

        if (!handle.IsInvalid)
            return handle;

        int error = Marshal.GetLastWin32Error();
        handle.Dispose();
        throw new Win32Exception(error);
    }

    private static void ValidateDirectory(
        SafeFileHandle handle,
        string expectedFinalPath,
        string expectedVolume,
        string notDirectoryCode,
        string reparseCode,
        string pathCode,
        string volumeCode)
    {
        HandleInfo info = Inspect(handle);
        if (!info.IsDirectory)
            throw new RecoveryRejectedException(notDirectoryCode);

        if (info.IsReparsePoint)
            throw new RecoveryRejectedException(reparseCode);

        ValidateFinalPath(info.FinalPath, expectedFinalPath, expectedVolume, pathCode, volumeCode);
    }

    private static void ValidateRecoveryFile(SafeFileHandle handle, string expectedVolume)
    {
        HandleInfo info = Inspect(handle);
        if (info.IsDirectory)
            throw new RecoveryRejectedException("RECOVERY_FILE_IS_DIRECTORY");

        if (info.IsReparsePoint)
            throw new RecoveryRejectedException("RECOVERY_FILE_REPARSE_POINT");

        if (info.NumberOfLinks != 1)
            throw new RecoveryRejectedException("RECOVERY_FILE_LINK_COUNT_INVALID");

        ValidateFinalPath(
            info.FinalPath,
            RecoveryFinalPath,
            expectedVolume,
            "RECOVERY_FILE_PATH_MISMATCH",
            "RECOVERY_FILE_VOLUME_MISMATCH");
    }

    private static void ValidateFinalPath(
        FinalPath finalPath,
        string expectedDosPath,
        string expectedVolume,
        string pathCode,
        string volumeCode)
    {
        if (!string.Equals(finalPath.DosPath, expectedDosPath, StringComparison.OrdinalIgnoreCase))
            throw new RecoveryRejectedException(pathCode);

        if (!string.Equals(finalPath.VolumeGuid, expectedVolume, StringComparison.OrdinalIgnoreCase))
            throw new RecoveryRejectedException(volumeCode);
    }

    private static HandleInfo Inspect(SafeFileHandle handle)
    {
        if (!GetFileInformationByHandle(handle, out ByHandleFileInformation information))
            throw LastWin32Exception();

        return new HandleInfo(
            IsDirectory: (information.FileAttributes & FileAttributeDirectory) != 0,
            IsReparsePoint: (information.FileAttributes & FileAttributeReparsePoint) != 0,
            NumberOfLinks: information.NumberOfLinks,
            FinalPath: ResolveFinalPath(handle));
    }

    private static FinalPath ResolveFinalPath(SafeFileHandle handle) => new(
        ExtractVolumeGuid(ReadFinalPath(handle, VolumeNameGuid)),
        ReadFinalPath(handle, VolumeNameDos));

    private static string ReadFinalPath(SafeFileHandle handle, uint flags)
    {
        StringBuilder buffer = new(260);
        while (true)
        {
            uint length = GetFinalPathNameByHandleW(handle, buffer, (uint)buffer.Capacity, flags);
            if (length == 0)
                throw LastWin32Exception();

            if (length < buffer.Capacity)
                return buffer.ToString();

            if (length >= MaximumFinalPathCharacters)
                throw new RecoveryRejectedException("RECOVERY_FINAL_PATH_INVALID");

            buffer = new StringBuilder(checked((int)length + 1));
        }
    }

    private static string GetCurrentDVolumeGuid()
    {
        StringBuilder buffer = new(64);
        if (!GetVolumeNameForVolumeMountPointW(DVolumeRoot, buffer, (uint)buffer.Capacity))
            throw LastWin32Exception();

        string volumeName = buffer.ToString();
        string volumeGuid = ExtractVolumeGuid(volumeName);
        if (!string.Equals(volumeName, volumeGuid, StringComparison.OrdinalIgnoreCase))
            throw new RecoveryRejectedException("RECOVERY_VOLUME_GUID_INVALID");

        return volumeGuid;
    }

    private static string ExtractVolumeGuid(string path)
    {
        const string Prefix = @"\\?\Volume{";
        if (!path.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
            throw new RecoveryRejectedException("RECOVERY_VOLUME_GUID_INVALID");

        int closeBrace = path.IndexOf("}\\", Prefix.Length, StringComparison.Ordinal);
        if (closeBrace <= Prefix.Length ||
            !Guid.TryParse(path.AsSpan(Prefix.Length, closeBrace - Prefix.Length), out _))
        {
            throw new RecoveryRejectedException("RECOVERY_VOLUME_GUID_INVALID");
        }

        return path[..(closeBrace + 2)];
    }

    private static bool TryReadExactly(FileStream stream, byte[] destination)
    {
        int offset = 0;
        while (offset < destination.Length)
        {
            int read = stream.Read(destination, offset, destination.Length - offset);
            if (read <= 0)
                return false;

            offset += read;
        }

        return true;
    }

    private static Win32Exception LastWin32Exception() => new(Marshal.GetLastWin32Error());

    private sealed record HandleInfo(bool IsDirectory, bool IsReparsePoint, uint NumberOfLinks, FinalPath FinalPath);

    private sealed record FinalPath(string VolumeGuid, string DosPath);

    private sealed class RecoveryRejectedException(string failureCode) : Exception
    {
        public string FailureCode { get; } = failureCode;
    }

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
    private static extern SafeFileHandle CreateFileW(
        string path,
        uint desiredAccess,
        uint shareMode,
        IntPtr security,
        uint disposition,
        uint flags,
        IntPtr reserved);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileSizeEx(SafeFileHandle handle, out long size);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern uint GetFinalPathNameByHandleW(
        SafeFileHandle handle,
        StringBuilder buffer,
        uint capacity,
        uint flags);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(
        SafeFileHandle handle,
        out ByHandleFileInformation information);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetVolumeNameForVolumeMountPointW(
        string rootPath,
        StringBuilder volumeName,
        uint capacity);
}
