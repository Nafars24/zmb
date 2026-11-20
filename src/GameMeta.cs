using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace ZMB
{
    public class MetaResult
    {
        public string Desc = "", Developer = "", Publisher = "", Year = "", Genre = "", Players = "", Rating = "";
        public List<string> Shots = new List<string>();
        public string Error;
    }

    public static class GameMeta
    {
        static JavaScriptSerializer js = new JavaScriptSerializer();
        static GameMeta() { js.MaxJsonLength = int.MaxValue; }

        static object P(object o, string k)
        {
            Dictionary<string, object> d = o as Dictionary<string, object>;
            object v;
            return (d != null && d.TryGetValue(k, out v)) ? v : null;
        }
        static string S(object o, string k) { object v = P(o, k); return v == null ? "" : Convert.ToString(v, CultureInfo.InvariantCulture); }
        static object[] A(object o, string k) { return P(o, k) as object[] ?? new object[0]; }

        static string StripHtml(string h)
        {
            if (string.IsNullOrEmpty(h)) return "";
            string t = Regex.Replace(h, @"<br\s*/?>|</p>|</li>|</h\d>", "\n", RegexOptions.IgnoreCase);
            t = Regex.Replace(t, "<[^>]+>", "");
            t = WebUtility.HtmlDecode(t);
            t = Regex.Replace(t, @"[ \t]+", " ");
            t = Regex.Replace(t, @"\n\s*\n+", "\n\n");
            return t.Trim();
        }

        static string Download(string url, string dest)
        {
            ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072;
            Directory.CreateDirectory(Path.GetDirectoryName(dest));
            if (File.Exists(dest)) return dest;
            using (WebClient wc = new WebClient())
            {
                wc.Headers[HttpRequestHeader.UserAgent] = "ZMB/2.5";
                wc.DownloadFile(url, dest + ".part");
            }
            if (new FileInfo(dest + ".part").Length < 512) { File.Delete(dest + ".part"); return null; }
            File.Move(dest + ".part", dest);
            return dest;
        }

        static string ShotPath(string plat, string key, int i, string ext)
        {
            return Path.Combine(Path.Combine(Path.Combine(Config.BaseDir, "shots"), plat), GameScanner.SafeName(key) + "_" + i + ext);
        }

        public static MetaResult FromSteam(string appId)
        {
            MetaResult r = new MetaResult();
            ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072;
            string json;
            using (WebClient wc = new WebClient())
            {
                wc.Headers[HttpRequestHeader.UserAgent] = "ZMB/2.5";
                wc.Encoding = Encoding.UTF8;
                json = wc.DownloadString("https://store.steampowered.com/api/appdetails?appids=" + appId + "&l=indonesian");
            }
            object root = js.DeserializeObject(json);
            object d = P(P(root, appId), "data");
            if (d == null) { r.Error = Lang.T("Data Steam tidak tersedia"); return r; }
            r.Desc = StripHtml(S(d, "short_description"));
            string about = StripHtml(S(d, "about_the_game"));
            if (about.Length > r.Desc.Length + 40) r.Desc = (r.Desc.Length > 0 ? r.Desc + "\n\n" : "") + about;
            if (r.Desc.Length > 2400) r.Desc = r.Desc.Substring(0, 2400) + "...";
            r.Developer = string.Join(", ", A(d, "developers").Select(x => Convert.ToString(x)).ToArray());
            r.Publisher = string.Join(", ", A(d, "publishers").Select(x => Convert.ToString(x)).ToArray());
            string date = S(P(d, "release_date"), "date");
            Match ym = Regex.Match(date, @"(19|20)\d\d");
            r.Year = ym.Success ? ym.Value : date;
            r.Genre = string.Join(", ", A(d, "genres").Select(g => S(g, "description")).Take(3).ToArray());
            string mc = S(P(d, "metacritic"), "score");
            if (mc.Length > 0) r.Rating = "Metacritic " + mc;
            List<string> cats = A(d, "categories").Select(c => S(c, "description").ToLowerInvariant()).ToList();
            bool multi = cats.Any(c => c.Contains("multi") || c.Contains("co-op") || c.Contains(Lang.T("pemain banyak")));
            r.Players = multi ? Lang.T("Satu & banyak pemain") : Lang.T("Satu pemain");
            int i = 0;
            foreach (object sh in A(d, "screenshots").Take(8))
            {
                string url = S(sh, "path_full");
                if (url.Length == 0) continue;
                try
                {
                    string f = Download(url, ShotPath("STEAM", appId, i, ".jpg"));
                    if (f != null) { r.Shots.Add(f); i++; }
                }
                catch { }
            }
            return r;
        }

        static string PickLang(object[] arr, string[] langs, string field)
        {
            foreach (string l in langs)
                foreach (object o in arr)
                    if (S(o, "langue") == l || S(o, "region") == l) return S(o, field);
            return arr.Length > 0 ? S(arr[0], field) : "";
        }

        public static MetaResult FromScreenScraper(ScrapeJob j, ScrapeCfg c)
        {
            MetaResult r = new MetaResult();
            object jeu = MediaScraper.SsGamePublic(j, c);
            if (jeu == null) { r.Error = Lang.T("Game tidak ditemukan di ScreenScraper"); return r; }
            string[] langs = { "en", "id", "fr" };
            r.Desc = PickLang(A(jeu, "synopsis"), langs, "text").Trim();
            r.Developer = S(P(jeu, "developpeur"), "text");
            r.Publisher = S(P(jeu, "editeur"), "text");
            string date = PickLang(A(jeu, "dates"), new string[] { "wor", "us", "eu", "jp" }, "text");
            r.Year = date.Length >= 4 ? date.Substring(0, 4) : date;
            List<string> genres = new List<string>();
            foreach (object g in A(jeu, "genres").Take(3))
            {
                string n = PickLang(A(g, "noms"), langs, "text");
                if (n.Length > 0) genres.Add(n);
            }
            r.Genre = string.Join(", ", genres.ToArray());
            string pl = S(P(jeu, "joueurs"), "text");
            if (pl.Length > 0) r.Players = pl + Lang.T(" pemain");
            string note = S(P(jeu, "note"), "text");
            if (note.Length > 0) r.Rating = note + " / 20";
            int i = 0;
            string key = Path.GetFileNameWithoutExtension(j.Path);
            foreach (object m in A(jeu, "medias"))
            {
                string ty = S(m, "type");
                if (ty != "ss" && ty != "sstitle" && ty != "fanart") continue;
                string url = S(m, "url");
                if (url.Length == 0) continue;
                string fmt = S(m, "format");
                try
                {
                    string f = Download(url, ShotPath(j.Platform, key, i, "." + (fmt.Length > 0 ? fmt : "png")));
                    if (f != null) { r.Shots.Add(f); i++; }
                }
                catch { }
                if (i >= 6) break;
            }
            return r;
        }
    }
}
