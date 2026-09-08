using System.ComponentModel;
using System.Runtime.InteropServices;

namespace MachiVerse.Simulation.Core.Persistence;

internal static class DurableFileSystem
{
    private const uint MoveFileReplaceExisting = 0x00000001;
    private const uint MoveFileWriteThrough = 0x00000008;

    public static void AtomicReplaceFile(string sourcePath, string destinationPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        var parent = Path.GetDirectoryName(destinationPath)
            ?? throw new ArgumentException("Destination must have a parent directory.", nameof(destinationPath));

        if (OperatingSystem.IsWindows())
        {
            if (!MoveFileEx(sourcePath, destinationPath, MoveFileReplaceExisting | MoveFileWriteThrough))
                throw new IOException("persistence.atomic-replace-failed", new Win32Exception(Marshal.GetLastPInvokeError()));
            return;
        }

        File.Move(sourcePath, destinationPath, overwrite: true);
        FlushDirectory(parent);
    }

    public static void AtomicMoveDirectory(string sourcePath, string destinationPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        var parent = Path.GetDirectoryName(destinationPath)
            ?? throw new ArgumentException("Destination must have a parent directory.", nameof(destinationPath));
        if (Directory.Exists(destinationPath))
            throw new IOException("persistence.atomic-directory-target-exists");

        if (OperatingSystem.IsWindows())
        {
            if (!MoveFileEx(sourcePath, destinationPath, MoveFileWriteThrough))
                throw new IOException("persistence.atomic-directory-move-failed", new Win32Exception(Marshal.GetLastPInvokeError()));
            return;
        }

        Directory.Move(sourcePath, destinationPath);
        FlushDirectory(parent);
    }

    public static void FlushDirectory(string directoryPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directoryPath);
        if (OperatingSystem.IsWindows())
        {
            // Windows rename callers use MOVEFILE_WRITE_THROUGH. There is no portable managed
            // directory-fsync primitive; do not pretend that a no-op is a durability barrier.
            throw new PlatformNotSupportedException("Direct directory fsync is not used on Windows; use a write-through move primitive.");
        }

        var fd = Open(directoryPath, 0, 0);
        if (fd < 0)
            throw new IOException("persistence.directory-open-failed", new Win32Exception(Marshal.GetLastPInvokeError()));
        try
        {
            if (Fsync(fd) != 0)
                throw new IOException("persistence.directory-fsync-failed", new Win32Exception(Marshal.GetLastPInvokeError()));
        }
        finally
        {
            if (Close(fd) != 0)
                throw new IOException("persistence.directory-close-failed", new Win32Exception(Marshal.GetLastPInvokeError()));
        }
    }

    [DllImport("kernel32.dll", EntryPoint = "MoveFileExW", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool MoveFileEx(string existingFileName, string newFileName, uint flags);

    [DllImport("libc", EntryPoint = "open", SetLastError = true)]
    private static extern int Open([MarshalAs(UnmanagedType.LPUTF8Str)] string pathname, int flags, int mode);

    [DllImport("libc", EntryPoint = "fsync", SetLastError = true)]
    private static extern int Fsync(int fd);

    [DllImport("libc", EntryPoint = "close", SetLastError = true)]
    private static extern int Close(int fd);
}
