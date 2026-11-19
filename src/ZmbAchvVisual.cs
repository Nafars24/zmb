using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ZMB
{
    class TrophyPopup
    {
        public string Head, Name, Sub, Icon, Grade;
        public double Start = -1;
        public bool Overlay;      // tampil di atas game yang sedang jalan (jendela overlay)
    }

    public partial class ZmbWindow
    {
        List<TrophyPopup> popups = new List<TrophyPopup>();
        Dictionary<Color, Brush> solidCache = new Dictionary<Color, Brush>();
        double listTextRight = 0;

        Brush SolidOf(Color c)
        {
            Brush b;
            if (solidCache.TryGetValue(c, out b)) return b;
            b = new SolidColorBrush(c); b.Freeze();
            solidCache[c] = b;
            return b;
        }

        // Level trofi ala PS3 (Perunggu 15, Perak 30, Emas 90, Platinum 180 poin)
        static int PsnLevel(int points, out double frac)
        {
            int[] th = { 0, 200, 600, 1200, 2400, 4000, 6000, 8000, 10000, 12000 };
            int lvl = 1;
            for (int i = 1; i < th.Length; i++) if (points >= th[i]) lvl = i + 1;
            int lo, hi;
            if (lvl < th.Length) { lo = th[lvl - 1]; hi = th[lvl]; }
            else
            {
                int extra = (points - 12000) / 8000;
                lvl = 10 + extra;
                lo = 12000 + extra * 8000; hi = lo + 8000;
            }
            frac = Math.Max(0, Math.Min(1, (double)(points - lo) / (hi - lo)));
            return lvl;
        }

        static IEnumerable<Achv> OrderedAchv(AchvSet s)
        {
            return s.Items.Where(a => a.Unlocked).OrderByDescending(a => a.When)
                .Concat(s.Items.Where(a => !a.Unlocked).OrderBy(a => a.Order));
        }

        FormattedText FTW(string s, double size, int face, double maxW, int lines)
        {
            string key = "W" + face + "|" + lines + "|" + Math.Round(size, 1).ToString(CultureInfo.InvariantCulture) + "|" + Math.Round(maxW) + "|" + s;
            FormattedText ft;
            if (textCache.TryGetValue(key, out ft)) return ft;
#pragma warning disable 618
            ft = new FormattedText(s, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, faces[face], Math.Max(1, size), brWhite);
#pragma warning restore 618
            ft.MaxTextWidth = Math.Max(10, maxW);
            ft.MaxLineCount = lines;
            ft.Trimming = TextTrimming.CharacterEllipsis;
            textCache[key] = ft;
            return ft;
        }

        double TxtW(DrawingContext dc, string s, double size, int face, double x, double y, double maxW, int lines, double a)
        {
            if (string.IsNullOrEmpty(s) || a <= 0.01) return 0;
            FormattedText ft = FTW(s, size, face, maxW, lines);
            dc.PushOpacity(Math.Min(1, a));
            dc.DrawText(ft, new Point(x, y));
            dc.Pop();
            return ft.Height;
        }

        void EnsureTrophyGeo()
        {
            if (trophyFill != null) return;
            trophyFill = Geometry.Parse("M28,12 H72 V34 C72,52 61,62 50,62 C39,62 28,52 28,34 Z M45,61 H55 V76 H45 Z M33,77 H67 V88 H33 Z");
            trophyFill.Freeze();
            trophyHandles = Geometry.Parse("M29,18 H17 C15,36 22,44 30,44 M71,18 H83 C85,36 78,44 70,44");
            trophyHandles.Freeze();
            trophyPen = new Pen(brWhite, 5); trophyPen.Freeze();
            trophyPenB = new Pen(brBlack, 5); trophyPenB.Freeze();
        }

        void DrawTrophyColored(DrawingContext dc, double cx, double cy, double size, Color c, double a)
        {
            if (DrawIcon(dc, "trophy", cx, cy, size * 1.05, a, c, true)) return;
            EnsureTrophyGeo();
            double k = size / 100.0 * 1.15;
            Brush b = SolidOf(c);
            Pen p = new Pen(b, 5);
            dc.PushOpacity(Math.Min(1, a));
            dc.PushTransform(new MatrixTransform(k, 0, 0, k, cx - 50 * k, cy - 50 * k));
            dc.DrawGeometry(b, null, trophyFill);
            dc.DrawGeometry(null, p, trophyHandles);
            dc.Pop();
            dc.Pop();
        }

        void DrawRing(DrawingContext dc, double cx, double cy, double r, double thick, double frac, double a)
        {
            dc.PushOpacity(Math.Min(1, a));
            Pen bgp = new Pen(SolidOf(Color.FromArgb(60, 255, 255, 255)), thick);
            dc.DrawEllipse(null, bgp, new Point(cx, cy), r, r);
            Color col = frac >= 0.999 ? Color.FromRgb(240, 200, 60) : Color.FromRgb(255, 255, 255);
            Pen fp = new Pen(SolidOf(col), thick);
            fp.StartLineCap = PenLineCap.Round; fp.EndLineCap = PenLineCap.Round;
            if (frac >= 0.999) dc.DrawEllipse(null, fp, new Point(cx, cy), r, r);
            else if (frac > 0.002)
            {
                double ang = frac * 2 * Math.PI;
                Point p0 = new Point(cx, cy - r);
                Point p1 = new Point(cx + r * Math.Sin(ang), cy - r * Math.Cos(ang));
                StreamGeometry g = new StreamGeometry();
                using (StreamGeometryContext c = g.Open())
                {
                    c.BeginFigure(p0, false, false);
                    c.ArcTo(p1, new Size(r, r), 0, frac > 0.5, SweepDirection.Clockwise, true, true);
                }
                g.Freeze();
                dc.DrawGeometry(null, fp, g);
            }
            dc.Pop();
            string pct = ((int)Math.Round(frac * 100)) + "%";
            double fs = r * 0.55;
            FormattedText ft = FT(pct, fs, 2, false, 0);
            dc.PushOpacity(Math.Min(1, a));
            dc.DrawText(ft, new Point(cx - ft.Width / 2, cy - ft.Height / 2));
            dc.Pop();
        }

        void DrawIconBox(DrawingContext dc, string icon, Rect r, double a, Achv ach, AchvSet set)
        {
            bool secret = ach != null && ach.Hidden && !ach.Unlocked && set.Source == "RPCS3";
            ImageSource img = null;
            if (!secret && !string.IsNullOrEmpty(icon)) img = GetImage("ach:" + icon, IconLoader(icon, 160));
            if (img != null) { BitmapSourceFit(dc, img, r, a, false); return; }
            dc.PushOpacity(Math.Min(1, a));
            dc.DrawRoundedRectangle(SolidOf(Color.FromArgb(60, 255, 255, 255)), penThin, r, r.Height * 0.12, r.Height * 0.12);
            dc.Pop();
            if (secret) Txt(dc, "?", r.Height * 0.5, 2, r.X + r.Width / 2, r.Y + (r.Height - TextH(r.Height * 0.5)) / 2, a, 0, 1, false);
            else if (ach != null && ach.Grade != null) DrawTrophyColored(dc, r.X + r.Width / 2, r.Y + r.Height / 2, r.Height * 0.7, GradeColor(ach.Grade), a);
            else Glyph(dc, "#TROPHY", "T", r.X + r.Width / 2, r.Y + r.Height / 2, r.Height * 0.6, a);
        }

        // Panel trofi visual di sisi kanan (muncul saat menjelajah daftar trofi)
        bool AchvPanelFor(XmbItem it, out AchvSet set)
        {
            set = null;
            if (it == null) return false;
            set = it.AchSet;
            if (set == null && it.AchKey != null) set = Peek<AchvSet>(it.AchKey);
            return set != null && set.Error == null && set.Total > 0;
        }

        void DrawAchvPanel(DrawingContext dc, double W, double H, XmbItem sel, AchvSet set, double alpha)
        {
            if (alpha <= 0.01) return;
            double s = S;
            double px = W * 0.585, pw = W - px - 56 * s;
            double py = H * 0.15, ph = H * 0.74;
            dc.PushOpacity(Math.Min(1, alpha));
            dc.DrawRoundedRectangle(SolidOf(Color.FromArgb(105, 0, 0, 0)), penThin, new Rect(px, py, pw, ph), 14 * s, 14 * s);
            dc.Pop();

            double pad = 24 * s;
            // --- header: ikon game, judul, ring progres
            double ringR = 50 * s;
            double ringCx = px + pw - pad - ringR - 4 * s, ringCy = py + pad + ringR + 4 * s;
            Rect gi = new Rect(px + pad, py + pad, 170 * s, 96 * s);
            if (!string.IsNullOrEmpty(set.Icon))
            {
                ImageSource gimg = GetImage("ach:" + set.Icon, IconLoader(set.Icon, 460));
                if (gimg != null) BitmapSourceFit(dc, gimg, gi, alpha, false);
                else Glyph(dc, "#TROPHY", "T", gi.X + gi.Width / 2, gi.Y + gi.Height / 2, gi.Height * 0.7, alpha);
            }
            else Glyph(dc, "#TROPHY", "T", gi.X + gi.Width / 2, gi.Y + gi.Height / 2, gi.Height * 0.7, alpha);
            double tx = gi.Right + 18 * s, tw = ringCx - ringR - 18 * s - tx;
            double th1 = TxtW(dc, set.Title, 24 * s, 2, tx, py + pad, tw, 2, alpha);
            string src = set.Source == "RPCS3" ? "Trofi PlayStation 3" : set.Source == "Steam" ? "Achievement Steam" : "RetroAchievements" + (set.Extra.Length > 0 ? "  \u2022  " + set.Extra : "");
            TxtW(dc, src, 16 * s, 0, tx, py + pad + th1 + 4 * s, tw, 1, 0.65 * alpha);
            TxtW(dc, set.Unlocked + " / " + set.Total + " terbuka", 18 * s, 0, tx, py + pad + th1 + 30 * s, tw, 1, 0.9 * alpha);
            DrawRing(dc, ringCx, ringCy, ringR, 9 * s, set.Total > 0 ? (double)set.Unlocked / set.Total : 0, alpha);

            // --- baris jenis trofi / poin
            double y = py + pad + 130 * s;
            if (set.Source == "RPCS3")
            {
                string[] gr = { "P", "G", "S", "B" };
                double cw = (pw - pad * 2) / 4;
                for (int i = 0; i < 4; i++)
                {
                    double cx = px + pad + cw * i + 24 * s;
                    DrawTrophyColored(dc, cx, y + 16 * s, 40 * s, GradeColor(gr[i]), alpha);
                    string cnt = gr[i] == "P" ? set.CountGrade("P", true).ToString() + "/" + set.CountGrade("P", false) : set.CountGrade(gr[i], true) + "/" + set.CountGrade(gr[i], false);
                    Txt(dc, cnt, 20 * s, 2, cx + 26 * s, y + 16 * s - TextH(20 * s) / 2, alpha, 0, 0, false);
                }
            }
            else if (set.Source == "RetroAchievements")
            {
                int pts = set.Items.Sum(a => a.Points), got = set.Items.Where(a => a.Unlocked).Sum(a => a.Points);
                int hc = set.Items.Count(a => a.Hardcore);
                TxtW(dc, "Poin  " + got + " / " + pts + "      Hardcore  " + hc + " / " + set.Total, 20 * s, 2, px + pad, y + 4 * s, pw - pad * 2, 1, alpha);
            }
            else
            {
                Achv rare = set.Items.Where(a => a.Unlocked && a.Rarity >= 0).OrderBy(a => a.Rarity).FirstOrDefault();
                TxtW(dc, rare != null ? "Paling langka: " + rare.Name + " (" + rare.Rarity.ToString("0.0") + "%)" : "Belum ada achievement langka", 18 * s, 0, px + pad, y + 6 * s, pw - pad * 2, 1, 0.85 * alpha);
            }
            y += 46 * s;
            dc.DrawRectangle(WhiteA(0.35 * alpha), null, new Rect(px + pad, y, pw - pad * 2, 1));
            y += 16 * s;

            // --- detail trofi terpilih
            Achv a0 = sel.Ach;
            double detailH = 170 * s;
            if (a0 != null)
            {
                bool secret = a0.Hidden && !a0.Unlocked;
                string icon = a0.Unlocked ? a0.Icon : (a0.IconLocked ?? a0.Icon);
                Rect ib = new Rect(px + pad, y, 128 * s, 128 * s);
                if (a0.Unlocked) Glow(dc, ib.X + ib.Width / 2, ib.Y + ib.Height / 2, ib.Width * 0.85, 0.25 * alpha);
                DrawIconBox(dc, icon, ib, (!a0.Unlocked && string.IsNullOrEmpty(a0.IconLocked)) ? alpha * 0.45 : alpha, a0, set);
                double dx = ib.Right + 20 * s, dw = px + pw - pad - dx;
                double hN = TxtW(dc, secret ? "Trofi Tersembunyi" : a0.Name, 24 * s, 2, dx, y, dw, 2, alpha);
                double hD = TxtW(dc, secret ? "Detail akan terlihat setelah trofi didapat." : (a0.Desc.Length > 0 ? a0.Desc : "-"), 17 * s, 0, dx, y + hN + 4 * s, dw, 3, 0.8 * alpha);
                double cy2 = y + hN + hD + 14 * s;
                string chip = a0.Unlocked ? (a0.When > DateTime.MinValue ? "Terbuka " + a0.When.ToString("dd MMM yyyy") : "Terbuka") : "Terkunci";
                Color chipC = a0.Unlocked ? Color.FromRgb(60, 170, 90) : Color.FromRgb(90, 94, 104);
                double cxp = dx;
                cxp += Chip(dc, chip, chipC, cxp, cy2, alpha) + 8 * s;
                if (a0.Grade != null) cxp += Chip(dc, GradeName(a0.Grade), GradeColor(a0.Grade), cxp, cy2, alpha) + 8 * s;
                if (a0.Points > 0 && set.Source == "RetroAchievements") cxp += Chip(dc, a0.Points + " poin" + (a0.Hardcore ? " HC" : ""), Color.FromRgb(30, 100, 180), cxp, cy2, alpha) + 8 * s;
                if (a0.Rarity >= 0) Chip(dc, a0.Rarity.ToString("0.0") + "% pemain", Color.FromRgb(120, 70, 160), cxp, cy2, alpha);
            }
            else
            {
                TxtW(dc, "Terakhir didapat", 17 * s, 0, px + pad, y, pw - pad * 2, 1, 0.65 * alpha);
                double ly = y + 28 * s;
                foreach (Achv a in set.Items.Where(x => x.Unlocked).OrderByDescending(x => x.When).Take(3))
                {
                    Rect ib = new Rect(px + pad, ly, 38 * s, 38 * s);
                    DrawIconBox(dc, a.Icon, ib, alpha, a, set);
                    TxtW(dc, a.Name + (a.When > DateTime.MinValue ? "   " + a.When.ToString("dd MMM yyyy") : ""), 18 * s, 0, ib.Right + 12 * s, ly + 8 * s, pw - pad * 2 - 50 * s, 1, 0.9 * alpha);
                    ly += 44 * s;
                }
                if (set.Unlocked == 0) TxtW(dc, "Belum ada yang terbuka. Ayo main!", 18 * s, 0, px + pad, ly, pw - pad * 2, 1, 0.8 * alpha);
            }
            y += detailH;

            // --- grid ikon semua trofi
            double cell = 50 * s, gap = 8 * s;
            int cols = Math.Max(1, (int)((pw - pad * 2 + gap) / (cell + gap)));
            int rows = Math.Max(0, (int)((py + ph - pad - y + gap) / (cell + gap)));
            List<Achv> all = OrderedAchv(set).ToList();
            int maxShow = cols * rows;
            int startIdx = 0;
            if (a0 != null && maxShow > 0)
            {
                int selIdx = all.IndexOf(a0);
                if (selIdx >= maxShow) startIdx = ((selIdx - maxShow) / cols + 1) * cols;
            }
            for (int i = 0; i < maxShow && startIdx + i < all.Count; i++)
            {
                Achv a = all[startIdx + i];
                double gx = px + pad + (i % cols) * (cell + gap), gy = y + (i / cols) * (cell + gap);
                Rect cr = new Rect(gx, gy, cell, cell);
                string icon = a.Unlocked ? a.Icon : (a.IconLocked ?? a.Icon);
                bool dim = !a.Unlocked && string.IsNullOrEmpty(a.IconLocked);
                DrawIconBox(dc, icon, cr, dim ? alpha * 0.35 : alpha, a, set);
                if (a == a0)
                {
                    Pen hp = new Pen(brWhite, 2.5 * s);
                    dc.PushOpacity(Math.Min(1, alpha * (0.75 + 0.25 * Math.Sin(T * 5))));
                    dc.DrawRoundedRectangle(null, hp, new Rect(gx - 3 * s, gy - 3 * s, cell + 6 * s, cell + 6 * s), 6 * s, 6 * s);
                    dc.Pop();
                }
            }
            if (all.Count > startIdx + maxShow && maxShow > 0)
                Txt(dc, "+" + (all.Count - startIdx - maxShow) + " lagi", 15 * s, 0, px + pw - pad, py + ph - pad + 2 * s, 0.6 * alpha, 0, 2, false);
        }

        double Chip(DrawingContext dc, string text, Color c, double x, double y, double a)
        {
            double s = S;
            FormattedText ft = FT(text, 15 * s, 2, false, 0);
            double w = ft.Width + 20 * s, h = ft.Height + 8 * s;
            dc.PushOpacity(Math.Min(1, a));
            dc.DrawRoundedRectangle(SolidOf(c), null, new Rect(x, y, w, h), h / 2, h / 2);
            dc.DrawText(ft, new Point(x + 10 * s, y + 4 * s));
            dc.Pop();
            return w;
        }

        // ===================== POPUP "TROFI DIDAPAT" (gaya PlayStation, kanan bawah) =====================
        const double TrophyDur = 6.0;

        // Grade untuk ikon trofi: PS3 punya sendiri; RA dari poin; Steam perunggu
        static string PopupGrade(Achv a, AchvSet set)
        {
            if (a.Grade != null) return a.Grade;
            if (set.Source == "RetroAchievements")
                return a.Points >= 25 ? "G" : (a.Points >= 10 ? "S" : "B");
            return "B";
        }

        void EnqueueTrophyPopup(Achv a, AchvSet set) { EnqueueTrophyPopup(a, set, false); }

        void EnqueueTrophyPopup(Achv a, AchvSet set, bool overlay)
        {
            TrophyPopup p = new TrophyPopup();
            p.Grade = PopupGrade(a, set);
            p.Head = "Kamu mendapat trofi.";
            p.Name = a.Name;
            List<string> sub = new List<string>();
            sub.Add(GradeName(p.Grade));
            if (a.Points > 0 && set.Source == "RetroAchievements") sub.Add(a.Points + " poin");
            sub.Add(set.Title);
            p.Sub = string.Join("  \u2022  ", sub.ToArray());
            p.Icon = a.Icon;
            p.Overlay = overlay && cfg.TrophyOverlay;
            if (!string.IsNullOrEmpty(p.Icon)) GetImage("ach:" + p.Icon, IconLoader(p.Icon, 160)); // mulai unduh ikon lebih awal
            if (p.Overlay) ShowOverlayPopup(p); else popups.Add(p);
        }

        // Trofi platinum ala PS saat semua trofi/achievement game sudah didapat
        void EnqueuePlatinum(AchvSet set, bool overlay)
        {
            TrophyPopup p = new TrophyPopup();
            p.Grade = "P";
            p.Head = "Kamu mendapat trofi platinum!";
            p.Name = set.Title;
            p.Sub = "Platinum  \u2022  " + set.Total + "/" + set.Total + " terbuka";
            p.Icon = set.Icon;
            p.Overlay = overlay && cfg.TrophyOverlay;
            if (p.Overlay) ShowOverlayPopup(p); else popups.Add(p);
        }

        void DrawTrophyPopups(DrawingContext dc, double W, double H)
        {
            if (popups.Count == 0) return;
            TrophyPopup p = popups[0];
            double now = T;
            if (p.Start < 0) { p.Start = now; Sfx.Trophy(); }
            double age = now - p.Start;
            if (age > TrophyDur) { popups.RemoveAt(0); return; }
            double s = S;
            double w = 460 * s, h = 104 * s;
            DrawTrophyCard(dc, p, W - w - 48 * s, H - h - 92 * s, s, age);
        }

        // Kartu notifikasi: ikon trofi game di kiri, trofi berwarna + teks di kanan
        void DrawTrophyCard(DrawingContext dc, TrophyPopup p, double x, double y, double s, double age)
        {
            double w = 460 * s, h = 104 * s;
            double inT = Math.Min(1, age / 0.4), outT = Math.Max(0, (age - (TrophyDur - 0.4)) / 0.4);
            double e = 1 - Math.Pow(1 - inT, 3);
            double a = Math.Max(0, Math.Min(e, 1 - outT));
            if (a <= 0) return;
            x += (1 - e) * 70 * s + outT * 50 * s;
            Rect r = new Rect(x, y, w, h);
            dc.PushOpacity(a);
            // bayangan lembut + panel gelap
            dc.DrawRoundedRectangle(SolidOf(Color.FromArgb(70, 0, 0, 0)), null, new Rect(x + 3 * s, y + 5 * s, w, h), 12 * s, 12 * s);
            LinearGradientBrush bg = new LinearGradientBrush(Color.FromArgb(246, 44, 45, 52), Color.FromArgb(246, 28, 29, 34), 90);
            bg.Freeze();
            dc.DrawRoundedRectangle(bg, new Pen(SolidOf(Color.FromArgb(38, 255, 255, 255)), Math.Max(1, s)), r, 12 * s, 12 * s);

            Rect ib = new Rect(x + 16 * s, y + 16 * s, 72 * s, 72 * s);
            Achv fake = new Achv(); fake.Grade = p.Grade; fake.Unlocked = true;
            DrawIconBox(dc, p.Icon, ib, 1, fake, new AchvSet());
            // kilau singkat saat muncul
            double shine = Math.Max(0, 1 - Math.Abs(age - 0.55) / 0.35);
            if (shine > 0) dc.DrawRoundedRectangle(WhiteA(0.28 * shine), null, ib, 6 * s, 6 * s);

            double tx = ib.Right + 16 * s, tw = r.Right - tx - 16 * s;
            Color gc = GradeColor(p.Grade);
            DrawTrophyColored(dc, tx + 9 * s, y + 29 * s, 20 * s, gc, 1);
            Txt(dc, p.Head, 15 * s, 0, tx + 24 * s, y + 19 * s, 0.82, tw - 24 * s, 0, false);
            Txt(dc, p.Name, 21 * s, 2, tx, y + 43 * s, 1, tw, 0, false);
            Txt(dc, p.Sub, 13.5 * s, 0, tx, y + 74 * s, 0.55, tw, 0, false);
            // garis warna grade di tepi bawah
            dc.DrawRoundedRectangle(SolidOf(Color.FromArgb(200, gc.R, gc.G, gc.B)), null, new Rect(x + 16 * s, r.Bottom - 4 * s, w - 32 * s, 2 * s), s, s);
            dc.Pop();
        }
    }
}
