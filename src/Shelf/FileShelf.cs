using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

// Files held on the island's shelf: paths only (the files stay where they are), kept in shelf.txt on C:.
class FileShelf
{
    readonly List<string> paths = new List<string>();
    readonly string file; // null in checks

    internal FileShelf(string file)
    {
        this.file = file;
        try { if (file != null && File.Exists(file)) paths.AddRange(File.ReadAllLines(file).Where(l => l.Length > 0)); }
        catch (Exception) { } // unreadable: start empty
        Prune();
    }

    internal static FileShelf Live() { return new FileShelf(AppPaths.For("shelf.txt")); }

    internal IList<string> Paths { get { return paths.AsReadOnly(); } }
    internal int Count { get { return paths.Count; } }

    // Returns how many were new; the same file twice is held once.
    internal int Add(IEnumerable<string> incoming)
    {
        int added = 0;
        foreach (var p in incoming)
            if (!paths.Any(q => string.Equals(q, p, StringComparison.OrdinalIgnoreCase))) { paths.Add(p); added++; }
        if (added > 0) Save();
        return added;
    }

    internal void Remove(string path) { if (paths.Remove(path)) Save(); }

    internal void Clear() { paths.Clear(); Save(); }

    // Files moved or deleted since (for example dragged out with a move) leave the shelf.
    internal void Prune()
    {
        if (paths.RemoveAll(p => !File.Exists(p) && !Directory.Exists(p)) > 0) Save();
    }

    void Save()
    {
        if (file == null) return;
        try { File.WriteAllLines(file, paths); } catch (Exception) { }
    }
}
