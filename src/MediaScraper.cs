using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

namespace ZMB
{
    public class ScrapeCfg
    {
        public string SgdbKey = "", SsUser = "", SsPass = "", SsDevId = "", SsDevPass = "", Region = "wor";
        public bool Videos = true;
    }

    public class ScrapeJob
    {
        public GameEntry G;
        public string Name, Platform, Path, AppId, PlatShort;
        public int SsSystem;
        public bool NeedCover, NeedBg, NeedVideo, NeedLogo, NeedBox;
    }

    public class ScrapeResult
    {
        public string Cover, Background, Video, Logo, Box;
        public List<string> Errors = new List<string>();
    }

    public static class MediaScraper
    {
        static JavaScriptSerializer js = new JavaScriptSerializer();
        static MediaScraper() { js.MaxJsonLength = int.MaxValue; }

        public static int DefaultSsSystem(string shrt)
        {
            switch ((shrt ?? "").ToUpperInvariant())
            {
                case "PS1": return 57;
                case "PS2": return 58;
                case "PS3": return 59;
                case "PSP": return 61;
                case "VITA": return 62;
                case "NES": return 3;
                case "SNES": return 4;
                case "N64": return 14;
                case "GBA": return 12;
                case "NDS": return 15;
                case "3DS": return 17;
                case "GCN": return 13;
                case "MD": return 1;
                case "ARC": return 75;
            }
            return 0;
        }

        public static int SsSystemFor(PlatformCfg p, string rom)
        {
            if (p == null) return 0;
            int s = p.SsSystem >= 0 ? p.SsSystem : DefaultSsSystem(p.Short);
            string e = rom == null ? "" : System.IO.Path.GetExtension(rom).ToLowerInvariant();
            if (s == 12 && e == ".gb") return 9;
            if (s == 12 && e == ".gbc") return 10;
            if (s == 13 && (e == ".wbfs" || e == ".rvz")) return 16;
            return s;
        }

        // ===================== HTTP =====================
        static void Tls() { ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072; }

        static string GetText(string url, string bearer)
        {
            Tls();
            using (WebClient wc = new WebClient())
            {
                wc.Headers[HttpRequestHeader.UserAgent] = "ZMB/2.2 (Windows launcher)";
                if (bearer != null) wc.Headers[HttpRequestHeader.Authorization] = "Bearer " + bearer;
                wc.Encoding = Encoding.UTF8;
                try { return wc.DownloadString(url); }
                catch (WebException ex)
                {
                    string body = "";
                    try
                    {
                        if (ex.Response != null)
                            using (StreamReader r = new StreamReader(ex.Response.GetResponseStream())) body = r.ReadToEnd();
                    }
                    catch { }
                    HttpWebResponse hr = ex.Response as HttpWebResponse;
                    int code = hr != null ? (int)hr.StatusCode : 0;
                    if (code == 404) return null;
                    string msg = body.Trim();
                    if (msg.Length > 160 || msg.StartsWith("<") || msg.StartsWith("{")) msg = "";
                    throw new Exception((code > 0 ? "HTTP " + code + " " : "") + (msg.Length > 0 ? msg : ex.Message));
                }
            }
        }

        static bool Download(string url, string dest)
        {
            Tls();
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(dest));
            string tmp = dest + ".part";
            using (WebClient wc = new WebClient())
            {
                wc.Headers[HttpRequestHeader.UserAgent] = "ZMB/2.2 (Windows launcher)";
                wc.DownloadFile(url, tmp);
            }
            FileInfo fi = new FileInfo(tmp);
            if (!fi.Exists || fi.Length < 512) { try { File.Delete(tmp); } catch { } return false; }
            // ScreenScraper kadang mengembalikan teks error, bukan file
            byte[] head = new byte[16];
            using (FileStream fs = File.OpenRead(tmp)) fs.Read(head, 0, head.Length);
            string hs = Encoding.ASCII.GetString(head);
            if (hs.StartsWith("Erreur") || hs.StartsWith("<!DOCTYPE") || hs.StartsWith("<html") || hs.StartsWith("NOMEDIA") || hs.StartsWith("CRCOK"))
            {
                try { File.Delete(tmp); } catch { }
                return false;
            }
            if (File.Exists(dest)) File.Delete(dest);
            File.Move(tmp, dest);
            return true;
        }

        static object P(object o, string k)
        {
            Dictionary<string, object> d = o as Dictionary<string, object>;
            object v;
            return (d != null && d.TryGetValue(k, out v)) ? v : null;
        }
        static string S(object o, string k) { object v = P(o, k); return v == null ? "" : Convert.ToString(v, CultureInfo.InvariantCulture); }
        static object[] A(object o, string k) { return P(o, k) as object[] ?? new object[0]; }

        static string ExtFromUrl(string url, string fallback)
        {
            try
            {
                string e = System.IO.Path.GetExtension(new Uri(url).AbsolutePath).ToLowerInvariant();
                if (e == ".png" || e == ".jpg" || e == ".jpeg" || e == ".webp" || e == ".mp4") return e;
            }
            catch { }
            return fallback;
        }

        static string Dest(string kind, ScrapeJob j, string ext)
        {
            string key = j.Platform == "STEAM" ? (j.AppId ?? GameScanner.SafeName(j.Name)) :
                         j.Platform == "WIN" ? GameScanner.SafeName(j.Name) :
                         System.IO.Path.GetFileNameWithoutExtension(j.Path);
            return System.IO.Path.Combine(Config.BaseDir, kind, j.Platform, GameScanner.SafeName(key) + ext);
        }

        static string CleanName(string n)
        {
            n = System.Text.RegularExpressions.Regex.Replace(n ?? "", @"\([^)]*\)|\[[^\]]*\]", " ");
            n = n.Replace("_", " ").Replace(".", " ");
            return System.Text.RegularExpressions.Regex.Replace(n, @"\s+", " ").Trim();
        }

        // ===================== STEAMGRIDDB =====================
        const string SGDB = "https://www.steamgriddb.com/api/v2";

        static int SgdbGameId(ScrapeJob j, string key)
        {
            if (!string.IsNullOrEmpty(j.AppId))
            {
                string t = GetText(SGDB + "/games/steam/" + j.AppId, key);
                if (t != null)
                {
                    object r = js.DeserializeObject(t);
                    int id; int.TryParse(S(P(r, "data"), "id"), out id);
                    if (id > 0) return id;
                }
            }
            string name = CleanName(j.Name);
            if (name.Length < 2) return 0;
            string st = GetText(SGDB + "/search/autocomplete/" + Uri.EscapeDataString(name), key);
            if (st == null) return 0;
            object sr = js.DeserializeObject(st);
            object[] data = A(sr, "data");
            if (data.Length == 0) return 0;
            string nn = Achievements.Norm(name);
            foreach (object g in data)
                if (Achievements.Norm(S(g, "name")) == nn) { int id; int.TryParse(S(g, "id"), out id); return id; }
            int first; int.TryParse(S(data[0], "id"), out first);
            return first;
        }

        static string SgdbBest(string kind, int gameId, string query, string key)
        {
            string t = GetText(SGDB + "/" + kind + "/game/" + gameId + "?" + query, key);
            if (t == null) return null;
            object r = js.DeserializeObject(t);
            foreach (object img in A(r, "data"))
            {
                string url = S(img, "url");
                if (S(img, "nsfw").ToLowerInvariant() == "true" || S(img, "humor").ToLowerInvariant() == "true") continue;
                if (url.Length > 0) return url;
            }
            return null;
        }

        static void FromSgdb(ScrapeJob j, ScrapeCfg c, ScrapeResult res)
        {
            if (string.IsNullOrEmpty(c.SgdbKey)) return;
            if (!(j.NeedCover && res.Cover == null) && !(j.NeedBg && res.Background == null) && !(j.NeedLogo && res.Logo == null) && !(j.NeedBox && res.Box == null)) return;
            int id;
            try { id = SgdbGameId(j, c.SgdbKey); }
            catch (Exception ex) { res.Errors.Add("SteamGridDB: " + ex.Message); return; }
            if (id <= 0) { res.Errors.Add("SteamGridDB: game tidak ditemukan"); return; }
            const string common = "types=static&nsfw=false&humor=false";
            try
            {
                bool rom = j.Platform != "WIN" && j.Platform != "STEAM";
                string wide = "dimensions=920x430,460x215&" + common, tall = "dimensions=600x900,342x482,660x930&" + common;
                if (j.NeedCover && res.Cover == null)
                {
                    // PC: grid lebar untuk tile XMB.  ROM: grid portrait (mirip box art), dipakai juga untuk Coverflow
                    string u = rom ? (SgdbBest("grids", id, tall, c.SgdbKey) ?? SgdbBest("grids", id, wide, c.SgdbKey))
                                   : (SgdbBest("grids", id, wide, c.SgdbKey) ?? SgdbBest("grids", id, tall, c.SgdbKey));
                    if (u != null) { string d = Dest("covers", j, ExtFromUrl(u, ".png")); if (Download(u, d)) { res.Cover = d; if (rom && res.Box == null) res.Box = d; } }
                }
                if (j.NeedBox && res.Box == null)
                {
                    string u = SgdbBest("grids", id, tall, c.SgdbKey);
                    if (u != null) { string d = Dest("boxart", j, ExtFromUrl(u, ".png")); if (Download(u, d)) res.Box = d; }
                }
                if (j.NeedBg && res.Background == null)
                {
                    string u = SgdbBest("heroes", id, common, c.SgdbKey);
                    if (u != null) { string d = Dest("backgrounds", j, ExtFromUrl(u, ".jpg")); if (Download(u, d)) res.Background = d; }
                }
                if (j.NeedLogo && res.Logo == null)
                {
                    string u = SgdbBest("logos", id, common, c.SgdbKey);
                    if (u != null) { string d = Dest("logos", j, ExtFromUrl(u, ".png")); if (Download(u, d)) res.Logo = d; }
                }
            }
            catch (Exception ex) { res.Errors.Add("SteamGridDB: " + ex.Message); }
        }

        public static string TestSgdb(string key)
        {
            string t = GetText(SGDB + "/search/autocomplete/" + Uri.EscapeDataString("Half-Life"), key);
            return t == null ? "Tidak ada respon" : "OK";
        }

        // ===================== SCREENSCRAPER =====================
        const string SS = "https://www.screenscraper.fr/api2/";

        static string SsAuth(ScrapeCfg c)
        {
            return "devid=" + Uri.EscapeDataString(c.SsDevId ?? "") + "&devpassword=" + Uri.EscapeDataString(c.SsDevPass ?? "") +
                "&softname=ZMB&output=json&ssid=" + Uri.EscapeDataString(c.SsUser ?? "") + "&sspassword=" + Uri.EscapeDataString(c.SsPass ?? "");
        }

        static uint[] crcTable;
        static uint Crc32(byte[] data)
        {
            if (crcTable == null)
            {
                uint[] t = new uint[256];
                for (uint i = 0; i < 256; i++)
                {
                    uint c = i;
                    for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
                    t[i] = c;
                }
                crcTable = t;
            }
            uint crc = 0xFFFFFFFF;
            foreach (byte b in data) crc = crcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
            return crc ^ 0xFFFFFFFF;
        }

        public static object SsGamePublic(ScrapeJob j, ScrapeCfg c) { return SsGame(j, c); }

        static object SsGame(ScrapeJob j, ScrapeCfg c)
        {
            FileInfo fi = new FileInfo(j.Path);
            string url = SS + "jeuInfos.php?" + SsAuth(c) + "&systemeid=" + j.SsSystem + "&romtype=rom&romnom=" + Uri.EscapeDataString(fi.Name) +
                "&romtaille=" + (fi.Exists ? fi.Length : 0);
            if (fi.Exists && fi.Length <= 48L * 1024 * 1024)
            {
                byte[] data = File.ReadAllBytes(j.Path);
                using (MD5 m = MD5.Create())
                    url += "&md5=" + BitConverter.ToString(m.ComputeHash(data)).Replace("-", "");
                url += "&crc=" + Crc32(data).ToString("X8");
            }
            string t = GetText(url, null);
            if (t == null) return null;
            object r = js.DeserializeObject(t);
            return P(P(r, "response"), "jeu");
        }

        static string PickMedia(object jeu, string[] types, string region)
        {
            object[] medias = A(jeu, "medias");
            string[] regions = { region, "wor", "us", "eu", "ss", "jp", "" };
            foreach (string ty in types)
                foreach (string rg in regions)
                    foreach (object m in medias)
                    {
                        if (S(m, "type") != ty) continue;
                        string mr = S(m, "region");
                        if (rg.Length > 0 && mr != rg) continue;
                        string u = S(m, "url");
                        if (u.Length > 0) return u + "|" + S(m, "format");
                    }
            return null;
        }

        static void FromScreenScraper(ScrapeJob j, ScrapeCfg c, ScrapeResult res)
        {
            if (string.IsNullOrEmpty(c.SsUser) || j.SsSystem <= 0 || j.Platform == "WIN" || j.Platform == "STEAM") return;
            object jeu;
            try { jeu = SsGame(j, c); }
            catch (Exception ex) { res.Errors.Add("ScreenScraper: " + ex.Message); return; }
            if (jeu == null) { res.Errors.Add("ScreenScraper: game tidak ditemukan"); return; }
            string region = string.IsNullOrEmpty(c.Region) ? "wor" : c.Region;
            try
            {
                if ((j.NeedCover && res.Cover == null) || (j.NeedBox && res.Box == null))
                {
                    string box = null;
                    SsGet(jeu, new string[] { "box-2D" }, region, "covers", ".png", j, ref box);
                    if (box != null) { if (j.NeedCover) res.Cover = box; res.Box = box; }
                    else if (j.NeedCover) SsGet(jeu, new string[] { "sstitle", "ss" }, region, "covers", ".png", j, ref res.Cover);
                }
                if (j.NeedBg && res.Background == null) SsGet(jeu, new string[] { "fanart", "ss" }, region, "backgrounds", ".jpg", j, ref res.Background);
                if (j.NeedLogo && res.Logo == null) SsGet(jeu, new string[] { "wheel-hd", "wheel" }, region, "logos", ".png", j, ref res.Logo);
                if (c.Videos && j.NeedVideo && res.Video == null) SsGet(jeu, new string[] { "video-normalized", "video" }, region, "trailers", ".mp4", j, ref res.Video);
            }
            catch (Exception ex) { res.Errors.Add("ScreenScraper: " + ex.Message); }
        }

        static void SsGet(object jeu, string[] types, string region, string kind, string defExt, ScrapeJob j, ref string target)
        {
            string pick = PickMedia(jeu, types, region);
            if (pick == null) return;
            int bar = pick.LastIndexOf('|');
            string url = pick.Substring(0, bar), fmt = pick.Substring(bar + 1).ToLowerInvariant();
            string ext = fmt.Length > 0 ? "." + (fmt == "jpeg" ? "jpg" : fmt) : defExt;
            string d = Dest(kind, j, ext);
            if (Download(url, d)) target = d;
        }

        public static string TestScreenScraper(ScrapeCfg c)
        {
            string t = GetText(SS + "ssuserInfos.php?" + SsAuth(c), null);
            if (t == null) return "Tidak ada respon";
            object r = js.DeserializeObject(t);
            object u = P(P(r, "response"), "ssuser");
            string max = S(u, "maxrequestsperday"), today = S(u, "requeststoday");
            return "OK" + (max.Length > 0 ? " (kuota hari ini " + today + "/" + max + ")" : "");
        }

        // ===================== JALANKAN =====================
        public static ScrapeResult Run(ScrapeJob j, ScrapeCfg c)
        {
            ScrapeResult res = new ScrapeResult();
            bool rom = j.Platform != "WIN" && j.Platform != "STEAM";
            if (rom) FromScreenScraper(j, c, res);   // retro: ScreenScraper dulu (box art, fanart, video)
            FromSgdb(j, c, res);                      // lengkapi sisanya dari SteamGridDB
            return res;
        }
    }
}
