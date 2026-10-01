using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace XboxModBridge
{
    /// <summary>A game the CurseForge app supports (from its own gamesCache.json).</summary>
    public class CfGame
    {
        public int Id;
        public string Name;
        public List<string> GameFiles = new List<string>();
        public List<string> Hints = new List<string>();
        public List<string> ModPaths = new List<string>();   // where each mod category installs, relative to the game folder
        public override string ToString() => Name;
    }

    public class CfInstance
    {
        public string Guid, Name, InstallPath;
        public int GameId;
        public bool Valid;
    }

    /// <summary>Reads (never writes) the CurseForge app's local game data: the games it supports
    /// (gamesCache.json) and the game folders it manages (GameInstances\AddonGameInstance.json).</summary>
    public class CurseForgeData
    {
        public string Dir;
        public List<CfGame> Games = new List<CfGame>();
        public List<CfInstance> Instances = new List<CfInstance>();

        string InstancesFile => Path.Combine(Dir, "GameInstances", "AddonGameInstance.json");

        public static CurseForgeData Load(string dirOverride = null)
        {
            var d = dirOverride ?? FindDataDir();
            if (d == null) return null;
            var cf = new CurseForgeData { Dir = d };
            cf.Reload();
            return cf;
        }

        public void Reload()
        {
            Games = new List<CfGame>();          // new lists, never cleared in place: the window may still show the old ones
            Instances = new List<CfInstance>();
            string cache = Path.Combine(Dir, "gamesCache.json");
            if (File.Exists(cache))
            {
                foreach (var g in (ReadJson(cache) as JsonArray) ?? new JsonArray())
                {
                    var cg = new CfGame { Id = Int(g?["id"]), Name = Str(g?["name"]) };
                    if (cg.Id == 0 || cg.Name == null) continue;
                    foreach (var f in Arr(g["gameFiles"])) { var n = Str(f?["fileName"]); if (n != null) cg.GameFiles.Add(n); }
                    foreach (var h in Arr(g["gameDetectionHints"])) { var n = Str(h?["hintPath"]); if (n != null) cg.Hints.Add(n.Replace("\\\\", "\\")); }
                    foreach (var c in Arr(g["categorySections"])) { var n = Str(c?["path"]); if (n != null) cg.ModPaths.Add(n.Replace('\\', '/')); }
                    Games.Add(cg);
                }
            }
            if (File.Exists(InstancesFile))
            {
                foreach (var i in (ReadJson(InstancesFile) as JsonArray) ?? new JsonArray())
                {
                    Instances.Add(new CfInstance
                    {
                        Guid = Str(i?["guid"]), Name = Str(i?["name"]), InstallPath = Str(i?["installPath"]),
                        GameId = Int(i?["gameTypeID"]), Valid = i?["isValid"]?.GetValue<bool>() ?? false,
                    });
                }
            }
            Games = Games.OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }

        /// <summary>The CurseForge app's data folder: the Overwolf edition keeps it in
        /// %LOCALAPPDATA%\Overwolf\Curse; the standalone edition in a "CurseForge" folder under AppData.</summary>
        public static string FindDataDir()
        {
            var bases = new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            };
            var cands = new List<string>
            {
                Path.Combine(bases[0], "Overwolf", "Curse"),
                Path.Combine(bases[1], "CurseForge"),
                Path.Combine(bases[0], "CurseForge"),
            };
            foreach (var b in bases)
            {
                try
                {
                    foreach (var d in Directory.GetDirectories(b))
                    {
                        if (Path.GetFileName(d).IndexOf("curse", StringComparison.OrdinalIgnoreCase) < 0 &&
                            Path.GetFileName(d).IndexOf("overwolf", StringComparison.OrdinalIgnoreCase) < 0) continue;
                        cands.Add(d);
                        try { cands.AddRange(Directory.GetDirectories(d)); } catch { }
                    }
                }
                catch { }
            }
            foreach (var c in cands)
                if (File.Exists(Path.Combine(c, "GameInstances", "AddonGameInstance.json")) || File.Exists(Path.Combine(c, "gamesCache.json")))
                    return c;
            return null;
        }

        public static string[] RunningProcesses()
        {
            return Process.GetProcesses()
                .Where(p => p.ProcessName.StartsWith("CurseForge", StringComparison.OrdinalIgnoreCase) ||
                            p.ProcessName.Equals("Overwolf", StringComparison.OrdinalIgnoreCase) ||
                            p.ProcessName.Equals("OverwolfBrowser", StringComparison.OrdinalIgnoreCase))
                .Select(p => p.ProcessName).Distinct().ToArray();
        }

        // ---- matching an Xbox game to a CurseForge game

        static string Norm(string s) => new string((s ?? "").ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());

        public (CfGame game, string why) Match(XboxGame x)
        {
            CfGame best = null; int bestScore = 0; string bestWhy = null;
            var exes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var e in x.Executables) exes.Add(Path.GetFileNameWithoutExtension(e));
            try { foreach (var f in Directory.GetFiles(x.Content, "*.exe")) exes.Add(Path.GetFileNameWithoutExtension(f)); } catch { }
            if (x.UeProject != null) exes.Add(x.UeProject);

            foreach (var g in Games)
            {
                int score = 0; string why = null;
                if (x.UeProject != null && g.ModPaths.Any(p => p.Split('/')[0].Equals(x.UeProject, StringComparison.OrdinalIgnoreCase)))
                { score += 50; why = "mod folders start with " + x.UeProject; }
                if (g.GameFiles.Any(f => exes.Contains(Path.GetFileNameWithoutExtension(f))))
                { score += 40; why = why ?? "same game executable"; }
                if (g.Hints.Any(h => { var leaf = Path.GetFileName(h.TrimEnd('\\', '/')); return Norm(leaf) == Norm(x.UeProject) || Norm(leaf) == Norm(Path.GetFileName(x.Folder)); }))
                { score += 30; why = why ?? "CurseForge looks for it in a folder of the same name"; }
                if (Norm(g.Name) == Norm(x.DisplayName)) { score += 30; why = why ?? "same name"; }
                else if (Norm(x.DisplayName).Length > 4 && (Norm(g.Name).Contains(Norm(x.DisplayName)) || Norm(x.DisplayName).Contains(Norm(g.Name)))) { score += 10; why = why ?? "similar name"; }
                if (score > bestScore) { best = g; bestScore = score; bestWhy = why; }
            }
            return bestScore >= 30 ? (best, bestWhy) : (null, null);
        }

        public CfInstance InstanceFor(XboxGame x) =>
            Instances.FirstOrDefault(i => i.InstallPath != null &&
                string.Equals(Path.GetFullPath(i.InstallPath.TrimEnd('\\') + "\\"), Path.GetFullPath(x.Content.TrimEnd('\\') + "\\"), StringComparison.OrdinalIgnoreCase));

        // ---- json helpers

        static JsonNode ReadJson(string path)
        {
            try { return JsonNode.Parse(File.ReadAllText(path, Encoding.UTF8)); } catch { return null; }
        }

        static IEnumerable<JsonNode> Arr(JsonNode n) => (n as JsonArray) ?? new JsonArray();
        static string Str(JsonNode n) { try { return n?.GetValue<string>(); } catch { return null; } }
        static int Int(JsonNode n) { try { return n?.GetValue<int>() ?? 0; } catch { return 0; } }
    }
}
