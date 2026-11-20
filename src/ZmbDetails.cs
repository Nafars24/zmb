using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ZMB
{
    public partial class ZmbWindow
    {
        GameEntry detGame = null;
        XmbItem detItem = null;
        int detShot = -1;
        double detOpenedAt = 0;
        double detScroll = 0, detScrollAnim = 0;
        bool detLoading = false;

        void OpenDetails(GameEntry g)
        {
            if (g == null) return;
            detGame = g;
            detItem = GameItem(g, null);
            detShot = -1;
            detScroll = 0; detScrollAnim = 0;
            detOpenedAt = T;
            mode = Mode.Details;
            focusKey = null; // muat ulang latar & trailer untuk game ini
            Sfx.Ok();
            if (!g.MetaFetched) FetchMeta(g, false);
        }

        void FetchMeta(GameEntry g, bool force)
        {
            PlatformCfg p = (g.Platform == "WIN" || g.Platform == "STEAM") ? null : cfg.FindPlatform(g.Platform);
            string appId = g.Platform == "STEAM" ? g.Path.Substring(g.Path.LastIndexOf('/') + 1) :
                           (g.Platform == "WIN" && !string.IsNullOrEmpty(g.SteamAppId) ? g.SteamAppId.Trim() : null);
            Func<MetaResult> fetch = null;
            if (appId != null) fetch = delegate { return GameMeta.FromSteam(appId); };
            else if (p != null && !string.IsNullOrEmpty(cfg.SsUser))
            {
                ScrapeJob j = MakeJob(g, false);
                ScrapeCfg c = ScrapeConfig();
                if (j.SsSystem > 0) fetch = delegate { return GameMeta.FromScreenScraper(j, c); };
            }
            if (fetch == null) return;
            string key = "meta:" + g.Platform + "|" + g.Path;
            if (force) InvalidateAsync(key);
            detLoading = true;
            GameEntry ge = g;
            GetAsync<MetaResult>(key, fetch, 1e9, delegate (MetaResult r)
            {
                detLoading = false;
                if (r == null) { string err = AsyncError(key); if (err != null && mode == Mode.Details) ShowToast("Detail: " + err); return; }
                if (r.Error != null) { if (mode == Mode.Details) ShowToast(r.Error); return; }
                ge.Desc = r.Desc; ge.Developer = r.Developer; ge.Publisher = r.Publisher; ge.Year = r.Year;
                ge.Genre = r.Genre; ge.Players = r.Players; ge.Rating = r.Rating;
                if (r.Shots.Count > 0) ge.Shots = r.Shots;
                ge.MetaFetched = true;
                cfg.EnsureStored(ge);
                cfg.Save();
            }, false);
        }

        void CloseDetails()
        {
            mode = Mode.Xmb;
            detGame = null;
            focusKey = null;
        }

        void ShowShot(int i)
        {
            if (detGame == null || detGame.Shots.Count == 0) return;
            if (i < -1) i = detGame.Shots.Count - 1;
            if (i >= detGame.Shots.Count) i = -1;
            detShot = i;
            if (i < 0) { focusKey = null; return; } // kembali ke latar game
            string path = detGame.Shots[i];
            string ikey = "bg:" + path;
            ImageSource cached;
            if (imgCache.TryGetValue(ikey, out cached)) { ShowBg(cached, true); return; }
            GameEntry want = detGame;
            System.Threading.ThreadPool.QueueUserWorkItem(delegate
            {
                BitmapSource bs = ImgConv.LoadFile(path, 1920);
                Dispatcher.BeginInvoke((Action)delegate
                {
                    if (bs == null) return;
                    imgCache[ikey] = bs; imgOrder.Enqueue(ikey);
                    if (mode == Mode.Details && detGame == want && detShot == i) ShowBg(bs, true);
                });
            });
        }

        void HandleDetailsKey(XKey k)
        {
            if (HandleOptionsKey(k)) return;
            if (detGame == null) { mode = Mode.Xmb; return; }
            switch (k)
            {
                case XKey.Back: Sfx.Back(); CloseDetails(); break;
                case XKey.Ok: case XKey.Start: Launch(detGame, false); break;
                case XKey.Left: if (detGame.Shots.Count > 0) { ShowShot(detShot - 1); Sfx.Tick(); } break;
                case XKey.Right: if (detGame.Shots.Count > 0) { ShowShot(detShot + 1); Sfx.Tick(); } break;
                case XKey.Up: detScroll = Math.Max(0, detScroll - 3); break;
                case XKey.Down: detScroll += 3; break;
                case XKey.PageUp: detScroll = Math.Max(0, detScroll - 10); break;
                case XKey.PageDown: detScroll += 10; break;
                case XKey.Options:
                case XKey.Select:
                    {
                        GameEntry g = detGame;
                        List<OptionItem> o = GameOptions(g);
                        o.Insert(1, new OptionItem(Lang.T("Muat Ulang Detail Game"), delegate { FetchMeta(g, true); }));
                        OpenOptions(g.Name, o);
                        break;
                    }
            }
        }

        double Chips(DrawingContext dc, IEnumerable<string> items, double x, double y, double maxX, double a)
        {
            double s = S;
            double cx = x, cy = y, rowH = 0;
            foreach (string t in items)
            {
                if (string.IsNullOrEmpty(t)) continue;
                FormattedText ft = FT(t, 16 * s, 0, false, 0);
                double w = ft.Width + 24 * s, h = ft.Height + 10 * s;
                if (cx + w > maxX && cx > x) { cx = x; cy += h + 8 * s; }
                dc.PushOpacity(a);
                dc.DrawRoundedRectangle(SolidOf(Color.FromArgb(70, 255, 255, 255)), null, new Rect(cx, cy, w, h), h / 2, h / 2);
                dc.DrawText(ft, new Point(cx + 12 * s, cy + 5 * s));
                dc.Pop();
                cx += w + 8 * s;
                rowH = h;
            }
            return cy + rowH - y;
        }

        void DrawDetails(DrawingContext dc, double W, double H)
        {
            GameEntry g = detGame;
            if (g == null || detItem == null) return;
            double s = S;
            double now = T;
            double a = Math.Min(1, (now - detOpenedAt) / 0.35);
            double e = 1 - Math.Pow(1 - a, 3);

            // overlay: gelap di kiri, lebih terang di kanan agar latar game terlihat
            LinearGradientBrush ov = new LinearGradientBrush();
            ov.StartPoint = new Point(0, 0); ov.EndPoint = new Point(1, 0);
            ov.GradientStops.Add(new GradientStop(Color.FromArgb(235, 6, 8, 14), 0));
            ov.GradientStops.Add(new GradientStop(Color.FromArgb(200, 6, 8, 14), 0.55));
            ov.GradientStops.Add(new GradientStop(Color.FromArgb(110, 6, 8, 14), 1));
            ov.Freeze();
            dc.PushOpacity(a);
            dc.DrawRectangle(ov, null, new Rect(0, 0, W, H));
            dc.Pop();

            // case game di kiri
            double caseH = H * 0.54;
            double caseCx = 90 * s + caseH * 0.42;
            DrawCaseAt(dc, detItem, caseCx - (1 - e) * 60 * s, H * 0.40, caseH, 1, 1, a, true);

            double x0 = Math.Max(caseCx + caseH * 0.45 + 50 * s, W * 0.33);
            double cw = W - x0 - 80 * s;
            double y = 80 * s;
            double hT = TxtW(dc, g.Name, 44 * s, 2, x0 + (1 - e) * 40 * s, y, cw, 2, a);
            y += hT + 10 * s;

            PlatformCfg p = cfg.FindPlatform(g.Platform);
            string plat = g.Platform == "WIN" ? "PC" : g.Platform == "STEAM" ? "PC (Steam)" : (p != null ? p.Name : g.Platform);
            List<string> chips = new List<string>();
            chips.Add(plat);
            if (g.Year.Length > 0) chips.Add(g.Year);
            if (g.Genre.Length > 0) chips.Add(g.Genre);
            if (g.Players.Length > 0) chips.Add(g.Players);
            if (g.Rating.Length > 0) chips.Add(g.Rating);
            y += Chips(dc, chips, x0, y, x0 + cw, a) + 18 * s;

            if (g.Developer.Length > 0 || g.Publisher.Length > 0)
            {
                string dv = (g.Developer.Length > 0 ? "Developer: " + g.Developer : "") + (g.Developer.Length > 0 && g.Publisher.Length > 0 ? "     " : "") + (g.Publisher.Length > 0 ? "Publisher: " + g.Publisher : "");
                y += TxtW(dc, dv, 17 * s, 0, x0, y, cw, 2, 0.75 * a) + 14 * s;
            }

            // statistik main + trofi
            string stat = g.PlayCount > 0
                ? Lang.T("Dimainkan ") + g.PlayCount + "x  \u2022  " + Jam(g.Minutes) + Lang.T("  \u2022  terakhir ") + g.LastPlayed.ToString("dd MMM yyyy")
                : Lang.T("Belum pernah dimainkan");
            string tro = AchvBrief(g);
            y += TxtW(dc, stat + tro, 17 * s, 0, x0, y, cw, 1, 0.85 * a) + 20 * s;

            // deskripsi (bisa digulir dengan Atas/Bawah)
            double shotsH = 150 * s;
            double descBottom = H - shotsH - 110 * s;
            double descH = Math.Max(60 * s, descBottom - y);
            string desc = g.Desc.Length > 0 ? g.Desc :
                (detLoading ? Lang.T("Memuat detail game...") :
                 (g.Platform == "STEAM" || !string.IsNullOrEmpty(g.SteamAppId) ? Lang.T("Deskripsi belum tersedia.") :
                  (string.IsNullOrEmpty(cfg.SsUser) ? Lang.T("Isi akun ScreenScraper (Pengaturan > Unduh Media Otomatis) untuk memuat deskripsi, developer, genre, dan screenshot game retro.") : Lang.T("Deskripsi belum tersedia."))));
#pragma warning disable 618
            FormattedText dft = new FormattedText(desc, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, faces[0], 19 * s, brWhite);
#pragma warning restore 618
            dft.MaxTextWidth = Math.Max(50, cw);
            dft.LineHeight = 28 * s;
            double maxScroll = Math.Max(0, (dft.Height - descH) / (28 * s));
            if (detScroll > maxScroll) detScroll = maxScroll;
            detScrollAnim += (detScroll - detScrollAnim) * 0.25;
            dc.PushClip(new RectangleGeometry(new Rect(x0, y, cw, descH)));
            dc.PushOpacity(0.88 * a);
            dc.DrawText(dft, new Point(x0, y - detScrollAnim * 28 * s));
            dc.Pop();
            dc.Pop();
            if (maxScroll > 0.5)
            {
                double frac = detScrollAnim / maxScroll;
                dc.DrawRoundedRectangle(WhiteA(0.2 * a), null, new Rect(x0 + cw + 12 * s, y, 4 * s, descH), 2 * s, 2 * s);
                double th = descH * Math.Min(1, descH / dft.Height);
                dc.DrawRoundedRectangle(WhiteA(0.7 * a), null, new Rect(x0 + cw + 12 * s, y + (descH - th) * frac, 4 * s, th), 2 * s, 2 * s);
            }

            // galeri screenshot
            double sy = H - shotsH - 80 * s;
            if (g.Shots.Count > 0)
            {
                double tw = shotsH * 16 / 9;
                double sx = x0;
                for (int i = 0; i < g.Shots.Count; i++)
                {
                    if (sx + tw > W - 40 * s) break;
                    string sp = g.Shots[i];
                    Rect r = new Rect(sx, sy, tw, shotsH);
                    ImageSource img = GetImage("shot:" + sp, delegate { return ImgConv.LoadFile(sp, 480); });
                    dc.PushOpacity(a * (i == detShot ? 1 : 0.75));
                    if (img != null) DrawImageFill(dc, img, r);
                    else dc.DrawRectangle(SolidOf(Color.FromArgb(60, 255, 255, 255)), null, r);
                    dc.Pop();
                    if (i == detShot)
                    {
                        dc.PushOpacity(a);
                        dc.DrawRectangle(null, new Pen(brWhite, 3 * s), new Rect(r.X - 2 * s, r.Y - 2 * s, r.Width + 4 * s, r.Height + 4 * s));
                        dc.Pop();
                    }
                    sx += tw + 14 * s;
                }
                Txt(dc, detShot >= 0 ? "Screenshot " + (detShot + 1) + " / " + g.Shots.Count + Lang.T("  (tampil di latar)") : Lang.T("Kiri/Kanan: lihat screenshot di layar penuh"), 15 * s, 0, x0, sy - 26 * s, 0.6 * a, cw, 0, false);
            }

            // petunjuk tombol
            bool pad = Gamepad.AnyConnected;
            string hint = pad ? Lang.T("A: Mainkan     B: Kembali     Y: Opsi     Kiri/Kanan: Screenshot     Atas/Bawah: Gulir deskripsi")
                              : Lang.T("Enter: Mainkan     Esc: Kembali     Tab: Opsi     Kiri/Kanan: Screenshot     Atas/Bawah: Gulir deskripsi");
            Txt(dc, hint, 16 * s, 0, W / 2, H - 46 * s, 0.7 * a, W - 80 * s, 1, false);
        }
    }
}
