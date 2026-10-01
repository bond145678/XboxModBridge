using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json.Nodes;

namespace XboxModBridge
{
    public class FixPack
    {
        public string Name, UeProject, IdentityName;
        public Dictionary<string, string> Files = new Dictionary<string, string>();
    }

    /// <summary>What makes CurseForge's Steam-shaped mod folders work in an Xbox install.</summary>
    public static class Bridge
    {
        public static string Log = "";
        static void Say(string s) { Log += s + Environment.NewLine; }

        // ---- folder redirect

        /// <summary>Unreal games on Steam run from &lt;Project&gt;\Binaries\Win64; their Xbox builds run from
        /// Binaries\WinGDK. CurseForge installs UE4SS and UE4SS mods into Win64, so Win64 is made a
        /// junction to WinGDK: everything CurseForge writes there lands where the Xbox game reads it.</summary>
        public static string Win64Link(XboxGame x) =>
            x.UeProject == null ? null : Path.Combine(x.Content, x.UeProject, "Binaries", "Win64");

        public static bool NeedsLink(XboxGame x) => x.Engine == "Unreal" && x.UeBinaries == "WinGDK";

        public static bool LinkOk(XboxGame x)
        {
            if (!NeedsLink(x)) return true;
            var w = Win64Link(x);
            return Directory.Exists(w) && XboxScanner.IsJunction(w);
        }

        public static bool MakeLink(XboxGame x)
        {
            if (!NeedsLink(x)) return true;
            string link = Win64Link(x);
            string target = Path.Combine(x.Content, x.UeProject, "Binaries", "WinGDK");
            if (Directory.Exists(link))
            {
                if (XboxScanner.IsJunction(link)) return true;
                // a real Win64 folder: someone copied Steam-layout files in by hand. Move them over, then link.
                Say("Moving files from " + link + " into " + target);
                MergeInto(link, target);
                try { Directory.Delete(link, true); }
                catch (Exception e) { Say("Could not replace " + link + ": " + e.Message); return false; }
            }
            // a junction needs no admin rights and no Developer Mode (a symbolic link would)
            var psi = new ProcessStartInfo("cmd.exe", "/c mklink /J \"" + link + "\" \"" + target + "\"")
            { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
            using (var p = Process.Start(psi))
            {
                string o = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
                p.WaitForExit();
                if (p.ExitCode != 0) { Say("mklink failed: " + o.Trim()); return false; }
            }
            Say("Linked " + link + " -> " + target);
            return true;
        }

        public static void RemoveLink(XboxGame x)
        {
            var w = Win64Link(x);
            if (w != null && Directory.Exists(w) && XboxScanner.IsJunction(w))
            {
                Directory.Delete(w, false);   // removes the junction only, never what it points at
                Say("Removed link " + w);
            }
        }

        static void MergeInto(string from, string to)
        {
            foreach (var f in Directory.GetFiles(from, "*", SearchOption.AllDirectories))
            {
                string dst = Path.Combine(to, Path.GetRelativePath(from, f));
                Directory.CreateDirectory(Path.GetDirectoryName(dst));
                File.Copy(f, dst, true);
            }
        }

        // ---- per-game fixes

        public static List<FixPack> LoadFixPacks()
        {
            var byName = new Dictionary<string, FixPack>(StringComparer.OrdinalIgnoreCase);
            void Add(string json)
            {
                try
                {
                    foreach (var p in (JsonNode.Parse(json)?["packs"] as JsonArray) ?? new JsonArray())
                    {
                        var fp = new FixPack
                        {
                            Name = (string)p["name"], UeProject = (string)p["ueProject"], IdentityName = (string)p["identityName"],
                        };
                        if (p["files"] is JsonObject fo)
                            foreach (var kv in fo) fp.Files[kv.Key] = (string)kv.Value;
                        if (fp.Name != null) byName[fp.Name] = fp;
                    }
                }
                catch (Exception e) { Say("fixpacks.json not read: " + e.Message); }
            }
            using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("fixpacks.json"))
                if (s != null) using (var r = new StreamReader(s)) Add(r.ReadToEnd());
            string local = Path.Combine(AppContext.BaseDirectory, "fixpacks.json");
            if (File.Exists(local)) Add(File.ReadAllText(local));
            return byName.Values.ToList();
        }

        public static List<FixPack> PacksFor(XboxGame x, List<FixPack> all) => all.Where(p =>
            (p.UeProject != null && string.Equals(p.UeProject, x.UeProject, StringComparison.OrdinalIgnoreCase)) ||
            (p.IdentityName != null && string.Equals(p.IdentityName, x.IdentityName, StringComparison.OrdinalIgnoreCase))).ToList();

        /// <summary>Writes any fix file that is missing or different. Returns how many were written.</summary>
        public static int ApplyFixes(XboxGame x, List<FixPack> all)
        {
            int n = 0;
            foreach (var p in PacksFor(x, all))
                foreach (var kv in p.Files)
                {
                    string dst = Path.Combine(x.Content, kv.Key.Replace('/', Path.DirectorySeparatorChar));
                    string want = kv.Value.Replace("\r\n", "\n");
                    try
                    {
                        if (File.Exists(dst) && File.ReadAllText(dst).Replace("\r\n", "\n") == want) continue;
                        Directory.CreateDirectory(Path.GetDirectoryName(dst));
                        File.WriteAllText(dst, want, new UTF8Encoding(false));
                        Say("Fix written: " + kv.Key);
                        n++;
                    }
                    catch (Exception e) { Say("Fix not written (" + kv.Key + "): " + e.Message); }
                }
            return n;
        }

        public static bool FixesOk(XboxGame x, List<FixPack> all) => PacksFor(x, all).All(p => p.Files.All(kv =>
        {
            string dst = Path.Combine(x.Content, kv.Key.Replace('/', Path.DirectorySeparatorChar));
            try { return File.Exists(dst) && File.ReadAllText(dst).Replace("\r\n", "\n") == kv.Value.Replace("\r\n", "\n"); }
            catch { return false; }
        }));

        /// <summary>Fixes are only worth writing once the game has UE4SS (the CurseForge app adds it with the
        /// first UE4SS mod). Before that, a lone ue4ss folder would do nothing.</summary>
        public static bool HasUe4ss(XboxGame x)
        {
            if (x.UeProject == null) return false;
            string bin = Path.Combine(x.Content, x.UeProject, "Binaries", x.UeBinaries ?? "WinGDK");
            return Directory.Exists(Path.Combine(bin, "ue4ss")) || File.Exists(Path.Combine(bin, "dwmapi.dll"));
        }

        // ---- the settings file: which Xbox games this PC has bridged

        public static string StateFile => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "XboxModBridge", "bridged.txt");

        /// <summary>Bridged games: Content folder -> the detection file this program created there (or "").</summary>
        public static Dictionary<string, string> BridgedMap()
        {
            var m = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (var l in File.ReadAllLines(StateFile))
                {
                    var parts = l.Split('\t');
                    if (parts[0].Trim().Length > 0) m[parts[0].Trim()] = parts.Length > 1 ? parts[1].Trim() : "";
                }
            }
            catch { }
            return m;
        }

        public static HashSet<string> Bridged() => new HashSet<string>(BridgedMap().Keys, StringComparer.OrdinalIgnoreCase);

        public static void SetBridged(XboxGame x, bool on, string detectFile)
        {
            var m = BridgedMap();
            if (on) m[x.Content] = detectFile ??
                (m.TryGetValue(x.Content, out var old) && !string.IsNullOrEmpty(old) && File.Exists(Path.Combine(x.Content, old)) ? old : "");
            else m.Remove(x.Content);
            Directory.CreateDirectory(Path.GetDirectoryName(StateFile));
            File.WriteAllLines(StateFile, m.OrderBy(kv => kv.Key).Select(kv => kv.Key + "\t" + kv.Value));
        }

        // ---- making CurseForge recognise the folder

        [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
        static extern bool CreateHardLink(string newFile, string existingFile, IntPtr security);

        /// <summary>CurseForge accepts a game folder only if it holds one of the game's known files - for most
        /// games the Steam launcher exe at the top (RSDragonwilds.exe). Xbox installs start through
        /// gamelaunchhelper.exe instead, so CurseForge says "game not detected". This adds the expected name
        /// as a hard link to the Xbox launcher: same file, no extra space, and double-clicking it still starts
        /// the game. Returns the name created, or null if nothing was needed.</summary>
        public static string EnsureDetectionFile(XboxGame x, CfGame cg)
        {
            var names = cg.GameFiles.Where(f => f.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && f.IndexOfAny(new[] { '/', '\\' }) < 0).ToList();
            if (names.Count == 0 || names.Any(n => File.Exists(Path.Combine(x.Content, n)))) return null;
            string src = x.Executables.Select(e => Path.Combine(x.Content, e)).FirstOrDefault(File.Exists)
                         ?? Path.Combine(x.Content, "gamelaunchhelper.exe");
            if (!File.Exists(src)) { Say("No launcher exe found to stand in for " + names[0]); return null; }
            string dst = Path.Combine(x.Content, names[0]);
            try
            {
                if (!CreateHardLink(dst, src, IntPtr.Zero)) File.Copy(src, dst);
                Say("Added " + names[0] + " (the Xbox launcher under the name CurseForge looks for)");
                return names[0];
            }
            catch (Exception e) { Say("Could not add " + names[0] + ": " + e.Message); return null; }
        }

        /// <summary>Removes the detection file, but only if this program created it.</summary>
        public static void RemoveDetectionFile(XboxGame x)
        {
            if (!BridgedMap().TryGetValue(x.Content, out var f) || string.IsNullOrEmpty(f)) return;
            string p = Path.Combine(x.Content, f);
            try { if (File.Exists(p)) { File.Delete(p); Say("Removed " + f); } } catch (Exception e) { Say("Could not remove " + f + ": " + e.Message); }
        }

        /// <summary>Puts back whatever went missing for every bridged game: the folder link (Xbox app updates
        /// can remove it) and the fix files (the CurseForge app replaces the ue4ss folder on each install).</summary>
        public static int RepairAll(List<XboxGame> games, List<FixPack> packs)
        {
            int n = 0;
            var on = BridgedMap();
            foreach (var g in games.Where(g => on.ContainsKey(g.Content)))
            {
                if (!LinkOk(g) && MakeLink(g)) n++;
                // an Xbox app update can remove the detection file; put it back from the launcher
                string det = on[g.Content];
                if (!string.IsNullOrEmpty(det) && !File.Exists(Path.Combine(g.Content, det)))
                {
                    string src = g.Executables.Select(e => Path.Combine(g.Content, e)).FirstOrDefault(File.Exists);
                    if (src != null)
                        try { if (!CreateHardLink(Path.Combine(g.Content, det), src, IntPtr.Zero)) File.Copy(src, Path.Combine(g.Content, det)); Say("Put back " + det); n++; }
                        catch (Exception e) { Say("Could not put back " + det + ": " + e.Message); }
                }
                if (HasUe4ss(g)) n += ApplyFixes(g, packs);
            }
            return n;
        }
    }
}
