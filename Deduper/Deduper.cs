

// Ignore Spelling: Deduplicate Deduper

using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace Deduper;

public class Deduper
{
    // For the partial hash, we'll read up to 4k (4096 bytes) from the file
    private const int PARTIAL_HASH_SIZE = 4096;

    /// <summary>
    /// Stores information about a file deduplication action or scan result.
    /// </summary>
    public class DedupResult
    {
        /// <summary>
        /// The file that was examined or processed.
        /// </summary>
        public string FilePath { get; set; } = string.Empty;

        /// <summary>
        /// The group or hash identifier. 
        /// E.g. "1", "2", "3", or partial hash prefix, etc.
        /// </summary>
        public string GroupId { get; set; } = string.Empty;

        /// <summary>
        /// Action taken: "Scanned", "Linked", "Skipped", "Error", etc.
        /// </summary>
        public string Action { get; set; } = "Scanned";

        /// <summary>
        /// Optional error message.
        /// </summary>
        public string? ErrorMessage { get; set; }

        public override string ToString()
        {
            return $"{Action}: {FilePath} (Group: {GroupId})";
        }
    }

    /// <summary>
    /// Options for the dedup process.
    /// </summary>
    public class DedupOptions
    {
        /// <summary>
        /// If true, do not prompt user for each file. Assume user wants to create hard links for duplicates.
        /// </summary>
        public bool ConfirmAll { get; set; } = false;

        /// <summary>
        /// If true, the default, then files that are replaced by a link will be marked as read-only.
        /// </summary>
        public bool DoNotMarkReadOnly { get; set; } = false;

        /// <summary>
        /// If specified, logs all file actions to CSV.
        /// </summary>
        public string? LogFilePath { get; set; }

        /// <summary>
        /// Called for each file before we create a hard link. 
        /// You can override this to ask user for input, etc.
        /// Return true if we should create the link, false otherwise.
        /// </summary>
        public Func<string, bool>? ConfirmCallback { get; set; }

        // Additional options can be added here...
    }

    #region Public API

    /// <summary>
    /// Recursively scans the target directory, finds duplicates, and replaces them with hard links.
    /// Yields a DedupResult for each file encountered or operated on.
    /// </summary>
    /// <param name="targetDirectory">Folder to scan for duplicates.</param>
    /// <param name="options">Dedup options.</param>
    /// <returns>Sequence of DedupResult objects representing progress or outcomes.</returns>
    /// <exception cref="DirectoryNotFoundException"></exception>
    /// <exception cref="UnauthorizedAccessException"></exception>
    /// <exception cref="IOException">For non-NTFS volumes or other IO issues.</exception>
    public static IEnumerable<DedupResult> Deduplicate(string targetDirectory, DedupOptions? options = null)
    {
        options ??= new DedupOptions();

        // 1. Validate directory
        if (!Directory.Exists(targetDirectory))
        {
            yield return new DedupResult
            {
                FilePath = targetDirectory,
                Action = "Error",
                ErrorMessage = "Target directory does not exist."
            };
            yield break;
        }

        // 2. Check if drive is NTFS
        DriveInfo driveInfo = new(Path.GetPathRoot(Path.GetFullPath(targetDirectory)) ?? string.Empty);
        if (!string.Equals(driveInfo.DriveFormat, "NTFS", StringComparison.OrdinalIgnoreCase))
        {
            yield return new DedupResult
            {
                FilePath = targetDirectory,
                Action = "Error",
                ErrorMessage = "Volume is not NTFS."
            };
            yield break;
        }

        // 3. Check permissions (basic check: can we list directory?)
        string? errorMessage = null;
        try
        {
            _ = Directory.EnumerateFileSystemEntries(targetDirectory);
        }
        catch (UnauthorizedAccessException ex)
        {
            errorMessage = $"UnauthorizedAccessException: {ex.Message}";
        }

        if (errorMessage != null)
        {
            yield return new DedupResult
            {
                FilePath = targetDirectory,
                Action = "Error",
                ErrorMessage = errorMessage
            };
            yield break;
        }

        // 4. Dictionary to group potential duplicates by partial-hash
        Dictionary<string, List<string>> partialHashMap = new(StringComparer.Ordinal);

        // This will help us map fullHash -> group ID and group file list
        Dictionary<string, List<string>> fullHashToGroup = new(StringComparer.Ordinal);
        int groupIndex = 1; // We'll assign groups as "1", "2", "3", ...

        // We'll do a recursive enumeration of files
        IEnumerable<string> allFiles = SafeEnumerateFiles(targetDirectory);

        foreach (string file in allFiles)
        {
            // Check if file is read-only and skip if it is
            FileAttributes attributes = File.GetAttributes(file);
            if ((attributes & FileAttributes.ReadOnly) != 0)
            {
                yield return new DedupResult
                {
                    FilePath = file,
                    Action = "Skipped",
                    ErrorMessage = "File is read-only."
                };
                continue;
            }

            // Skip symbolic links to folders (and possibly to files as well).
            if (IsDirectorySymlink(file))
            {
                // We skip symlink directories or reparse points
                yield return new DedupResult
                {
                    FilePath = file,
                    Action = "Skipped",
                    ErrorMessage = "Symbolic link detected - skipping."
                };
                continue;
            }

            // For each file, get partial hash (4k)
            string partialHash = "";
            string? errorMessage2 = null;
            try
            {
                partialHash = GetFilePartialHash(file);
            }
            catch (Exception ex)
            {
                errorMessage2 = $"Could not get partial hash: {ex.Message}";
            }

            if (errorMessage2 != null)
            {
                // If we can't read partial hash, skip
                yield return new DedupResult
                {
                    FilePath = file,
                    Action = "Error",
                    ErrorMessage = errorMessage2
                };
                continue;
            }

            if (!partialHashMap.TryGetValue(partialHash, out List<string>? samePartialHashFiles))
            {
                samePartialHashFiles = [];
                partialHashMap[partialHash] = samePartialHashFiles;
            }

            samePartialHashFiles.Add(file);

            // We'll yield a "Scanned" result for progress
            yield return new DedupResult
            {
                FilePath = file,
                Action = "Scanned",
                GroupId = $"Partial-{partialHash[..Math.Min(8, partialHash.Length)]}"
            };
        }

        // Now we have a partial-hash map grouping files. 
        // Let's refine each group by computing their full hashes:
        foreach (KeyValuePair<string, List<string>> kvp in partialHashMap)
        {
            List<string> candidateFiles = kvp.Value;
            if (candidateFiles.Count < 2)
            {
                // No duplicates with this partial hash
                continue;
            }

            // For each group that has 2 or more files with same partial hash
            // we compute the full hash:
            Dictionary<string, List<string>> fullHashMap = new(StringComparer.Ordinal);
            foreach (string file in candidateFiles)
            {
                string fullHash = string.Empty;
                string? errorMessage3 = null;
                try
                {
                    fullHash = GetFileFullHash(file);
                }
                catch (Exception ex)
                {
                    errorMessage3 = $"Could not compute full hash: {ex.Message}";
                }

                if (errorMessage3 != null)
                {
                    yield return new DedupResult
                    {
                        FilePath = file,
                        Action = "Error",
                        ErrorMessage = errorMessage3
                    };
                    continue;
                }


                if (!fullHashMap.TryGetValue(fullHash, out List<string>? listByFullHash))
                {
                    listByFullHash = [];
                    fullHashMap[fullHash] = listByFullHash;
                }
                listByFullHash.Add(file);
            }

            // Now link duplicates that share the same full hash
            foreach (KeyValuePair<string, List<string>> fullHashEntry in fullHashMap)
            {
                List<string> duplicates = fullHashEntry.Value;
                if (duplicates.Count < 2)
                {
                    continue; // no duplicates here
                }

                // Group ID
                if (!fullHashToGroup.TryGetValue(fullHashEntry.Key, out List<string>? value))
                {
                    value = ([]);
                    fullHashToGroup[fullHashEntry.Key] = value;
                    groupIndex++;
                }
                string groupId = groupIndex.ToString();

                // Sort duplicates in alphabetical order if desired:
                duplicates.Sort(StringComparer.OrdinalIgnoreCase);

                // Master file is the first in the list
                string masterFile = duplicates[0];
                value.Add(masterFile);

                // All subsequent duplicates can be replaced with a hard link
                for (int i = 1; i < duplicates.Count; i++)
                {
                    string dupeFile = duplicates[i];

                    // Ask user if needed
                    bool doReplace = options.ConfirmAll;
                    if (!options.ConfirmAll && options.ConfirmCallback != null)
                    {
                        doReplace = options.ConfirmCallback.Invoke(dupeFile);
                    }

                    if (!doReplace)
                    {
                        yield return new DedupResult
                        {
                            FilePath = dupeFile,
                            Action = "Skipped",
                            GroupId = groupId
                        };
                        continue;
                    }

                    // Delete existing file so we can replace with a hard link
                    if (File.Exists(dupeFile))
                    {
                        string? deleteErrorMesssage = null;
                        try
                        {
                            File.Delete(dupeFile);
                        }
                        catch (Exception ex)
                        {
                            deleteErrorMesssage = $"Failed to delete existing file: {ex.Message}";
                        }
                        if (deleteErrorMesssage != null)
                        {
                            yield return new DedupResult
                            {
                                FilePath = dupeFile,
                                Action = "Error",
                                GroupId = groupId,
                                ErrorMessage = deleteErrorMesssage
                            };
                            continue;
                        }
                    }

                    // Create the replacement hard link
                    bool success = CreateHardLink(dupeFile, masterFile);
                    if (!success)
                    {
                        int err = Marshal.GetLastPInvokeError();
                        yield return new DedupResult
                        {
                            FilePath = dupeFile,
                            Action = "Error",
                            GroupId = groupId,
                            ErrorMessage = $"CreateHardLink failed with code {err}."
                        };
                    }

                    // Mark the file as read-only
                    if (!options.DoNotMarkReadOnly)
                    {
                        File.SetAttributes(dupeFile, File.GetAttributes(dupeFile) | FileAttributes.ReadOnly);
                    }

                    // Done, yield the result
                    yield return new DedupResult
                    {
                        FilePath = dupeFile,
                        Action = "Linked",
                        GroupId = groupId
                    };
                }
            }
        }
    }

    #endregion

    #region Helpers

    /// <summary>
    /// Enumerates files safely, skipping directories that might cause exceptions.
    /// </summary>
    private static IEnumerable<string> SafeEnumerateFiles(string rootDirectory)
    {
        // BFS or DFS enumerations can work; here's a simple queue-based BFS:
        Queue<string> dirs = new();
        dirs.Enqueue(rootDirectory);

        while (dirs.Count > 0)
        {
            string currentDir = dirs.Dequeue();
            string[] files = [];
            try
            {
                // Add subdirectories
                foreach (string d in Directory.GetDirectories(currentDir))
                {
                    // Check if it's a symlink to skip
                    if (IsDirectorySymlink(d))
                    {
                        continue;
                    }
                    dirs.Enqueue(d);
                }

                // Enumerate files
                files = Directory.GetFiles(currentDir);
            }
            catch
            {
                // Skipping directories that we can't read
            }

            foreach (string file in files)
            {
                yield return file;
            }
        }
    }

    /// <summary>
    /// Checks if the specified path is a reparse point or symlink (for directories).
    /// </summary>
    private static bool IsDirectorySymlink(string path)
    {
        try
        {
            FileAttributes attr = File.GetAttributes(path);
            // If ReparsePoint is set, it might be a symlink or junction. 
            return (attr & FileAttributes.ReparsePoint) != 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Returns the partial hash (SHA256) of the first PARTIAL_HASH_SIZE bytes of a file.
    /// </summary>
    private static string GetFilePartialHash(string filePath)
    {
        using FileStream fs = new(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        byte[] buffer = new byte[PARTIAL_HASH_SIZE];

        int readBytes = fs.Read(buffer, 0, buffer.Length);
        byte[] actualBytes = buffer;
        if (readBytes < PARTIAL_HASH_SIZE)
        {
            // If file is under 4k, we only read readBytes
            actualBytes = new byte[readBytes];
            Array.Copy(buffer, actualBytes, readBytes);
        }

        byte[] hash = SHA256.HashData(actualBytes);
        return BitConverter.ToString(hash).Replace("-", "");
    }

    /// <summary>
    /// Returns the full SHA256 hash of the file.
    /// </summary>
    private static string GetFileFullHash(string filePath)
    {
        using FileStream fs = new(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        using SHA256 sha = SHA256.Create();
        byte[] hash = sha.ComputeHash(fs);
        return BitConverter.ToString(hash).Replace("-", "");
    }

    /// <summary>
    /// Creates a hard link from linkPath to existingFileName.
    /// Returns false if it fails.
    /// </summary>
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CreateHardLink(
        string lpFileName,
        string lpExistingFileName,
        nint lpSecurityAttributes = default);

    #endregion
}
