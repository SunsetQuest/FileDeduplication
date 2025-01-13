using System.Text;
using static Inflater.FileInflator;  // We'll define FileInflator below


//////// For Testing Purposes //////
//#if DEBUG
//args = [
//    "S:\\Test",
//    //   ,"-confirm",
//    "-log", "S:\\FileInfate.csv"
//    ];
//#endif
////////////////////////////////////

if (args.Length == 0 || args.Contains("-help", StringComparer.OrdinalIgnoreCase))
{
    ShowHelp();
    return;
}

string targetDirectory = args[0];
bool confirmAll = args.Contains("-confirm", StringComparer.OrdinalIgnoreCase);
string? logFile = null;

// If user specified "-log someFilePath"
for (int i = 0; i < args.Length; i++)
{
    if (args[i].Equals("-log", StringComparison.OrdinalIgnoreCase) && (i + 1 < args.Length))
    {
        logFile = args[i + 1];
    }
}

// For summary & logs
List<InflateResult> results = new();
int filesScanned = 0;
int filesInflated = 0;

// Callback if we want interactive confirmation
bool applyAll = false;
InflateOptions options = new()
{
    ConfirmAll = confirmAll,
    ConfirmCallback = filePath =>
    {
        if (applyAll) return true; // user already said "All"

        Console.WriteLine();
        Console.Write($"{filePath} is a hard link. Break link?  (Y)es  (N)o  (A)ll? ");
        ConsoleKeyInfo key = Console.ReadKey(intercept: true);
        char c = char.ToUpperInvariant(key.KeyChar);
        Console.WriteLine(c);

        if (c == 'Y') return true;
        if (c == 'A')
        {
            applyAll = true;
            return true;
        }
        return false; // default skip
    }
};

Console.WriteLine($"""Scanning "{targetDirectory}" for hard-linked groups...""");
Console.WriteLine();

foreach (InflateResult r in InflateDirectory(targetDirectory, options))
{
    results.Add(r);

    if (r.Action == "Scanned") filesScanned++;
    if (r.Action == "Inflated") 
        filesInflated++;
    if (r.Action == "Error")
    {
        Console.WriteLine($"Error: {r.FilePath} - {r.ErrorMessage}");
    }
}

// Summaries
Console.WriteLine();
Console.WriteLine("===== Summary =====");
Console.WriteLine($"Files scanned: {filesScanned}");
Console.WriteLine($"Files inflated (copies restored): {filesInflated}");
Console.WriteLine("===================");

// If user requested a log, write it
if (!string.IsNullOrEmpty(logFile))
{
    WriteLog(results, logFile);
}

void WriteLog(IEnumerable<InflateResult> results, string logFile)
{
    try
    {
        using StreamWriter sw = new(logFile, false, Encoding.UTF8);
        // CSV header
        sw.WriteLine("FilePath,Action,GroupId,ErrorMessage");
        foreach (InflateResult r in results)
        {
            string line = $"{Escape(r.FilePath)},{Escape(r.Action)},{Escape(r.GroupId)},{Escape(r.ErrorMessage ?? "")}";
            sw.WriteLine(line);
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Failed to write log file: {ex.Message}");
    }
}

string Escape(string text)
{
    if (text.Contains(",") || text.Contains("\""))
    {
        text = "\"" + text.Replace("\"", "\"\"") + "\"";
    }
    return text;
}

void ShowHelp()
{
    Console.WriteLine("FileInflate.exe <TargetFolder> [options]");
    Console.WriteLine("   Restores individual copies from hard-linked files.");
    Console.WriteLine();
    Console.WriteLine("Options:");
    Console.WriteLine("  -help      Shows this help text.");
    Console.WriteLine("  -confirm   Automatically confirm breaking links without prompting.");
    Console.WriteLine("  -log <file>  Write actions to CSV log file.");
    Console.WriteLine();
    Console.WriteLine("Example:");
    Console.WriteLine("  FileInflate.exe C:\\MyTargetFolder -confirm");
    Console.WriteLine();
}
