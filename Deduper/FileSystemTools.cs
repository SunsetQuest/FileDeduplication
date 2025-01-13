using System.Runtime.InteropServices;

namespace Deduper;

public static class FileSystemTools
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool GetFileInformationByHandle(IntPtr hFile, out BY_HANDLE_FILE_INFORMATION lpFileInformation);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr CreateFile(
        string lpFileName,
        uint dwDesiredAccess,
        uint dwShareMode,
        IntPtr lpSecurityAttributes,
        uint dwCreationDisposition,
        uint dwFlagsAndAttributes,
        IntPtr hTemplateFile);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CreateHardLink(
    string lpFileName,
    string lpExistingFileName,
    nint lpSecurityAttributes = default);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    [StructLayout(LayoutKind.Sequential)]
    private struct BY_HANDLE_FILE_INFORMATION
    {
        public uint FileAttributes;
        public System.Runtime.InteropServices.ComTypes.FILETIME CreationTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastAccessTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWriteTime;
        public uint VolumeSerialNumber;
        public uint FileSizeHigh;
        public uint FileSizeLow;
        public uint NumberOfLinks;
        public uint FileIndexHigh;
        public uint FileIndexLow;
    }

    private const uint GENERIC_READ = 0x80000000;
    private const uint FILE_SHARE_READ = 0x00000001;
    private const uint FILE_SHARE_WRITE = 0x00000002;
    private const uint FILE_SHARE_DELETE = 0x00000004;
    private const uint OPEN_EXISTING = 3;
    private const uint FILE_FLAG_BACKUP_SEMANTICS = 0x02000000;

    private static readonly IntPtr INVALID_HANDLE_VALUE = new IntPtr(-1);

    /// <summary>
    /// A struct that uniquely identifies a file on a volume. 
    /// We combine VolumeSerialNumber, FileIndexHigh, FileIndexLow.
    /// </summary>
    public struct FileIdentity : IEquatable<FileIdentity>
    {
        public uint VolumeSerialNumber;
        public uint FileIndexHigh;
        public uint FileIndexLow;

        public bool Equals(FileIdentity other)
        {
            return VolumeSerialNumber == other.VolumeSerialNumber
                && FileIndexHigh == other.FileIndexHigh
                && FileIndexLow == other.FileIndexLow;
        }

        public override bool Equals(object obj)
        {
            return obj is FileIdentity other && Equals(other);
        }

        public override int GetHashCode()
        {
            // Simple combination
            unchecked
            {
                int hash = (int)VolumeSerialNumber;
                hash = (hash * 397) ^ (int)FileIndexHigh;
                hash = (hash * 397) ^ (int)FileIndexLow;
                return hash;
            }
        }

        public override string ToString()
        {
            return $"{VolumeSerialNumber}-{FileIndexHigh}-{FileIndexLow}";
        }
    }

    /// <summary>
    /// Returns the FileIdentity (Volume + FileIndex) for the given file, or null on error.
    /// </summary>
    public static FileIdentity? GetFileIdentity(string filePath)
    {
        IntPtr hFile = IntPtr.Zero;
        try
        {
            hFile = CreateFile(
                filePath,
                GENERIC_READ,
                FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE,
                IntPtr.Zero,
                OPEN_EXISTING,
                FILE_FLAG_BACKUP_SEMANTICS,
                IntPtr.Zero);

            if (hFile == INVALID_HANDLE_VALUE || hFile == IntPtr.Zero)
            {
                return null;
            }

            if (!GetFileInformationByHandle(hFile, out BY_HANDLE_FILE_INFORMATION info))
            {
                return null;
            }

            return new FileIdentity
            {
                VolumeSerialNumber = info.VolumeSerialNumber,
                FileIndexHigh = info.FileIndexHigh,
                FileIndexLow = info.FileIndexLow,
            };
        }
        catch
        {
            return null;
        }
        finally
        {
            if (hFile != IntPtr.Zero && hFile != INVALID_HANDLE_VALUE)
            {
                CloseHandle(hFile);
            }
        }
    }

    /// <summary>
    /// Determines if a file is part of a hardlink and retrieves the hardlink count.
    /// </summary>
    /// <param name="filePath">The full path to the file.</param>
    /// <param name="hardLinkCount">The count of hardlinks for the file. Returns 0 if an error occurs.</param>
    /// <returns>True if the file is part of a hardlink (hardLinkCount > 1), false otherwise.</returns>
    public static bool IsPartOfHardLink(string filePath, out int hardLinkCount)
    {
        hardLinkCount = 0;
        IntPtr fileHandle = IntPtr.Zero;

        try
        {
            fileHandle = CreateFile(
                filePath,
                GENERIC_READ,
                FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE,
                IntPtr.Zero,
                OPEN_EXISTING,
                FILE_FLAG_BACKUP_SEMANTICS,
                IntPtr.Zero);

            if (fileHandle == IntPtr.Zero || fileHandle == INVALID_HANDLE_VALUE)
            {
                return false;
            }

            if (GetFileInformationByHandle(fileHandle, out BY_HANDLE_FILE_INFORMATION fileInfo))
            {
                hardLinkCount = (int)fileInfo.NumberOfLinks;
                return hardLinkCount > 1;
            }
        }
        catch
        {
            // Handle/log exception if necessary.
            return false;
        }
        finally
        {
            if (fileHandle != IntPtr.Zero && fileHandle != INVALID_HANDLE_VALUE)
            {
                CloseHandle(fileHandle);
            }
        }

        return false;
    }

    /// <summary>
    /// Creates a hard link from a target file to an existing file.
    /// </summary>
    /// <param name="newLinkPath">The full path for the new hard link to be created.</param>
    /// <param name="existingFilePath">The full path of the existing file.</param>
    /// <returns>True if the hard link was created successfully, false otherwise.</returns>
    public static bool CreateHardLink(string newLinkPath, string existingFilePath)
    {
        try
        {
            return CreateHardLink(newLinkPath, existingFilePath, IntPtr.Zero);
        }
        catch
        {
            // Handle/log exception if necessary.
            return false;
        }
    }
}