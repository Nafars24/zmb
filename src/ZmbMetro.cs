using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ZMB
{
    // Mode Aurora: dashboard bergaya Xbox 360 (tab di atas, ubin/tile, latar gradasi abu).
    // Daftar game tetap memakai rak coverflow; semua menu lain memakai ubin.
    public class MetroTile
    {
        public XmbItem Item;
        public int Col, Row, W = 1, H = 1;
        public Color Color = Color.FromRgb(82, 176, 68);
        public string Label;
        public string Glyph;
        public Action Act;
        public bool Hero;
    }

    public partial class ZmbWindow
    {
        int metroPivot;              // 0 = beranda, selebihnya kategori
        double metroPivotAnim;
        int metroHomeSel;
        double metroScroll, metroScrollTarget, metroLastT = -1, metroLeft = -1;
        List<MetroTile> metroHomeCache;
        double metroHomeBuiltAt = -10;
        Dictionary<string, string> metroBgPath = new Dictionary<string, string>();

        static readonly Color MetroGreen = Color.FromRgb(82, 176, 68);

        // urutan tab: beranda, game, video, musik, foto, pengaturan, pengguna
        List<int> MetroOrder()
        {
            List<int> o = new List<int>();
            o.Add(-1);
            string[] want = { "Game", "Video", "Musik", "Foto", "Pengaturan", "Pengguna" };
            foreach (string w in want)
                for (int i = 0; i < cats.Count; i++) if (cats[i].Title == w && !o.Contains(i)) o.Add(i);
            for (int i = 0; i < cats.Count; i++) if (!o.Contains(i)) o.Add(i);
            return o;
        }

        string PivotLabel(int catIdx)
        {
            if (catIdx < 0) return "beranda";
            return cats[catIdx].Title.ToLowerInvariant();
        }

        // Mode ubin aktif: di level atas, atau di sub-menu yang tidak berisi game
        bool MetroActive()
        {
            if (cfg.ViewMode != 1 || mode != Mode.Xmb) return false;
            if (stack.Count == 0) return true;
            return !stack[stack.Count - 1].Items.Any(i => i.Game != null);
        }

        // ---------- penyusunan ubin ----------
        List<MetroTile> MetroTiles()
        {
            if (stack.Count > 0) return GridTiles(stack[stack.Count - 1].Items);
            List<int> order = MetroOrder();
            int ci = order[Math.Max(0, Math.Min(metroPivot, order.Count - 1))];
            if (ci < 0) return HomeTiles();
            return GridTiles(cats[ci].Items);
        }

        List<MetroTile> GridTiles(List<XmbItem> items)
        {
            List<MetroTile> l = new List<MetroTile>();
            for (int i = 0; i < items.Count; i++)
            {
                MetroTile t = new MetroTile();
                t.Item = items[i];
                t.Col = i / 3; t.Row = i % 3;
                t.Label = items[i].Title;
                t.Glyph = items[i].Glyph;
                if (items[i].Badge != null) t.Color = Mix(items[i].BadgeColor, Colors.White, 0.08);
                l.Add(t);
            }
            return l;
        }

        List<MetroTile> HomeTiles()
        {
            if (metroHomeCache != null && T - metroHomeBuiltAt < 2) return metroHomeCache;
            List<MetroTile> l = new List<MetroTile>();
            List<GameEntry> recent = cfg.AllKnown().Where(g => g.PlayCount > 0 && g.LastPlayed > DateTime.MinValue)
                .OrderByDescending(g => g.LastPlayed).Take(6).ToList();
            List<int> order = MetroOrder();
            Func<string, int> pivotOf = delegate (string title)
            {
                for (int p = 0; p < order.Count; p++) if (order[p] >= 0 && cats[order[p]].Title == title) return p;
                return -1;
            };

            // ubin besar: game terakhir dimainkan
            MetroTile hero = new MetroTile();
            hero.Col = 1; hero.Row = 0; hero.W = 2; hero.H = 2; hero.Hero = true;
            if (recent.Count > 0) { hero.Item = GameItem(recent[0], null); hero.Label = "Main " + recent[0].Name; }
            else
            {
                int gp = pivotOf("Game");
                hero.Label = "Game"; hero.Glyph = "\uE7FC";
                hero.Act = delegate { if (gp >= 0) SetPivot(gp); };
            }
            l.Add(hero);

            // kolom kiri: game sebelumnya, favorit, terakhir dimainkan
            MetroTile c0 = new MetroTile(); c0.Col = 0; c0.Row = 0;
            if (recent.Count > 1) { c0.Item = GameItem(recent[1], null); c0.Label = recent[1].Name; }
            else { c0.Label = "Profil"; c0.Glyph = "\uE77B"; c0.Act = delegate { int pp = pivotOf("Pengguna"); if (pp >= 0) SetPivot(pp); }; }
            l.Add(c0);

            MetroTile fav = new MetroTile(); fav.Col = 0; fav.Row = 1; fav.Label = "Favorit"; fav.Glyph = "\uE735";
            fav.Item = MakeItem("Favorit", "", "\uE735", null);
            fav.Item.Children = delegate { return cfg.AllKnown().Where(g => g.Favorite).OrderBy(g => g.Name).Select(g => GameItem(g, null)).ToList(); };
            l.Add(fav);

            MetroTile rec = new MetroTile(); rec.Col = 0; rec.Row = 2; rec.Label = "Terakhir Dimainkan"; rec.Glyph = "\uE81C";
            rec.Item = MakeItem("Terakhir Dimainkan", "", "\uE81C", null);
            rec.Item.Children = delegate { return cfg.AllKnown().Where(g => g.PlayCount > 0).OrderByDescending(g => g.LastPlayed).Take(12).Select(g => GameItem(g, null)).ToList(); };
            l.Add(rec);

            // baris bawah ubin besar: game lain yang baru dimainkan
            for (int k = 0; k < 2; k++)
            {
                MetroTile t = new MetroTile(); t.Col = 1 + k; t.Row = 2;
                if (recent.Count > 2 + k) { t.Item = GameItem(recent[2 + k], null); t.Label = recent[2 + k].Name; }
                else
                {
                    string[] nm = { "Video", "Foto" };
                    string[] gl = { "\uE714", "\uEB9F" };
                    string target = nm[k];
                    t.Label = nm[k]; t.Glyph = gl[k]; t.Color = k == 0 ? Color.FromRgb(214, 64, 52) : Color.FromRgb(46, 130, 200);
                    t.Act = delegate { int pp = pivotOf(target); if (pp >= 0) SetPivot(pp); };
                }
                l.Add(t);
            }

            // kolom kanan: pintasan menu
            string[][] sc = {
                new[] { "Musik", "\uEC4F" }, new[] { "Video", "\uE714" }, new[] { "Pengaturan", "\uE713" },
                new[] { "Foto", "\uEB9F" }, new[] { "Pengguna", "\uE77B" }, new[] { "Koleksi Trofi", "#TROPHY" } };
            Color[] scCol = { Color.FromRgb(232, 120, 30), Color.FromRgb(214, 64, 52), Color.FromRgb(90, 96, 110),
                              Color.FromRgb(46, 130, 200), Color.FromRgb(120, 80, 170), Color.FromRgb(200, 160, 40) };
            for (int k = 0; k < sc.Length; k++)
            {
                MetroTile t = new MetroTile(); t.Col = 3 + k / 3; t.Row = k % 3;
                t.Label = sc[k][0]; t.Glyph = sc[k][1]; t.Color = scCol[k];
                string target = sc[k][0];
                if (target == "Koleksi Trofi")
                {
                    t.Item = MakeItem("Koleksi Trofi", "", "#TROPHY", null);
                    t.Item.Children = BuildTrophyHub;
                }
                else t.Act = delegate { int pp = pivotOf(target); if (pp >= 0) SetPivot(pp); };
                l.Add(t);
            }
            metroHomeCache = l;
            metroHomeBuiltAt = T;
            return l;
        }

        // ---------- pilihan ----------
        int MetroSel()
        {
            if (stack.Count > 0) return stack[stack.Count - 1].Index;
            List<int> order = MetroOrder();
            int ci = order[Math.Max(0, Math.Min(metroPivot, order.Count - 1))];
            return ci < 0 ? metroHomeSel : cats[ci].Index;
        }

        void SetMetroSel(int i)
        {
            if (stack.Count > 0) { stack[stack.Count - 1].Index = i; return; }
            List<int> order = MetroOrder();
            int ci = order[Math.Max(0, Math.Min(metroPivot, order.Count - 1))];
            if (ci < 0) metroHomeSel = i; else cats[ci].Index = i;
        }

        void SetPivot(int p)
        {
            List<int> order = MetroOrder();
            if (p < 0 || p >= order.Count || p == metroPivot) return;
            metroPivot = p;
            if (order[p] >= 0) { catIndex = order[p]; cfg.LastCategory = catIndex; }
            metroScrollTarget = 0;
            Sfx.Tick();
        }

        // pindah ke ubin terdekat ke arah (dx, dy)
        static int MetroMove(List<MetroTile> t, int sel, int dx, int dy)
        {
            if (sel < 0 || sel >= t.Count) return t.Count > 0 ? 0 : -1;
            double cx = t[sel].Col + t[sel].W / 2.0, cy = t[sel].Row + t[sel].H / 2.0;
            int best = -1; double bestScore = 1e9;
            for (int j = 0; j < t.Count; j++)
            {
                if (j == sel) continue;
                double ox = t[j].Col + t[j].W / 2.0 - cx, oy = t[j].Row + t[j].H / 2.0 - cy;
                double primary = dx != 0 ? ox * dx : oy * dy;
                if (primary <= 0.01) continue;
                double secondary = dx != 0 ? Math.Abs(oy) : Math.Abs(ox);
                double score = primary + secondary * 2;
                if (score < bestScore) { bestScore = score; best = j; }
            }
            return best;
        }

        void HandleMetroKey(XKey k)
        {
            List<MetroTile> tiles = MetroTiles();
            int sel = Math.Max(0, Math.Min(MetroSel(), tiles.Count - 1));
            List<int> order = MetroOrder();
            switch (k)
            {
                case XKey.Left:
                case XKey.Right:
                case XKey.Up:
                case XKey.Down:
                    {
                        int dx = k == XKey.Left ? -1 : k == XKey.Right ? 1 : 0;
                        int dy = k == XKey.Up ? -1 : k == XKey.Down ? 1 : 0;
                        int n = MetroMove(tiles, sel, dx, dy);
                        if (n >= 0) { SetMetroSel(n); Sfx.Tick(); }
                        else if (dx != 0 && stack.Count == 0) SetPivot(metroPivot + dx);   // ujung baris: pindah tab
                        else if (dx < 0 && stack.Count > 0) PopLevel();
                        break;
                    }
                case XKey.PageUp:
                    if (stack.Count == 0) SetPivot(metroPivot - 1);
                    break;
                case XKey.PageDown:
                    if (stack.Count == 0) SetPivot(metroPivot + 1);
                    break;
                case XKey.Ok:
                    if (sel >= 0 && sel < tiles.Count)
                    {
                        MetroTile t = tiles[sel];
                        if (t.Act != null) { Sfx.Ok(); t.Act(); }
                        else if (t.Item != null) ActivateItem(t.Item);
                    }
                    break;
                case XKey.Back:
                    if (stack.Count > 0) PopLevel();
                    else if (metroPivot != 0) { metroPivot = 0; metroScrollTarget = 0; Sfx.Back(); }
                    break;
                case XKey.Select:
                case XKey.Options:
                    if (sel >= 0 && sel < tiles.Count && tiles[sel].Item != null)
                    {
                        XmbItem it = tiles[sel].Item;
                        if (k == XKey.Select && it.Game != null) OpenDetails(it.Game);
                        else if (it.Options != null) OpenOptions(it.Title, it.Options());
                        else Sfx.Error();
                    }
                    else Sfx.Error();
                    break;
            }
        }

        // ---------- gambar ----------
        void DrawMetroBack(DrawingContext dc, double W, double H)
        {
            LinearGradientBrush lb = new LinearGradientBrush();
            lb.StartPoint = new Point(0, 0); lb.EndPoint = new Point(0, 1);
            lb.GradientStops.Add(new GradientStop(Color.FromRgb(176, 178, 182), 0));
            lb.GradientStops.Add(new GradientStop(Color.FromRgb(150, 152, 157), 0.55));
            lb.GradientStops.Add(new GradientStop(Color.FromRgb(104, 106, 111), 1));
            lb.Freeze();
            dc.DrawRectangle(lb, null, new Rect(0, 0, W, H));
            RadialGradientBrush rg = new RadialGradientBrush();
            rg.GradientStops.Add(new GradientStop(Color.FromArgb(120, 236, 237, 240), 0));
            rg.GradientStops.Add(new GradientStop(Color.FromArgb(0, 236, 237, 240), 1));
            rg.Freeze();
            dc.DrawEllipse(rg, null, new Point(W * 0.45, H * 0.38), W * 0.62, H * 0.62);
        }

        ImageSource TileImage(MetroTile t)
        {
            XmbItem it = t.Item;
            if (it == null) return null;
            if (it.Game != null)
            {
                // ubin game memakai gambar latar (lebar) supaya mirip ubin Xbox; kalau tidak ada pakai box art
                GameEntry g = it.Game;
                string key = g.Platform + "|" + g.Path;
                string path;
                if (!metroBgPath.TryGetValue(key, out path))
                {
                    bool crisp;
                    try { path = GameScanner.FindBackground(g, cfg.FindPlatform(g.Platform), out crisp); } catch { path = null; }
                    metroBgPath[key] = path;
                }
                if (path != null)
                {
                    string pth = path;
                    ImageSource bg = GetImage("tile:" + pth, delegate { return ImgConv.LoadFile(pth, t.Hero ? 1280 : 640); });
                    if (bg != null) return bg;
                }
                ImageSource box = BoxImage(it);
                if (box != null && !it.BoxIsIcon) return box;
            }
            if (it.Kind == "platform") return null; // ikon platform digambar di tengah ubin berwarna
            if (it.ImageKey != null) return GetImage(it.ImageKey, it.ImageLoader);
            return null;
        }

        void DrawMetro(DrawingContext dc, double W, double H, double fade, double now)
        {
            double s = S;
            double dt = metroLastT < 0 ? 0 : Math.Min(0.1, now - metroLastT);
            metroLastT = now;
            List<int> order = MetroOrder();
            if (metroPivot >= order.Count) metroPivot = 0;
            double ka = 1 - Math.Exp(-dt * 12);
            metroPivotAnim += (metroPivot - metroPivotAnim) * ka;
            if (Math.Abs(metroPivot - metroPivotAnim) < 0.001) metroPivotAnim = metroPivot;

            // konten di tengah layar kalau muat; kalau lebih lebar, mulai dari kiri dan bisa digeser
            List<MetroTile> tiles = MetroTiles();
            double tw = 270 * s, th = 196 * s, gap = 10 * s, top = 250 * s;
            int cols = 0;
            foreach (MetroTile t0 in tiles) cols = Math.Max(cols, t0.Col + t0.W);
            double gridW = cols * (tw + gap) - gap;
            double margin = 150 * s;
            bool fits = gridW <= W - margin * 2;
            double leftT = fits ? Math.Round((W - gridW) / 2) : margin;
            if (fits) { metroScrollTarget = 0; }
            // geser posisi konten dengan halus saat pindah tab (lebar grid tiap tab berbeda)
            if (metroLeft < 0 || dt == 0) metroLeft = leftT;
            metroLeft += (leftT - metroLeft) * ka;
            double left = metroLeft;

            // ---- tab di atas ----
            double py = 150 * s, pfs = 40 * s;
            if (stack.Count == 0)
            {
                // posisi tab berurutan; tab aktif (beranimasi) selalu tepat di tepi kiri konten,
                // tab sebelumnya mengintip di kiri seperti dashboard Xbox
                int n = order.Count;
                double[] pos = new double[n];
                double[] wid = new double[n];
                double acc = 0, sp = 36 * s;
                for (int p = 0; p < n; p++) { wid[p] = FT(PivotLabel(order[p]), pfs, 0, false, 0).Width; pos[p] = acc; acc += wid[p] + sp; }
                int f0 = (int)Math.Floor(metroPivotAnim);
                f0 = Math.Max(0, Math.Min(n - 1, f0));
                int f1 = Math.Min(n - 1, f0 + 1);
                double fr = metroPivotAnim - f0;
                double anchor = pos[f0] + (pos[f1] - pos[f0]) * fr;
                for (int p = 0; p < n; p++)
                {
                    double xx = left + pos[p] - anchor;
                    if (xx + wid[p] < 0) continue;
                    if (xx > W) break;
                    double a = p == metroPivot ? 1 : 0.62;
                    if (xx < left - 1) a *= Math.Max(0, 1 - (left - xx) / (wid[p] + sp)) * 0.55; // tab sebelumnya memudar
                    Txt(dc, PivotLabel(order[p]), pfs, 0, xx, py, a * fade, 0, 0, false);
                }
            }
            else
            {
                // di dalam sub-menu: judul jalur
                List<string> path = new List<string>();
                int ci = order[Math.Max(0, Math.Min(metroPivot, order.Count - 1))];
                path.Add(PivotLabel(ci));
                foreach (Level l in stack) path.Add(l.Title.ToLowerInvariant());
                Txt(dc, string.Join("  \u203A  ", path.ToArray()), pfs, 0, left, py, fade, W - left * 2, 0, false);
            }

            // ---- ubin ----
            int sel = Math.Max(0, Math.Min(MetroSel(), tiles.Count - 1));
            if (tiles.Count > 0 && !fits)
            {
                MetroTile st = tiles[sel];
                double sx0 = st.Col * (tw + gap), sx1 = sx0 + st.W * tw + (st.W - 1) * gap;
                double viewW = W - left - margin;
                if (sx1 - metroScrollTarget > viewW) metroScrollTarget = sx1 - viewW;
                if (sx0 - metroScrollTarget < 0) metroScrollTarget = Math.Max(0, sx0 - (stack.Count == 0 ? 0 : 0));
            }
            metroScroll += (metroScrollTarget - metroScroll) * ka;
            double slide = stack.Count == 0 ? (metroPivot - metroPivotAnim) * 220 * s : levelSlide * 160 * s;
            double ga = fade * Math.Max(0, 1 - Math.Abs(stack.Count == 0 ? metroPivot - metroPivotAnim : levelSlide) * 1.2);

            for (int i = 0; i < tiles.Count; i++)
            {
                if (i == sel) continue;
                DrawTile(dc, tiles[i], left - metroScroll + slide, top, tw, th, gap, false, ga, W);
            }
            if (tiles.Count > 0) DrawTile(dc, tiles[sel], left - metroScroll + slide, top, tw, th, gap, true, ga, W);

            // ---- petunjuk tombol kiri bawah ----
            DrawMetroHints(dc, W, H, fade, left);
        }

        void DrawTile(DrawingContext dc, MetroTile t, double ox, double oy, double tw, double th, double gap, bool selected, double a, double W)
        {
            double s = S;
            Rect r = new Rect(ox + t.Col * (tw + gap), oy + t.Row * (th + gap), t.W * tw + (t.W - 1) * gap, t.H * th + (t.H - 1) * gap);
            if (r.Right < -20 || r.Left > W + 20 || a <= 0.01) return;
            if (selected)
            {
                double g = 6 * s;
                r = new Rect(r.X - g / 2, r.Y - g / 2, r.Width + g, r.Height + g);
                dc.DrawRectangle(BlackA(0.28 * a), null, new Rect(r.X + 5 * s, r.Y + 7 * s, r.Width, r.Height));
            }
            ImageSource img = TileImage(t);
            if (img != null)
            {
                dc.DrawRectangle(SolidOf(Color.FromRgb(40, 42, 46)), null, r);
                dc.PushClip(new RectangleGeometry(r));
                DrawImageFill(dc, img, r, a);
                dc.Pop();
                // strip label bawah
                double lh = Math.Min(r.Height * 0.3, 40 * s);
                LinearGradientBrush lg = new LinearGradientBrush(Color.FromArgb(0, 0, 0, 0), Color.FromArgb(200, 0, 0, 0), 90);
                lg.Freeze();
                dc.DrawRectangle(BrushA(lg, a), null, new Rect(r.X, r.Bottom - lh * 1.4, r.Width, lh * 1.4));
                Txt(dc, t.Label ?? "", (t.Hero ? 22 : 17) * s, 0, r.X + 10 * s, r.Bottom - lh + 4 * s, a, r.Width - 20 * s, 0, false);
            }
            else
            {
                LinearGradientBrush tb = new LinearGradientBrush(Mix(t.Color, Colors.White, 0.12), Mix(t.Color, Colors.Black, 0.12), 90);
                tb.Freeze();
                dc.DrawRectangle(BrushA(tb, a), null, r);
                double icon = Math.Min(r.Width, r.Height) * (t.Hero ? 0.32 : 0.36);
                ImageSource pic = t.Item != null && t.Item.Kind == "platform" && t.Item.ImageKey != null ? GetImage(t.Item.ImageKey, t.Item.ImageLoader) : null;
                if (pic != null)
                {
                    double isz = Math.Min(r.Width, r.Height) * 0.55;
                    DrawIconImage(dc, pic, new Rect(r.X + (r.Width - isz) / 2, r.Y + r.Height * 0.42 - isz / 2, isz, isz), a);
                }
                else if (t.Item != null && t.Item.Badge != null)
                {
                    FormattedText bf = FT(t.Item.Badge, r.Height * 0.26, 2, false, r.Width - 20 * s);
                    bf.SetForegroundBrush(WhiteA(a));
                    dc.DrawText(bf, new Point(r.X + (r.Width - bf.Width) / 2, r.Y + r.Height * 0.40 - bf.Height / 2));
                    bf.SetForegroundBrush(brWhite);
                }
                else Glyph(dc, t.Glyph ?? (t.Item != null ? t.Item.Glyph : null), t.Label, r.X + r.Width / 2, r.Y + r.Height * 0.42, icon, a);
                Txt(dc, t.Label ?? "", (t.Hero ? 22 : 17) * s, 0, r.X + 10 * s, r.Bottom - 32 * s, a, r.Width - 20 * s, 0, false);
            }
            if (selected) dc.DrawRectangle(null, new Pen(WhiteA(a), 4 * s), r);
        }

        void DrawMetroHints(DrawingContext dc, double W, double H, double fade, double left)
        {
            double s = S;
            bool pad = PadPrompts;
            string[] keys = { "ok", "back", "opt" };
            string[] labels = { "Pilih", "Kembali", "Opsi" };
            double fs = 17 * s, kh = 26 * s, x = left, y = H - 78 * s;
            double th = TextH(fs);
            for (int i = 0; i < keys.Length; i++)
            {
                double kw = PromptWidth(keys[i], fs, kh, pad);
                DrawPrompt(dc, keys[i], new Rect(x, y - kh / 2, kw, kh), fs, fade, pad);
                x += kw + 8 * s;
                double lw = Txt(dc, labels[i], fs, 0, x, y - th / 2, 0.9 * fade, 0, 0, false);
                x += lw + 30 * s;
            }
        }
    }
}
