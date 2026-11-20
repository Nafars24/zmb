using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ZMB
{
    // Bentuk case untuk mode Coverflow
    class CaseStyle
    {
        public const int DVD = 0, BLURAY = 1, JEWEL = 2, CART = 3, PHOTO = 4, TILE = 5;
        public int Kind = TILE;
        public double Aspect = 1.0;     // lebar / tinggi
        public bool ImageAspect = false; // pakai rasio gambar (kotak kardus, foto)
        public Color Body = Color.FromRgb(14, 14, 16);
        public Color Band = Color.FromRgb(20, 20, 24);
        public bool DarkText = false;
        public string Label = "";
        public double SpineRatio = 0.075; // tebal punggung case / tinggi case
        public CaseOverlay Overlay;       // case dengan lapisan plastik asli (PS1, PS3)

        public CaseStyle(int kind, double aspect, Color body, Color band, string label)
        {
            Kind = kind; Aspect = aspect; Body = body; Band = band; Label = label;
            if (kind == JEWEL) SpineRatio = 0.085;
            else if (kind == BLURAY) SpineRatio = 0.072;
            else if (kind == CART) SpineRatio = 0.15;
            else if (kind == PHOTO) SpineRatio = 0.05;
            else if (kind == TILE) SpineRatio = 0.09;
            if (kind == DVD && aspect < 0.62) SpineRatio = 0.09; // UMD (PSP)
        }
    }

    public partial class ZmbWindow
    {
        Brush brGloss, brBar;
        Dictionary<string, CaseStyle> caseCache = new Dictionary<string, CaseStyle>();

        static Color Rgb(byte r, byte g, byte b) { return Color.FromRgb(r, g, b); }

        CaseStyle CaseStyleFor(XmbItem it)
        {
            string key = it.Kind == "game" && it.Game != null ? "g:" + it.Game.Platform : (it.Kind ?? "tile");
            CaseStyle cs;
            if (caseCache.TryGetValue(key, out cs)) return cs;
            if (it.Kind == "video") cs = new CaseStyle(CaseStyle.BLURAY, 0.79, Rgb(18, 52, 130), Rgb(24, 84, 196), "Blu-ray Disc");
            else if (it.Kind == "music") cs = new CaseStyle(CaseStyle.JEWEL, 1.13, Rgb(38, 40, 46), Rgb(0, 0, 0), "");
            else if (it.Kind == "photo") { cs = new CaseStyle(CaseStyle.PHOTO, 1.33, Colors.White, Colors.White, ""); cs.ImageAspect = true; }
            else if (it.Kind == "game" && it.Game != null)
            {
                string plat = it.Game.Platform;
                PlatformCfg p = cfg.FindPlatform(plat);
                string sh = plat == "WIN" || plat == "STEAM" ? plat : (p != null ? p.Short : plat);
                switch (sh)
                {
                    case "WIN": cs = new CaseStyle(CaseStyle.DVD, 0.71, Rgb(14, 14, 16), Rgb(22, 22, 26), "PC"); break;
                    case "STEAM": cs = new CaseStyle(CaseStyle.DVD, 0.71, Rgb(16, 24, 36), Rgb(27, 40, 56), "PC  |  Steam"); break;
                    case "PS1":
                        cs = new CaseStyle(CaseStyle.JEWEL, CaseOverlay.Ps1.Aspect, Rgb(18, 18, 20), Rgb(0, 0, 0), "");
                        cs.Overlay = CaseOverlay.Ps1;
                        cs.SpineRatio = CaseOverlay.Ps1.SpineRatio;
                        break;
                    case "PS2":
                        if (CaseOverlay.For("PS2") != null) cs = OverlayStyle(CaseStyle.DVD, CaseOverlay.Ps2, Rgb(10, 10, 14), Rgb(6, 6, 8));
                        else cs = new CaseStyle(CaseStyle.DVD, 0.71, Rgb(10, 10, 14), Rgb(6, 6, 8), "PlayStation 2");
                        break;
                    case "PS3":
                        if (CaseOverlay.Ps3.Ready)
                        {
                            cs = new CaseStyle(CaseStyle.BLURAY, CaseOverlay.Ps3.Aspect, Rgb(12, 16, 26), Rgb(22, 26, 36), "");
                            cs.Overlay = CaseOverlay.Ps3;
                            cs.SpineRatio = CaseOverlay.Ps3.SpineRatio;
                        }
                        else cs = new CaseStyle(CaseStyle.BLURAY, 0.79, Rgb(12, 16, 26), Rgb(22, 26, 36), "PS3");
                        break;
                    case "PSP":
                        if (CaseOverlay.For("PSP") != null) cs = OverlayStyle(CaseStyle.DVD, CaseOverlay.Psp, Rgb(12, 12, 14), Rgb(18, 18, 22));
                        else cs = new CaseStyle(CaseStyle.DVD, 0.58, Rgb(12, 12, 14), Rgb(18, 18, 22), "PSP");
                        break;
                    case "VITA": cs = new CaseStyle(CaseStyle.DVD, 0.78, Rgb(20, 40, 90), Rgb(28, 62, 140), "PS Vita"); break;
                    case "NDS": cs = new CaseStyle(CaseStyle.DVD, 0.88, Rgb(222, 223, 228), Rgb(236, 237, 241), "Nintendo DS"); cs.DarkText = true; break;
                    case "3DS": cs = new CaseStyle(CaseStyle.DVD, 0.88, Rgb(232, 232, 236), Rgb(190, 28, 40), "Nintendo 3DS"); break;
                    case "SWITCH": cs = new CaseStyle(CaseStyle.DVD, 0.62, Rgb(26, 26, 30), Rgb(230, 0, 18), "NINTENDO SWITCH"); break;
                    case "X360": cs = new CaseStyle(CaseStyle.DVD, 0.71, Rgb(24, 26, 24), Rgb(16, 124, 16), "XBOX 360"); break;
                    case "GCN":
                        if (CaseOverlay.For("GCN") != null) cs = OverlayStyle(CaseStyle.DVD, CaseOverlay.Gcn, Rgb(235, 235, 238), Rgb(210, 210, 214));
                        else cs = new CaseStyle(CaseStyle.DVD, 0.71, Rgb(12, 12, 16), Rgb(76, 54, 150), "GameCube / Wii");
                        break;
                    case "NES": cs = new CaseStyle(CaseStyle.CART, 0.72, Rgb(30, 30, 30), Rgb(0, 0, 0), ""); cs.ImageAspect = true; break;
                    case "SNES": cs = new CaseStyle(CaseStyle.CART, 1.38, Rgb(30, 30, 30), Rgb(0, 0, 0), ""); cs.ImageAspect = true; break;
                    case "N64": cs = new CaseStyle(CaseStyle.CART, 1.38, Rgb(30, 30, 30), Rgb(0, 0, 0), ""); cs.ImageAspect = true; break;
                    case "GBA": cs = new CaseStyle(CaseStyle.CART, 1.0, Rgb(30, 30, 30), Rgb(0, 0, 0), ""); cs.ImageAspect = true; break;
                    case "MD": cs = new CaseStyle(CaseStyle.DVD, 0.71, Rgb(12, 12, 12), Rgb(20, 20, 20), "Mega Drive / Genesis"); break;
                    case "ARC": cs = new CaseStyle(CaseStyle.CART, 0.75, Rgb(30, 30, 30), Rgb(0, 0, 0), ""); cs.ImageAspect = true; break;
                    default:
                        cs = new CaseStyle(CaseStyle.DVD, 0.71, Rgb(14, 14, 16), Rgb(22, 22, 26), p != null ? p.Name : sh);
                        break;
                }
            }
            else cs = new CaseStyle(CaseStyle.TILE, 1.0, Rgb(0, 0, 0), Rgb(0, 0, 0), "");
            caseCache[key] = cs;
            return cs;
        }

        ImageSource BoxImage(XmbItem it)
        {
            if (it.BoxKey != null) return GetImage(it.BoxKey, it.BoxLoader);
            if (it.ImageKey != null) return GetImage(it.ImageKey, it.ImageLoader);
            return null;
        }

        double AspectOf(XmbItem it)
        {
            CaseStyle cs = CaseStyleFor(it);
            if (cs.ImageAspect)
            {
                BitmapSource bs = BoxImage(it) as BitmapSource;
                if (bs != null && bs.PixelHeight > 0 && !it.BoxIsIcon)
                    return Math.Max(0.55, Math.Min(1.6, (double)bs.PixelWidth / bs.PixelHeight));
            }
            return cs.Aspect;
        }

        void EnsureCoverflowBrushes()
        {
            if (brGloss != null) return;
            LinearGradientBrush g = new LinearGradientBrush();
            g.StartPoint = new Point(0, 0); g.EndPoint = new Point(1, 1);
            g.GradientStops.Add(new GradientStop(Color.FromArgb(70, 255, 255, 255), 0));
            g.GradientStops.Add(new GradientStop(Color.FromArgb(18, 255, 255, 255), 0.42));
            g.GradientStops.Add(new GradientStop(Color.FromArgb(0, 255, 255, 255), 0.5));
            g.GradientStops.Add(new GradientStop(Color.FromArgb(0, 255, 255, 255), 1));
            g.Freeze();
            brGloss = g;
            LinearGradientBrush b = new LinearGradientBrush();
            b.StartPoint = new Point(0, 0); b.EndPoint = new Point(0, 1);
            b.GradientStops.Add(new GradientStop(Color.FromArgb(215, 22, 22, 26), 0));
            b.GradientStops.Add(new GradientStop(Color.FromArgb(240, 8, 8, 10), 1));
            b.Freeze();
            brBar = b;
        }

        void DrawImageFill(DrawingContext dc, ImageSource img, Rect target)
        {
            DrawImageFill(dc, img, target, 1.0);
        }

        void DrawImageFillOld(DrawingContext dc, ImageSource img, Rect target)
        {
            BitmapSource bs = img as BitmapSource;
            double iw = bs != null ? bs.PixelWidth : img.Width, ih = bs != null ? bs.PixelHeight : img.Height;
            if (iw <= 0 || ih <= 0 || target.Width <= 0 || target.Height <= 0) return;
            double sc = Math.Max(target.Width / iw, target.Height / ih);
            double w = iw * sc, h = ih * sc;
            dc.PushClip(new RectangleGeometry(target));
            dc.DrawImage(img, new Rect(target.X + (target.Width - w) / 2, target.Y + (target.Height - h) / 2, w, h));
            dc.Pop();
        }

        void DrawCoverArea(DrawingContext dc, XmbItem it, CaseStyle cs, Rect cover)
        {
            ImageSource img = BoxImage(it);
            if (img != null && !it.BoxIsIcon) { DrawImageFill(dc, img, cover); return; }
            // tanpa box art: latar gradasi + ikon + judul
            LinearGradientBrush lb = new LinearGradientBrush(Mix(cs.Band, Colors.White, 0.12), Mix(cs.Body, Colors.Black, 0.3), 90);
            lb.Freeze();
            dc.DrawRectangle(lb, null, cover);
            double iconS = Math.Min(cover.Width, cover.Height) * 0.42;
            Rect ib = new Rect(cover.X + (cover.Width - iconS) / 2, cover.Y + cover.Height * 0.16, iconS, iconS);
            if (img != null) BitmapSourceFit(dc, img, ib, 1, false);
            else if (it.Badge != null) DrawBadge(dc, it.Badge, it.BadgeColor, new Rect(ib.X - iconS * 0.2, ib.Y, iconS * 1.4, iconS), 1);
            else Glyph(dc, it.Glyph ?? "\uE7FC", it.Title, ib.X + iconS / 2, ib.Y + iconS / 2, iconS * 0.8, 0.9);
            double fs = Math.Max(8, cover.Width * 0.09);
            FormattedText ft = FTW(it.Title, fs, 2, cover.Width * 0.86, 3);
            dc.DrawText(ft, new Point(cover.X + (cover.Width - ft.Width) / 2, ib.Bottom + cover.Height * 0.06));
        }

        static CaseStyle OverlayStyle(int kind, CaseOverlay ov, Color body, Color spine)
        {
            CaseStyle cs = new CaseStyle(kind, ov.Aspect, body, spine, "");
            cs.Overlay = ov;
            cs.SpineRatio = ov.SpineRatio;
            return cs;
        }

        void DrawCase(DrawingContext dc, XmbItem it, CaseStyle cs, Rect r)
        {
            if (cs.Overlay != null && cs.Overlay.Ready) { DrawOverlayCase(dc, it, cs, r); return; }
            double rad = r.Width * 0.03;
            switch (cs.Kind)
            {
                case CaseStyle.DVD:
                case CaseStyle.BLURAY:
                    {
                        dc.DrawRoundedRectangle(SolidOf(cs.Body), null, r, rad, rad);
                        double m = r.Width * 0.028;
                        double bh = r.Height * (cs.Kind == CaseStyle.BLURAY ? 0.075 : 0.085);
                        Rect band = new Rect(r.X + m, r.Y + m, r.Width - 2 * m, bh);
                        dc.DrawRectangle(SolidOf(cs.Band), null, band);
                        if (cs.Label.Length > 0)
                        {
                            double fsz = Math.Max(6, bh * 0.56);
                            FormattedText ft = FT(cs.Label, fsz, 2, cs.DarkText, band.Width * 0.9);
                            dc.DrawText(ft, new Point(band.X + bh * 0.35, band.Y + (bh - ft.Height) / 2));
                        }
                        Rect cover = new Rect(r.X + m, band.Bottom, r.Width - 2 * m, Math.Max(1, r.Bottom - m - band.Bottom));
                        DrawCoverArea(dc, it, cs, cover);
                        break;
                    }
                case CaseStyle.JEWEL:
                    {
                        dc.DrawRoundedRectangle(SolidOf(Color.FromArgb(235, cs.Body.R, cs.Body.G, cs.Body.B)), null, r, rad * 0.5, rad * 0.5);
                        Rect hinge = new Rect(r.X, r.Y, r.Width * 0.075, r.Height);
                        dc.DrawRectangle(SolidOf(Mix(cs.Body, Colors.Black, 0.5)), null, hinge);
                        for (int i = 1; i <= 3; i++)
                        {
                            dc.DrawRectangle(WhiteA(0.18), null, new Rect(hinge.X + hinge.Width * i / 4.0, r.Y + r.Height * 0.02, Math.Max(0.8, r.Width * 0.004), r.Height * 0.96));
                        }
                        double inX = hinge.Right + r.Width * 0.015;
                        Rect cover = new Rect(inX, r.Y + r.Height * 0.03, Math.Max(1, r.Right - r.Width * 0.03 - inX), r.Height * 0.94);
                        DrawCoverArea(dc, it, cs, cover);
                        break;
                    }
                case CaseStyle.CART:
                    DrawCoverArea(dc, it, cs, r);
                    dc.DrawRectangle(null, new Pen(SolidOf(Color.FromArgb(120, 0, 0, 0)), Math.Max(1, r.Width * 0.006)), r);
                    break;
                case CaseStyle.PHOTO:
                    {
                        dc.DrawRectangle(brWhite, null, r);
                        double m = r.Width * 0.045;
                        DrawCoverArea(dc, it, cs, new Rect(r.X + m, r.Y + m, r.Width - 2 * m, r.Height - 2 * m));
                        break;
                    }
                default:
                    {
                        dc.DrawRoundedRectangle(SolidOf(Color.FromArgb(70, 255, 255, 255)), penThin, r, r.Width * 0.08, r.Width * 0.08);
                        DrawTile(dc, it, r.X + r.Width / 2, r.Y + r.Height / 2, r.Width * 0.84, r.Height * 0.62, 1);
                        break;
                    }
            }
            // kilau plastik
            if (cs.Kind != CaseStyle.TILE)
            {
                dc.DrawRoundedRectangle(brGloss, null, r, rad, rad);
                dc.DrawRoundedRectangle(null, penThin, r, rad, rad);
            }
        }

        // ===================== PUNGGUNG CASE (spine) =====================
        Dictionary<string, Color> avgCache = new Dictionary<string, Color>();
        Brush brSpineShade;

        Color AvgColor(string key, ImageSource img, Color fallback)
        {
            if (key == null) return fallback;
            Color c;
            if (avgCache.TryGetValue(key, out c)) return c;
            BitmapSource bs = img as BitmapSource;
            if (bs == null || bs.PixelWidth < 2 || bs.PixelHeight < 2) return fallback;
            try
            {
                TransformedBitmap small = new TransformedBitmap(bs, new ScaleTransform(8.0 / bs.PixelWidth, 16.0 / bs.PixelHeight));
                FormatConvertedBitmap conv = new FormatConvertedBitmap(small, PixelFormats.Bgra32, null, 0);
                int w = conv.PixelWidth, h = conv.PixelHeight;
                byte[] px = new byte[w * h * 4];
                conv.CopyPixels(px, w * 4, 0);
                double r = 0, g = 0, b = 0, n = 0;
                for (int i = 0; i < px.Length; i += 4)
                {
                    double al = px[i + 3] / 255.0;
                    b += px[i] * al; g += px[i + 1] * al; r += px[i + 2] * al; n += al;
                }
                if (n < 0.5) c = fallback;
                else
                {
                    r /= n; g /= n; b /= n;
                    // sedikit naikkan saturasi supaya rak terlihat berwarna
                    double gray = (r + g + b) / 3;
                    r = gray + (r - gray) * 1.35; g = gray + (g - gray) * 1.35; b = gray + (b - gray) * 1.35;
                    c = Color.FromRgb((byte)Math.Max(0, Math.Min(255, r)), (byte)Math.Max(0, Math.Min(255, g)), (byte)Math.Max(0, Math.Min(255, b)));
                }
            }
            catch { c = fallback; }
            avgCache[key] = c;
            return c;
        }

        static double Luma(Color c) { return (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255.0; }

        void DrawSpine(DrawingContext dc, XmbItem it, CaseStyle cs, Rect r)
        {
            if (cs.Overlay != null && cs.Overlay.Ready) { DrawOverlaySpine(dc, it, cs.Overlay, r); return; }
            if (brSpineShade == null)
            {
                LinearGradientBrush sh = new LinearGradientBrush();
                sh.StartPoint = new Point(0, 0); sh.EndPoint = new Point(1, 0);
                sh.GradientStops.Add(new GradientStop(Color.FromArgb(70, 255, 255, 255), 0));
                sh.GradientStops.Add(new GradientStop(Color.FromArgb(25, 255, 255, 255), 0.25));
                sh.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0, 0, 0), 0.55));
                sh.GradientStops.Add(new GradientStop(Color.FromArgb(110, 0, 0, 0), 1));
                sh.Freeze();
                brSpineShade = sh;
            }
            ImageSource img = BoxImage(it);
            bool hasImg = img != null && !it.BoxIsIcon;
            string ckey = it.BoxKey ?? it.ImageKey;
            Color art = hasImg ? AvgColor(ckey, img, cs.Body) : (it.Badge != null ? it.BadgeColor : cs.Body);
            double rad = Math.Min(r.Width * 0.14, 4 * S);

            // badan punggung
            Color body;
            if (cs.Kind == CaseStyle.TILE) body = Color.FromArgb(255, 60, 64, 74);
            else if (cs.Kind == CaseStyle.PHOTO) body = Color.FromRgb(236, 236, 236);
            else body = Mix(art, Colors.Black, 0.28);
            dc.DrawRoundedRectangle(SolidOf(body), null, r, rad, rad);

            // tekstur: irisan tengah box art, samar
            double bandH = 0;
            if (cs.Kind == CaseStyle.DVD || cs.Kind == CaseStyle.BLURAY) bandH = Math.Min(r.Height * 0.16, r.Width * 1.5);
            Rect artR = new Rect(r.X, r.Y + bandH, r.Width, Math.Max(1, r.Height - bandH));
            if (hasImg && cs.Kind != CaseStyle.PHOTO)
            {
                DrawImageFill(dc, img, artR, cs.Kind == CaseStyle.CART ? 0.75 : 0.45);
                dc.DrawRectangle(BrushA(SolidOf(body), 0.4), null, artR);
            }

            // CD jewel: plastik bening + garis engsel
            if (cs.Kind == CaseStyle.JEWEL)
            {
                Brush side = SolidOf(Color.FromArgb(140, 20, 22, 26));
                dc.DrawRectangle(side, null, new Rect(r.X, r.Y, r.Width * 0.16, r.Height));
                dc.DrawRectangle(side, null, new Rect(r.Right - r.Width * 0.16, r.Y, r.Width * 0.16, r.Height));
            }

            // pita platform di atas
            if (bandH > 0)
            {
                Rect band = new Rect(r.X, r.Y, r.Width, bandH);
                dc.PushClip(new RectangleGeometry(r, rad, rad));
                dc.DrawRectangle(SolidOf(cs.Band), null, band);
                dc.Pop();
                if (cs.Label.Length > 0)
                {
                    string lab = cs.Label.Length > 6 ? cs.Label.Replace("PlayStation", "PS").Replace("Nintendo ", "").Replace("  |  Steam", "") : cs.Label;
                    double lf = Math.Max(5, r.Width * 0.30);
                    FormattedText lt = FT(lab, lf, 2, cs.DarkText, bandH * 0.86);
                    dc.PushTransform(new MatrixTransform(0, 1, -1, 0, r.X + r.Width / 2 + lt.Height / 2, r.Y + (bandH - lt.Width) / 2));
                    dc.DrawText(lt, new Point(0, 0));
                    dc.Pop();
                }
                dc.DrawRectangle(BlackA(0.5), null, new Rect(r.X, r.Y + bandH - Math.Max(0.6, r.Width * 0.03), r.Width, Math.Max(0.6, r.Width * 0.03)));
            }

            // judul vertikal (dibaca dari atas ke bawah, seperti punggung case di rak)
            bool dark = cs.Kind == CaseStyle.PHOTO || Luma(body) > 0.62;
            double fsz = Math.Max(6, Math.Min(r.Width * 0.44, 30 * S));
            double pad = r.Width * 0.5;
            double maxLen = Math.Max(10, r.Height - bandH - pad * 2);
            FormattedText ft = FT(it.Title, fsz, 2, dark, maxLen);
            double tx = r.X + r.Width / 2 + ft.Height / 2, ty = r.Y + bandH + pad;
            if (!dark)
            {
                dc.PushTransform(new MatrixTransform(0, 1, -1, 0, tx + 1.2 * S, ty + 1.2 * S));
                ft.SetForegroundBrush(BlackA(0.55));
                dc.DrawText(ft, new Point(0, 0));
                ft.SetForegroundBrush(brWhite);
                dc.Pop();
            }
            dc.PushTransform(new MatrixTransform(0, 1, -1, 0, tx, ty));
            dc.DrawText(ft, new Point(0, 0));
            dc.Pop();

            // badge platform kecil di bawah untuk kotak kardus / jewel
            if ((cs.Kind == CaseStyle.CART || cs.Kind == CaseStyle.JEWEL) && it.Game != null && it.Badge != null)
            {
                double bs = r.Width * 0.7;
                Rect br = new Rect(r.X + (r.Width - bs) / 2, r.Bottom - bs - r.Width * 0.25, bs, bs);
                dc.DrawRoundedRectangle(SolidOf(Color.FromArgb(200, 0, 0, 0)), null, br, bs * 0.2, bs * 0.2);
            }

            // shading plastik + tepi
            dc.DrawRoundedRectangle(brSpineShade, null, r, rad, rad);
            dc.DrawRoundedRectangle(null, penThin, r, rad, rad);
        }

        double SpineW(XmbItem it, double h) { return Math.Max(10, h * CaseStyleFor(it).SpineRatio); }

        // Lebar total item: gabungan muka (cover) dan punggung, mengikuti seberapa jauh case "diputar"
        double ItemW(XmbItem it, double h, double selF)
        {
            return h * AspectOf(it) * selF + SpineW(it, h) * (1 - selF);
        }

        // Gambar satu case. selF = 1: muka penuh di tengah;  selF = 0: hanya punggung (di rak).
        // side > 0: case di kanan (muka berputar ke kiri), side < 0: di kiri.
        void DrawCaseAt(DrawingContext dc, XmbItem it, double cx, double cy, double h, double selF, int side, double alpha, bool reflect)
        {
            CaseStyle cs = CaseStyleFor(it);
            double natW = h * AspectOf(it);
            double spN = SpineW(it, h);
            double coverW = natW * selF, spW = spN * (1 - selF);
            double total = coverW + spW;
            double left = cx - total / 2;
            double coverX, spineX;
            if (side >= 0) { coverX = left; spineX = left + coverW; }
            else { spineX = left; coverX = left + spW; }
            Rect rc = new Rect(coverX, cy - h / 2, natW, h);      // ukuran asli muka
            Rect rs = new Rect(spineX, cy - h / 2, spN, h);       // ukuran asli punggung
            double bottom = cy + h / 2;

            for (int pass = reflect ? 0 : 1; pass < 2; pass++)
            {
                bool refl = pass == 0;
                bool pushedA = alpha < 0.99;
                if (pushedA) dc.PushOpacity(alpha);
                if (refl)
                {
                    LinearGradientBrush mask = new LinearGradientBrush();
                    mask.MappingMode = BrushMappingMode.Absolute;
                    mask.StartPoint = new Point(0, bottom);
                    mask.EndPoint = new Point(0, bottom - h * 0.36);
                    mask.GradientStops.Add(new GradientStop(Color.FromArgb(85, 0, 0, 0), 0));
                    mask.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0, 0, 0), 1));
                    mask.Freeze();
                    dc.PushTransform(new MatrixTransform(1, 0, 0, -1, 0, 2 * bottom + 2 * S));
                    dc.PushOpacityMask(mask);
                }
                if (coverW > 0.5)
                {
                    double k = coverW / natW;
                    dc.PushTransform(new MatrixTransform(k, 0, 0, 1, coverX - coverX * k, 0));
                    DrawCase(dc, it, cs, rc);
                    if (selF < 0.98)
                    {
                        // muka yang sedang berputar sedikit lebih gelap
                        dc.DrawRectangle(BlackA(0.45 * (1 - selF)), null, rc);
                    }
                    dc.Pop();
                }
                if (spW > 0.5)
                {
                    double k = spW / spN;
                    dc.PushTransform(new MatrixTransform(k, 0, 0, 1, spineX - spineX * k, 0));
                    DrawSpine(dc, it, cs, rs);
                    dc.Pop();
                }
                if (refl) { dc.Pop(); dc.Pop(); }
                if (pushedA) dc.Pop();
            }
        }

        void DrawCoverflow(DrawingContext dc, double W, double H, double fade, double now)
        {
            EnsureCoverflowBrushes();
            double s = S;
            List<XmbItem> items; int index; double anim;
            if (stack.Count > 0)
            {
                Level l = stack[stack.Count - 1];
                items = l.Items; index = l.Index; anim = l.Anim;
            }
            else
            {
                Category c = cats[catIndex];
                items = c.Items; index = c.Index; anim = c.Anim;
            }
            double switching = stack.Count == 0 ? Math.Min(1, Math.Abs(catIndex - catAnim) * 1.6) : Math.Min(1, Math.Abs(levelSlide));
            double ca = fade * (1 - switching);

            // ---- tab kategori / breadcrumb
            if (stack.Count == 0)
            {
                double tx = 60 * s;
                for (int i = 0; i < cats.Count; i++)
                {
                    bool sel = i == catIndex;
                    double w = Txt(dc, cats[i].Title.ToUpperInvariant(), 21 * s, sel ? 2 : 0, tx, 36 * s, (sel ? 1 : 0.5) * fade, 0, 0, true);
                    if (sel)
                    {
                        dc.DrawRoundedRectangle(WhiteA(fade), null, new Rect(tx, 36 * s + TextH(21 * s) + 4 * s, w, 3 * s), 1.5 * s, 1.5 * s);
                    }
                    tx += w + 34 * s;
                }
            }
            else
            {
                string crumb = cats[catIndex].Title;
                foreach (Level lv in stack) crumb += "  \u203A  " + lv.Title;
                Txt(dc, crumb, 22 * s, 2, 60 * s, 36 * s, fade, W * 0.6, 0, true);
            }

            if (items == null || items.Count == 0) return;

            // ---- rak case
            double cy = H * 0.44;
            double hc = H * 0.54, hs = H * 0.42;
            double gapS = 2 * s, gapC = W * 0.014;
            int ia = Math.Max(0, Math.Min(items.Count - 1, (int)Math.Floor(anim)));
            double fr = Math.Max(0, Math.Min(1, anim - Math.Floor(anim)));
            int span = (int)(W / Math.Max(8, hs * 0.07)) / 2 + 3;
            int from = Math.Max(0, ia - span), to = Math.Min(items.Count - 1, ia + span + 1);
            int n = to - from + 1;
            double[] xs = new double[n], hsArr = new double[n], selA = new double[n], ws = new double[n];
            for (int k = 0; k < n; k++)
            {
                int i = from + k;
                double t = Math.Abs(i - anim);
                selA[k] = 1 - Math.Min(1, t);
                hsArr[k] = hs + (hc - hs) * selA[k];
                ws[k] = ItemW(items[i], hsArr[k], selA[k]);
            }
            // posisi: item ia di tengah, bergeser halus menuju ia+1
            int ka = ia - from;
            double gapNext = (ka + 1 < n) ? gapS + (gapC - gapS) * Math.Max(selA[ka], selA[ka + 1]) : 0;
            xs[ka] = W / 2 - (ka + 1 < n ? fr * (ws[ka] / 2 + gapNext + ws[ka + 1] / 2) : 0);
            for (int k = ka + 1; k < n; k++)
            {
                double g = gapS + (gapC - gapS) * Math.Max(selA[k - 1], selA[k]);
                xs[k] = xs[k - 1] + ws[k - 1] / 2 + g + ws[k] / 2;
            }
            for (int k = ka - 1; k >= 0; k--)
            {
                double g = gapS + (gapC - gapS) * Math.Max(selA[k + 1], selA[k]);
                xs[k] = xs[k + 1] - ws[k + 1] / 2 - g - ws[k] / 2;
            }

            List<int> order = new List<int>();
            for (int k = 0; k < n; k++) order.Add(k);
            order.Sort((p1, p2) => selA[p1].CompareTo(selA[p2]));   // yang paling dekat tengah digambar terakhir

            foreach (int k in order)
            {
                int i = from + k;
                if (xs[k] + ws[k] / 2 < -20 || xs[k] - ws[k] / 2 > W + 20) continue;
                double rel = i - anim;
                double edge = Math.Min(xs[k], W - xs[k]) / (W * 0.08);
                double a = ca * Math.Max(0.15, Math.Min(1, edge));
                if (selA[k] > 0.3)
                {
                    dc.DrawEllipse(BlackA(0.28 * selA[k] * a), null, new Point(xs[k], cy + hsArr[k] / 2 + 6 * s), ws[k] * 0.6, 14 * s);
                }
                DrawCaseAt(dc, items[i], xs[k], cy, hsArr[k], selA[k], rel >= 0 ? 1 : -1, a, Math.Abs(rel) < 3.5);
            }

            // ---- bar judul bawah (gaya dashboard)
            double barH = 150 * s, by = H - barH;
            dc.DrawRectangle(BrushA(brBar, fade), null, new Rect(0, by, W, barH));
            dc.DrawRectangle(WhiteA(0.25 * fade), null, new Rect(0, by, W, 1.2 * s));
            XmbItem cur = items[Math.Max(0, Math.Min(items.Count - 1, index))];
            Txt(dc, cur.Title, 32 * s, 2, W / 2, by + 12 * s, fade, W * 0.8, 1, true);
            string info = (index + 1) + " dari " + items.Count;
            string sub = cur.Sub;
            Txt(dc, info + (string.IsNullOrEmpty(sub) ? "" : "   \u2022   " + sub), 17 * s, 0, W / 2, by + 12 * s + TextH(32 * s), 0.75 * fade, W * 0.86, 1, false);
            if (cur.Progress >= 0)
            {
                double pw = 300 * s, px = (W - pw) / 2, py = by + 12 * s + TextH(32 * s) + TextH(17 * s) + 6 * s;
                dc.DrawRoundedRectangle(WhiteA(0.25 * fade), null, new Rect(px, py, pw, 5 * s), 2.5 * s, 2.5 * s);
                dc.DrawRoundedRectangle(WhiteA(fade), null, new Rect(px, py, pw * Math.Min(1, cur.Progress), 5 * s), 2.5 * s, 2.5 * s);
            }
            DrawCoverflowHints(dc, W, H - 44 * s, fade);
        }

        void DrawCoverflowHints(DrawingContext dc, double W, double y, double fade)
        {
            double s = S;
            bool pad = PadPrompts;
            string[] keys = { "ok", "back", "sel", "opt", "updown", "bumpers" };
            string[] labels = { "Pilih", "Kembali", "Detail", "Opsi", stack.Count > 0 ? "Atas: Kembali" : "Kategori", "Lagu" };
            double fs = 17 * s, kh = 28 * s;
            double total = 0;
            double[] kw = new double[keys.Length], lw = new double[keys.Length];
            for (int i = 0; i < keys.Length; i++)
            {
                kw[i] = PromptWidth(keys[i], fs, kh, pad);
                lw[i] = FT(labels[i], fs, 0, false, 0).Width;
                total += kw[i] + 8 * s + lw[i] + (i < keys.Length - 1 ? 46 * s : 0);
            }
            double x = (W - total) / 2;
            double th = TextH(fs);
            for (int i = 0; i < keys.Length; i++)
            {
                Rect kr = new Rect(x, y - kh / 2, kw[i], kh);
                DrawPrompt(dc, keys[i], kr, fs, fade, pad);
                x += kw[i] + 8 * s;
                Txt(dc, labels[i], fs, 0, x, y - th / 2, 0.85 * fade, 0, 0, false);
                x += lw[i] + 46 * s;
            }
        }
    }
}
