using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ZMB
{
    class ProfileData
    {
        public double TotalMin, WeekMin, TodayMin;
        public int Games, Played, Sessions;
        public List<GameEntry> Top = new List<GameEntry>();
        public double[] Days = new double[7];
        public string[] DayLabels = new string[7];
        public double[] Weeks = new double[12];
        public List<PlaySession> Recent = new List<PlaySession>();
        public int Plat, Gold, Silver, Bronze, Ps3Games;
        public int SteamGot, SteamTotal, RaGot, RaTotal, RaPoints;
        public int Level, Xp, XpNext, XpLevelStart;
        public string Favorite = "-";
    }

    public partial class ZmbWindow
    {
        ProfileData prof = null;
        Dictionary<string, XmbItem> profItems = new Dictionary<string, XmbItem>();
        double profOpenedAt = 0;

        static readonly string[] HariPendek = { Lang.T("Min"), Lang.T("Sen"), Lang.T("Sel"), Lang.T("Rab"), Lang.T("Kam"), Lang.T("Jum"), Lang.T("Sab") };

        // Level ala Steam: tiap 10 level butuh XP per level lebih banyak
        static int LevelFromXp(int xp, out int levelStart, out int next)
        {
            int level = 0, acc = 0;
            while (true)
            {
                int need = 100 * (level / 10 + 1);
                if (acc + need > xp) { levelStart = acc; next = acc + need; return level; }
                acc += need;
                level++;
                if (level > 999) { levelStart = acc; next = acc + need; return level; }
            }
        }

        ProfileData BuildProfile()
        {
            ProfileData d = new ProfileData();
            List<GameEntry> all = cfg.AllKnown().ToList();
            d.Games = cfg.WindowsGames.Count + (cfg.ShowSteam ? GameScanner.SteamGames(false).Count : 0);
            foreach (PlatformCfg p in cfg.Platforms) if (!p.Hidden) d.Games += GameScanner.ListRoms(p).Count;
            d.Played = all.Count(g => g.PlayCount > 0);
            d.TotalMin = all.Sum(g => g.Minutes);
            d.Top = all.Where(g => g.Minutes > 0 || g.PlayCount > 0).OrderByDescending(g => g.Minutes).ThenByDescending(g => g.PlayCount).Take(5).ToList();
            if (d.Top.Count > 0) d.Favorite = d.Top[0].Name;

            DateTime today = DateTime.Today;
            for (int i = 0; i < 7; i++)
            {
                DateTime day = today.AddDays(i - 6);
                d.DayLabels[i] = i == 6 ? Lang.T("Hari ini") : HariPendek[(int)day.DayOfWeek];
            }
            foreach (PlaySession s in cfg.Sessions)
            {
                int dayIdx = 6 - (int)(today - s.Start.Date).TotalDays;
                if (dayIdx >= 0 && dayIdx < 7) d.Days[dayIdx] += s.Minutes;
                int wk = 11 - (int)((today - s.Start.Date).TotalDays / 7);
                if (wk >= 0 && wk < 12) d.Weeks[wk] += s.Minutes;
            }
            d.WeekMin = d.Days.Sum();
            d.TodayMin = d.Days[6];
            d.Sessions = cfg.Sessions.Count;
            d.Recent = cfg.Sessions.OrderByDescending(s => s.Start).Take(6).ToList();

            List<AchvSet> ps3 = Peek<List<AchvSet>>("rpcs3:all");
            if (ps3 != null)
            {
                d.Ps3Games = ps3.Count;
                d.Plat = ps3.Sum(x => x.CountGrade("P", true));
                d.Gold = ps3.Sum(x => x.CountGrade("G", true));
                d.Silver = ps3.Sum(x => x.CountGrade("S", true));
                d.Bronze = ps3.Sum(x => x.CountGrade("B", true));
            }
            foreach (KeyValuePair<string, object> kv in asyncVals)
            {
                AchvSet s = kv.Value as AchvSet;
                if (s == null || s.Error != null) continue;
                if (s.Source == "Steam") { d.SteamGot += s.Unlocked; d.SteamTotal += s.Total; }
                else if (s.Source == "RetroAchievements")
                {
                    d.RaGot += s.Unlocked; d.RaTotal += s.Total;
                    d.RaPoints += s.Items.Where(a => a.Unlocked).Sum(a => a.Points);
                }
            }
            List<AchvGameRef> raList = Peek<List<AchvGameRef>>("ra:played");
            if (raList != null)
            {
                int got = raList.Sum(r => Math.Max(0, r.Got)), tot = raList.Sum(r => Math.Max(0, r.Total));
                if (tot > d.RaTotal) { d.RaGot = got; d.RaTotal = tot; }
            }

            // XP: 10 XP per jam main + poin trofi
            d.Xp = (int)(d.TotalMin / 6) + d.Plat * 180 + d.Gold * 90 + d.Silver * 30 + d.Bronze * 15 + d.SteamGot * 10 + d.RaGot * 10;
            d.Level = LevelFromXp(d.Xp, out d.XpLevelStart, out d.XpNext);
            return d;
        }

        void OpenProfile()
        {
            if (Achievements.Rpcs3Root(cfg) != null)
            {
                string root = Achievements.Rpcs3Root(cfg);
                GetAsync<List<AchvSet>>("rpcs3:all", delegate { return Achievements.Rpcs3All(root); }, 5, delegate (List<AchvSet> r) { if (mode == Mode.Profile) prof = BuildProfile(); }, false);
            }
            if (RaReady)
            {
                string u = cfg.RaUser.Trim(), k = cfg.RaApiKey.Trim();
                GetAsync<List<AchvGameRef>>("ra:played", delegate { return Achievements.RetroPlayed(u, k); }, 10, delegate (List<AchvGameRef> r) { if (mode == Mode.Profile) prof = BuildProfile(); }, false);
            }
            profItems.Clear();
            prof = BuildProfile();
            mode = Mode.Profile;
            profOpenedAt = T;
            StopTrailer(true);
            Sfx.Ok();
        }

        void HandleProfileKey(XKey k)
        {
            if (HandleOptionsKey(k)) return;
            if (k == XKey.Back || k == XKey.Ok) { Sfx.Back(); mode = Mode.Xmb; return; }
            if (k == XKey.Options || k == XKey.Select)
            {
                List<OptionItem> o = new List<OptionItem>();
                o.Add(new OptionItem(Lang.T("Ganti Foto Profil..."), delegate
                {
                    string f = Dialogs.PickFile(Owner32, Lang.T("Pilih foto profil"), Lang.T("Gambar|*.jpg;*.jpeg;*.png;*.bmp"), cfg.PhotoFolder);
                    if (f != null) { cfg.AvatarPath = f; cfg.Save(); ForgetImage("avatar:" + f); }
                }));
                o.Add(new OptionItem(Lang.T("Hapus Foto Profil"), delegate { cfg.AvatarPath = ""; cfg.Save(); }));
                o.Add(new OptionItem(Lang.T("Hapus Riwayat Sesi Main"), delegate
                {
                    Confirm(Lang.T("Hapus semua riwayat sesi main? (total jam per game tetap)"), delegate { cfg.Sessions.Clear(); cfg.Save(); prof = BuildProfile(); });
                }));
                o.Add(new OptionItem(Lang.T("Tutup Profil"), delegate { mode = Mode.Xmb; }));
                OpenOptions(Lang.T("Profil"), o);
            }
        }

        static string Jam(double minutes)
        {
            if (minutes < 60) return ((int)Math.Round(minutes)) + " mnt";
            double h = minutes / 60.0;
            return (h < 10 ? h.ToString("0.0") : ((int)Math.Round(h)).ToString()) + Lang.T(" jam");
        }

        static Color LevelColor(int lvl)
        {
            Color[] c = { Rgb(150, 150, 150), Rgb(200, 60, 60), Rgb(220, 120, 40), Rgb(220, 190, 50), Rgb(80, 180, 80), Rgb(60, 140, 220), Rgb(130, 80, 210), Rgb(220, 80, 180), Rgb(110, 60, 40), Rgb(160, 160, 60) };
            return c[(lvl / 10) % c.Length];
        }

        void DrawCard(DrawingContext dc, Rect r, double a)
        {
            dc.PushOpacity(a);
            dc.DrawRoundedRectangle(SolidOf(Color.FromArgb(150, 18, 24, 36)), penThin, r, 10 * S, 10 * S);
            dc.Pop();
        }

        void DrawProfile(DrawingContext dc, double W, double H)
        {
            if (prof == null) return;
            ProfileData d = prof;
            double s = S;
            double a = Math.Min(1, (T - profOpenedAt) / 0.3);
            // latar gelap biru ala halaman profil Steam
            LinearGradientBrush bg = new LinearGradientBrush(Color.FromArgb(240, 14, 22, 36), Color.FromArgb(250, 6, 10, 18), 90);
            bg.Freeze();
            dc.PushOpacity(a);
            dc.DrawRectangle(bg, null, new Rect(0, 0, W, H));
            dc.Pop();

            double m = 70 * s;
            double cw = W - m * 2;
            // ---- header
            Rect av = new Rect(m, 60 * s, 150 * s, 150 * s);
            ImageSource avImg = null;
            if (!string.IsNullOrEmpty(cfg.AvatarPath))
            {
                string ap = cfg.AvatarPath;
                avImg = GetImage("avatar:" + ap, delegate { return ImgConv.LoadFile(ap, 300); });
            }
            dc.PushOpacity(a);
            dc.DrawRectangle(SolidOf(Rgb(40, 52, 74)), new Pen(SolidOf(LevelColor(d.Level)), 3 * s), av);
            dc.Pop();
            if (avImg != null) { dc.PushOpacity(a); DrawImageFill(dc, avImg, av); dc.Pop(); }
            else Txt(dc, Environment.UserName.Substring(0, 1).ToUpperInvariant(), 80 * s, 1, av.X + av.Width / 2, av.Y + (av.Height - TextH(80 * s)) / 2, a, 0, 1, false);

            double hx = av.Right + 30 * s;
            Txt(dc, Environment.UserName, 42 * s, 2, hx, 62 * s, a, cw * 0.5, 0, true);
            Txt(dc, Lang.T("Pemain sejak ") + (cfg.Sessions.Count > 0 ? cfg.Sessions.Min(x => x.Start).ToString("MMMM yyyy") : DateTime.Now.ToString("MMMM yyyy")) + Lang.T("  \u2022  Game favorit: ") + d.Favorite, 18 * s, 0, hx, 62 * s + TextH(42 * s), 0.7 * a, cw * 0.55, 0, false);

            // lencana level
            double lx = W - m - 360 * s, ly = 66 * s;
            double lr = 34 * s;
            dc.PushOpacity(a);
            dc.DrawEllipse(null, new Pen(SolidOf(LevelColor(d.Level)), 4 * s), new Point(lx + lr, ly + lr), lr, lr);
            dc.Pop();
            FormattedText lt = FT(d.Level.ToString(), 26 * s, 2, false, 0);
            dc.PushOpacity(a); dc.DrawText(lt, new Point(lx + lr - lt.Width / 2, ly + lr - lt.Height / 2)); dc.Pop();
            Txt(dc, "Level " + d.Level, 24 * s, 2, lx + lr * 2 + 18 * s, ly, a, 0, 0, false);
            Txt(dc, d.Xp + " XP  \u2022  " + (d.XpNext - d.Xp) + Lang.T(" XP lagi ke level ") + (d.Level + 1), 15 * s, 0, lx + lr * 2 + 18 * s, ly + 34 * s, 0.7 * a, 260 * s, 0, false);
            double xpFrac = d.XpNext > d.XpLevelStart ? (double)(d.Xp - d.XpLevelStart) / (d.XpNext - d.XpLevelStart) : 0;
            dc.DrawRoundedRectangle(WhiteA(0.25 * a), null, new Rect(lx + lr * 2 + 18 * s, ly + 58 * s, 250 * s, 6 * s), 3 * s, 3 * s);
            dc.PushOpacity(a); dc.DrawRoundedRectangle(SolidOf(LevelColor(d.Level)), null, new Rect(lx + lr * 2 + 18 * s, ly + 58 * s, 250 * s * xpFrac, 6 * s), 3 * s, 3 * s); dc.Pop();

            // ---- kartu statistik
            double sy = 240 * s, sh = 100 * s;
            string[] lab = { Lang.T("Total waktu main"), Lang.T("Minggu ini"), Lang.T("Hari ini"), Lang.T("Game di koleksi"), Lang.T("Pernah dimainkan") };
            string[] val = { Jam(d.TotalMin), Jam(d.WeekMin), Jam(d.TodayMin), d.Games.ToString(), d.Played.ToString() };
            double sw = (cw - 4 * 16 * s) / 5;
            for (int i = 0; i < 5; i++)
            {
                Rect r = new Rect(m + i * (sw + 16 * s), sy, sw, sh);
                DrawCard(dc, r, a);
                Txt(dc, val[i], 34 * s, 2, r.X + 20 * s, r.Y + 14 * s, a, sw - 30 * s, 0, false);
                Txt(dc, lab[i], 16 * s, 0, r.X + 20 * s, r.Y + 64 * s, 0.65 * a, sw - 30 * s, 0, false);
            }

            // ---- paling sering dimainkan (kiri)
            double cy0 = sy + sh + 26 * s;
            double colW = cw * 0.56;
            double bottom = H - 70 * s;
            Rect topCard = new Rect(m, cy0, colW, bottom - cy0);
            DrawCard(dc, topCard, a);
            Txt(dc, Lang.T("Paling sering dimainkan"), 22 * s, 2, topCard.X + 22 * s, topCard.Y + 16 * s, a, 0, 0, false);
            double rowH = Math.Min(92 * s, (topCard.Height - 70 * s) / 5);
            double maxMin = d.Top.Count > 0 ? Math.Max(1, d.Top.Max(g => g.Minutes)) : 1;
            for (int i = 0; i < d.Top.Count; i++)
            {
                GameEntry g = d.Top[i];
                double ry = topCard.Y + 60 * s + i * rowH;
                Rect th = new Rect(topCard.X + 22 * s, ry, rowH * 1.9, rowH - 12 * s);
                XmbItem gi;
                if (!profItems.TryGetValue(g.Platform + "|" + g.Path, out gi)) { gi = GameItem(g, null); profItems[g.Platform + "|" + g.Path] = gi; }
                ImageSource img = gi.ImageKey != null ? GetImage(gi.ImageKey, gi.ImageLoader) : null;
                if (img != null) { dc.PushOpacity(a); DrawImageFill(dc, img, th); dc.Pop(); }
                else DrawTile(dc, gi, th.X + th.Width / 2, th.Y + th.Height / 2, th.Width, th.Height, a);
                double tx = th.Right + 18 * s, tw = topCard.Right - tx - 22 * s;
                Txt(dc, g.Name, 21 * s, 2, tx, ry + 2 * s, a, tw, 0, false);
                string info = Jam(g.Minutes) + Lang.T(" total  \u2022  dimainkan ") + g.PlayCount + "x" + (g.LastPlayed > DateTime.MinValue ? Lang.T("  \u2022  terakhir ") + g.LastPlayed.ToString("dd MMM") : "");
                Txt(dc, info, 15 * s, 0, tx, ry + 32 * s, 0.65 * a, tw, 0, false);
                dc.DrawRoundedRectangle(WhiteA(0.2 * a), null, new Rect(tx, ry + rowH - 26 * s, tw, 5 * s), 2.5 * s, 2.5 * s);
                dc.PushOpacity(a); dc.DrawRoundedRectangle(SolidOf(Rgb(102, 192, 244)), null, new Rect(tx, ry + rowH - 26 * s, tw * g.Minutes / maxMin, 5 * s), 2.5 * s, 2.5 * s); dc.Pop();
            }
            if (d.Top.Count == 0) Txt(dc, Lang.T("Belum ada game yang dimainkan lewat ZMB."), 18 * s, 0, topCard.X + 22 * s, topCard.Y + 70 * s, 0.7 * a, colW - 44 * s, 0, false);

            // ---- grafik mingguan (kanan atas)
            double rx = m + colW + 20 * s, rw = cw - colW - 20 * s;
            double chartH = (bottom - cy0) * 0.52;
            Rect chart = new Rect(rx, cy0, rw, chartH);
            DrawCard(dc, chart, a);
            Txt(dc, Lang.T("Aktivitas 7 hari terakhir"), 22 * s, 2, chart.X + 22 * s, chart.Y + 16 * s, a, 0, 0, false);
            Txt(dc, Jam(d.WeekMin), 18 * s, 0, chart.Right - 22 * s, chart.Y + 18 * s, 0.7 * a, 0, 2, false);
            double gx = chart.X + 26 * s, gw = chart.Width - 52 * s;
            double gTop = chart.Y + 60 * s, gBot = chart.Bottom - 40 * s;
            double maxDay = Math.Max(30, d.Days.Max());
            double bw = gw / 7;
            for (int i = 0; i < 7; i++)
            {
                double v = d.Days[i];
                double bh = (gBot - gTop) * v / maxDay;
                double grow = Math.Min(1, (T - profOpenedAt - i * 0.05) / 0.5);
                if (grow < 0) grow = 0;
                bh *= 1 - Math.Pow(1 - grow, 3);
                Rect bar = new Rect(gx + i * bw + bw * 0.18, gBot - bh, bw * 0.64, Math.Max(0, bh));
                dc.PushOpacity(a);
                dc.DrawRoundedRectangle(SolidOf(i == 6 ? Rgb(102, 192, 244) : Rgb(70, 120, 170)), null, bar, 4 * s, 4 * s);
                dc.Pop();
                if (v > 0) Txt(dc, Jam(v), 13 * s, 0, bar.X + bar.Width / 2, bar.Y - 20 * s, 0.75 * a, 0, 1, false);
                Txt(dc, d.DayLabels[i], 14 * s, 0, bar.X + bar.Width / 2, gBot + 8 * s, 0.6 * a, 0, 1, false);
            }

            // ---- trofi (kanan bawah)
            Rect tro = new Rect(rx, chart.Bottom + 16 * s, rw, bottom - chart.Bottom - 16 * s);
            DrawCard(dc, tro, a);
            Txt(dc, Lang.T("Trofi & Achievement"), 22 * s, 2, tro.X + 22 * s, tro.Y + 16 * s, a, 0, 0, false);
            double ty = tro.Y + 62 * s;
            string[] gr = { "P", "G", "S", "B" };
            int[] cnt = { d.Plat, d.Gold, d.Silver, d.Bronze };
            double gcw = (tro.Width - 44 * s) / 4;
            for (int i = 0; i < 4; i++)
            {
                double cx = tro.X + 22 * s + gcw * i + 18 * s;
                DrawTrophyColored(dc, cx, ty + 14 * s, 36 * s, GradeColor(gr[i]), a);
                Txt(dc, cnt[i].ToString(), 22 * s, 2, cx + 24 * s, ty + 14 * s - TextH(22 * s) / 2, a, 0, 0, false);
            }
            double ly2 = ty + 50 * s;
            Txt(dc, "PS3 (RPCS3): " + d.Ps3Games + Lang.T(" game"), 16 * s, 0, tro.X + 22 * s, ly2, 0.75 * a, tro.Width - 44 * s, 0, false);
            Txt(dc, "Steam: " + (d.SteamTotal > 0 ? d.SteamGot + " / " + d.SteamTotal + Lang.T(" achievement (dari game yang sudah dibuka)") : Lang.T("buka Koleksi Trofi untuk memuat")), 16 * s, 0, tro.X + 22 * s, ly2 + 26 * s, 0.75 * a, tro.Width - 44 * s, 0, false);
            Txt(dc, "RetroAchievements: " + (d.RaTotal > 0 ? d.RaGot + " / " + d.RaTotal + (d.RaPoints > 0 ? "  \u2022  " + d.RaPoints + Lang.T(" poin") : "") : (RaReady ? "memuat..." : Lang.T("belum diatur"))), 16 * s, 0, tro.X + 22 * s, ly2 + 52 * s, 0.75 * a, tro.Width - 44 * s, 0, false);

            // ---- riwayat terbaru
            string recent = d.Recent.Count == 0 ? Lang.T("Riwayat sesi akan muncul setelah kamu bermain lewat ZMB.") :
                Lang.T("Terakhir: ") + string.Join("   \u2022   ", d.Recent.Take(4).Select(x => x.Name + " (" + Jam(x.Minutes) + ", " + x.Start.ToString("dd MMM") + ")").ToArray());
            Txt(dc, recent, 15 * s, 0, m, H - 52 * s, 0.6 * a, cw * 0.75, 0, false);
            Txt(dc, Lang.T("Esc: Kembali   \u2022   Tab: Opsi Profil"), 15 * s, 0, W - m, H - 52 * s, 0.6 * a, 0, 2, false);
        }
    }
}
