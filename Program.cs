using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;

namespace XboxModBridge
{
    public static class Program
    {
        [DllImport("kernel32.dll")] static extern bool AttachConsole(int pid);

        /// <summary>
        ///   (no args)            the window
        ///   --repair             one repair pass, then exit
        ///   --scan [--root D]    print what it finds (D: extra folder to treat like XboxGames)
        ///   --link "Content" id  bridge one game to CurseForge game id   [--cf DIR: use another CurseForge data folder]
        ///   --unlink "Content"
        /// </summary>
        [STAThread]
        public static int Main(string[] args)
        {
            var a = args.ToList();
            string Opt(string name)
            {
                int i = a.IndexOf(name);
                return i >= 0 && i + 1 < a.Count ? a[i + 1] : null;
            }
            var roots = Opt("--root") is string r ? new[] { r } : null;
            string cfDir = Opt("--cf");

            if (a.Contains("--repair") || a.Contains("--scan") || a.Contains("--link") || a.Contains("--unlink"))
            {
                AttachConsole(-1);
                Console.WriteLine();
                return Cli(a, roots, cfDir, Opt);
            }

            var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
            var w = new MainWindow(roots, cfDir);
            return app.Run(w);
        }

        static int Cli(List<string> a, string[] roots, string cfDir, Func<string, string> opt)
        {
            var games = XboxScanner.Scan(roots);
            var cf = CurseForgeData.Load(cfDir);
            var packs = Bridge.LoadFixPacks();
            if (a.Contains("--scan"))
            {
                Console.WriteLine("CurseForge data: " + (cf?.Dir ?? "(not found)") + "  games known: " + (cf?.Games.Count ?? 0));
                foreach (var g in games)
                {
                    var (m, why) = cf?.Match(g) ?? (null, null);
                    Console.WriteLine($"- {g.DisplayName}  [{g.Engine}{(g.UeProject != null ? " " + g.UeProject + "/" + g.UeBinaries : "")}]  writable={g.Writable}");
                    Console.WriteLine($"    {g.Content}");
                    Console.WriteLine($"    CurseForge: {(m != null ? m.Name + " (" + m.Id + ", " + why + ")" : "no match")}" +
                        $"  in CF: {(cf?.InstanceFor(g) != null)}  link ok: {Bridge.LinkOk(g)}  UE4SS: {Bridge.HasUe4ss(g)}" +
                        $"  fixes: {Bridge.PacksFor(g, packs).Count} ok={Bridge.FixesOk(g, packs)}");
                }
                return 0;
            }
            if (a.Contains("--repair"))
            {
                int n = Bridge.RepairAll(games, packs);
                Console.Write(Bridge.Log);
                Console.WriteLine("repaired " + n);
                return 0;
            }
            if (a.Contains("--link") || a.Contains("--unlink"))
            {
                string content = opt(a.Contains("--link") ? "--link" : "--unlink");
                var g = games.FirstOrDefault(x => string.Equals(Path.GetFullPath(x.Content), Path.GetFullPath(content ?? "."), StringComparison.OrdinalIgnoreCase));
                if (g == null) { Console.WriteLine("No Xbox game at " + content); return 2; }
                if (a.Contains("--unlink"))
                {
                    Console.WriteLine(Actions.Disconnect(g));
                    return 0;
                }
                if (cf == null) { Console.WriteLine("CurseForge data folder not found"); return 2; }
                int idx = a.IndexOf("--link");
                int id = idx + 2 < a.Count && int.TryParse(a[idx + 2], out var v) ? v : 0;
                var cg = id != 0 ? cf.Games.FirstOrDefault(x => x.Id == id) : cf.Match(g).game;
                if (cg == null) { Console.WriteLine("CurseForge game not found"); return 2; }
                var (ok, msg) = Actions.Connect(g, cg, cf, packs);
                Console.WriteLine(msg);
                return ok ? 0 : 1;
            }
            return 0;
        }
    }

    /// <summary>Connect / disconnect, shared by the window and the command line.
    /// The game is added to CurseForge through the app's own "Locate Game" button; this program only
    /// gets the Xbox folder ready so the app recognises it and its mods land where the Xbox game reads them.</summary>
    public static class Actions
    {
        public static (bool ok, string msg) Connect(XboxGame g, CfGame cg, CurseForgeData cf, List<FixPack> packs)
        {
            Bridge.Log = "";
            if (!g.Writable)
                return (false, "Windows won't let anything write into this game's folder:\n" + g.Content +
                    "\n\nIn the Xbox app: Settings > Install options > pick a folder like C:\\XboxGames, then uninstall and reinstall the game (or use its \"Move\" option). Games installed there can be modded.");
            if (!Bridge.MakeLink(g)) return (false, "Could not set up the mod folders.\n\n" + Bridge.Log);
            // connected before to a different CurseForge game? take back the file made for that one
            if (Bridge.BridgedMap().TryGetValue(g.Content, out var oldDetect) && !string.IsNullOrEmpty(oldDetect) &&
                !cg.GameFiles.Any(f => string.Equals(f, oldDetect, StringComparison.OrdinalIgnoreCase)))
                Bridge.RemoveDetectionFile(g);
            string detect = Bridge.EnsureDetectionFile(g, cg);
            Bridge.SetBridged(g, true, detect);
            if (Bridge.HasUe4ss(g)) Bridge.ApplyFixes(g, packs);
            bool tracked = cf?.InstanceFor(g) != null;
            var sb = new System.Text.StringBuilder();
            sb.AppendLine(g.DisplayName + " is ready for CurseForge.");
            sb.AppendLine();
            if (tracked)
                sb.AppendLine("CurseForge already has this folder - install mods for " + cg.Name + " as usual.");
            else
            {
                sb.AppendLine("Last step, in the CurseForge app:");
                sb.AppendLine("  1. Pick " + cg.Name + " (Home > search for it if it isn't on the left).");
                sb.AppendLine("  2. Click \"Locate Game\" (or right-click the game > \"Manually add a game\").");
                sb.AppendLine("  3. Choose this folder (it's copied - paste it into the folder box with Ctrl+V):");
                sb.AppendLine("     " + g.Content);
                sb.AppendLine("Then install mods as usual.");
            }
            if (Bridge.NeedsLink(g))
                sb.AppendLine("\nMods CurseForge puts in Binaries\\Win64 go straight into the Xbox game's Binaries\\WinGDK.");
            if (Bridge.PacksFor(g, packs).Count > 0)
                sb.AppendLine("This game needs an Xbox fix for its mod loader; it's applied automatically" +
                    (Bridge.HasUe4ss(g) ? "." : " once CurseForge installs the loader: open this program and click Repair now after installing mods."));
            if (Bridge.Log.Length > 0) sb.AppendLine("\n" + Bridge.Log.Trim());
            return (true, sb.ToString().Trim());
        }

        public static string Disconnect(XboxGame g)
        {
            Bridge.Log = "";
            Bridge.RemoveLink(g);
            Bridge.RemoveDetectionFile(g);
            Bridge.SetBridged(g, false, null);
            return g.DisplayName + ": disconnected." + (Bridge.Log.Length > 0 ? "\n" + Bridge.Log.Trim() : "") +
                "\nRemove the game from CurseForge too (right-click it > Remove) and delete any mods you installed for it there first.";
        }
    }
}
