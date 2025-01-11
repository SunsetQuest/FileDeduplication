using System.Text;
using static Deduper.Deduper;

namespace FileDeduplicationCommandLine;

internal class Program
{
    private static void Main(string[] args)
    {
        args = [
            "S:\\Test",
            //   ,"-confirm",
            "-log", "S:\\FileDedup.csv"
            ];

        if (args.Length == 0 || args.Contains("-help", StringComparer.OrdinalIgnoreCase))
        {
            ShowHelp();
            return;
        }

        string targetDirectory = args[0];
        bool confirmAll = args.Contains("-confirm", StringComparer.OrdinalIgnoreCase);
        bool doNotMarkReadOnly = args.Contains("-DoNotMarkReadOnly", StringComparer.OrdinalIgnoreCase);
        string? logFile = null;

        // If user specified "-Log someFilePath"
        // let's parse that
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i].Equals("-log", StringComparison.OrdinalIgnoreCase) && (i + 1 < args.Length))
            {
                logFile = args[i + 1];
            }
        }

        List<Deduper.Deduper.DedupResult> results = [];

        // We'll keep a few counters for summary
        int filesScanned = 0;
        int linksCreated = 0;
        long spaceSavedFromNewLinks = 0;
        int linksAlreadyExist = 0;
        long spaceSavedFromExistingLinks = 0;

        // We might want to store all file sizes in a dictionary for summations:
        Dictionary<string, long> fileSizes = new(StringComparer.OrdinalIgnoreCase);

        // We'll set up the confirm callback if needed:
        bool replaceAll = false; // once user picks (A), we won't ask again
        bool skipAll = false;    // If we had an option to skip all duplicates, you could set this.

        DedupOptions options = new()
        {
            ConfirmAll = confirmAll,
            DoNotMarkReadOnly = doNotMarkReadOnly,
            LogFilePath = logFile,
            ConfirmCallback = (filePath) =>
            {
                if (replaceAll)
                {
                    return true;
                }

                if (skipAll)
                {
                    return false;
                }

                Console.WriteLine();
                Console.Write($"Duplicate found: {filePath}\n(C)reate Link  (S)kip  (A)ll? ");

                // We do a quick console read of a single key
                ConsoleKeyInfo key = Console.ReadKey(intercept: true);
                char c = char.ToUpperInvariant(key.KeyChar);
                Console.WriteLine(c);

                if (c == 'C')
                {
                    return true;
                }
                else if (c == 'S')
                {
                    return false;
                }
                else if (c == 'A')
                {
                    replaceAll = true;
                    return true;
                }
                else
                {
                    // Default skip
                    return false;
                }
            }
        };

        // We’ll accumulate actions in memory so we can log at the end if needed.
        Console.WriteLine($"""Scanning "{targetDirectory}" for duplicates...""");
        Console.WriteLine();

        // We can fetch the total file count for “progress” if we want, but that can be expensive.
        // For now, let's just process.
        foreach (DedupResult result in Deduplicate(targetDirectory, options))
        {
            results.Add(result);

            if (result.Action == "Scanned")
            {
                filesScanned++;
                // Track file size
                long size = 0;
                try
                {
                    FileInfo info = new(result.FilePath);
                    size = info.Length;
                }
                catch { /* ignore */ }

                fileSizes[result.FilePath] = size;
            }
            else if (result.Action == "Linked")
            {
                linksCreated++;
                // If we replaced a full file with a link, we “saved” its size. 
                // (Though physically, multiple links to a single file only store data once.)
                // This is a rough approximation
                if (fileSizes.TryGetValue(result.FilePath, out long s))
                {
                    spaceSavedFromNewLinks += s;
                }
            }
            else if (result.Action == "Skipped")
            {
                // Possibly do nothing, or keep a counter
            }
            else if (result.Action == "Error")
            {
                // Show error
                Console.WriteLine($"Error: {result.FilePath} - {result.ErrorMessage}");
            }
        }

        // If we had existing links, we would need to detect them in a separate pass. 
        // For now, let's just keep linksAlreadyExist = 0 for demonstration.

        // Summaries:
        Console.WriteLine();
        Console.WriteLine("===== Summary =====");
        Console.WriteLine($"Files scanned: {filesScanned}");
        Console.WriteLine($"Hard links created: {linksCreated}");
        Console.WriteLine($"Space saved (new links): {PrettySize(spaceSavedFromNewLinks)}");
        Console.WriteLine($"Existing links found: {linksAlreadyExist}");
        Console.WriteLine($"Space saved (existing links): {PrettySize(spaceSavedFromExistingLinks)}");
        Console.WriteLine($"Total space savings: {PrettySize(spaceSavedFromNewLinks + spaceSavedFromExistingLinks)}");
        Console.WriteLine("===================");

        // If user requested a log, write it out
        if (!string.IsNullOrEmpty(logFile))
        {
            WriteLog(results, logFile);
        }
    }

    private static string PrettySize(long bytes)
    {
        // Basic method to print friendly sizes
        double kb = bytes / 1024.0;
        double mb = kb / 1024.0;
        double gb = mb / 1024.0;

        return gb >= 1.0 ? $"{gb:F2} GB" : mb >= 1.0 ? $"{mb:F2} MB" : kb >= 1.0 ? $"{kb:F2} KB" : $"{bytes} bytes";
    }

    private static void WriteLog(IEnumerable<DedupResult> results, string logFile)
    {
        try
        {
            using StreamWriter sw = new(logFile, append: false, Encoding.UTF8);
            // Let's write CSV headers
            sw.WriteLine("FilePath,Action,GroupId,ErrorMessage");
            foreach (DedupResult r in results)
            {
                // CSV
                string line = $"{Escape(r.FilePath)},{Escape(r.Action)},{Escape(r.GroupId)},{Escape(r.ErrorMessage ?? "")}";
                sw.WriteLine(line);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to write log file: {ex.Message}");
        }
    }

    private static string Escape(string text)
    {
        if (text.Contains(",") || text.Contains("\""))
        {
            text = "\"" + text.Replace("\"", "\"\"") + "\"";
        }
        return text;
    }

    private static void ShowHelp()
    {
        Console.WriteLine("FileDedup.exe <TargetFolder> [options]");
        Console.WriteLine("   This tool was created by ChatGPT.");
        Console.WriteLine();
        Console.WriteLine("Caution: Modifying a hard-linked file later will affect all linked files.");
        Console.WriteLine("         Always make sure you have a backup before using this tool.");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  -help      Shows this help text.");
        Console.WriteLine("  -confirm   Automatically confirm creation of all hard links without prompting.");
        Console.WriteLine("  -DoNotMarkReadOnly   Does not mark all hard-linked files as read-only.");
        Console.WriteLine("  -log <file>  Writes actions to a CSV log file.");
        Console.WriteLine();
        Console.WriteLine("Notes:");
        Console.WriteLine("  - All hard-linked files are marked as read-only by default.");
        Console.WriteLine("  - To modify a hard-linked file, you must remove the read-only attribute.");
        Console.WriteLine();
        Console.WriteLine("Examples:");
        Console.WriteLine("  FileDedup.exe C:\\MyTargetFolder -confirm");
        Console.WriteLine("  FileDedup.exe C:\\MyTargetFolder -log C:\\MyLogs\\FileDedup.csv");
        Console.WriteLine();
    }
}
