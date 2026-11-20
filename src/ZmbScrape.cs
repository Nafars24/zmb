using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Media;

namespace ZMB
{
    public partial class ZmbWindow
    {
        bool scraping = false;
        volatile bool scrapeCancel = false;
        int scrapeDone = 0, scrapeTotal = 0;
        string scrapeName = "";
        int cntCover, cntBg, cntVid, cntLogo, cntErr;
        string lastScrapeErr = null;
        string focusLogo = null;

        ScrapeCfg ScrapeConfig()
        {
            ScrapeCfg c = new ScrapeCfg();
            c.SgdbKey = (cfg.SgdbKey ?? "").Trim();
            c.SsUser = (cfg.SsUser ?? "").Trim();
            c.SsPass = cfg.SsPass ?? "";
            c.SsDevId = (cfg.SsDevId ?? "").Trim();
            c.SsDevPass = cfg.SsDevPass ?? "";
            c.Videos = cfg.ScrapeVideos;
            c.Region = cfg.ScrapeRegion;
            return c;
        }

        bool ScrapeReady { get { return !string.IsNullOrEmpty(cfg.SgdbKey) || !string.IsNullOrEmpty(cfg.SsUser); } }

        string FindLogo(GameEntry g)
        {
            if (g == null) return null;
            if (!string.IsNullOrEmpty(g.Logo) && File.Exists(g.Logo)) return g.Logo;
            if (g.Platform == "WIN" || g.Platform == "STEAM") return null;
            try
            {
                string rd = Path.GetDirectoryName(g.Path), key = Path.GetFileNameWithoutExtension(g.Path);
                foreach (string sub in new string[] { "logos", "wheel", "wheels" })
                    foreach (string e in new string[] { ".png", ".jpg" })
                    {
                        string f = Path.Combine(Path.Combine(rd, sub), key + e);
                        if (File.Exists(f)) return f;
                    }
            }
            catch { }
            return null;
        }

        bool HasCover(GameEntry g, PlatformCfg p)
        {
            if (!string.IsNullOrEmpty(g.Cover) && File.Exists(g.Cover)) return true;
            if (g.Platform == "STEAM")
            {
                SteamGame sg = GameScanner.FindSteam(g.Path.Substring(g.Path.LastIndexOf('/') + 1));
                return sg != null && sg.Icon != null;
            }
            if (g.Platform == "WIN") return false;
            return GameScanner.FindCover(p, g.Path, g.Cover) != null;
        }

        ScrapeJob MakeJob(GameEntry g, bool overwrite)
        {
            PlatformCfg p = (g.Platform == "WIN" || g.Platform == "STEAM") ? null : cfg.FindPlatform(g.Platform);
            ScrapeJob j = new ScrapeJob();
            j.G = g;
            j.Name = g.Name;
            j.Platform = g.Platform;
            j.Path = g.Path;
            j.PlatShort = p != null ? p.Short : g.Platform;
            if (g.Platform == "STEAM") j.AppId = g.Path.Substring(g.Path.LastIndexOf('/') + 1);
            else if (g.Platform == "WIN" && !string.IsNullOrEmpty(g.SteamAppId)) j.AppId = g.SteamAppId.Trim();
            j.SsSystem = MediaScraper.SsSystemFor(p, g.Path);
            bool rom = p != null;
            bool crisp = false;
            string bg = null;
            try { bg = GameScanner.FindBackground(g, p, out crisp); } catch { }
            j.NeedCover = overwrite || !HasCover(g, p);
            j.NeedBg = overwrite || bg == null || !crisp;
            j.NeedLogo = cfg.ShowLogo && (overwrite || FindLogo(g) == null);
            string boxArt = null;
            try { boxArt = GameScanner.FindBoxArt(g, p); } catch { }
            j.NeedBox = overwrite || boxArt == null;
            j.NeedSpine = rom && j.SsSystem > 0 && (overwrite || SpinePath(g) == null);
            string discF = null;
            try { discF = GameScanner.FindDisc(g); } catch { }
            j.NeedDisc = rom && j.SsSystem > 0 && (g.Platform == "PS1" || g.Platform == "PS2" || g.Platform == "PS3" || g.Platform == "PSP") && (overwrite || discF == null);
            string tr = null;
            try { tr = GameScanner.FindTrailer(g, p); } catch { }
            j.NeedVideo = rom && cfg.ScrapeVideos && (overwrite || tr == null);
            return j;
        }

        List<GameEntry> GamesOf(PlatformCfg only)
        {
            List<GameEntry> l = new List<GameEntry>();
            if (only == null)
            {
                l.AddRange(cfg.WindowsGames);
                if (cfg.ShowSteam)
                    foreach (SteamGame sg in GameScanner.SteamGames(false))
                        l.Add(cfg.GetMeta("steam://rungameid/" + sg.AppId, "STEAM", sg.Name));
            }
            foreach (PlatformCfg p in cfg.Platforms)
            {
                if (only != null && p != only) continue;
                if (only == null && p.Hidden) continue;
                foreach (string rom in GameScanner.ListRoms(p))
                    l.Add(cfg.GetMeta(rom, p.Short, GameScanner.RomDisplayName(p, rom)));
            }
            return l;
        }

        void StartScrape(List<GameEntry> games, bool overwrite)
        {
            if (scraping) { ShowToast("Unduhan media sedang berjalan"); return; }
            if (!ScrapeReady)
            {
                Sfx.Error();
                ShowToast("Isi SteamGridDB API Key atau akun ScreenScraper dulu");
                XmbItem it = MakeItem("Unduh Media Otomatis", "", "\uE896", null);
                it.Children = BuildScrapeMenu;
                PushLevel(it);
                return;
            }
            List<ScrapeJob> jobs = new List<ScrapeJob>();
            foreach (GameEntry g in games)
            {
                ScrapeJob j = MakeJob(g, overwrite);
                if (j.NeedCover || j.NeedBg || j.NeedLogo || j.NeedVideo || j.NeedBox || ((j.NeedSpine || j.NeedDisc) && !string.IsNullOrEmpty(cfg.SsUser))) jobs.Add(j);
            }
            if (jobs.Count == 0) { ShowToast("Semua media sudah lengkap"); return; }

            scraping = true; scrapeCancel = false;
            scrapeDone = 0; scrapeTotal = jobs.Count; scrapeName = "";
            cntCover = cntBg = cntVid = cntLogo = cntErr = 0;
            lastScrapeErr = null;
            ScrapeCfg c = ScrapeConfig();
            bool useSs = !string.IsNullOrEmpty(c.SsUser);
            ShowToast("Mulai mengunduh media untuk " + jobs.Count + " game");

            Thread th = new Thread(delegate ()
            {
                try
                {
                    foreach (ScrapeJob j in jobs)
                    {
                        if (scrapeCancel) break;
                        string nm = j.Name;
                        Dispatcher.BeginInvoke((Action)delegate { scrapeName = nm; });
                        ScrapeResult r;
                        try { r = MediaScraper.Run(j, c); }
                        catch (Exception ex) { r = new ScrapeResult(); r.Errors.Add(ex.Message); }
                        ScrapeJob jj = j;
                        ScrapeResult rr = r;
                        Dispatcher.BeginInvoke((Action)delegate { ApplyScrape(jj, rr); });
                        bool romJob = j.Platform != "WIN" && j.Platform != "STEAM";
                        Thread.Sleep(useSs && romJob ? 1100 : 250); // jangan membanjiri server
                    }
                }
                finally
                {
                    Dispatcher.BeginInvoke((Action)FinishScrape);
                }
            });
            th.IsBackground = true;
            th.Start();
        }

        void ApplyScrape(ScrapeJob j, ScrapeResult r)
        {
            GameEntry g = j.G;
            bool changed = false;
            if (r.Cover != null) { ForgetImage("img:" + g.Cover); g.Cover = r.Cover; ForgetImage("img:" + r.Cover); cntCover++; changed = true; }
            if (r.Background != null) { g.Background = r.Background; ForgetImage("bg:" + r.Background); cntBg++; changed = true; }
            if (r.Video != null) { g.Trailer = r.Video; cntVid++; changed = true; }
            if (r.Disc != null) { g.Disc = r.Disc; changed = true; }
            if (r.Spine != null) { g.Spine = r.Spine; ForgetImage("spine:" + r.Spine); spinePathCache.Clear(); changed = true; }
            if (r.Box != null) { g.BoxArt = r.Box; ForgetImage("box:" + r.Box); changed = true; }
            if (r.Logo != null) { g.Logo = r.Logo; ForgetImage("logo:" + r.Logo); cntLogo++; changed = true; }
            if (r.Errors.Count > 0 && !changed) { cntErr++; lastScrapeErr = j.Name + ": " + r.Errors[0]; }
            if (changed) cfg.EnsureStored(g);
            scrapeDone++;
            if (changed && scrapeDone % 8 == 0) { cfg.Save(); RefreshItemsOnly(); }
        }

        void FinishScrape()
        {
            scraping = false;
            cfg.Save();
            RefreshAll();
            string msg = (scrapeCancel ? "Dibatalkan. " : "Selesai! ") + cntCover + " cover, " + cntBg + " latar, " + cntLogo + " logo, " + cntVid + " video";
            if (cntErr > 0) msg += "  \u2022  " + cntErr + " gagal" + (lastScrapeErr != null ? " (" + lastScrapeErr + ")" : "");
            Sfx.Ok();
            ShowToast(msg);
        }

        void DrawScrapeStatus(DrawingContext dc, double W, double H)
        {
            if (!scraping) return;
            double s = S;
            double w = Math.Min(W * 0.5, 720 * s), h = 76 * s;
            Rect r = new Rect((W - w) / 2, H - h - 30 * s, w, h);
            dc.PushOpacity(0.92);
            dc.DrawRoundedRectangle(brToast, penThin, r, 12 * s, 12 * s);
            dc.Pop();
            Txt(dc, "Mengunduh media " + Math.Min(scrapeDone + 1, scrapeTotal) + " / " + scrapeTotal + "  \u2022  " + scrapeName, 18 * s, 0, r.X + 22 * s, r.Y + 12 * s, 0.95, w - 44 * s, 0, false);
            double frac = scrapeTotal > 0 ? (double)scrapeDone / scrapeTotal : 0;
            dc.DrawRoundedRectangle(WhiteA(0.25), null, new Rect(r.X + 22 * s, r.Y + 46 * s, w - 44 * s, 6 * s), 3 * s, 3 * s);
            dc.DrawRoundedRectangle(brWhite, null, new Rect(r.X + 22 * s, r.Y + 46 * s, (w - 44 * s) * frac, 6 * s), 3 * s, 3 * s);
            Txt(dc, cntCover + " cover  \u2022  " + cntBg + " latar  \u2022  " + cntVid + " video", 14 * s, 0, r.Right - 22 * s, r.Y + 56 * s, 0.6, 0, 2, false);
        }

        void DrawGameLogo(DrawingContext dc, double W, double H, double vis)
        {
            if (!cfg.ShowLogo || focusLogo == null || vis < 0.05 || mode != Mode.Xmb) return;
            string path = focusLogo;
            ImageSource img = GetImage("logo:" + path, delegate { return ImgConv.LoadFile(path, 900); });
            if (img == null) return;
            Rect box = new Rect(W * 0.62, H * 0.68, W * 0.33, H * 0.2);
            BitmapSourceFit(dc, img, box, Math.Min(1, vis) * 0.95, true);
        }

        void BitmapSourceFit(DrawingContext dc, ImageSource img, Rect r, double a, bool alignRight)
        {
            System.Windows.Media.Imaging.BitmapSource bs = img as System.Windows.Media.Imaging.BitmapSource;
            double iw = bs != null ? bs.PixelWidth : img.Width, ih = bs != null ? bs.PixelHeight : img.Height;
            if (iw <= 0 || ih <= 0) return;
            double sc = Math.Min(r.Width / iw, r.Height / ih);
            double w = iw * sc, h = ih * sc;
            double x = alignRight ? r.Right - w : r.X + (r.Width - w) / 2;
            DrawImageA(dc, img, new Rect(x, r.Y + (r.Height - h) / 2, w, h), a);
        }

        // ===================== MENU =====================
        XmbItem SecretSetting(string title, string glyph, Func<string> get, Action<string> set, string prompt, bool mask, string helpUrl)
        {
            XmbItem it = MakeItem(title, "", glyph, null);
            it.DynSub = delegate { string v = get(); return mask ? Mask(v) : (string.IsNullOrEmpty(v) ? "Belum diatur" : v); };
            it.Activate = delegate
            {
                string v = Dialogs.Input(Owner32, title, prompt, get());
                if (v != null) { set(v.Trim()); cfg.Save(); }
            };
            if (helpUrl != null)
                it.Options = delegate
                {
                    List<OptionItem> o = new List<OptionItem>();
                    o.Add(new OptionItem("Ubah...", it.Activate));
                    o.Add(new OptionItem("Buka Halaman Bantuan", delegate { OpenDefault(helpUrl); }));
                    o.Add(new OptionItem("Hapus", delegate { set(""); cfg.Save(); }));
                    return o;
                };
            return it;
        }

        List<XmbItem> BuildScrapeMenu()
        {
            List<XmbItem> l = new List<XmbItem>();
            XmbItem run = MakeItem("Unduh Media yang Belum Ada", "Cover, latar, logo, dan video untuk semua game", "\uE896", delegate { StartScrape(GamesOf(null), false); });
            run.DynSub = delegate
            {
                if (scraping) return "Berjalan: " + scrapeDone + " / " + scrapeTotal + "  \u2022  " + scrapeName;
                return ScrapeReady ? "Cover, latar, logo, dan video untuk semua game" : "Isi SteamGridDB API Key / akun ScreenScraper di bawah dulu";
            };
            l.Add(run);
            l.Add(MakeItem("Unduh Ulang Semua (Timpa)", "Ganti semua media dengan versi baru", "\uE72C", delegate
            {
                Confirm("Unduh ulang & timpa media semua game?", delegate { StartScrape(GamesOf(null), true); });
            }));
            XmbItem cancel = MakeItem("Batalkan Unduhan", "", "\uE711", delegate
            {
                if (scraping) { scrapeCancel = true; ShowToast("Membatalkan..."); } else ShowToast("Tidak ada unduhan yang berjalan");
            });
            cancel.DynSub = delegate { return scraping ? "Hentikan setelah game yang sedang diproses" : "Tidak ada unduhan berjalan"; };
            l.Add(cancel);

            l.Add(SecretSetting("SteamGridDB API Key", "\uE8D7", delegate { return cfg.SgdbKey; }, delegate (string v) { cfg.SgdbKey = v; },
                "API Key SteamGridDB (gratis): login di steamgriddb.com > Preferences > API, salin key-nya.", true,
                "https://www.steamgriddb.com/profile/preferences/api"));
            l.Add(SecretSetting("ScreenScraper: Username", "\uE77B", delegate { return cfg.SsUser; }, delegate (string v) { cfg.SsUser = v; },
                "Username akun screenscraper.fr (daftar gratis):", false, "https://www.screenscraper.fr/membreinscription.php"));
            l.Add(SecretSetting("ScreenScraper: Password", "\uE72E", delegate { return cfg.SsPass; }, delegate (string v) { cfg.SsPass = v; },
                "Password akun screenscraper.fr:", true, null));
            l.Add(SecretSetting("ScreenScraper: Dev ID", "\uE943", delegate { return cfg.SsDevId; }, delegate (string v) { cfg.SsDevId = v; },
                "Developer ID ScreenScraper. API ScreenScraper meminta identitas developer: minta di forum screenscraper.fr (bagian API). Jika kosong, tes koneksi akan memberi tahu apakah diperlukan.", false,
                "https://www.screenscraper.fr/forumsujets.php?frub=12&numpage=0"));
            l.Add(SecretSetting("ScreenScraper: Dev Password", "\uE72E", delegate { return cfg.SsDevPass; }, delegate (string v) { cfg.SsDevPass = v; },
                "Developer password ScreenScraper:", true, null));

            l.Add(Toggle("Unduh Video Gameplay", "Dari ScreenScraper, dipakai sebagai trailer latar", "\uE714", delegate { return cfg.ScrapeVideos; }, delegate (bool v) { cfg.ScrapeVideos = v; }));
            l.Add(Toggle("Logo Game di Latar", "Tampilkan logo game di pojok kanan bawah seperti PS3", "\uE8B9", delegate { return cfg.ShowLogo; }, delegate (bool v) { cfg.ShowLogo = v; }));

            XmbItem reg = MakeItem("Region Media", "", "\uE774", null);
            string[] codes = { "wor", "us", "eu", "jp", "ss" };
            string[] names = { "Dunia", "Amerika (US)", "Eropa (EU)", "Jepang (JP)", "ScreenScraper" };
            reg.DynSub = delegate { int i = Array.IndexOf(codes, cfg.ScrapeRegion); return i >= 0 ? names[i] : cfg.ScrapeRegion; };
            reg.Options = delegate
            {
                List<OptionItem> o = new List<OptionItem>();
                for (int i = 0; i < codes.Length; i++) { string cc = codes[i]; o.Add(new OptionItem(names[i], delegate { cfg.ScrapeRegion = cc; cfg.Save(); }, cfg.ScrapeRegion == cc)); }
                return o;
            };
            reg.Activate = delegate { OpenOptions("Region Media", reg.Options()); };
            l.Add(reg);

            l.Add(MakeItem("Tes Koneksi", "Cek SteamGridDB & ScreenScraper", "\uE895", TestScrapers));
            return l;
        }

        void TestScrapers()
        {
            ScrapeCfg c = ScrapeConfig();
            ShowToast("Mengecek layanan media...");
            ThreadPool.QueueUserWorkItem(delegate
            {
                List<string> res = new List<string>();
                if (c.SgdbKey.Length > 0)
                {
                    try { res.Add("SteamGridDB " + MediaScraper.TestSgdb(c.SgdbKey)); }
                    catch (Exception ex) { res.Add("SteamGridDB: " + ex.Message); }
                }
                else res.Add("SteamGridDB belum diatur");
                if (c.SsUser.Length > 0)
                {
                    try { res.Add("ScreenScraper " + MediaScraper.TestScreenScraper(c)); }
                    catch (Exception ex) { res.Add("ScreenScraper: " + ex.Message); }
                }
                else res.Add("ScreenScraper belum diatur");
                Dispatcher.BeginInvoke((Action)delegate { ShowToast(string.Join("   |   ", res.ToArray())); });
            });
        }
    }
}
