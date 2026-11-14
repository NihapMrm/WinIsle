using System;
using System.IO;
using System.Reflection;

// "Start with Windows": a shortcut to the app in the user's Startup folder, the same one set up by hand before.
static class Startup
{
    internal static string LinkPath
    {
        get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), "WinIsle.lnk"); }
    }

    internal static bool Enabled
    {
        get { return File.Exists(LinkPath); }
        set
        {
            try
            {
                if (!value) { if (File.Exists(LinkPath)) File.Delete(LinkPath); return; }
                var exe = Assembly.GetEntryAssembly().Location;
                // WScript.Shell late-bound: the build has no COM interop references.
                var shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));
                var link = shell.GetType().InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { LinkPath });
                var type = link.GetType();
                type.InvokeMember("TargetPath", BindingFlags.SetProperty, null, link, new object[] { exe });
                type.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, link, new object[] { Path.GetDirectoryName(exe) });
                type.InvokeMember("Save", BindingFlags.InvokeMethod, null, link, null);
            }
            catch (Exception) { } // the switch re-reads Enabled, so a failure shows as still off/on
        }
    }
}
