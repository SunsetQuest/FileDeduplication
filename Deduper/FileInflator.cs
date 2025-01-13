// Ignore Spelling: Inflator

using Deduper;
using System.Runtime.InteropServices;
using static Deduper.FileSystemTools;

namespace Inflater
{
    public class FileInflator
    {
        public class InflateResult
        {
            public string FilePath { get; set; } = string.Empty;
            public string Action { get; set; } = "Scanned"; // "Scanned", "Inflated", "Skipped", "Error"
            public string GroupId { get; set; } = string.Empty;
            public string ErrorMessage { get; set; }
        }

        public class InflateOptions
        {
            /// <summary>
            /// If true, do not prompt user for each file. Assume user wants to inflate all linked files.
            /// </summary>
            public bool ConfirmAll { get; set; } = false;

            /// <summary>
            /// Called for each file before inflating. 
            /// Return true if we should break link, false otherwise.
            /// </summary>
            public Func<string, bool> ConfirmCallback { get; set; }
        }

        #region Public API

        /// <summary>
        /// Recursively scans the target directory, finds hard-linked groups, and "inflates" them 
        /// (breaks links) so that each file becomes a unique copy again.
        /// </summary>
        public static IEnumerable<InflateResult> InflateDirectory(string targetDirectory, InflateOptions options = null)
        {
            options ??= new InflateOptions();

            // 1. Validate directory
            if (!Directory.Exists(targetDirectory))
            {
                yield return new InflateResult
                {
                    FilePath = targetDirectory,
                    Action = "Error",
                    ErrorMessage = "Target directory does not exist."
                };
                yield break;
            }

            // 2. Collect file info
            Dictionary<FileIdentity, List<string>> identityMap = new();
            foreach (string file in SafeEnumerateFiles(targetDirectory))
            {
                // We'll yield a "Scanned" for progress
                yield return new InflateResult
                {
                    FilePath = file,
                    Action = "Scanned"
                };

                // Attempt to get file identity
                FileIdentity? fid = FileSystemTools.GetFileIdentity(file);
                if (fid == null)
                {
                    // Some error or no identity, skip
                    continue;
                }

                if (!identityMap.TryGetValue(fid.Value, out List<string> list))
                {
                    list = new List<string>();
                    identityMap[fid.Value] = list;
                }
                list.Add(file);
            }

            // 3. For each identity that has more than 1 file, we attempt to break them up
            int groupIndex = 1;
            foreach (var kvp in identityMap)
            {
                List<string> groupFiles = kvp.Value;
                if (groupFiles.Count < 2)
                {
                    continue; // not actually a shared link
                }

                // Sort so we have a stable “master” file
                groupFiles.Sort(StringComparer.OrdinalIgnoreCase);
                string master = groupFiles[0];
                string groupId = groupIndex.ToString();
                groupIndex++;

                // The rest of the files in the group are duplicates / hard links
                for (int i = 1; i < groupFiles.Count; i++)
                {
                    string dupe = groupFiles[i];

                    // Confirm with user (unless confirmAll is set)
                    bool doInflate = options.ConfirmAll;
                    if (!options.ConfirmAll && options.ConfirmCallback != null)
                    {
                        doInflate = options.ConfirmCallback.Invoke(dupe);
                    }

                    if (!doInflate)
                    {
                        yield return new InflateResult
                        {
                            FilePath = dupe,
                            Action = "Skipped",
                            GroupId = groupId
                        };
                        continue;
                    }

                    // Attempt to break link: 
                    // 1) remove read-only
                    string errorMessage = null;
                    try
                    {
                        FileAttributes atts = File.GetAttributes(dupe);
                        if ((atts & FileAttributes.ReadOnly) != 0)
                        {
                            atts &= ~FileAttributes.ReadOnly;
                            File.SetAttributes(dupe, atts);
                        }
                    }
                    catch (Exception ex)
                    {
                        errorMessage = "Failed to remove read-only: " + ex.Message;
                    }
                    if (errorMessage != null)
                    {
                        yield return new InflateResult
                        {
                            FilePath = dupe,
                            Action = "Error",
                            GroupId = groupId,
                            ErrorMessage = errorMessage
                        };
                        continue;
                    }

                    // 2) delete the dupe
                    errorMessage = null;
                    try
                    {
                        File.Delete(dupe);
                    }
                    catch (Exception ex)
                    {
                        errorMessage = "Failed to delete file: " + ex.Message;
                    }
                    if (errorMessage != null)
                    {
                        yield return new InflateResult
                        {
                            FilePath = dupe,
                            Action = "Error",
                            GroupId = groupId,
                            ErrorMessage = errorMessage
                        };
                        continue;
                    }

                    // 3) copy from master to dupe
                    errorMessage = null;
                    try
                    {
                        File.Copy(master, dupe, overwrite: false);
                    }
                    catch (Exception ex)
                    {
                        errorMessage = "Failed to copy from master: " + ex.Message;
                    }
                    if ( errorMessage != null)
                    {
                        yield return new InflateResult
                        {
                            FilePath = dupe,
                            Action = "Error",
                            GroupId = groupId,
                            ErrorMessage = errorMessage
                        };
                        continue;
                    }

                    yield return new InflateResult
                    {
                        FilePath = dupe,
                        Action = "Inflated",
                        GroupId = groupId
                    };
                }
            }
        }

        #endregion

        #region Helpers

        /// <summary>
        /// Simple BFS enumerator that recursively lists all files, skipping any errors.
        /// </summary>
        private static IEnumerable<string> SafeEnumerateFiles(string rootDirectory)
        {
            Queue<string> dirs = new();
            dirs.Enqueue(rootDirectory);

            while (dirs.Count > 0)
            {
                string currentDir = dirs.Dequeue();
                string[] files = Array.Empty<string>();
                try
                {
                    foreach (string d in Directory.GetDirectories(currentDir))
                    {
                        // We could skip symlinks, etc. if desired
                        dirs.Enqueue(d);
                    }
                    files = Directory.GetFiles(currentDir);
                }
                catch
                {
                    // ignore
                }

                foreach (string file in files)
                {
                    yield return file;
                }
            }
        }

        #endregion
    }
}
