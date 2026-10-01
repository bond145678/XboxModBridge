using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace XboxModBridge
{
    /// <summary>One game installed by the Xbox app.</summary>
    public class XboxGame
    {
        public string Folder;          // <drive>:\XboxGames\<Game>
        public string Content;         // ...\Content: what the game runs from, and the folder CurseForge is pointed at
        public string DisplayName;
        public string IdentityName;
        public List<string> Executables = new List<string>();
        public string Engine = "Unknown";
        public string UeProject;       // Unreal: the project folder inside Content (e.g. "RSDragonwilds")
        public string UeBinaries;      // Unreal: the platform folder the Xbox build runs from (usually WinGDK)
        public bool Writable;

        public override string ToString() => DisplayName;
    }

    public static class XboxScanner
    {
        /// <summary>Every Xbox app game on every drive. extraRoots: more folders to treat like XboxGames (testing).</summary>
        public static List<XboxGame> Scan(IEnumerable<string> extraRoots = null)
        {
            var roots = new List<string>();
            foreach (var d in DriveInfo.GetDrives())
            {
                try
                {
                    if (d.DriveType != DriveType.Fixed && d.DriveType != DriveType.Removable) continue;
                    if (!d.IsReady) continue;
                    roots.AddRange(GamingRoots(d.RootDirectory.FullName));
                    roots.Add(Path.Combine(d.RootDirectory.FullName, "XboxGames"));
                }
                catch { }
            }
            if (extraRoots != null) roots.AddRange(extraRoots);

            var games = new List<XboxGame>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var r in roots.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!Directory.Exists(r)) continue;
                foreach (var dir in SafeDirs(r))
                {
                    string content = Path.Combine(dir, "Content");
                    if (!File.Exists(Path.Combine(content, "MicrosoftGame.config"))) continue;
                    if (!seen.Add(Path.GetFullPath(content))) continue;
                    games.Add(Read(dir, content));
                }
            }
            return games.OrderBy(g => g.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();
        }

        /// <summary>The hidden .GamingRoot file at a drive root lists the Xbox app's install folders on that
        /// drive: an 8-byte header, then null-separated UTF-16 paths relative to the drive root.</summary>
        static IEnumerable<string> GamingRoots(string driveRoot)
        {
            string f = Path.Combine(driveRoot, ".GamingRoot");
            if (!File.Exists(f)) yield break;
            byte[] b;
            try { b = File.ReadAllBytes(f); } catch { yield break; }
            if (b.Length <= 8) yield break;
            foreach (var p in Encoding.Unicode.GetString(b, 8, b.Length - 8).Split('\0'))
            {
                var t = p.Trim().TrimStart('\\');
                if (t.Length > 0) yield return Path.Combine(driveRoot, t);
            }
        }

        static XboxGame Read(string folder, string content)
        {
            var g = new XboxGame { Folder = folder, Content = content, DisplayName = Path.GetFileName(folder) };
            try
            {
                var x = XDocument.Load(Path.Combine(content, "MicrosoftGame.config"));
                var ns = x.Root.Name.Namespace;
                var id = x.Root.Element(ns + "Identity");
                g.IdentityName = (string)id?.Attribute("Name");
                var vis = x.Root.Element(ns + "ShellVisuals");
                var dn = (string)vis?.Attribute("DefaultDisplayName");
                if (!string.IsNullOrWhiteSpace(dn) && !dn.StartsWith("ms-resource", StringComparison.OrdinalIgnoreCase))
                    g.DisplayName = dn;
                foreach (var e in x.Descendants(ns + "Executable"))
                {
                    var n = (string)e.Attribute("Name");
                    if (!string.IsNullOrEmpty(n)) g.Executables.Add(n);
                }
            }
            catch { }

            // Unreal: <Content>\<Project>\Binaries\<Platform>\<Project>-<Platform>-Shipping.exe
            foreach (var sub in SafeDirs(content))
            {
                string bin = Path.Combine(sub, "Binaries");
                if (!Directory.Exists(bin)) continue;
                string plat = null;
                foreach (var p in new[] { "WinGDK", "Win64" })
                    if (Directory.Exists(Path.Combine(bin, p)) && !IsJunction(Path.Combine(bin, p))) { plat = p; break; }
                if (plat == null) continue;
                g.Engine = "Unreal";
                g.UeProject = Path.GetFileName(sub);
                g.UeBinaries = plat;
                break;
            }
            if (g.Engine == "Unknown")
            {
                if (File.Exists(Path.Combine(content, "UnityPlayer.dll")) || SafeDirs(content).Any(d => d.EndsWith("_Data", StringComparison.OrdinalIgnoreCase)))
                    g.Engine = "Unity";
            }
            g.Writable = CanWrite(content);
            return g;
        }

        public static bool IsJunction(string dir)
        {
            try { return (File.GetAttributes(dir) & FileAttributes.ReparsePoint) != 0; } catch { return false; }
        }

        static bool CanWrite(string dir)
        {
            try
            {
                string t = Path.Combine(dir, ".xboxmodbridge_test");
                File.WriteAllText(t, "");
                File.Delete(t);
                return true;
            }
            catch { return false; }
        }

        static string[] SafeDirs(string d)
        {
            try { return Directory.GetDirectories(d); } catch { return new string[0]; }
        }
    }
}
