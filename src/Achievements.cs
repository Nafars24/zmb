using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using System.Xml;
using Microsoft.Win32;

namespace ZMB
{
    public class Achv
    {
        public string Id = "";
        public string Name = "";
        public string Desc = "";
        public string Icon;        // URL atau path lokal
        public string IconLocked;  // versi abu-abu (kalau ada)
        public bool Unlocked;
        public bool Hidden;
        public bool Hardcore;
        public DateTime When = DateTime.MinValue;
        public string Grade;       // PS3: P G S B
        public int Points;
        public double Rarity = -1; // Steam: % pemain global
        public int Order;
    }

    public class AchvSet
    {
        public string Source = "";
        public string Title = "";
        public string Icon;
        public string Extra = "";
        public List<Achv> Items = new List<Achv>();
        public string Error;
        public int ResolvedRaId;
        public int Unlocked { get { return Items.Count(a => a.Unlocked); } }
        public int Total { get { return Items.Count; } }
        public int Percent { get { return Total == 0 ? 0 : (int)Math.Round(100.0 * Unlocked / Total); } }
        public int CountGrade(string g, bool unlockedOnly) { return Items.Count(a => a.Grade == g && (!unlockedOnly || a.Unlocked)); }

        public static AchvSet Fail(string source, string msg)
        {
            AchvSet s = new AchvSet();
            s.Source = source; s.Error = msg;
            return s;
        }
    }

    public class AchvGameRef
    {
        public string Key;
        public string Title;
        public string Icon;
        public string Sub;
        public int Got = -1, Total = -1;
        public Func<AchvSet> Fetch;
    }

    public static class Achievements
    {
        static JavaScriptSerializer js = new JavaScriptSerializer();
        static Achievements() { js.MaxJsonLength = int.MaxValue; }

        // ===================== HTTP & JSON =====================
        public static string Get(string url)
        {
            ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072; // TLS 1.2
            using (WebClient wc = new WebClient())
            {
                wc.Headers[HttpRequestHeader.UserAgent] = "ZMB/2.1 (Windows launcher)";
                wc.Encoding = Encoding.UTF8;
                return wc.DownloadString(url);
            }
        }

        static object Json(string url) { return js.DeserializeObject(Get(url)); }
        static object P(object o, string k)
        {
            Dictionary<string, object> d = o as Dictionary<string, object>;
            object v;
            return (d != null && d.TryGetValue(k, out v)) ? v : null;
        }
        static string S(object o, string k)
        {
            object v = P(o, k);
            return v == null ? "" : Convert.ToString(v, CultureInfo.InvariantCulture);
        }
        static long L(object o, string k)
        {
            long r; long.TryParse(S(o, k), NumberStyles.Integer, CultureInfo.InvariantCulture, out r); return r;
        }
        static int I(object o, string k) { return (int)L(o, k); }
        static double D(object o, string k)
        {
            double r;
            if (!double.TryParse(S(o, k), NumberStyles.Float, CultureInfo.InvariantCulture, out r)) r = -1;
            return r;
        }
        static object[] A(object o, string k) { return P(o, k) as object[] ?? new object[0]; }

        static string HttpError(WebException ex, out int code)
        {
            code = 0;
            HttpWebResponse r = ex.Response as HttpWebResponse;
            if (r != null) code = (int)r.StatusCode;
            return ex.Message;
        }

        static DateTime FromUnix(long s)
        {
            if (s <= 0) return DateTime.MinValue;
            return new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(s).ToLocalTime();
        }

        public static string Norm(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            s = s.ToLowerInvariant();
            s = Regex.Replace(s, @"\([^)]*\)|\[[^\]]*\]", " ");
            s = s.Replace("&", " and ");
            s = Regex.Replace(s, @"\bthe\b", " ");
            s = Regex.Replace(s, @"[^a-z0-9]", "");
            return s;
        }

        static string CacheDir()
        {
            string d = Path.Combine(Config.BaseDir, "cache");
            Directory.CreateDirectory(d);
            return d;
        }

        public static void ClearCache()
        {
            try { Directory.Delete(Path.Combine(Config.BaseDir, "cache"), true); } catch { }
        }

        // ===================== STEAM =====================
        static string detectedSteamId = null;

        public static string DetectSteamId()
        {
            if (detectedSteamId != null) return detectedSteamId;
            detectedSteamId = "";
            try
            {
                string steam = null;
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam"))
                    if (k != null) steam = k.GetValue("SteamPath") as string;
                if (steam == null) return detectedSteamId;
                string f = Path.Combine(steam.Replace('/', '\\'), @"config\loginusers.vdf");
                if (!File.Exists(f)) return detectedSteamId;
                string t = File.ReadAllText(f);
                Match m = Regex.Match(t, "\"(\\d{17})\"\\s*\\{[^}]*?\"MostRecent\"\\s*\"1\"", RegexOptions.Singleline);
                if (!m.Success) m = Regex.Match(t, "\"(\\d{17})\"");
                if (m.Success) detectedSteamId = m.Groups[1].Value;
            }
            catch { }
            return detectedSteamId;
        }

        public static string SteamHeader(string appId)
        {
            return "https://cdn.cloudflare.steamstatic.com/steam/apps/" + appId + "/header.jpg";
        }

        public static AchvSet Steam(string appId, string key, string steamId)
        {
            if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(steamId))
                return AchvSet.Fail("Steam", Lang.T("Steam API Key / Steam ID belum diatur (Pengaturan > Akun Trofi)"));
            AchvSet set = new AchvSet();
            set.Source = "Steam";
            set.Icon = SteamHeader(appId);
            object pa;
            try
            {
                pa = Json("https://api.steampowered.com/ISteamUserStats/GetPlayerAchievements/v1/?appid=" + appId +
                    "&key=" + Uri.EscapeDataString(key) + "&steamid=" + steamId + "&l=indonesian");
            }
            catch (WebException ex)
            {
                int code; string msg = HttpError(ex, out code);
                if (code == 403) return AchvSet.Fail("Steam", Lang.T("Ditolak Steam: cek API key, dan set 'Game details' profil ke Public"));
                if (code == 400) return AchvSet.Fail("Steam", Lang.T("Game ini tidak punya achievement Steam"));
                return AchvSet.Fail("Steam", Lang.T("Gagal terhubung ke Steam: ") + msg);
            }
            object ps = P(pa, "playerstats");
            set.Title = S(ps, "gameName");
            if (S(ps, "success").ToLowerInvariant() == "false")
                return AchvSet.Fail("Steam", S(ps, "error").Length > 0 ? S(ps, "error") : Lang.T("Data achievement tidak tersedia"));

            Dictionary<string, Achv> map = new Dictionary<string, Achv>();
            int order = 0;
            foreach (object a in A(ps, "achievements"))
            {
                Achv x = new Achv();
                x.Id = S(a, "apiname");
                x.Name = S(a, "name");
                x.Desc = S(a, "description");
                x.Unlocked = I(a, "achieved") == 1;
                x.When = FromUnix(L(a, "unlocktime"));
                x.Order = order++;
                map[x.Id] = x;
                set.Items.Add(x);
            }
            if (set.Items.Count == 0) return AchvSet.Fail("Steam", Lang.T("Game ini tidak punya achievement Steam"));

            try
            {
                object sc = Json("https://api.steampowered.com/ISteamUserStats/GetSchemaForGame/v2/?key=" + Uri.EscapeDataString(key) + "&appid=" + appId + "&l=indonesian");
                object game = P(sc, "game");
                if (set.Title.Length == 0) set.Title = S(game, "gameName");
                foreach (object a in A(P(game, "availableGameStats"), "achievements"))
                {
                    Achv x;
                    if (!map.TryGetValue(S(a, "name"), out x)) continue;
                    if (x.Name.Length == 0) x.Name = S(a, "displayName");
                    if (x.Desc.Length == 0) x.Desc = S(a, "description");
                    x.Icon = S(a, "icon");
                    x.IconLocked = S(a, "icongray");
                    x.Hidden = I(a, "hidden") == 1;
                }
            }
            catch { }

            try
            {
                object gp = Json("https://api.steampowered.com/ISteamUserStats/GetGlobalAchievementPercentagesForApp/v2/?gameid=" + appId);
                foreach (object a in A(P(gp, "achievementpercentages"), "achievements"))
                {
                    Achv x;
                    if (map.TryGetValue(S(a, "name"), out x)) x.Rarity = D(a, "percent");
                }
            }
            catch { }
            return set;
        }

        public static List<AchvGameRef> SteamOwned(string key, string steamId)
        {
            if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(steamId)) throw new Exception(Lang.T("Steam API Key / Steam ID belum diatur"));
            object r;
            try
            {
                r = Json("https://api.steampowered.com/IPlayerService/GetOwnedGames/v1/?key=" + Uri.EscapeDataString(key) +
                    "&steamid=" + steamId + "&include_appinfo=1&include_played_free_games=1&format=json");
            }
            catch (WebException ex)
            {
                int code; string msg = HttpError(ex, out code);
                if (code == 401 || code == 403) throw new Exception(Lang.T("Ditolak Steam: cek API key & Steam ID"));
                throw new Exception(Lang.T("Gagal terhubung ke Steam: ") + msg);
            }
            object resp = P(r, "response");
            object[] games = A(resp, "games");
            if (games.Length == 0 && P(resp, "game_count") == null)
                throw new Exception(Lang.T("Daftar game kosong. Set 'Game details' profil Steam ke Public"));
            List<KeyValuePair<long, AchvGameRef>> tmp = new List<KeyValuePair<long, AchvGameRef>>();
            foreach (object g in games)
            {
                if (S(g, "has_community_visible_stats").ToLowerInvariant() != "true") continue;
                string app = S(g, "appid");
                long mins = L(g, "playtime_forever");
                AchvGameRef a = new AchvGameRef();
                a.Key = "steam:" + app;
                a.Title = S(g, "name");
                SteamGame local = GameScanner.FindSteam(app);
                a.Icon = (local != null && local.Icon != null) ? local.Icon : SteamHeader(app);
                a.Sub = mins > 0 ? (Lang.T("Dimainkan ") + (mins >= 60 ? (mins / 60) + Lang.T(" jam") : mins + Lang.T(" menit"))) : Lang.T("Belum dimainkan");
                string sk = key, sid = steamId, ap = app;
                a.Fetch = delegate { return Steam(ap, sk, sid); };
                tmp.Add(new KeyValuePair<long, AchvGameRef>(mins, a));
            }
            return tmp.OrderByDescending(x => x.Key).Select(x => x.Value).ToList();
        }

        // ===================== RETROACHIEVEMENTS =====================
        const string RA = "https://retroachievements.org/API/";
        const string RAMEDIA = "https://media.retroachievements.org";

        static string RaUrl(string ep, string user, string key, string extra)
        {
            return RA + ep + "?y=" + Uri.EscapeDataString(key ?? "") + (user != null ? "&u=" + Uri.EscapeDataString(user) : "") + extra;
        }

        public static int DefaultRaConsole(string shrt)
        {
            switch ((shrt ?? "").ToUpperInvariant())
            {
                case "PS1": return 12;
                case "PS2": return 21;
                case "PSP": return 41;
                case "NES": return 7;
                case "SNES": return 3;
                case "N64": return 2;
                case "GBA": return 5;
                case "NDS": return 18;
                case "GCN": return 16;
                case "MD": return 1;
                case "ARC": return 27;
            }
            return 0;
        }

        public static int RaConsoleFor(PlatformCfg p, string rom)
        {
            if (p == null) return 0;
            int c = p.RaConsole >= 0 ? p.RaConsole : DefaultRaConsole(p.Short);
            if (c == 5 && rom != null)
            {
                string e = Path.GetExtension(rom).ToLowerInvariant();
                if (e == ".gb") return 4;
                if (e == ".gbc") return 6;
            }
            return c;
        }

        static DateTime ParseRaDate(string s)
        {
            DateTime d;
            if (!string.IsNullOrEmpty(s) && DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out d))
                return DateTime.SpecifyKind(d, DateTimeKind.Utc).ToLocalTime();
            return DateTime.MinValue;
        }

        public static AchvSet RetroGame(int gameId, string user, string key)
        {
            if (string.IsNullOrEmpty(user) || string.IsNullOrEmpty(key))
                return AchvSet.Fail("RetroAchievements", Lang.T("Username / Web API Key RetroAchievements belum diatur"));
            object g;
            try { g = Json(RaUrl("API_GetGameInfoAndUserProgress.php", user, key, "&g=" + gameId)); }
            catch (WebException ex)
            {
                int code; string msg = HttpError(ex, out code);
                if (code == 401 || code == 403) return AchvSet.Fail("RetroAchievements", Lang.T("Ditolak RetroAchievements: cek username & Web API Key"));
                return AchvSet.Fail("RetroAchievements", Lang.T("Gagal terhubung: ") + msg);
            }
            AchvSet set = new AchvSet();
            set.Source = "RetroAchievements";
            set.Title = S(g, "Title");
            set.Extra = S(g, "ConsoleName");
            set.ResolvedRaId = gameId;
            string icon = S(g, "ImageIcon");
            if (icon.Length > 0) set.Icon = RAMEDIA + icon;
            Dictionary<string, object> ach = P(g, "Achievements") as Dictionary<string, object>;
            if (ach != null)
            {
                foreach (KeyValuePair<string, object> kv in ach)
                {
                    object a = kv.Value;
                    Achv x = new Achv();
                    x.Id = S(a, "ID");
                    x.Name = S(a, "Title");
                    x.Desc = S(a, "Description");
                    x.Points = I(a, "Points");
                    x.Order = I(a, "DisplayOrder");
                    string badge = S(a, "BadgeName");
                    if (badge.Length > 0)
                    {
                        x.Icon = RAMEDIA + "/Badge/" + badge + ".png";
                        x.IconLocked = RAMEDIA + "/Badge/" + badge + "_lock.png";
                    }
                    string dh = S(a, "DateEarnedHardcore"), ds = S(a, "DateEarned");
                    x.Hardcore = dh.Length > 0;
                    x.Unlocked = dh.Length > 0 || ds.Length > 0;
                    x.When = ParseRaDate(dh.Length > 0 ? dh : ds);
                    set.Items.Add(x);
                }
            }
            set.Items = set.Items.OrderBy(x => x.Order).ThenBy(x => x.Id).ToList();
            if (set.Items.Count == 0) set.Error = Lang.T("Game ini belum punya achievement di RetroAchievements");
            return set;
        }

        public static List<AchvGameRef> RetroPlayed(string user, string key)
        {
            if (string.IsNullOrEmpty(user) || string.IsNullOrEmpty(key)) throw new Exception(Lang.T("Username / Web API Key RetroAchievements belum diatur"));
            object r;
            try { r = Json(RaUrl("API_GetUserCompletionProgress.php", user, key, "&c=500")); }
            catch (WebException ex)
            {
                int code; string msg = HttpError(ex, out code);
                if (code == 401 || code == 403) throw new Exception(Lang.T("Ditolak RetroAchievements: cek username & Web API Key"));
                throw new Exception(Lang.T("Gagal terhubung: ") + msg);
            }
            List<AchvGameRef> list = new List<AchvGameRef>();
            foreach (object g in A(r, "Results"))
            {
                int id = I(g, "GameID");
                if (id <= 0) continue;
                AchvGameRef a = new AchvGameRef();
                a.Key = "ra:game:" + id;
                a.Title = S(g, "Title");
                string ic = S(g, "ImageIcon");
                a.Icon = ic.Length > 0 ? RAMEDIA + ic : null;
                a.Total = I(g, "MaxPossible");
                a.Got = Math.Max(I(g, "NumAwarded"), I(g, "NumAwardedHardcore"));
                a.Sub = S(g, "ConsoleName");
                string u = user, k = key; int gid = id;
                a.Fetch = delegate { return RetroGame(gid, u, k); };
                list.Add(a);
            }
            return list;
        }

        public static string RetroProfile(string user, string key)
        {
            object p = Json(RaUrl("API_GetUserProfile.php", user, key, ""));
            string pts = S(p, "TotalPoints"), soft = S(p, "TotalSoftcorePoints"), tru = S(p, "TotalTruePoints");
            return "Hardcore " + (pts.Length > 0 ? pts : "0") + Lang.T(" poin  \u2022  Softcore ") + (soft.Length > 0 ? soft : "0") + "  \u2022  RetroPoint " + (tru.Length > 0 ? tru : "0");
        }

        public static AchvSet RetroForRom(string rom, string name, int consoleId, int knownId, string user, string key)
        {
            if (string.IsNullOrEmpty(user) || string.IsNullOrEmpty(key))
                return AchvSet.Fail("RetroAchievements", Lang.T("Username / Web API Key RetroAchievements belum diatur"));
            int id = knownId;
            if (id <= 0)
            {
                try { id = ResolveRaId(rom, name, consoleId, key); }
                catch (Exception ex) { return AchvSet.Fail("RetroAchievements", Lang.T("Gagal mencari game: ") + ex.Message); }
            }
            if (id <= 0) return AchvSet.Fail("RetroAchievements", Lang.T("Game tidak ditemukan di RetroAchievements. Atur ID game lewat menu Opsi (Tab)."));
            return RetroGame(id, user, key);
        }

        static object[] RaGameList(int cid, string key)
        {
            string file = Path.Combine(CacheDir(), "ra_games_" + cid + ".json");
            string text = null;
            if (File.Exists(file) && (DateTime.Now - File.GetLastWriteTime(file)).TotalDays < 7) text = File.ReadAllText(file);
            if (text == null)
            {
                text = Get(RaUrl("API_GetGameList.php", null, key, "&i=" + cid + "&h=1&f=1"));
                try { File.WriteAllText(file, text); } catch { }
            }
            return js.DeserializeObject(text) as object[] ?? new object[0];
        }

        static int ResolveRaId(string rom, string name, int cid, string key)
        {
            object[] list = RaGameList(cid, key);
            string hash = null;
            try { hash = RaHash(rom, cid); } catch { }
            if (hash != null)
                foreach (object g in list)
                    foreach (object h in A(g, "Hashes"))
                        if (string.Equals(Convert.ToString(h), hash, StringComparison.OrdinalIgnoreCase)) return I(g, "ID");

            string n1 = Norm(name), n2 = Norm(Path.GetFileNameWithoutExtension(rom));
            int bestId = 0; double best = 0;
            foreach (object g in list)
            {
                string t = S(g, "Title");
                if (t.StartsWith("~") || t.Contains("[Subset")) continue;
                string nt = Norm(t);
                if (nt.Length == 0) continue;
                if (nt == n1 || nt == n2) return I(g, "ID");
                foreach (string n in new string[] { n1, n2 })
                {
                    if (n.Length < 4) continue;
                    if (n.Contains(nt) || nt.Contains(n))
                    {
                        double ratio = (double)Math.Min(n.Length, nt.Length) / Math.Max(n.Length, nt.Length);
                        if (ratio > best) { best = ratio; bestId = I(g, "ID"); }
                    }
                }
            }
            return best >= 0.72 ? bestId : 0;
        }

        // Hash ROM sesuai aturan RetroAchievements (hanya sistem kartrid sederhana)
        static string RaHash(string rom, int cid)
        {
            if (cid == 27) return Md5(Encoding.UTF8.GetBytes(Path.GetFileNameWithoutExtension(rom)));
            int[] simple = { 1, 2, 3, 4, 5, 6, 7, 11, 15, 25, 28, 33 };
            if (!simple.Contains(cid)) return null;
            string ext = Path.GetExtension(rom).ToLowerInvariant();
            if (ext == ".smd") return null;
            byte[] data = ReadRomBytes(rom);
            if (data == null || data.Length == 0) return null;
            int off = 0;
            if (cid == 7 && data.Length > 16 && ((data[0] == 'N' && data[1] == 'E' && data[2] == 'S') || (data[0] == 'F' && data[1] == 'D' && data[2] == 'S')) && data[3] == 0x1A) off = 16;
            if (cid == 3 && data.Length % 8192 == 512) off = 512;
            if (cid == 2 && data.Length >= 4)
            {
                if (data[0] == 0x37) { for (int i = 0; i + 1 < data.Length; i += 2) { byte t = data[i]; data[i] = data[i + 1]; data[i + 1] = t; } }
                else if (data[0] == 0x40) { for (int i = 0; i + 3 < data.Length; i += 4) { byte a = data[i], b = data[i + 1]; data[i] = data[i + 3]; data[i + 1] = data[i + 2]; data[i + 2] = b; data[i + 3] = a; } }
            }
            using (MD5 md5 = MD5.Create())
                return Hex(md5.ComputeHash(data, off, data.Length - off));
        }

        static byte[] ReadRomBytes(string rom)
        {
            const long max = 64L * 1024 * 1024;
            if (Path.GetExtension(rom).ToLowerInvariant() == ".zip")
            {
                using (FileStream fs = File.OpenRead(rom))
                using (ZipArchive z = new ZipArchive(fs, ZipArchiveMode.Read))
                {
                    ZipArchiveEntry e = z.Entries.FirstOrDefault(x => x.Length > 0);
                    if (e == null || e.Length > max) return null;
                    using (Stream s = e.Open())
                    using (MemoryStream ms = new MemoryStream())
                    {
                        s.CopyTo(ms);
                        return ms.ToArray();
                    }
                }
            }
            FileInfo fi = new FileInfo(rom);
            if (fi.Length > max) return null;
            return File.ReadAllBytes(rom);
        }

        static string Md5(byte[] b) { using (MD5 m = MD5.Create()) return Hex(m.ComputeHash(b)); }
        static string Hex(byte[] h)
        {
            StringBuilder sb = new StringBuilder();
            foreach (byte x in h) sb.Append(x.ToString("x2"));
            return sb.ToString();
        }

        // ===================== RPCS3 (trofi PS3 lokal) =====================
        static string rootCache = null;
        static DateTime rootCacheAt = DateTime.MinValue;

        public static string Rpcs3Root(Config cfg)
        {
            if ((DateTime.Now - rootCacheAt).TotalSeconds < 3) return rootCache;
            rootCache = Rpcs3RootScan(cfg);
            rootCacheAt = DateTime.Now;
            return rootCache;
        }

        public static void ResetRootCache() { rootCacheAt = DateTime.MinValue; }

        static string Rpcs3RootScan(Config cfg)
        {
            if (!string.IsNullOrEmpty(cfg.Rpcs3Folder) && Directory.Exists(Path.Combine(cfg.Rpcs3Folder, "dev_hdd0"))) return cfg.Rpcs3Folder;
            foreach (PlatformCfg p in cfg.Platforms)
            {
                if (string.IsNullOrEmpty(p.Emulator)) continue;
                bool isRpcs3 = p.Short == "PS3" || Path.GetFileName(p.Emulator).ToLowerInvariant().Contains("rpcs3");
                if (!isRpcs3) continue;
                try
                {
                    string d = Path.GetDirectoryName(p.Emulator);
                    if (Directory.Exists(Path.Combine(d, "dev_hdd0"))) return d;
                }
                catch { }
            }
            return null;
        }

        public static List<string> Rpcs3TrophyDirs(string root)
        {
            List<string> res = new List<string>();
            try
            {
                string home = Path.Combine(root, @"dev_hdd0\home");
                if (!Directory.Exists(home)) return res;
                foreach (string user in Directory.GetDirectories(home))
                {
                    string tro = Path.Combine(user, "trophy");
                    if (!Directory.Exists(tro)) continue;
                    foreach (string d in Directory.GetDirectories(tro))
                        if (File.Exists(Path.Combine(d, "TROPCONF.SFM")) || File.Exists(Path.Combine(d, "TROP.SFM"))) res.Add(d);
                }
            }
            catch { }
            return res;
        }

        static XmlDocument LoadSfm(string file)
        {
            if (!File.Exists(file)) return null;
            string t = File.ReadAllText(file, Encoding.UTF8);
            int a = t.IndexOf("<trophyconf", StringComparison.Ordinal);
            int b = t.LastIndexOf("</trophyconf>", StringComparison.Ordinal);
            if (a < 0 || b < a) return null;
            t = t.Substring(a, b - a + "</trophyconf>".Length);
            XmlDocument doc = new XmlDocument();
            doc.XmlResolver = null;
            doc.LoadXml(t);
            return doc;
        }

        static uint BE32(byte[] d, long p) { return (uint)(d[p] << 24 | d[p + 1] << 16 | d[p + 2] << 8 | d[p + 3]); }
        static ulong BE64(byte[] d, long p) { return ((ulong)BE32(d, p) << 32) | BE32(d, p + 4); }

        // Membaca status unlock dari TROPUSR.DAT (format RPCS3/PS3, big-endian)
        static Dictionary<int, DateTime> ReadTropusr(string file)
        {
            Dictionary<int, DateTime> res = new Dictionary<int, DateTime>();
            if (!File.Exists(file)) return res;
            byte[] d = File.ReadAllBytes(file);
            if (d.Length < 48 || BE32(d, 0) != 0x818F54AD) return res;
            int tables = (int)BE32(d, 8);
            long pos = 48;
            for (int t = 0; t < tables && pos + 32 <= d.Length; t++, pos += 32)
            {
                uint type = BE32(d, pos);
                uint count = BE32(d, pos + 12);
                long off = (long)BE64(d, pos + 16);
                if (type != 6) continue;
                long p = off;
                for (uint i = 0; i < count && p + 40 <= d.Length; i++)
                {
                    uint esize = BE32(d, p + 4);
                    int id = (int)BE32(d, p + 16);
                    uint state = BE32(d, p + 20);
                    ulong ts = BE64(d, p + 32);
                    if (state != 0)
                    {
                        DateTime when = DateTime.MinValue;
                        try
                        {
                            // tick = mikrodetik sejak 0001-01-01 (CellRtcTick)
                            if (ts > 0 && ts < (ulong)(DateTime.MaxValue.Ticks / 10))
                            {
                                DateTime w = new DateTime((long)ts * 10, DateTimeKind.Utc);
                                if (w.Year >= 2006 && w.Year <= 2100) when = w.ToLocalTime();
                            }
                        }
                        catch { }
                        res[id] = when;
                    }
                    p += 16 + (esize > 0 ? esize : 0x60);
                }
            }
            return res;
        }

        public static string Rpcs3Title(string dir)
        {
            try
            {
                foreach (string f in new string[] { "TROPCONF.SFM", "TROP.SFM" })
                {
                    XmlDocument doc = LoadSfm(Path.Combine(dir, f));
                    if (doc == null) continue;
                    XmlNode n = doc.SelectSingleNode("/trophyconf/title-name");
                    if (n != null && n.InnerText.Trim().Length > 0) return n.InnerText.Trim();
                }
            }
            catch { }
            return Path.GetFileName(dir);
        }

        public static AchvSet Rpcs3Set(string dir)
        {
            AchvSet set = new AchvSet();
            set.Source = "RPCS3";
            set.Title = Path.GetFileName(dir);
            string icon = Path.Combine(dir, "ICON0.PNG");
            if (File.Exists(icon)) set.Icon = icon;
            set.Extra = Path.GetFileName(dir);
            XmlNodeList trophies = null;
            foreach (string f in new string[] { "TROPCONF.SFM", "TROP.SFM" })
            {
                XmlDocument doc = null;
                try { doc = LoadSfm(Path.Combine(dir, f)); } catch { doc = null; }
                if (doc == null) continue;
                XmlNode tn = doc.SelectSingleNode("/trophyconf/title-name");
                if (tn != null && tn.InnerText.Trim().Length > 0) set.Title = tn.InnerText.Trim();
                XmlNodeList list = doc.SelectNodes("/trophyconf/trophy");
                if (list == null || list.Count == 0) continue;
                XmlNode first = list[0].SelectSingleNode("name");
                trophies = list;
                if (first != null && first.InnerText.Trim().Length > 0) break; // punya nama -> pakai ini
            }
            if (trophies == null) return AchvSet.Fail("RPCS3", Lang.T("Data trofi tidak bisa dibaca"));
            Dictionary<int, DateTime> unlocked = ReadTropusr(Path.Combine(dir, "TROPUSR.DAT"));
            foreach (XmlNode t in trophies)
            {
                Achv x = new Achv();
                string ids = t.Attributes["id"] != null ? t.Attributes["id"].Value : "0";
                int id; int.TryParse(ids, out id);
                x.Id = id.ToString();
                x.Order = id;
                x.Grade = t.Attributes["ttype"] != null ? t.Attributes["ttype"].Value.ToUpperInvariant() : "B";
                x.Hidden = t.Attributes["hidden"] != null && t.Attributes["hidden"].Value == "yes";
                XmlNode nm = t.SelectSingleNode("name"), dt = t.SelectSingleNode("detail");
                x.Name = nm != null ? nm.InnerText.Trim() : (Lang.T("Trofi ") + id);
                x.Desc = dt != null ? dt.InnerText.Trim() : "";
                string png = Path.Combine(dir, "TROP" + id.ToString("000") + ".PNG");
                if (File.Exists(png)) x.Icon = png;
                DateTime w;
                if (unlocked.TryGetValue(id, out w)) { x.Unlocked = true; x.When = w; }
                x.Points = x.Grade == "P" ? 180 : x.Grade == "G" ? 90 : x.Grade == "S" ? 30 : 15;
                set.Items.Add(x);
            }
            return set;
        }

        public static List<AchvSet> Rpcs3All(string root)
        {
            List<AchvSet> res = new List<AchvSet>();
            foreach (string d in Rpcs3TrophyDirs(root))
            {
                try { AchvSet s = Rpcs3Set(d); if (s.Error == null) res.Add(s); } catch { }
            }
            return res.OrderBy(s => s.Title, StringComparer.OrdinalIgnoreCase).ToList();
        }

        static string FindNpCommId(string eboot)
        {
            try
            {
                string np = Ps3Meta.Get(eboot).NpCommId;
                if (!string.IsNullOrEmpty(np)) return np;
            }
            catch { }
            if (Ps3Meta.IsIso(eboot)) return null;
            try
            {
                DirectoryInfo d = new DirectoryInfo(Path.GetDirectoryName(eboot));
                for (int i = 0; i < 3 && d != null; i++, d = d.Parent)
                {
                    string tropdir = Path.Combine(d.FullName, "TROPDIR");
                    if (Directory.Exists(tropdir))
                    {
                        string[] subs = Directory.GetDirectories(tropdir);
                        if (subs.Length > 0) return Path.GetFileName(subs[0]);
                    }
                }
            }
            catch { }
            return null;
        }

        public static AchvSet Rpcs3ForGame(string eboot, string name, string root, string npOverride)
        {
            if (root == null) return AchvSet.Fail("RPCS3", Lang.T("Folder RPCS3 belum diatur"));
            List<string> dirs = Rpcs3TrophyDirs(root);
            if (dirs.Count == 0) return AchvSet.Fail("RPCS3", Lang.T("Belum ada data trofi di RPCS3. Mainkan game-nya dulu di RPCS3."));
            string np = string.IsNullOrEmpty(npOverride) ? FindNpCommId(eboot) : npOverride.Trim();
            if (!string.IsNullOrEmpty(np))
            {
                string hit = dirs.FirstOrDefault(d => Path.GetFileName(d).StartsWith(np, StringComparison.OrdinalIgnoreCase));
                if (hit != null) return Rpcs3Set(hit);
            }
            string n = Norm(name);
            string best = null; double bestR = 0;
            foreach (string d in dirs)
            {
                string t = Norm(Rpcs3Title(d));
                if (t.Length == 0) continue;
                if (t == n) { best = d; bestR = 1; break; }
                if (n.Length >= 4 && (t.Contains(n) || n.Contains(t)))
                {
                    double r = (double)Math.Min(t.Length, n.Length) / Math.Max(t.Length, n.Length);
                    if (r > bestR) { bestR = r; best = d; }
                }
            }
            if (best != null && bestR >= 0.6) return Rpcs3Set(best);
            return AchvSet.Fail("RPCS3", Lang.T("Trofi game ini belum ada. Mainkan dulu di RPCS3, atau atur NP Comm ID lewat menu Opsi."));
        }
    }
}
