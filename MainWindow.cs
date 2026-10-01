using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace XboxModBridge
{
    public class Row
    {
        public XboxGame Game { get; set; }
        public string Name => Game.DisplayName;
        public string Engine => Game.Engine + (Game.UeProject != null ? " (" + Game.UeBinaries + ")" : "");
        public CfGame Cf { get; set; }
        public string CfName => Cf?.Name ?? "not found - pick one below";
        public string Status { get; set; }
    }

    public class MainWindow : Window
    {
        readonly string[] roots;
        readonly string cfDir;
        CurseForgeData cf;
        List<FixPack> packs;
        readonly ListView list = new ListView();
        readonly ComboBox cfPick = new ComboBox { MinWidth = 260 };
        readonly TextBox info = new TextBox { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MinHeight = 120 };
        readonly Button connect = new Button { Content = "Connect to CurseForge", Padding = new Thickness(12, 4, 12, 4) };
        readonly Button disconnect = new Button { Content = "Disconnect", Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(8, 0, 0, 0) };
        readonly Button repair = new Button { Content = "Repair now", Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(8, 0, 0, 0) };
        readonly Button refresh = new Button { Content = "Refresh", Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(8, 0, 0, 0) };
        readonly DispatcherTimer autoRepair = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };

        public MainWindow(string[] roots, string cfDir)
        {
            this.roots = roots;
            this.cfDir = cfDir;
            Title = "Xbox Mod Bridge - CurseForge mods for Xbox app / Game Pass PC games";
            Width = 900; Height = 620; MinWidth = 640; MinHeight = 480;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;

            var root = new DockPanel { Margin = new Thickness(14) };
            var head = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10),
                Text = "The CurseForge app only installs mods into Steam/Epic copies of a game. This connects your Xbox app " +
                       "(Game Pass / Microsoft Store) games to CurseForge, so you install, update and remove mods there as usual.\n" +
                       "1. Pick your game and click Connect.  2. In CurseForge, open the game, click \"Locate Game\" and paste the folder (it's copied for you).\n" +
                       "After installing or updating mods, open this again and click Repair now (while this window is open it repairs by itself)."
            };
            DockPanel.SetDock(head, Dock.Top);
            root.Children.Add(head);

            var gv = new GridView();
            gv.Columns.Add(new GridViewColumn { Header = "Xbox game", Width = 230, DisplayMemberBinding = new System.Windows.Data.Binding("Name") });
            gv.Columns.Add(new GridViewColumn { Header = "Engine", Width = 120, DisplayMemberBinding = new System.Windows.Data.Binding("Engine") });
            gv.Columns.Add(new GridViewColumn { Header = "CurseForge game", Width = 230, DisplayMemberBinding = new System.Windows.Data.Binding("CfName") });
            gv.Columns.Add(new GridViewColumn { Header = "Status", Width = 260, DisplayMemberBinding = new System.Windows.Data.Binding("Status") });
            list.View = gv;
            list.SelectionMode = SelectionMode.Single;
            list.SelectionChanged += (s, e) => Selected();
            // picking a CurseForge game by hand changes the match for the selected Xbox game only
            cfPick.SelectionChanged += (s, e) =>
            {
                if (syncing || Current == null || !(cfPick.SelectedItem is CfGame cg) || cg == Current.Cf) return;
                Current.Cf = cg;
                list.Items.Refresh();
            };

            var pickRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
            pickRow.Children.Add(new TextBlock { Text = "CurseForge game: ", VerticalAlignment = VerticalAlignment.Center });
            pickRow.Children.Add(cfPick);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
            buttons.Children.Add(connect); buttons.Children.Add(disconnect); buttons.Children.Add(repair); buttons.Children.Add(refresh);

            var bottom = new StackPanel();
            bottom.Children.Add(pickRow);
            bottom.Children.Add(buttons);
            bottom.Children.Add(new Label { Content = "Details", Padding = new Thickness(0, 8, 0, 2) });
            bottom.Children.Add(info);
            DockPanel.SetDock(bottom, Dock.Bottom);
            root.Children.Add(bottom);
            root.Children.Add(list);
            Content = root;

            connect.Click += (s, e) => DoConnect();
            disconnect.Click += (s, e) => DoDisconnect();
            repair.Click += (s, e) => DoRepair(true);
            refresh.Click += (s, e) => Load();
            autoRepair.Tick += (s, e) => DoRepair(false);
            Loaded += (s, e) => { Load(); DoRepair(false); autoRepair.Start(); };
        }

        Row Current => list.SelectedItem as Row;
        bool syncing;

        void Load()
        {
            packs = Bridge.LoadFixPacks();
            cf = CurseForgeData.Load(cfDir);
            var games = XboxScanner.Scan(roots);
            var on = Bridge.Bridged();
            var rows = new List<Row>();
            foreach (var g in games)
            {
                var row = new Row { Game = g, Cf = cf?.Match(g).game };
                rows.Add(row);
                row.Status = StatusOf(row, on);
            }
            cfPick.ItemsSource = cf?.Games ?? new List<CfGame>();
            list.ItemsSource = rows;
            if (rows.Count > 0) list.SelectedIndex = 0;
            if (cf == null)
                info.Text = "The CurseForge app's data wasn't found. Install CurseForge (curseforge.com/download/app), open it once, close it, then click Refresh.";
            else if (rows.Count == 0)
                info.Text = "No Xbox app games found. Games must be installed by the Xbox app into a folder like C:\\XboxGames (Xbox app > Settings > Install options).";
        }

        string StatusOf(Row r, HashSet<string> on)
        {
            var g = r.Game;
            if (!g.Writable) return "can't be modded where it's installed";
            if (!on.Contains(g.Content)) return r.Cf == null ? "not on CurseForge (or pick it)" : "not connected";
            if (!Bridge.LinkOk(g)) return "connected - needs repair";
            if (Bridge.PacksFor(g, packs).Count > 0 && Bridge.HasUe4ss(g) && !Bridge.FixesOk(g, packs)) return "connected - needs repair";
            if (cf?.InstanceFor(g) == null) return "ready - add it in CurseForge (Locate Game)";
            return "connected";
        }

        void Selected()
        {
            var r = Current;
            if (r == null) return;
            syncing = true;
            cfPick.SelectedItem = r.Cf;
            syncing = false;
            var g = r.Game;
            var lines = new List<string>
            {
                g.DisplayName + "   " + g.Content,
                "Engine: " + g.Engine + (g.UeProject != null ? ", project " + g.UeProject + ", runs from Binaries\\" + g.UeBinaries : ""),
            };
            if (r.Cf != null) lines.Add("CurseForge: " + r.Cf.Name + " (installs mods into " + string.Join(", ", r.Cf.ModPaths.Distinct()) + ")");
            var fp = Bridge.PacksFor(g, packs);
            if (fp.Count > 0) lines.Add("Xbox fixes: " + string.Join("; ", fp.Select(p => p.Name)));
            else if (g.Engine == "Unreal")
                lines.Add("No Xbox fix is known for this game. If UE4SS mods crash or don't load on Xbox, check ue4ss\\UE4SS.log - the Xbox build may need UE4SS signatures (a fixpacks.json next to this program can add them).");
            if (r.Cf == null)
                lines.Add("CurseForge game not recognised. Pick it in the list below. Only games CurseForge has shown you appear there - open the game's page in the CurseForge app once, close the app, then click Refresh.");
            info.Text = string.Join(Environment.NewLine, lines);
        }

        void DoConnect()
        {
            var r = Current;
            if (r == null) return;
            var cg = r.Cf;
            if (cg == null) { info.Text = "Pick the matching CurseForge game first."; return; }
            if (cf == null) { info.Text = "CurseForge data not found."; return; }
            var (ok, msg) = Actions.Connect(r.Game, cg, cf, packs);
            info.Text = msg;
            if (ok) try { Clipboard.SetText(r.Game.Content); } catch { }
            MessageBox.Show(this, msg, ok ? "Connected" : "Not connected", MessageBoxButton.OK, ok ? MessageBoxImage.Information : MessageBoxImage.Warning);
            RefreshStatus();
        }

        void DoDisconnect()
        {
            var r = Current;
            if (r == null) return;
            info.Text = Actions.Disconnect(r.Game);
            RefreshStatus();
        }

        void DoRepair(bool report)
        {
            Bridge.Log = "";
            int n = Bridge.RepairAll(XboxScanner.Scan(roots), packs ?? Bridge.LoadFixPacks());
            if (report || n > 0) info.Text = n == 0 ? "Nothing needed repairing." : Bridge.Log.Trim();
            if (n > 0 || report) RefreshStatus();
        }

        void RefreshStatus()
        {
            cf?.Reload();
            var on = Bridge.Bridged();
            // Reload builds new game objects: point the picker and every row at the new ones
            syncing = true;
            cfPick.ItemsSource = cf?.Games ?? new List<CfGame>();
            if (list.ItemsSource is List<Row> rows)
                foreach (var r in rows)
                {
                    if (r.Cf != null) r.Cf = cf?.Games.FirstOrDefault(x => x.Id == r.Cf.Id) ?? r.Cf;
                    r.Status = StatusOf(r, on);
                }
            cfPick.SelectedItem = Current?.Cf;
            syncing = false;
            list.Items.Refresh();
        }
    }
}
