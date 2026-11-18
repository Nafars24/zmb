using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace ZMB
{
    public class EmuFound
    {
        public string Name;
        public string Exe;
        public string Platform; // Short
        public string Args;
    }

    public static class EmuDetect
    {
        class Def
        {
            public string Name; public string[] Patterns; public string[] Platforms; public string Args;
            public Def(string n, string[] p, string[] pl, string a) { Name = n; Patterns = p; Platforms = pl; Args = a; }
        }

        static readonly Def[] Defs = {
            new Def("DuckStation", new[] { "duckstation" }, new[] { "PS1" }, "-fullscreen -batch \"{rom}\""),
            new Def("PCSX2", new[] { "pcsx2" }, new[] { "PS2" }, "-batch -fullscreen \"{rom}\""),
            new Def("PPSSPP", new[] { "ppssppwindows", "ppsspp" }, new[] { "PSP" }, "--fullscreen \"{rom}\""),
            new Def("RPCS3", new[] { "rpcs3" }, new[] { "PS3" }, "--no-gui \"{rom}\""),
            new Def("Vita3K", new[] { "vita3k" }, new[] { "VITA" }, "\"{rom}\""),
            new Def("Dolphin", new[] { "dolphin" }, new[] { "GCN" }, "-b -e \"{rom}\""),
            new Def("melonDS", new[] { "melonds" }, new[] { "NDS" }, "-f \"{rom}\""),
            new Def("Azahar / Citra", new[] { "azahar", "citra-qt", "citra", "lime3ds" }, new[] { "3DS" }, "\"{rom}\""),
            new Def("mGBA", new[] { "mgba" }, new[] { "GBA" }, "-f \"{rom}\""),
            new Def("Project64", new[] { "project64" }, new[] { "N64" }, "\"{rom}\""),
            new Def("simple64", new[] { "simple64-gui", "simple64" }, new[] { "N64" }, "\"{rom}\""),
            new Def("Snes9x", new[] { "snes9x" }, new[] { "SNES" }, "-fullscreen \"{rom}\""),
            new Def("Mesen", new[] { "mesen" }, new[] { "NES", "SNES" }, "\"{rom}\" --fullscreen"),
            new Def("MAME", new[] { "mame" }, new[] { "ARC" }, "-rompath \"{romdir}\" {romname}"),
            new Def("Kega Fusion", new[] { "fusion" }, new[] { "MD" }, "-fullscreen \"{rom}\""),
            new Def("RetroArch", new[] { "retroarch" }, new string[0], "")
        };

        // core RetroArch per platform (urutan = prioritas)
        static readonly Dictionary<string, string[]> RaCores = new Dictionary<string, string[]> {
            { "PS1", new[] { "swanstation", "pcsx_rearmed", "mednafen_psx_hw", "mednafen_psx" } },
            { "NES", new[] { "mesen", "nestopia", "fceumm" } },
            { "SNES", new[] { "snes9x", "bsnes", "snes9x2010" } },
            { "N64", new[] { "mupen64plus_next", "parallel_n64" } },
            { "GBA", new[] { "mgba", "vba_next", "gpsp" } },
            { "NDS", new[] { "melonds", "melondsds", "desmume" } },
            { "MD", new[] { "genesis_plus_gx", "picodrive" } },
            { "ARC", new[] { "fbneo", "mame" } },
            { "PSP", new[] { "ppsspp" } },
            { "GCN", new[] { "dolphin" } }
        };

        static readonly string[] SkipDirs = { "windows", "$recycle.bin", "system volume information", "programdata", "recovery", "perflogs", "msocache", "windowsapps", "node_modules", ".git", "appdata" };
        static readonly string[] BadWords = { "unins", "update", "crash", "setup", "install", "helper", "vc_redist", "dxsetup" };

        static Def Match(string exePath)
        {
            string n = Path.GetFileNameWithoutExtension(exePath).ToLowerInvariant();
            if (BadWords.Any(b => n.Contains(b))) return null;
            foreach (Def d in Defs)
                foreach (string p in d.Patterns)
                    if (n.StartsWith(p)) return d;
            return null;
        }

        static void Scan(string dir, int depth, int maxDepth, List<string> found, ref int budget)
        {
            if (budget <= 0) return;
            budget--;
            try
            {
                foreach (string f in Directory.GetFiles(dir, "*.exe"))
                    if (Match(f) != null) found.Add(f);
                if (depth >= maxDepth) return;
                foreach (string d in Directory.GetDirectories(dir))
                {
                    string dn = Path.GetFileName(d).ToLowerInvariant();
                    if (SkipDirs.Contains(dn)) continue;
                    try { if ((File.GetAttributes(d) & FileAttributes.ReparsePoint) != 0) continue; } catch { continue; }
                    Scan(d, depth + 1, maxDepth, found, ref budget);
                }
            }
            catch { }
        }

        public static List<EmuFound> Detect()
        {
            List<string> exes = new List<string>();
            int budget = 25000;
            string up = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            List<KeyValuePair<string, int>> roots = new List<KeyValuePair<string, int>>();
            roots.Add(new KeyValuePair<string, int>(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), 2));
            roots.Add(new KeyValuePair<string, int>(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), 2));
            roots.Add(new KeyValuePair<string, int>(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs"), 2));
            roots.Add(new KeyValuePair<string, int>(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RetroArch"), 0));
            foreach (string sub in new string[] { "Desktop", "Downloads", "Documents", "Emulators", "Emulator", "Games" })
                roots.Add(new KeyValuePair<string, int>(Path.Combine(up, sub), 3));
            foreach (DriveInfo di in DriveInfo.GetDrives())
            {
                try { if (di.DriveType == DriveType.Fixed && di.IsReady) roots.Add(new KeyValuePair<string, int>(di.RootDirectory.FullName, 3)); } catch { }
            }
            if (GameScanner.SteamPath != null) roots.Add(new KeyValuePair<string, int>(Path.Combine(GameScanner.SteamPath, @"steamapps\common"), 2));
            HashSet<string> seenRoots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, int> r in roots)
            {
                if (string.IsNullOrEmpty(r.Key) || !Directory.Exists(r.Key) || !seenRoots.Add(r.Key)) continue;
                Scan(r.Key, 0, r.Value, exes, ref budget);
            }
            // lokasi dari daftar program terinstal
            foreach (string hive in new string[] { @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall" })
            {
                foreach (RegistryKey root in new RegistryKey[] { Registry.LocalMachine, Registry.CurrentUser })
                {
                    try
                    {
                        using (RegistryKey k = root.OpenSubKey(hive))
                        {
                            if (k == null) continue;
                            foreach (string sk in k.GetSubKeyNames())
                            {
                                using (RegistryKey e = k.OpenSubKey(sk))
                                {
                                    if (e == null) continue;
                                    string loc = e.GetValue("InstallLocation") as string;
                                    if (!string.IsNullOrEmpty(loc) && Directory.Exists(loc)) { int b2 = 200; Scan(loc, 0, 1, exes, ref b2); }
                                }
                            }
                        }
                    }
                    catch { }
                }
            }

            List<EmuFound> res = new List<EmuFound>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string exe in exes)
            {
                if (!seen.Add(exe)) continue;
                Def d = Match(exe);
                if (d == null) continue;
                if (d.Name == "RetroArch")
                {
                    string cores = Path.Combine(Path.GetDirectoryName(exe), "cores");
                    if (!Directory.Exists(cores)) continue;
                    foreach (KeyValuePair<string, string[]> kv in RaCores)
                        foreach (string core in kv.Value)
                        {
                            string dll = core + "_libretro.dll";
                            if (!File.Exists(Path.Combine(cores, dll))) continue;
                            EmuFound f = new EmuFound();
                            f.Name = "RetroArch (" + core + ")";
                            f.Exe = exe; f.Platform = kv.Key;
                            f.Args = "-f -L \"cores\\" + dll + "\" \"{rom}\"";
                            res.Add(f);
                            break;
                        }
                    continue;
                }
                foreach (string pl in d.Platforms)
                {
                    EmuFound f = new EmuFound();
                    f.Name = d.Name; f.Exe = exe; f.Platform = pl; f.Args = d.Args;
                    res.Add(f);
                }
            }
            return res;
        }

        // Pilih emulator terbaik per platform: emulator khusus didahulukan, RetroArch sebagai cadangan
        public static Dictionary<string, EmuFound> BestPerPlatform(List<EmuFound> list)
        {
            Dictionary<string, EmuFound> best = new Dictionary<string, EmuFound>();
            foreach (EmuFound f in list.OrderBy(x => x.Name.StartsWith("RetroArch") ? 1 : 0))
                if (!best.ContainsKey(f.Platform)) best[f.Platform] = f;
            return best;
        }
    }

    // ===================== PEMERIKSA ROM =====================
    public class RomIssue
    {
        public string Kind;     // rusak, duplikat, cover, box, latar
        public string Path;
        public string Name;
        public string Platform;
        public string Detail;
        public GameEntry Game;
    }

    public class RomCheckInput
    {
        public GameEntry Game;
        public PlatformCfg Platform;
        public bool HasCover, HasBox, HasBg;
    }

    public static class RomChecker
    {
        public static List<RomIssue> Check(List<RomCheckInput> input)
        {
            List<RomIssue> res = new List<RomIssue>();
            Dictionary<string, List<RomCheckInput>> groups = new Dictionary<string, List<RomCheckInput>>();
            foreach (RomCheckInput ri in input)
            {
                string path = ri.Game.Path;
                FileInfo fi;
                try { fi = new FileInfo(path); } catch { continue; }
                if (!fi.Exists) { Add(res, ri, "rusak", "File tidak ditemukan"); continue; }
                string ext = fi.Extension.ToLowerInvariant();
                string problem = null;
                if (fi.Length == 0) problem = "Ukuran file 0 byte";
                else if (ext == ".cue" || ext == ".m3u" || ext == ".gdi") problem = CheckSheet(path, ext);
                else if (fi.Length < 1024 && ext != ".bin") problem = "File terlalu kecil (" + fi.Length + " byte)";
                else if (ext == ".zip") problem = CheckZip(path);
                if (problem != null) { Add(res, ri, "rusak", problem); continue; }

                if (!ri.HasCover) Add(res, ri, "cover", "Belum ada cover");
                if (!ri.HasBox) Add(res, ri, "box", "Belum ada box art");
                if (!ri.HasBg) Add(res, ri, "latar", "Belum ada gambar latar HD");

                if (ext == ".cue" || ext == ".m3u") continue;
                string key;
                if (fi.Length <= 64L * 1024 * 1024)
                {
                    try
                    {
                        using (MD5 m = MD5.Create())
                        using (FileStream fs = File.OpenRead(path))
                            key = ri.Platform.Short + ":" + BitConverter.ToString(m.ComputeHash(fs));
                    }
                    catch { key = null; }
                }
                else key = ri.Platform.Short + ":" + fi.Length + ":" + Achievements.Norm(Path.GetFileNameWithoutExtension(path));
                if (key == null) continue;
                List<RomCheckInput> l;
                if (!groups.TryGetValue(key, out l)) { l = new List<RomCheckInput>(); groups[key] = l; }
                l.Add(ri);
            }
            foreach (List<RomCheckInput> g in groups.Values)
            {
                if (g.Count < 2) continue;
                for (int i = 1; i < g.Count; i++)
                    Add(res, g[i], "duplikat", "Sama dengan: " + Path.GetFileName(g[0].Game.Path));
            }
            return res;
        }

        static void Add(List<RomIssue> res, RomCheckInput ri, string kind, string detail)
        {
            RomIssue x = new RomIssue();
            x.Kind = kind; x.Path = ri.Game.Path; x.Name = ri.Game.Name; x.Platform = ri.Platform.Short; x.Detail = detail; x.Game = ri.Game;
            res.Add(x);
        }

        static string CheckSheet(string path, string ext)
        {
            try
            {
                string dir = Path.GetDirectoryName(path);
                foreach (string line in File.ReadAllLines(path))
                {
                    string ref1 = null;
                    if (ext == ".cue")
                    {
                        Match m = Regex.Match(line, "^\\s*FILE\\s+\"?([^\"]+?)\"?\\s+\\w+\\s*$", RegexOptions.IgnoreCase);
                        if (m.Success) ref1 = m.Groups[1].Value;
                    }
                    else if (ext == ".m3u")
                    {
                        string t = line.Trim();
                        if (t.Length > 0 && !t.StartsWith("#")) ref1 = t;
                    }
                    else if (ext == ".gdi")
                    {
                        Match m = Regex.Match(line, "\\s(\"[^\"]+\"|\\S+\\.(bin|raw))\\s", RegexOptions.IgnoreCase);
                        if (m.Success) ref1 = m.Groups[1].Value.Trim('"');
                    }
                    if (ref1 != null && !File.Exists(Path.Combine(dir, ref1))) return "File pasangan hilang: " + ref1;
                }
            }
            catch (Exception ex) { return "Tidak bisa dibaca: " + ex.Message; }
            return null;
        }

        static string CheckZip(string path)
        {
            try
            {
                using (FileStream fs = File.OpenRead(path))
                using (System.IO.Compression.ZipArchive z = new System.IO.Compression.ZipArchive(fs, System.IO.Compression.ZipArchiveMode.Read))
                {
                    if (z.Entries.Count == 0) return "ZIP kosong";
                }
            }
            catch { return "ZIP rusak / tidak bisa dibuka"; }
            return null;
        }
    }
}
