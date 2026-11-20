using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace ZMB
{
    public class SteamGame
    {
        public string AppId;
        public string Name;
        public string Icon;
        public string Hero;
        public string Portrait;
    }

    public static class GameScanner
    {
        static List<SteamGame> steamCache = null;
        static DateTime steamCacheTime = DateTime.MinValue;
        public static string SteamPath = null;

        public static List<SteamGame> SteamGames(bool force)
        {
            if (!force && steamCache != null && (DateTime.Now - steamCacheTime).TotalMinutes < 5) return steamCache;
            List<SteamGame> list = new List<SteamGame>();
            try
            {
                string steam = null;
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam"))
                    if (k != null) steam = k.GetValue("SteamPath") as string;
                if (steam == null)
                {
                    string pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
                    if (Directory.Exists(Path.Combine(pf, "Steam"))) steam = Path.Combine(pf, "Steam");
                }
                if (steam != null && Directory.Exists(steam))
                {
                    steam = steam.Replace('/', '\\');
                    SteamPath = steam;
                    List<string> libs = new List<string>();
                    libs.Add(steam);
                    string vdf = Path.Combine(steam, @"steamapps\libraryfolders.vdf");
                    if (File.Exists(vdf))
                    {
                        string txt = File.ReadAllText(vdf);
                        foreach (Match m in Regex.Matches(txt, "\"path\"\\s+\"([^\"]+)\""))
                        {
                            string p = m.Groups[1].Value.Replace("\\\\", "\\");
                            if (!libs.Any(x => string.Equals(x.TrimEnd('\\'), p.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))) libs.Add(p);
                        }
                    }
                    HashSet<string> seen = new HashSet<string>();
                    string[] skipNames = { "Steamworks Common Redistributables", "Steam Linux Runtime", "Proton", "SteamVR", "Steam Controller Configs" };
                    foreach (string lib in libs)
                    {
                        string apps = Path.Combine(lib, "steamapps");
                        if (!Directory.Exists(apps)) continue;
                        foreach (string acf in Directory.GetFiles(apps, "appmanifest_*.acf"))
                        {
                            try
                            {
                                string t = File.ReadAllText(acf);
                                Match id = Regex.Match(t, "\"appid\"\\s+\"(\\d+)\"");
                                Match nm = Regex.Match(t, "\"name\"\\s+\"([^\"]+)\"");
                                if (!id.Success || !nm.Success) continue;
                                string name = nm.Groups[1].Value;
                                if (seen.Contains(id.Groups[1].Value)) continue;
                                if (skipNames.Any(s => name.StartsWith(s, StringComparison.OrdinalIgnoreCase))) continue;
                                seen.Add(id.Groups[1].Value);
                                SteamGame g = new SteamGame();
                                g.AppId = id.Groups[1].Value;
                                g.Name = name;
                                g.Icon = FindSteamArt(steam, g.AppId);
                                g.Hero = FindSteamHero(steam, g.AppId);
                                g.Portrait = FindSteamPortrait(steam, g.AppId);
                                list.Add(g);
                            }
                            catch { }
                        }
                    }
                }
            }
            catch { }
            list.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            steamCache = list; steamCacheTime = DateTime.Now;
            return list;
        }

        static string FindSteamArt(string steam, string id)
        {
            string cache = Path.Combine(steam, @"appcache\librarycache");
            string[] cands = {
                Path.Combine(cache, id + "_header.jpg"),
                Path.Combine(cache, id + "_library_600x900.jpg"),
                Path.Combine(cache, id, "header.jpg"),
                Path.Combine(cache, id, "library_600x900.jpg"),
                Path.Combine(cache, id + "_icon.jpg")
            };
            foreach (string c in cands) if (File.Exists(c)) return c;
            try
            {
                string d = Path.Combine(cache, id);
                if (Directory.Exists(d))
                {
                    string[] jpgs = Directory.GetFiles(d, "*.jpg", SearchOption.AllDirectories);
                    string h = jpgs.FirstOrDefault(x => Path.GetFileName(x).ToLowerInvariant().Contains("header"));
                    if (h != null) return h;
                    if (jpgs.Length > 0) return jpgs[0];
                }
            }
            catch { }
            return null;
        }

        static readonly string[] badExe = { "unins", "setup", "redist", "crash", "vc_", "vcredist", "dxsetup", "dotnet", "update", "report",
            "helper", "install", "uninst", "cleanup", "touchup", "ue4prereq", "prereq", "easyanticheat", "battleye", "launcherpatcher", "notification", "physx", "oalinst", "dxwebsetup" };

        // Scan folder berisi banyak game (tiap subfolder = 1 game), pilih exe terbesar
        public static List<GameEntry> ScanGameFolder(string root)
        {
            List<GameEntry> res = new List<GameEntry>();
            try
            {
                foreach (string dir in Directory.GetDirectories(root))
                {
                    List<string> exes = new List<string>();
                    CollectExe(dir, 0, exes);
                    string best = null; long bestSize = 0;
                    foreach (string e in exes)
                    {
                        string n = Path.GetFileName(e).ToLowerInvariant();
                        if (badExe.Any(b => n.Contains(b))) continue;
                        long sz = 0;
                        try { sz = new FileInfo(e).Length; } catch { }
                        if (sz > bestSize) { bestSize = sz; best = e; }
                    }
                    if (best != null)
                    {
                        GameEntry g = new GameEntry();
                        g.Name = Path.GetFileName(dir);
                        g.Path = best;
                        g.Platform = "WIN";
                        res.Add(g);
                    }
                }
            }
            catch { }
            return res;
        }

        static void CollectExe(string dir, int depth, List<string> outList)
        {
            try
            {
                outList.AddRange(Directory.GetFiles(dir, "*.exe"));
                if (depth < 3)
                    foreach (string d in Directory.GetDirectories(dir))
                    {
                        string dn = Path.GetFileName(d).ToLowerInvariant();
                        if (dn.Contains("redist") || dn.Contains("directx") || dn.Contains("_commonredist") || dn.Contains("support")) continue;
                        CollectExe(d, depth + 1, outList);
                    }
            }
            catch { }
        }

        public static string NiceName(string exePath)
        {
            try
            {
                FileVersionInfo fv = FileVersionInfo.GetVersionInfo(exePath);
                if (!string.IsNullOrEmpty(fv.ProductName) && fv.ProductName.Trim().Length > 1 && fv.ProductName.Length < 60) return fv.ProductName.Trim();
                if (!string.IsNullOrEmpty(fv.FileDescription) && fv.FileDescription.Trim().Length > 1 && fv.FileDescription.Length < 60) return fv.FileDescription.Trim();
            }
            catch { }
            return Path.GetFileNameWithoutExtension(exePath);
        }

        // Daftar ROM untuk platform (rekursif, buang .bin yang sudah punya .cue)
        public static List<string> ListRoms(PlatformCfg p)
        {
            List<string> res = new List<string>();
            if (string.IsNullOrEmpty(p.RomFolder) || !Directory.Exists(p.RomFolder)) return res;
            string[] exts = p.ExtList();
            List<string> all = MediaExt.Files(p.RomFolder, exts, true)
                .Where(f => !f.ToLowerInvariant().Contains("\\covers\\")).ToList();
            HashSet<string> sheets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            HashSet<string> m3uDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string f in all)
            {
                string e = Path.GetExtension(f).ToLowerInvariant();
                if (e == ".cue" || e == ".m3u" || e == ".gdi" || e == ".ccd")
                    sheets.Add(Path.Combine(Path.GetDirectoryName(f), Path.GetFileNameWithoutExtension(f)));
                if (e == ".m3u") m3uDirs.Add(Path.GetDirectoryName(f));
            }
            foreach (string f in all)
            {
                string e = Path.GetExtension(f).ToLowerInvariant();
                string dir = Path.GetDirectoryName(f);
                if (e == ".bin" || e == ".img" || e == ".sub")
                {
                    string baseNoTrack = Regex.Replace(Path.GetFileNameWithoutExtension(f), @"\s*\(Track\s*\d+\)\s*$", "", RegexOptions.IgnoreCase);
                    if (sheets.Contains(Path.Combine(dir, Path.GetFileNameWithoutExtension(f))) || sheets.Contains(Path.Combine(dir, baseNoTrack))) continue;
                    if (exts.Contains(".cue") && sheets.Any(s => string.Equals(Path.GetDirectoryName(s), dir, StringComparison.OrdinalIgnoreCase))) continue;
                }
                if ((e == ".cue" || e == ".chd") && m3uDirs.Contains(dir) && Regex.IsMatch(f, @"\((Disc|Disk|CD)\s*\d", RegexOptions.IgnoreCase)) continue;
                res.Add(f);
            }
            res = CleanRomList(res);
            // PS3 / RPCS3: tampilkan hanya EBOOT.BIN
            if (p.Short == "PS3")
                res = res.Where(f => Ps3Meta.IsIso(f) ||
                    (Path.GetFileName(f).Equals("EBOOT.BIN", StringComparison.OrdinalIgnoreCase) &&
                     Path.GetFileName(Path.GetDirectoryName(f)).Equals("USRDIR", StringComparison.OrdinalIgnoreCase))).ToList();
            // Switch 2 (Lichii): folder game hasil ekstrak, yang dijalankan exefs\main
            if (p.Short == "SWITCH2")
            {
                List<string> mains = new List<string>();
                FindExefs(p.RomFolder, 0, mains);
                foreach (string m in mains) if (!res.Contains(m, StringComparer.OrdinalIgnoreCase)) res.Add(m);
            }
            return res;
        }

        // Penanda disc: "(Disc 1)", "[Disc 2 of 3]", "CD2", "_Disk 1", dll.
        static readonly Regex DiscRx = new Regex(@"[\s_\-\.]*[\(\[]?(?<![A-Za-z0-9])(?:Disc|Disk|CD)[\s_\-\.]*(\d{1,2})(?:\s*(?:of|/)\s*\d{1,2})?\b[\)\]]?", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static string StripDisc(string name)
        {
            if (string.IsNullOrEmpty(name)) return name;
            string r = DiscRx.Replace(name, "");
            r = Regex.Replace(r, @"\s{2,}", " ").Trim(' ', '-', '_', '.');
            return r.Length > 0 ? r : name;
        }

        // Nomor disc dari nama file (atau nama folder induknya), 0 kalau bukan game multi-disc
        static int DiscNo(string f, out string groupKey)
        {
            groupKey = null;
            string dir = Path.GetDirectoryName(f);
            string nm = Path.GetFileNameWithoutExtension(f);
            Match m = DiscRx.Match(nm);
            if (m.Success)
            {
                groupKey = (dir + "|" + StripDisc(nm) + "|" + Path.GetExtension(f)).ToLowerInvariant();
                return int.Parse(m.Groups[1].Value);
            }
            // tiap disc di foldernya sendiri: "Game (Disc 1)\game.cue"
            string folder = Path.GetFileName(dir);
            m = DiscRx.Match(folder ?? "");
            if (m.Success)
            {
                groupKey = (Path.GetDirectoryName(dir) + "|" + StripDisc(folder) + "|" + Path.GetExtension(f)).ToLowerInvariant();
                return int.Parse(m.Groups[1].Value);
            }
            return 0;
        }

        static readonly Regex TrackRx = new Regex(@"\s*[\(\[]\s*Track\s*(\d{1,2})\s*[\)\]]", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // Nama tanpa label disc/track: "Parasite Eve (Disc 1)" -> "Parasite Eve"
        public static string CleanRomName(string name)
        {
            if (string.IsNullOrEmpty(name)) return name;
            string n = TrackRx.Replace(name, "");
            n = StripDisc(n);
            n = Regex.Replace(n, @"\s{2,}", " ").Trim();
            return n.Length > 0 ? n : name;
        }

        public static bool HasDiscOrTrackLabel(string name)
        {
            return !string.IsNullOrEmpty(name) && (TrackRx.IsMatch(name) || DiscRx.IsMatch(name));
        }

        // Rapikan daftar rom (juga dipakai untuk entri tersimpan dari versi lama):
        // - file track .bin/.img yang punya .cue -> disembunyikan
        // - "(Track 01..NN)" tanpa .cue -> cukup track terkecil
        // - game multi-disc -> cukup disc 1 (atau .m3u)
        public static List<string> CleanRomList(List<string> roms)
        {
            List<string> keep = new List<string>();
            Dictionary<string, int> trackBest = new Dictionary<string, int>();
            Dictionary<string, int> trackPos = new Dictionary<string, int>();
            foreach (string f in roms)
            {
                string e = Path.GetExtension(f).ToLowerInvariant();
                if (e == ".bin" || e == ".img")
                {
                    string dir = Path.GetDirectoryName(f) ?? "";
                    string nm = Path.GetFileNameWithoutExtension(f);
                    Match tm = TrackRx.Match(nm);
                    string bare = tm.Success ? TrackRx.Replace(nm, "").Trim() : nm;
                    bool hasSheet = false;
                    try
                    {
                        hasSheet = File.Exists(Path.Combine(dir, nm + ".cue")) || File.Exists(Path.Combine(dir, bare + ".cue"))
                            || (tm.Success && Directory.GetFiles(dir, "*.cue").Length > 0);
                    }
                    catch { }
                    if (hasSheet) continue;
                    if (tm.Success)
                    {
                        string key = (dir + "|" + bare + e).ToLowerInvariant();
                        int no = int.Parse(tm.Groups[1].Value);
                        int cur;
                        if (!trackBest.TryGetValue(key, out cur)) { trackBest[key] = no; trackPos[key] = keep.Count; keep.Add(f); }
                        else if (no < cur) { trackBest[key] = no; keep[trackPos[key]] = f; }
                        continue;
                    }
                }
                keep.Add(f);
            }
            return CollapseDiscs(keep);
        }

        // Game multi-disc cukup tampil sekali (disc 1). Kalau ada .m3u untuk game itu, .m3u yang dipakai.
        static List<string> CollapseDiscs(List<string> roms)
        {
            HashSet<string> m3uNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string f in roms)
                if (Path.GetExtension(f).Equals(".m3u", StringComparison.OrdinalIgnoreCase))
                {
                    m3uNames.Add(Path.GetDirectoryName(f) + "|" + StripDisc(Path.GetFileNameWithoutExtension(f)));
                    try { m3uNames.Add(Path.GetDirectoryName(Path.GetDirectoryName(f)) + "|" + StripDisc(Path.GetFileNameWithoutExtension(f))); } catch { }
                }
            Dictionary<string, string> best = new Dictionary<string, string>();
            Dictionary<string, int> bestNo = new Dictionary<string, int>();
            List<string> outList = new List<string>();
            foreach (string f in roms)
            {
                string key = null;
                int no = Path.GetExtension(f).Equals(".m3u", StringComparison.OrdinalIgnoreCase) ? 0 : DiscNo(f, out key);
                if (no == 0 || key == null) { outList.Add(f); continue; }
                string dir = Path.GetDirectoryName(f);
                string bare = StripDisc(Path.GetFileNameWithoutExtension(f));
                string bareFolder = StripDisc(Path.GetFileName(dir) ?? "");
                if (m3uNames.Contains(dir + "|" + bare) || m3uNames.Contains(Path.GetDirectoryName(dir) + "|" + bareFolder)) continue;
                int cur;
                if (!bestNo.TryGetValue(key, out cur))
                {
                    bestNo[key] = no; best[key] = f; outList.Add(f);
                }
                else if (no < cur)
                {
                    outList[outList.IndexOf(best[key])] = f;
                    bestNo[key] = no; best[key] = f;
                }
            }
            return outList;
        }

        // Game Switch 2 hasil ekstrak (Lichii): <folder game>\exefs\main (tanpa ekstensi)
        public static bool IsExefsMain(string f)
        {
            if (string.IsNullOrEmpty(f)) return false;
            try
            {
                return Path.GetFileName(f).Equals("main", StringComparison.OrdinalIgnoreCase) &&
                       (Path.GetFileName(Path.GetDirectoryName(f)) ?? "").Equals("exefs", StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        // Nama kunci file media (cover/logo/spine/...) untuk sebuah ROM
        public static string RomKey(string rom)
        {
            if (IsExefsMain(rom))
            {
                try { return Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(rom))); } catch { }
            }
            return Path.GetFileNameWithoutExtension(rom);
        }

        // cari <folder>\exefs\main di bawah folder ROM
        static void FindExefs(string dir, int depth, List<string> res)
        {
            try
            {
                foreach (string d in Directory.GetDirectories(dir))
                {
                    if (Path.GetFileName(d).Equals("exefs", StringComparison.OrdinalIgnoreCase))
                    {
                        string m = Path.Combine(d, "main");
                        if (File.Exists(m)) res.Add(m);
                        continue;
                    }
                    if (depth < 4) FindExefs(d, depth + 1, res);
                }
            }
            catch { }
        }

        public static string RomDisplayName(PlatformCfg p, string rom)
        {
            if (IsExefsMain(rom))
            {
                string k = RomKey(rom);
                if (!string.IsNullOrEmpty(k)) return Regex.Replace(k, @"\s*\[[^\]]*\]", "").Trim();
            }
            if (p != null && p.Short == "PS3")
            {
                try
                {
                    string t = Ps3Meta.Get(rom).Title;
                    if (!string.IsNullOrEmpty(t)) return t;
                }
                catch { }
                if (Ps3Meta.IsIso(rom)) return Regex.Replace(Path.GetFileNameWithoutExtension(rom), @"\s*\[[^\]]*\]", "").Trim();
                // .../PS3_GAME/USRDIR/EBOOT.BIN  -> nama folder game
                try
                {
                    DirectoryInfo d = new DirectoryInfo(Path.GetDirectoryName(rom));
                    while (d != null && (d.Name.Equals("USRDIR", StringComparison.OrdinalIgnoreCase) || d.Name.Equals("PS3_GAME", StringComparison.OrdinalIgnoreCase))) d = d.Parent;
                    if (d != null) return d.Name;
                }
                catch { }
            }
            string n = Path.GetFileNameWithoutExtension(rom);
            n = TrackRx.Replace(n, "").Trim();
            if (DiscRx.IsMatch(n)) n = StripDisc(n);
            else
            {
                // disc di folder sendiri: pakai nama folder tanpa penanda disc
                string folder = Path.GetFileName(Path.GetDirectoryName(rom)) ?? "";
                if (DiscRx.IsMatch(folder) && StripDisc(folder).Length > 0) n = StripDisc(folder);
            }
            n = Regex.Replace(n, @"\s*\[[^\]]*\]", "");
            return n.Trim();
        }

        public static string FindCover(PlatformCfg p, string rom, string custom)
        {
            if (!string.IsNullOrEmpty(custom) && File.Exists(custom)) return custom;
            string[] imgExt = { ".png", ".jpg", ".jpeg" };
            string name = RomKey(rom);
            List<string> dirs = new List<string>();
            string rd = Path.GetDirectoryName(rom);
            if (IsExefsMain(rom)) rd = Path.GetDirectoryName(Path.GetDirectoryName(rd)); // folder berisi folder game
            dirs.Add(rd);
            dirs.Add(Path.Combine(rd, "covers"));
            if (p != null && !string.IsNullOrEmpty(p.RomFolder)) dirs.Add(Path.Combine(p.RomFolder, "covers"));
            if (p != null) dirs.Add(Path.Combine(Config.BaseDir, "covers", p.Short));
            foreach (string d in dirs)
                foreach (string e in imgExt)
                {
                    string c = Path.Combine(d, name + e);
                    if (File.Exists(c)) return c;
                }
            // game multi-disc: cover boleh bernama tanpa "(Disc 1)"
            string bareName = StripDisc(name);
            if (bareName != name)
                foreach (string d in dirs)
                    foreach (string e in imgExt)
                    {
                        string c = Path.Combine(d, bareName + e);
                        if (File.Exists(c)) return c;
                    }
            if (p != null && p.Short == "PS3")
            {
                try
                {
                    string ic = Ps3Meta.Get(rom).Icon;
                    if (ic != null && File.Exists(ic)) return ic;
                }
                catch { }
            }
            return null;
        }

        static string FindSteamPortrait(string steam, string id)
        {
            string cache = Path.Combine(steam, @"appcache\librarycache");
            string[] cands = {
                Path.Combine(cache, id + "_library_600x900_2x.jpg"),
                Path.Combine(cache, id + "_library_600x900.jpg"),
                Path.Combine(cache, id, "library_600x900_2x.jpg"),
                Path.Combine(cache, id, "library_600x900.jpg")
            };
            foreach (string c in cands) if (File.Exists(c)) return c;
            try
            {
                string d = Path.Combine(cache, id);
                if (Directory.Exists(d))
                {
                    string h = Directory.GetFiles(d, "*.jpg", SearchOption.AllDirectories)
                        .FirstOrDefault(x => Path.GetFileName(x).ToLowerInvariant().Contains("600x900"));
                    if (h != null) return h;
                }
            }
            catch { }
            return null;
        }

        // Gambar disc game (untuk animasi boot konsol)
        public static string FindDisc(GameEntry g)
        {
            if (!string.IsNullOrEmpty(g.Disc) && File.Exists(g.Disc)) return g.Disc;
            return FindSideArt(g, "discs", "disc");
        }

        // Gambar punggung case (spine) untuk rak Coverflow
        public static string FindSpine(GameEntry g)
        {
            if (!string.IsNullOrEmpty(g.Spine) && File.Exists(g.Spine)) return g.Spine;
            return FindSideArt(g, "spines", "spine");
        }

        static string FindSideArt(GameEntry g, string folder, string suffix)
        {
            if (g.Platform == "WIN" || g.Platform == "STEAM") return null;
            string key = RomKey(g.Path);
            if (key.Equals("EBOOT", StringComparison.OrdinalIgnoreCase) || (g.Platform == "PS3" && Ps3Meta.IsIso(g.Path))) key = GameScanner.SafeName(g.Name);
            List<string> dirs = new List<string>();
            dirs.Add(Path.Combine(Path.Combine(Config.BaseDir, folder), g.Platform));
            try
            {
                string rd = Path.GetDirectoryName(g.Path);
                dirs.Add(Path.Combine(rd, folder));
                dirs.Add(Path.Combine(rd, suffix));
                string f0 = FirstExisting(new string[] { rd }, new string[] { key + "-" + suffix, key + "_" + suffix, key + " " + suffix }, ImgExt);
                if (f0 != null) return f0;
            }
            catch { }
            return FirstExisting(dirs, new string[] { key, StripDisc(key), GameScanner.SafeName(g.Name) }, ImgExt);
        }

        // Box art portrait untuk mode Coverflow (null kalau tidak ada)
        public static string FindBoxArt(GameEntry g, PlatformCfg p)
        {
            if (!string.IsNullOrEmpty(g.BoxArt) && File.Exists(g.BoxArt)) return g.BoxArt;
            if (g.Platform == "STEAM")
            {
                SteamGame sg = FindSteam(g.Path.Substring(g.Path.LastIndexOf('/') + 1));
                return sg != null ? sg.Portrait : null;
            }
            if (g.Platform == "WIN") return null;
            return FindCover(p, g.Path, g.Cover);
        }

        static string FindSteamHero(string steam, string id)
        {
            string cache = Path.Combine(steam, @"appcache\librarycache");
            string[] cands = {
                Path.Combine(cache, id + "_library_hero.jpg"),
                Path.Combine(cache, id, "library_hero.jpg")
            };
            foreach (string c in cands) if (File.Exists(c)) return c;
            try
            {
                string d = Path.Combine(cache, id);
                if (Directory.Exists(d))
                {
                    string h = Directory.GetFiles(d, "*.jpg", SearchOption.AllDirectories)
                        .FirstOrDefault(x => Path.GetFileName(x).ToLowerInvariant().Contains("hero") && !Path.GetFileName(x).ToLowerInvariant().Contains("blur"));
                    if (h != null) return h;
                }
            }
            catch { }
            return null;
        }

        public static SteamGame FindSteam(string appId)
        {
            foreach (SteamGame sg in SteamGames(false)) if (sg.AppId == appId) return sg;
            return null;
        }

        public static string SafeName(string name)
        {
            return string.Join("_", (name ?? "game").Split(Path.GetInvalidFileNameChars())).Trim();
        }

        static readonly string[] ImgExt = { ".jpg", ".jpeg", ".png" };
        static readonly string[] VidExt = { ".mp4", ".wmv", ".avi", ".m4v", ".mov", ".mkv", ".webm" };

        static string FirstExisting(IEnumerable<string> dirs, IEnumerable<string> names, string[] exts)
        {
            foreach (string d in dirs)
            {
                if (string.IsNullOrEmpty(d)) continue;
                try { if (!Directory.Exists(d)) continue; } catch { continue; }
                foreach (string n in names)
                    foreach (string e in exts)
                    {
                        string c = Path.Combine(d, n + e);
                        if (File.Exists(c)) return c;
                    }
            }
            return null;
        }

        static string KeyName(GameEntry g)
        {
            if (g.Platform == "STEAM") return g.Path.Substring(g.Path.LastIndexOf('/') + 1);
            if (g.Platform == "WIN") return SafeName(g.Name);
            return RomKey(g.Path);
        }

        // Gambar latar game. crisp = true kalau gambar memang latar (tidak perlu di-blur)
        public static string FindBackground(GameEntry g, PlatformCfg p, out bool crisp)
        {
            crisp = true;
            if (!string.IsNullOrEmpty(g.Background) && File.Exists(g.Background)) return g.Background;
            string key = KeyName(g);
            string own = Path.Combine(Config.BaseDir, "backgrounds", g.Platform);
            string[] bgNames = { key, key + "-bg", key + "_bg", key + "-background", key + "-fanart" };
            List<string> dirs = new List<string>();
            dirs.Add(own);
            if (g.Platform == "STEAM")
            {
                string f0 = FirstExisting(dirs, bgNames, ImgExt);
                if (f0 != null) return f0;
                SteamGame sg = FindSteam(key);
                if (sg != null && sg.Hero != null) return sg.Hero;
                crisp = false;
                return (sg != null) ? sg.Icon : null;
            }
            if (g.Platform == "WIN")
            {
                try
                {
                    string gd = Path.GetDirectoryName(g.Path);
                    string f1 = FirstExisting(new string[] { own }, new string[] { key }, ImgExt);
                    if (f1 != null) return f1;
                    f1 = FirstExisting(new string[] { gd }, new string[] { "background", "fanart", "zmb_bg" }, ImgExt);
                    if (f1 != null) return f1;
                }
                catch { }
                crisp = false;
                if (!string.IsNullOrEmpty(g.Cover) && File.Exists(g.Cover)) return g.Cover;
                return null;
            }
            // ROM
            string rd = Path.GetDirectoryName(g.Path);
            dirs.Add(Path.Combine(rd, "backgrounds"));
            dirs.Add(Path.Combine(rd, "fanart"));
            if (p != null && !string.IsNullOrEmpty(p.RomFolder))
            {
                dirs.Add(Path.Combine(p.RomFolder, "backgrounds"));
                dirs.Add(Path.Combine(p.RomFolder, "fanart"));
                dirs.Add(Path.Combine(p.RomFolder, "images"));
            }
            dirs.Add(rd);
            string f = FirstExisting(dirs, new string[] { key + "-bg", key + "_bg", key + "-background", key + "-fanart" }, ImgExt);
            if (f == null) f = FirstExisting(dirs.Take(dirs.Count - 1), new string[] { key }, ImgExt);
            if (f != null) return f;
            if (p != null && p.Short == "PS3")
            {
                try
                {
                    string pic = Ps3Meta.Get(g.Path).Pic1;
                    if (pic != null && File.Exists(pic)) return pic;
                }
                catch { }
            }
            crisp = false;
            return FindCover(p, g.Path, g.Cover);
        }

        // Video trailer lokal
        public static string FindTrailer(GameEntry g, PlatformCfg p)
        {
            if (!string.IsNullOrEmpty(g.Trailer) && (g.Trailer.Contains("://") || File.Exists(g.Trailer))) return g.Trailer;
            string key = KeyName(g);
            List<string> dirs = new List<string>();
            dirs.Add(Path.Combine(Config.BaseDir, "trailers", g.Platform));
            if (g.Platform == "WIN")
            {
                string f0 = FirstExisting(dirs, new string[] { key }, VidExt);
                if (f0 != null) return f0;
                try { return FirstExisting(new string[] { Path.GetDirectoryName(g.Path) }, new string[] { "trailer", "zmb_trailer" }, VidExt); }
                catch { return null; }
            }
            if (g.Platform == "STEAM") return FirstExisting(dirs, new string[] { key, SafeName(g.Name) }, VidExt);
            string rd = Path.GetDirectoryName(g.Path);
            dirs.Add(Path.Combine(rd, "trailers"));
            dirs.Add(Path.Combine(rd, "videos"));
            if (p != null && !string.IsNullOrEmpty(p.RomFolder))
            {
                dirs.Add(Path.Combine(p.RomFolder, "trailers"));
                dirs.Add(Path.Combine(p.RomFolder, "videos"));
            }
            dirs.Add(rd);
            return FirstExisting(dirs, new string[] { key, key + "-video", key + "-trailer" }, VidExt);
        }

        // Trailer online dari Steam Store (butuh internet). Mengembalikan URL mp4 atau null.
        static Dictionary<string, string> steamTrailerCache = new Dictionary<string, string>();
        static object steamTrailerLock = new object();

        public static bool TryCachedSteamTrailer(string appId, out string url)
        {
            lock (steamTrailerLock) return steamTrailerCache.TryGetValue(appId, out url);
        }

        public static string FetchSteamTrailer(string appId)
        {
            string cached;
            if (TryCachedSteamTrailer(appId, out cached)) return cached;
            string url = null;
            try
            {
                ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072; // TLS 1.2
                using (WebClient wc = new WebClient())
                {
                    wc.Headers[HttpRequestHeader.UserAgent] = "Mozilla/5.0 ZMB";
                    wc.Encoding = System.Text.Encoding.UTF8;
                    string json = wc.DownloadString("https://store.steampowered.com/api/appdetails?appids=" + appId + "&filters=movies");
                    Match m = Regex.Match(json, "\"mp4\"\\s*:\\s*\\{[^}]*?\"480\"\\s*:\\s*\"([^\"]+)\"");
                    if (!m.Success) m = Regex.Match(json, "\"mp4\"\\s*:\\s*\\{[^}]*?\"max\"\\s*:\\s*\"([^\"]+)\"");
                    if (m.Success) url = m.Groups[1].Value.Replace("\\/", "/").Replace("http://", "https://");
                }
            }
            catch { url = null; }
            lock (steamTrailerLock) steamTrailerCache[appId] = url;
            return url;
        }

        public static string BuildArgs(PlatformCfg p, string rom)
        {
            string a = string.IsNullOrEmpty(p.Args) ? "\"{rom}\"" : p.Args;
            return a.Replace("{rom}", rom)
                .Replace("{romdir}", Path.GetDirectoryName(rom))
                .Replace("{romname}", Path.GetFileNameWithoutExtension(rom))
                .Replace("{emudir}", string.IsNullOrEmpty(p.Emulator) ? "" : Path.GetDirectoryName(p.Emulator));
        }
    }
}
