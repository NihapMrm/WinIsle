using System;
using System.IO;

// Saved data lives in %LOCALAPPDATA%\WinIsle on C:, apart from the program.
static class AppPaths
{
    static readonly string[] DataFiles = { "todos.txt", "position.txt", "prayed.txt", "prayer-settings.txt" };

    public static readonly string Folder = Path.Combine(
        Environment.GetEnvironmentVariable("LOCALAPPDATA") ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WinIsle");

    public static string For(string name) { return Path.Combine(Folder, name); }

    // Run before anything reads data. Data saved beside the exe by older versions moves over once.
    public static void Prepare()
    {
        // The app was called MyTodo: its data folder moves over once.
        var old = Path.Combine(Path.GetDirectoryName(Folder), "MyTodo");
        if (Directory.Exists(old) && !Directory.Exists(Folder)) Directory.Move(old, Folder);
        CopyMissing(AppDomain.CurrentDomain.BaseDirectory, Folder);
    }

    // Never overwrites, so newer data in the target is safe and this can run on every start.
    public static void CopyMissing(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var name in DataFiles)
        {
            string source = Path.Combine(from, name), target = Path.Combine(to, name);
            if (File.Exists(source) && !File.Exists(target)) File.Copy(source, target);
        }
    }
}
