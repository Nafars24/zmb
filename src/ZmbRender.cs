using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ZMB
{
    public partial class ZmbWindow
    {
        public static readonly string[] ThemeNames = {
            "Otomatis (ganti tiap bulan)", "Biru Klasik", "Merah", "Hijau", "Ungu", "Emas", "Pink",
            "Tosca", "Oranye", "Silver", "Hitam Pekat", "Coklat", "Biru Langit"
        };
        static readonly Color[] ThemeColors = {
            Colors.Transparent, Color.FromRgb(24, 70, 170), Color.FromRgb(165, 28, 32), Color.FromRgb(30, 128, 52),
            Color.FromRgb(100, 40, 160), Color.FromRgb(168, 128, 22), Color.FromRgb(196, 70, 140),
            Color.FromRgb(18, 136, 140), Color.FromRgb(206, 98, 20), Color.FromRgb(112, 118, 130),
            Color.FromRgb(16, 16, 20), Color.FromRgb(110, 70, 36), Color.FromRgb(40, 140, 210)
        };
        static readonly Color[] MonthColors = {
            Color.FromRgb(122, 128, 140), Color.FromRgb(170, 140, 40), Color.FromRgb(108, 150, 30),
            Color.FromRgb(200, 90, 150), Color.FromRgb(30, 140, 62), Color.FromRgb(120, 60, 170),
            Color.FromRgb(20, 140, 150), Color.FromRgb(40, 140, 210), Color.FromRgb(24, 70, 170),
            Color.FromRgb(130, 50, 160), Color.FromRgb(140, 85, 40), Color.FromRgb(170, 30, 40)
        };

        double S = 1;
        bool hasMdl2 = false;
        Typeface[] faces;
        Dictionary<string, FormattedText> textCache = new Dictionary<string, FormattedText>();
        Dictionary<string, Geometry> glyphCache = new Dictionary<string, Geometry>();
        Dictionary<Color, Brush> badgeBrushes = new Dictionary<Color, Brush>();
        Brush brWhite, brBlack, brDimLeft, brGlow, brWaveFill, brPanel, brToast, brOsd, brHiOpt;
        Pen penWave, penThin, penLine;
        ImageSource backImg = null;
        Geometry trophyFill, trophyHandles;
        Pen trophyPen, trophyPenB;
        string backImgPath = null;

        // baterai (dicek tiap 5 detik)
        double battCheckAt = -10;
        bool battHas = false, battOnline = false;
        float battPct = 1;

        static Brush Frz(Brush b) { b.Freeze(); return b; }
        static SolidColorBrush Solid(byte a, byte r, byte g, byte b)
        {
            SolidColorBrush s = new SolidColorBrush(Color.FromArgb(a, r, g, b));
            s.Freeze();
            return s;
        }

        void InitRender()
        {
            foreach (FontFamily ff in Fonts.SystemFontFamilies)
                if (ff.Source == "Segoe MDL2 Assets") { hasMdl2 = true; break; }
            faces = new Typeface[] {
                new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal),
                new Typeface(new FontFamily("Segoe UI Light"), FontStyles.Normal, FontWeights.Light, FontStretches.Normal),
                new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal),
                new Typeface(new FontFamily(hasMdl2 ? "Segoe MDL2 Assets" : "Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal)
            };
            brWhite = Solid(255, 255, 255, 255);
            brBlack = Solid(255, 0, 0, 0);
            brWaveFill = Solid(26, 255, 255, 255);
            brToast = Solid(200, 10, 12, 20);
            brOsd = Solid(170, 0, 0, 0);

            LinearGradientBrush dim = new LinearGradientBrush();
            dim.StartPoint = new Point(0, 0); dim.EndPoint = new Point(1, 0);
            dim.GradientStops.Add(new GradientStop(Color.FromArgb(200, 0, 0, 0), 0));
            dim.GradientStops.Add(new GradientStop(Color.FromArgb(120, 0, 0, 0), 0.5));
            dim.GradientStops.Add(new GradientStop(Color.FromArgb(40, 0, 0, 0), 1));
            brDimLeft = Frz(dim);

            RadialGradientBrush glow = new RadialGradientBrush();
            glow.GradientStops.Add(new GradientStop(Color.FromArgb(255, 255, 255, 255), 0));
            glow.GradientStops.Add(new GradientStop(Color.FromArgb(90, 255, 255, 255), 0.45));
            glow.GradientStops.Add(new GradientStop(Color.FromArgb(0, 255, 255, 255), 1));
            brGlow = Frz(glow);

            LinearGradientBrush panel = new LinearGradientBrush();
            panel.StartPoint = new Point(0, 0); panel.EndPoint = new Point(1, 0);
            panel.GradientStops.Add(new GradientStop(Color.FromArgb(150, 0, 0, 0), 0));
            panel.GradientStops.Add(new GradientStop(Color.FromArgb(220, 0, 0, 0), 1));
            brPanel = Frz(panel);

            LinearGradientBrush hi = new LinearGradientBrush();
            hi.StartPoint = new Point(0, 0); hi.EndPoint = new Point(1, 0);
            hi.GradientStops.Add(new GradientStop(Color.FromArgb(80, 255, 255, 255), 0));
            hi.GradientStops.Add(new GradientStop(Color.FromArgb(14, 255, 255, 255), 1));
            brHiOpt = Frz(hi);

            penWave = new Pen(Solid(110, 255, 255, 255), 1.4); penWave.Freeze();
            penThin = new Pen(Solid(70, 255, 255, 255), 1); penThin.Freeze();
            penLine = new Pen(Solid(120, 255, 255, 255), 1.3); penLine.Freeze();
        }

        Color ThemeColor()
        {
            int t = cfg.Theme;
            if (t <= 0 && themePackColor.HasValue) return themePackColor.Value;
            if (t <= 0 || t >= ThemeColors.Length) return MonthColors[DateTime.Now.Month - 1];
            return ThemeColors[t];
        }

        static Color Mix(Color a, Color b, double t)
        {
            t = Math.Max(0, Math.Min(1, t));
            return Color.FromRgb((byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));
        }

        string BackKey()
        {
            return ThemeColor().ToString() + "|" + DateTime.Now.Hour + "|" + cfg.BackgroundImage + "|" + themeBgPath + "|" + cfg.ViewMode;
        }

        // ===================== TEKS =====================
        FormattedText FT(string s, double size, int face, bool black, double maxW)
        {
            string key = face + (black ? "b" : "w") + Math.Round(size, 1).ToString(CultureInfo.InvariantCulture) + "|" + Math.Round(maxW) + "|" + s;
            FormattedText ft;
            if (textCache.TryGetValue(key, out ft)) return ft;
            if (textCache.Count > 900) textCache.Clear();
#pragma warning disable 618
            ft = new FormattedText(s, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, faces[face], Math.Max(1, size), black ? brBlack : brWhite);
#pragma warning restore 618
            if (maxW > 0)
            {
                ft.MaxTextWidth = maxW;
                ft.MaxLineCount = 1;
                ft.Trimming = TextTrimming.CharacterEllipsis;
            }
            textCache[key] = ft;
            return ft;
        }

        // align: 0 kiri, 1 tengah, 2 kanan.  y = sisi atas teks
        double Txt(DrawingContext dc, string s, double size, int face, double x, double y, double alpha, double maxW, int align, bool shadow)
        {
            if (string.IsNullOrEmpty(s) || alpha <= 0.01) return 0;
            FormattedText ft = FT(s, size, face, false, maxW);
            double w = ft.Width;
            if (maxW > 0) w = Math.Min(w, maxW);
            if (align == 1) x -= w / 2;
            else if (align == 2) x -= w;
            if (shadow)
            {
                ft.SetForegroundBrush(BlackA(0.45 * Math.Min(1, alpha)));
                dc.DrawText(ft, new Point(x + 1.6 * S, y + 1.6 * S));
            }
            ft.SetForegroundBrush(alpha >= 0.995 ? brWhite : WhiteA(alpha));
            dc.DrawText(ft, new Point(x, y));
            ft.SetForegroundBrush(brWhite);
            return w;
        }

        double TextH(double size) { return FT("Ag", size, 0, false, 0).Height; }

        Geometry GlyphGeo(string txt)
        {
            Geometry g;
            if (glyphCache.TryGetValue(txt, out g)) return g;
            FormattedText ft = FT(txt, 100, hasMdl2 ? 3 : 2, false, 0);
            g = ft.BuildGeometry(new Point(0, 0));
            g.Freeze();
            glyphCache[txt] = g;
            return g;
        }

        // ===================== SET IKON VEKTOR =====================
        Dictionary<string, Geometry> iconGeo = new Dictionary<string, Geometry>();
        Dictionary<Color, Brush> iconTint = new Dictionary<Color, Brush>();
        Brush brIconFill;

        Geometry IconGeo(string name)
        {
            Geometry g;
            if (iconGeo.TryGetValue(name, out g)) return g;
            string d;
            g = null;
            if (IconSet.Paths.TryGetValue(name, out d))
            {
                try { g = Geometry.Parse("F1 " + d); g.Freeze(); } catch { g = null; }
            }
            iconGeo[name] = g;
            return g;
        }

        // Ikon putih bergradasi dengan bayangan lembut (gaya XMB). tint: warna khusus (mis. trofi emas)
        bool DrawIcon(DrawingContext dc, string name, double cx, double cy, double size, double a, Color tint, bool useTint)
        {
            if (a <= 0.01) return true;
            Geometry g = IconGeo(name);
            if (g == null) return false;
            if (brIconFill == null)
            {
                LinearGradientBrush lb = new LinearGradientBrush(Color.FromRgb(255, 255, 255), Color.FromRgb(208, 214, 226), 90);
                lb.Freeze();
                brIconFill = lb;
            }
            Brush fill = brIconFill;
            if (useTint && !iconTint.TryGetValue(tint, out fill))
            {
                LinearGradientBrush tb = new LinearGradientBrush(Mix(tint, Colors.White, 0.35), Mix(tint, Colors.Black, 0.18), 90);
                tb.Freeze();
                iconTint[tint] = tb;
                fill = tb;
            }
            ImageSource bmp = IconBitmap(name, tint, useTint);
            if (bmp == null) return false;
            double full = size * IconPx / (100.0 * IconScale);
            DrawImageA(dc, bmp, new Rect(cx - full / 2, cy - full / 2, full, full), a);
            return true;
        }

        void Glyph(DrawingContext dc, string glyph, string fallback, double cx, double cy, double size, double alpha)
        {
            if (alpha <= 0.01) return;
            string iconName = null;
            if (glyph == "#WIN") iconName = "windowsgames";
            else if (glyph == "#TROPHY") iconName = "trophy";
            else if (glyph != null) IconSet.FromGlyph.TryGetValue(glyph, out iconName);
            if (iconName != null && DrawIcon(dc, iconName, cx, cy, size, alpha, Colors.White, false)) return;
            dc.PushOpacity(Math.Min(1, alpha));
            if (glyph == "#WIN")
            {
                double q = size * 0.4, gap = size * 0.06;
                dc.DrawRectangle(brWhite, null, new Rect(cx - q - gap / 2, cy - q - gap / 2, q, q));
                dc.DrawRectangle(brWhite, null, new Rect(cx + gap / 2, cy - q - gap / 2, q, q));
                dc.DrawRectangle(brWhite, null, new Rect(cx - q - gap / 2, cy + gap / 2, q, q));
                dc.DrawRectangle(brWhite, null, new Rect(cx + gap / 2, cy + gap / 2, q, q));
                dc.Pop();
                return;
            }
            if (glyph == "#TROPHY")
            {
                if (trophyFill == null)
                {
                    trophyFill = Geometry.Parse("M28,12 H72 V34 C72,52 61,62 50,62 C39,62 28,52 28,34 Z M45,61 H55 V76 H45 Z M33,77 H67 V88 H33 Z");
                    trophyFill.Freeze();
                    trophyHandles = Geometry.Parse("M29,18 H17 C15,36 22,44 30,44 M71,18 H83 C85,36 78,44 70,44");
                    trophyHandles.Freeze();
                    trophyPen = new Pen(brWhite, 5); trophyPen.Freeze();
                    trophyPenB = new Pen(brBlack, 5); trophyPenB.Freeze();
                }
                double kk = size / 100.0 * 1.15;
                double tx = cx - 50 * kk, ty2 = cy - 50 * kk;
                dc.PushTransform(new MatrixTransform(kk, 0, 0, kk, tx + 2 * S, ty2 + 2 * S));
                dc.PushOpacity(0.25); dc.DrawGeometry(brBlack, trophyPenB, trophyFill); dc.DrawGeometry(null, trophyPenB, trophyHandles); dc.Pop();
                dc.Pop();
                dc.PushTransform(new MatrixTransform(kk, 0, 0, kk, tx, ty2));
                dc.DrawGeometry(brWhite, null, trophyFill);
                dc.DrawGeometry(null, trophyPen, trophyHandles);
                dc.Pop();
                dc.Pop();
                return;
            }
            string txt = hasMdl2 ? (glyph ?? "\uE8B7") : (string.IsNullOrEmpty(fallback) ? "?" : fallback.Substring(0, 1).ToUpperInvariant());
            Geometry geo = GlyphGeo(txt);
            Rect b = geo.Bounds;
            if (b.IsEmpty) { dc.Pop(); return; }
            double k = size / 100.0;
            double ox = cx - (b.X + b.Width / 2) * k, oy = cy - (b.Y + b.Height / 2) * k;
            dc.PushTransform(new MatrixTransform(k, 0, 0, k, ox + 2 * S, oy + 2 * S));
            dc.PushOpacity(0.25);
            dc.DrawGeometry(brBlack, null, geo);
            dc.Pop();
            dc.Pop();
            dc.PushTransform(new MatrixTransform(k, 0, 0, k, ox, oy));
            dc.DrawGeometry(brWhite, null, geo);
            dc.Pop();
            dc.Pop();
        }

        void Glow(DrawingContext dc, double cx, double cy, double r, double a)
        {
            if (a <= 0.01) return;
            dc.DrawEllipse(BrushA(brGlow, a), null, new Point(cx, cy), r, r * 0.8);
        }

        // ===================== LAPISAN BELAKANG =====================
        void DrawBack(DrawingContext dc)
        {
            double W = surfBack.ActualWidth, H = surfBack.ActualHeight;
            if (W < 20 || H < 20) return;
            if (cfg.ViewMode == 1) { DrawMetroBack(dc, W, H); return; }
            int hour = DateTime.Now.Hour;
            Color c = ThemeColor();
            double night = (hour >= 19 || hour < 5) ? 0.38 : (hour >= 17 || hour < 7) ? 0.18 : 0;
            Color top = Mix(Mix(c, Colors.White, 0.12), Colors.Black, night);
            Color mid = Mix(c, Colors.Black, 0.25 + night);
            Color bot = Mix(c, Colors.Black, 0.82);
            LinearGradientBrush lb = new LinearGradientBrush();
            lb.StartPoint = new Point(0, 0); lb.EndPoint = new Point(0, 1);
            lb.GradientStops.Add(new GradientStop(top, 0));
            lb.GradientStops.Add(new GradientStop(mid, 0.45));
            lb.GradientStops.Add(new GradientStop(bot, 1));
            lb.Freeze();
            dc.DrawRectangle(lb, null, new Rect(0, 0, W, H));

            string bgPath = !string.IsNullOrEmpty(cfg.BackgroundImage) ? cfg.BackgroundImage : themeBgPath;
            if (!string.IsNullOrEmpty(bgPath))
            {
                if (backImgPath != bgPath || backImg == null)
                {
                    backImgPath = bgPath;
                    backImg = ImgConv.LoadFile(bgPath, 2560);
                }
                BitmapSource bs = backImg as BitmapSource;
                if (bs != null)
                {
                    double sc = Math.Max(W / bs.PixelWidth, H / bs.PixelHeight);
                    double iw = bs.PixelWidth * sc, ih = bs.PixelHeight * sc;
                    dc.DrawImage(bs, new Rect((W - iw) / 2, (H - ih) / 2, iw, ih));
                    SolidColorBrush tint = new SolidColorBrush(Color.FromArgb(110, (byte)(c.R * 0.4), (byte)(c.G * 0.4), (byte)(c.B * 0.4)));
                    tint.Freeze();
                    dc.DrawRectangle(tint, null, new Rect(0, 0, W, H));
                }
            }

            RadialGradientBrush rg = new RadialGradientBrush();
            rg.GradientStops.Add(new GradientStop(Color.FromArgb(46, 255, 255, 255), 0));
            rg.GradientStops.Add(new GradientStop(Color.FromArgb(0, 255, 255, 255), 1));
            rg.Freeze();
            dc.DrawEllipse(rg, null, new Point(W * 0.25, 0), W * 0.6, H * 0.6);
        }

        // ===================== LAPISAN UTAMA =====================
        void DrawMain(DrawingContext dc)
        {
            double W = surfMain.ActualWidth, H = surfMain.ActualHeight;
            if (W < 20 || H < 20) return;
            S = H / 1080.0 * cfg.UiScale;
            double now = T;

            double vis = GameBgVisibility();
            if (vis > 0.005)
            {
                dc.DrawRectangle(BrushA(brDimLeft, vis), null, new Rect(0, 0, W, H));
            }
            bool aurora = cfg.ViewMode == 1;   // mode Aurora: latar gradasi abu tanpa gelombang/partikel
            if (cfg.ShowWaves && !aurora) DrawWaves(dc, W, H, now, 1 - 0.85 * vis);
            if (SnowActive() && !aurora) DrawSnow(dc, W, H, now, 1 - 0.6 * vis);

            double bootT = booting ? now - bootStart : 99;
            if (bootT < 2.6)
            {
                DrawBoot(dc, W, H, bootT);
                return;
            }
            double fade = Math.Min(1, (bootT - 2.6) / 0.6);

            DrawClock(dc, W, H, fade, now);
            if (cfg.ViewMode == 1)
            {
                if (MetroActive())
                {
                    DrawMetro(dc, W, H, fade, now);
                    DrawNowPlaying(dc, W, H, 96 * S);
                    DrawScrapeStatus(dc, W, H - 160 * S);
                    return;
                }
                DrawCoverflow(dc, W, H, fade, now);
                DrawNowPlaying(dc, W, H, 96 * S);
                DrawScrapeStatus(dc, W, H - 160 * S);
                return;
            }
            DrawGameLogo(dc, W, H, vis * fade);
            DrawXmb(dc, W, H, fade, now);
            DrawScrapeStatus(dc, W, H);
            DrawNowPlaying(dc, W, H, -1);
            if (mode == Mode.Xmb) DrawHints(dc, W, H);
        }

        void DrawWaves(DrawingContext dc, double W, double H, double t, double alpha)
        {
            if (alpha <= 0.02) return;
            int n = 64;
            bool linesOnly = cfg.WaveStyle == 1;
            for (int k = 0; k < (linesOnly ? 6 : 3); k++)
            {
                Point[] top = new Point[n + 1];
                Point[] bot = new Point[n + 1];
                double baseY = H * (0.60 + k * 0.035);
                double amp = H * (0.055 + 0.02 * k);
                double thick = H * (0.025 + 0.03 * k);
                for (int i = 0; i <= n; i++)
                {
                    double u = (double)i / n;
                    double x = u * W;
                    double ph = u * Math.PI * 2;
                    double y = baseY
                        + Math.Sin(ph * (0.75 + 0.22 * k) + t * (0.32 + 0.11 * k) + k * 1.9) * amp
                        + Math.Sin(ph * 1.7 - t * 0.21 + k * 0.7) * amp * 0.45;
                    double th = thick * (0.45 + 0.55 * (0.5 + 0.5 * Math.Sin(ph * 1.3 + t * 0.45 + k * 2.1)));
                    top[i] = new Point(x, y - th * 0.5);
                    bot[n - i] = new Point(x, y + th * 0.5);
                }
                StreamGeometry fill = new StreamGeometry();
                using (StreamGeometryContext c = fill.Open())
                {
                    c.BeginFigure(top[0], true, true);
                    c.PolyLineTo(top, false, true);
                    c.PolyLineTo(bot, false, true);
                }
                fill.Freeze();
                StreamGeometry line = new StreamGeometry();
                using (StreamGeometryContext c = line.Open())
                {
                    c.BeginFigure(top[0], false, false);
                    c.PolyLineTo(top, true, true);
                }
                line.Freeze();
                if (!linesOnly)
                {
                    dc.DrawGeometry(BrushA(brWaveFill, (0.6 + 0.2 * k) * alpha), null, fill);
                }
                dc.DrawGeometry(null, new Pen(WhiteA(0.43 * (linesOnly ? 0.5 - 0.05 * k : 0.55 - 0.13 * k) * alpha), 1.4), line);
            }
            int sparkles = (cfg.Particles == 3 || SnowActive()) ? 0 : 34;
            for (int i = 0; i < sparkles; i++)
            {
                double seed = (i * 7919 % 1000) / 1000.0;
                double seed2 = (i * 104729 % 1000) / 1000.0;
                double x = ((seed + t * (0.006 + seed2 * 0.012)) % 1.0) * W;
                double y = H * (0.56 + 0.16 * Math.Sin(seed2 * 12 + x / W * 6 + t * 0.2));
                double tw = 0.5 + 0.5 * Math.Sin(t * (1.2 + seed * 2) + i);
                double r = (1.5 + 2.5 * seed2) * S;
                dc.DrawEllipse(WhiteA((0.12 + 0.5 * tw) * alpha), null, new Point(x, y), r, r);
            }
        }

        // ===================== SPLASH SCREEN =====================
        ImageSource splashLogo;
        bool splashTried;
        double[] splashSeeds;

        static double SpClamp(double v) { return v < 0 ? 0 : v > 1 ? 1 : v; }
        static double SpOutBack(double u) { u = SpClamp(u); const double c1 = 1.5, c3 = c1 + 1; double v = u - 1; return 1 + c3 * v * v * v + c1 * v * v; }
        static double SpOut3(double u) { u = 1 - SpClamp(u); return 1 - u * u * u; }
        static double SpSmooth(double u) { u = SpClamp(u); return u * u * (3 - 2 * u); }

        // Splash beranimasi (0 - 2.6 detik): garis cahaya berkumpul ke tengah, gelombang cincin,
        // logo ZMB memantul masuk dengan kilau, huruf Z M B muncul satu per satu, lalu semuanya membesar dan memudar ke menu.
        void DrawBoot(DrawingContext dc, double W, double H, double bt)
        {
            if (!splashTried) { splashTried = true; splashLogo = LoadAsset("zmb_logo.png"); }
            if (splashSeeds == null)
            {
                Random rnd = new Random(7);
                splashSeeds = new double[48 * 3];
                for (int i = 0; i < splashSeeds.Length; i++) splashSeeds[i] = rnd.NextDouble();
            }
            double s = S;
            double cx = W / 2, cy = H * 0.44;
            double exit = SpSmooth((bt - 1.9) / 0.7);          // 1.9 - 2.6: keluar
            double bgA = 1 - exit;

            // latar gelap dengan cahaya biru di tengah
            dc.DrawRectangle(BlackA(bgA), null, new Rect(0, 0, W, H));
            double glow = SpOut3(bt / 1.2) * (1 - exit * 0.6);
            if (glow > 0.01)
            {
                RadialGradientBrush rg = new RadialGradientBrush();
                rg.GradientStops.Add(new GradientStop(Color.FromArgb((byte)(150 * glow * bgA), 20, 110, 230), 0));
                rg.GradientStops.Add(new GradientStop(Color.FromArgb((byte)(50 * glow * bgA), 10, 60, 160), 0.45));
                rg.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0, 0, 0), 1));
                rg.Freeze();
                double gr = H * (0.35 + 0.25 * glow);
                dc.DrawEllipse(rg, null, new Point(cx, cy), gr * 1.6, gr);
            }

            // garis cahaya dari tepi menuju logo (0 - 0.75 detik)
            double conv = SpClamp(bt / 0.75);
            if (conv < 1)
            {
                for (int i = 0; i < 48; i++)
                {
                    double ang = splashSeeds[i * 3] * Math.PI * 2;
                    double delay = splashSeeds[i * 3 + 1] * 0.35;
                    double u = SpClamp((bt - delay) / 0.45);
                    if (u <= 0 || u >= 1) continue;
                    double far = Math.Max(W, H) * (0.55 + 0.25 * splashSeeds[i * 3 + 2]);
                    double d0 = far * (1 - SpOut3(u)), d1 = d0 + far * 0.18 * (1 - u);
                    Point p0 = new Point(cx + Math.Cos(ang) * d0, cy + Math.Sin(ang) * d0 * 0.75);
                    Point p1 = new Point(cx + Math.Cos(ang) * d1, cy + Math.Sin(ang) * d1 * 0.75);
                    double la = Math.Sin(Math.PI * u) * 0.8;
                    LinearGradientBrush lb = new LinearGradientBrush(Color.FromArgb((byte)(255 * la), 170, 220, 255), Color.FromArgb(0, 40, 140, 255), new Point(0, 0), new Point(1, 0));
                    lb.MappingMode = BrushMappingMode.Absolute; lb.StartPoint = p0; lb.EndPoint = p1;
                    dc.DrawLine(new Pen(lb, 2.2 * s), p0, p1);
                }
            }

            // cincin gelombang saat logo mendarat
            double ring = SpClamp((bt - 0.55) / 0.9);
            if (ring > 0 && ring < 1)
            {
                double rr = H * 0.12 + H * 0.55 * SpOut3(ring);
                double ra = (1 - ring) * 0.55 * bgA;
                dc.DrawEllipse(null, new Pen(Brush3(140, 200, 255, ra), (6 - 4 * ring) * s), new Point(cx, cy), rr * 1.25, rr);
            }

            // logo
            double li = SpClamp((bt - 0.45) / 0.6);
            if (li > 0 && splashLogo != null)
            {
                double sc = (0.35 + 0.65 * SpOutBack(li)) * (1 + 0.18 * exit);
                double la = SpClamp(li * 2.2) * (1 - exit);
                double ls = 230 * s * sc;
                Rect lr = new Rect(cx - ls / 2, cy - ls / 2, ls, ls);
                double rot = -10 * (1 - SpOut3(li));
                dc.PushTransform(new RotateTransform(rot, cx, cy));
                // bayangan cahaya di belakang logo
                RadialGradientBrush halo = new RadialGradientBrush(Color.FromArgb((byte)(110 * la), 60, 160, 255), Color.FromArgb(0, 60, 160, 255));
                halo.Freeze();
                dc.DrawEllipse(halo, null, new Point(cx, cy), ls * 0.8, ls * 0.8);
                DrawImageA(dc, splashLogo, lr, la);
                // kilau menyapu logo (1.0 - 1.45 detik), mengikuti bentuk logo
                double sw = SpClamp((bt - 1.0) / 0.45);
                if (sw > 0 && sw < 1)
                {
                    ImageBrush mask = new ImageBrush(splashLogo);
                    mask.ViewportUnits = BrushMappingMode.Absolute; mask.Viewport = lr;
                    dc.PushOpacityMask(mask);
                    double bx = lr.X - ls * 0.6 + (ls * 2.2) * sw;
                    LinearGradientBrush band = new LinearGradientBrush();
                    band.MappingMode = BrushMappingMode.Absolute;
                    band.StartPoint = new Point(bx - ls * 0.25, lr.Y); band.EndPoint = new Point(bx + ls * 0.25, lr.Bottom);
                    band.GradientStops.Add(new GradientStop(Color.FromArgb(0, 255, 255, 255), 0));
                    band.GradientStops.Add(new GradientStop(Color.FromArgb((byte)(200 * la), 255, 255, 255), 0.5));
                    band.GradientStops.Add(new GradientStop(Color.FromArgb(0, 255, 255, 255), 1));
                    dc.DrawRectangle(band, null, lr);
                    dc.Pop();
                }
                dc.Pop();
            }

            // huruf Z M B muncul satu per satu, jarak huruf merapat
            string[] letters = { "Z", "M", "B" };
            double fs = 64 * s * (1 + 0.1 * exit);
            double ty = cy + 150 * s;
            double spread = 26 * s + 40 * s * (1 - SpOut3((bt - 1.0) / 0.7));
            double totalW = 0;
            double[] lw = new double[3];
            for (int i = 0; i < 3; i++) { lw[i] = FT(letters[i], fs, 1, false, 0).Width; totalW += lw[i]; }
            totalW += spread * 2;
            double lx = cx - totalW / 2;
            for (int i = 0; i < 3; i++)
            {
                double u = SpClamp((bt - 1.0 - i * 0.12) / 0.4);
                double a = SpOut3(u) * (1 - exit);
                double dy = (1 - SpOut3(u)) * 30 * s;
                if (a > 0.01) Txt(dc, letters[i], fs, 1, lx, ty + dy, a, 0, 0, false);
                lx += lw[i] + spread;
            }
            double wa = SpSmooth((bt - 1.45) / 0.4) * (1 - exit);
            if (wa > 0.01)
                Txt(dc, "Selamat datang, " + Environment.UserName, 24 * s, 0, cx, ty + TextH(fs) + 12 * s, wa * 0.7, 0, 1, false);
        }

        Brush Brush3(byte r, byte g, byte b, double a)
        {
            SolidColorBrush br = new SolidColorBrush(Color.FromArgb((byte)(255 * SpClamp(a)), r, g, b));
            br.Freeze();
            return br;
        }

        void DrawClock(DrawingContext dc, double W, double H, double alpha, double now)
        {
            DateTime n = DateTime.Now;
            string time = cfg.Clock24 ? n.ToString("HH:mm") : n.ToString("h:mm tt");
            string txt = n.ToString("dd/MM") + "   " + time;
            double size = 24 * S;
            double th = TextH(size);
            double right = W - 70 * S;
            double y = 46 * S;
            double w = Txt(dc, txt, size, 0, right, y, 0.95 * alpha, 0, 2, true);
            double x = right - w;
            dc.DrawRectangle(WhiteA(0.4 * alpha), null, new Rect(x - 20 * S, y + th + 4 * S, W - 50 * S - (x - 20 * S), 1.2 * S));

            if (now >= battCheckAt)
            {
                battCheckAt = now + 5;
                try
                {
                    System.Windows.Forms.PowerStatus ps = System.Windows.Forms.SystemInformation.PowerStatus;
                    battHas = (ps.BatteryChargeStatus & System.Windows.Forms.BatteryChargeStatus.NoSystemBattery) == 0 && ps.BatteryLifePercent <= 1.0f;
                    battPct = ps.BatteryLifePercent;
                    battOnline = ps.PowerLineStatus == System.Windows.Forms.PowerLineStatus.Online;
                }
                catch { battHas = false; }
            }
            double gx = x - 60 * S;
            if (battHas)
            {
                double bw = 38 * S, bh = 18 * S;
                double bx = x - 150 * S, by = y + (th - bh) / 2;
                dc.PushOpacity(0.9 * alpha);
                Pen p = new Pen(brWhite, 1.6 * S);
                dc.DrawRectangle(null, p, new Rect(bx, by, bw, bh));
                dc.DrawRectangle(brWhite, null, new Rect(bx + bw, by + bh * 0.3, 3 * S, bh * 0.4));
                Brush fill = battPct < 0.2f ? (Brush)Solid(255, 255, 90, 80) : brWhite;
                dc.DrawRectangle(fill, null, new Rect(bx + 3 * S, by + 3 * S, Math.Max(0, (bw - 6 * S) * battPct), bh - 6 * S));
                dc.Pop();
                Txt(dc, ((int)Math.Round(battPct * 100)) + "%" + (battOnline ? "+" : ""), 17 * S, 0, bx + bw + 8 * S, y + (th - TextH(17 * S)) / 2, 0.85 * alpha, 0, 0, false);
                gx = bx - 40 * S;
            }
            if (Gamepad.AnyConnected) Glyph(dc, "\uE7FC", "G", gx, y + th / 2, 26 * S, 0.8 * alpha);
        }

        // ===================== XMB =====================
        void DrawXmb(DrawingContext dc, double W, double H, double fade, double now)
        {
            double s = S;
            double colX = W * 0.22;
            double catY = H * 0.25;
            double catSp = 205 * s;
            double fa = folderAnim;

            for (int i = 0; i < cats.Count; i++)
            {
                double rel = i - catAnim;
                double x = colX + rel * catSp - fa * W * 0.12;
                if (x < -catSp || x > W + catSp) continue;
                double sel = 1 - Math.Min(1, Math.Abs(rel));
                double size = (60 + 28 * sel) * s;
                double alpha = (0.5 + 0.5 * sel) * (1 - fa) * fade;
                if (alpha <= 0.01) continue;
                if (sel > 0.3) Glow(dc, x, catY, size * 1.2, 0.3 * alpha * sel);
                ImageSource cic;
                if (cats[i].IconKey != null && themeIcons.TryGetValue(cats[i].IconKey, out cic))
                    BitmapSourceFit(dc, cic, new Rect(x - size * 0.6, catY - size * 0.6, size * 1.2, size * 1.2), alpha, false);
                else Glyph(dc, cats[i].Glyph, cats[i].Title, x, catY, size, alpha);
                if (sel > 0.3)
                    Txt(dc, cats[i].Title, 21 * s, 0, x, catY + size * 0.62, alpha * (sel - 0.3) / 0.7, 0, 1, true);
            }

            // panel trofi visual
            listTextRight = 0;
            XmbItem selItem = null;
            {
                List<XmbItem> its; int ix;
                GetCurrent(out its, out ix);
                if (ix >= 0 && ix < its.Count) selItem = its[ix];
            }
            AchvSet panelSet = null;
            bool showPanel = mode == Mode.Xmb && cfg.ViewMode == 0 && AchvPanelFor(selItem, out panelSet);
            if (showPanel) listTextRight = W * 0.57;

            if (stack.Count == 0)
            {
                Category c = cats[catIndex];
                double switching = Math.Min(1, Math.Abs(catIndex - catAnim) * 1.6);
                DrawItemList(dc, W, H, c.Items, c.Anim, c.Index, colX, catY + 50 * s, fade * (1 - switching), now);
            }
            else
            {
                Level l = stack[stack.Count - 1];
                double la = fade * (1 - Math.Min(1, Math.Abs(levelSlide)));
                double lx = colX + levelSlide * W * 0.08;

                string crumb = cats[catIndex].Title;
                foreach (Level lv in stack) crumb += "  \u203A  " + lv.Title;
                Txt(dc, crumb, 24 * s, 0, 60 * s, 40 * s, 0.85 * fade, W * 0.55, 0, true);
                Txt(dc, (l.Index + 1) + " / " + l.Items.Count, 17 * s, 0, 62 * s, 40 * s + TextH(24 * s), 0.55 * fade, 0, 0, false);

                if (l.Parent != null)
                {
                    double px = lx - 250 * s;
                    double py = catY + 200 * s;
                    DrawTile(dc, l.Parent, px, py, 128 * s, 80 * s, 0.45 * la);
                    Txt(dc, "\u2039", 30 * s, 0, px + 82 * s, py - TextH(30 * s) / 2, 0.4 * la, 0, 0, false);
                }
                DrawItemList(dc, W, H, l.Items, l.Anim, l.Index, lx, catY + 50 * s, la, now);
            }
            if (showPanel) DrawAchvPanel(dc, W, H, selItem, panelSet, fade * (1 - Math.Min(1, Math.Abs(levelSlide))));
        }

        void DrawItemList(DrawingContext dc, double W, double H, List<XmbItem> items, double anim, int index, double colX, double baseY, double alpha, double now)
        {
            if (alpha <= 0.01 || items == null || items.Count == 0) return;
            double s = S;
            double selY = baseY + 150 * s;
            double aboveY = baseY - 140 * s;
            double sp = 104 * s;
            double tileHBase = 64 * s, tileHSel = 104 * s;
            double ratio = 1.6;
            double textX = colX + tileHSel * ratio / 2 + 26 * s;
            double rightEdge = listTextRight > 0 ? Math.Min(W, listTextRight) : W;
            double textW = Math.Max(50, rightEdge - textX - 40 * s);
            double fsT = 25 * s, fsTS = 30 * s, fsSub = 19 * s;

            int from = Math.Max(0, (int)Math.Floor(anim) - 8);
            int to = Math.Min(items.Count - 1, (int)Math.Ceiling(anim) + 12);
            for (int i = from; i <= to; i++)
            {
                double rel = i - anim;
                double y;
                if (rel >= 0) y = selY + rel * sp + Math.Min(1, rel) * 34 * s;
                else if (rel <= -1) y = aboveY + (rel + 1) * sp;
                else y = aboveY + (selY - aboveY) * (rel + 1);
                if (y < -sp || y > H + sp) continue;

                double selF = 1 - Math.Min(1, Math.Abs(rel));
                double th = tileHBase + (tileHSel - tileHBase) * selF;
                double tw = th * ratio;
                double a = alpha;
                if (y < baseY - 40 * s) a *= Math.Max(0, Math.Min(1, (y + 40 * s) / (baseY * 0.9)));
                a *= (0.72 + 0.28 * selF);
                if (a <= 0.01) continue;

                XmbItem it = items[i];
                bool isSel = (i == index);
                if (isSel && selF > 0.5)
                    Glow(dc, colX, y, th * 1.15, (0.2 + 0.08 * Math.Sin(now * 3.2)) * a * selF);
                DrawTile(dc, it, colX, y, tw, th, a);

                // ukuran judul berubah halus mengikuti animasi
                double fs = fsT + (fsTS - fsT) * selF;
                double scale = fs / fsTS;
                FormattedText ftT = FT(it.Title, fsTS, 0, false, textW);
                double tH = ftT.Height * scale;
                string sub = isSel && !it.HideSub ? it.Sub : null;
                bool showSub = isSel && selF > 0.5 && !string.IsNullOrEmpty(sub);
                double ty = showSub ? y - tH * 0.85 : y - tH * 0.5;

                dc.PushTransform(new MatrixTransform(scale, 0, 0, scale, textX, ty));
                ftT.SetForegroundBrush(BlackA(0.4 * Math.Min(1, a)));
                dc.DrawText(ftT, new Point(1.6 * s / scale, 1.6 * s / scale));
                ftT.SetForegroundBrush(a >= 0.995 ? brWhite : WhiteA(a));
                dc.DrawText(ftT, new Point(0, 0));
                ftT.SetForegroundBrush(brWhite);
                dc.Pop();

                if (showSub)
                    Txt(dc, sub, fsSub, 0, textX, ty + tH * 1.02, 0.78 * a * (selF - 0.5) * 2, textW, 0, true);
                if (isSel && it.Progress >= 0 && selF > 0.5)
                {
                    double py = ty + tH * 1.02 + (showSub ? TextH(fsSub) + 6 * s : 4 * s);
                    double pw = Math.Min(320 * s, textW);
                    double pa = a * (selF - 0.5) * 2;
                    dc.DrawRoundedRectangle(WhiteA(0.25 * pa), null, new Rect(textX, py, pw, 6 * s), 3 * s, 3 * s);
                    dc.DrawRoundedRectangle(WhiteA(pa), null, new Rect(textX, py, Math.Max(0, pw * Math.Min(1, it.Progress)), 6 * s), 3 * s, 3 * s);
                }
            }
        }

        void DrawTile(DrawingContext dc, XmbItem it, double cx, double cy, double tw, double th, double a)
        {
            Rect r = new Rect(cx - tw / 2, cy - th / 2, tw, th);
            ImageSource img = it.ImageKey != null ? GetImage(it.ImageKey, it.ImageLoader) : null;
            if (it.Dim) a *= 0.4;
            if (img != null && it.Kind == "platform") { DrawIconImage(dc, img, r, a); return; }
            if (img != null) { DrawImageFit(dc, img, r, a); return; }
            if (it.Badge != null) { DrawBadge(dc, it.Badge, it.BadgeColor, r, a); return; }
            Glyph(dc, it.Glyph, it.Title, cx, cy, th * 0.6, a);
        }

        // ikon kustom: pas di dalam kotak, tanpa bingkai
        void DrawIconImage(DrawingContext dc, ImageSource img, Rect r, double a)
        {
            BitmapSource bs = img as BitmapSource;
            double iw = bs != null ? bs.PixelWidth : img.Width, ih = bs != null ? bs.PixelHeight : img.Height;
            if (iw <= 0 || ih <= 0) return;
            double sc = Math.Min(r.Width / iw, r.Height / ih);
            double w = iw * sc, h = ih * sc;
            DrawImageA(dc, img, new Rect(r.X + (r.Width - w) / 2, r.Y + (r.Height - h) / 2, w, h), a);
        }

        void DrawImageFit(DrawingContext dc, ImageSource img, Rect r, double a)
        {
            double iw, ih;
            BitmapSource bs = img as BitmapSource;
            if (bs != null) { iw = bs.PixelWidth; ih = bs.PixelHeight; } else { iw = img.Width; ih = img.Height; }
            if (iw <= 0 || ih <= 0) return;
            double sc = Math.Min(r.Width / iw, r.Height / ih);
            double w = iw * sc, h = ih * sc;
            Rect d = new Rect(r.X + (r.Width - w) / 2, r.Y + (r.Height - h) / 2, w, h);
            DrawImageA(dc, img, d, a);
            if (a > 0.5) dc.DrawRectangle(null, penThin, d);
        }

        Brush BadgeBrush(Color col)
        {
            Brush b;
            if (badgeBrushes.TryGetValue(col, out b)) return b;
            LinearGradientBrush lb = new LinearGradientBrush();
            lb.StartPoint = new Point(0, 0); lb.EndPoint = new Point(0, 1);
            lb.GradientStops.Add(new GradientStop(Mix(col, Colors.White, 0.25), 0));
            lb.GradientStops.Add(new GradientStop(Mix(col, Colors.Black, 0.35), 1));
            lb.Freeze();
            badgeBrushes[col] = lb;
            return lb;
        }

        void DrawBadge(DrawingContext dc, string text, Color col, Rect r, double a)
        {
            if (text.Length == 1 && "PGSB".Contains(text))
            {
                DrawTrophyColored(dc, r.X + r.Width / 2, r.Y + r.Height / 2, Math.Min(r.Width, r.Height) * 0.9, GradeColor(text), a);
                return;
            }
            string ic = IconSet.PlatformIcon(text);
            if (ic != null)
            {
                double lblH = r.Height * 0.2;
                double isz = Math.Min(r.Width, r.Height - lblH) * 0.95;
                DrawIcon(dc, ic, r.X + r.Width / 2, r.Y + (r.Height - lblH) / 2 + r.Height * 0.02, isz, a, Colors.White, false);
                FormattedText lf = FT(text, Math.Max(6, lblH * 0.8), 2, false, 0);
                lf.SetForegroundBrush(WhiteA(Math.Min(1, a) * 0.85));
                dc.DrawText(lf, new Point(r.X + (r.Width - lf.Width) / 2, r.Bottom - lf.Height));
                lf.SetForegroundBrush(brWhite);
                return;
            }
            Rect b = new Rect(r.X + r.Width * 0.08, r.Y + r.Height * 0.1, r.Width * 0.84, r.Height * 0.8);
            dc.PushOpacity(Math.Min(1, a));
            dc.DrawRoundedRectangle(SolidOf(Color.FromArgb(48, 255, 255, 255)), penLine, b, b.Height * 0.16, b.Height * 0.16);
            dc.DrawRoundedRectangle(SolidOf(col), null, new Rect(b.X + b.Width * 0.3, b.Bottom - b.Height * 0.16, b.Width * 0.4, b.Height * 0.06), b.Height * 0.03, b.Height * 0.03);
            double fs = b.Height * (text.Length <= 3 ? 0.40 : text.Length <= 4 ? 0.34 : 0.27);
            // teks badge digambar pada ukuran tetap lalu diskalakan agar halus saat animasi
            double baseFs = 40;
            double k = fs / baseFs;
            FormattedText ft = FT(text, baseFs, 2, false, 0);
            dc.PushTransform(new MatrixTransform(k, 0, 0, k, b.X + (b.Width - ft.Width * k) / 2, b.Y + (b.Height - ft.Height * k) / 2));
            dc.DrawText(ft, new Point(0, 0));
            dc.Pop();
            dc.Pop();
        }

        // ===================== OVERLAY =====================
        void DrawNowPlaying(DrawingContext dc, double W, double H, double yTop)
        {
            if (!music.Playing) return;
            double s = S;
            double x = 60 * s, y = yTop >= 0 ? yTop : H - 112 * s;
            double bw = 360 * s;
            Glyph(dc, music.Paused ? "\uE769" : "\uE8D6", "M", x + 14 * s, y + 14 * s, 26 * s, 0.9);
            Txt(dc, music.Title(), 18 * s, 0, x + 40 * s, y, 0.92, bw, 0, true);
            double prog = musicLen > 0 ? Math.Min(1, (double)musicPos / musicLen) : 0;
            dc.DrawRectangle(WhiteA(0.25), null, new Rect(x + 40 * s, y + 34 * s, bw, 4 * s));
            dc.DrawRectangle(WhiteA(0.9), null, new Rect(x + 40 * s, y + 34 * s, bw * prog, 4 * s));
            Txt(dc, MediaExt.FormatTime(musicPos) + " / " + MediaExt.FormatTime(musicLen) + (music.Shuffle ? "   Acak" : ""), 16 * s, 0, x + 40 * s, y + 42 * s, 0.6, 0, 0, false);
        }

        void DrawHints(DrawingContext dc, double W, double H)
        {
            double s = S;
            double x = W - 60 * s;
            double fs = 17 * s;
            double th = TextH(fs);
            double y = H - 64 * s;
            bool pad = PadPrompts;
            string[] keys = { "opt", "back", "ok" };
            string[] labels = { "Opsi", "Kembali", "Pilih" };
            for (int i = 0; i < 3; i++)
            {
                double lw = Txt(dc, labels[i], fs, 0, x, y, 0.85, 0, 2, true);
                x -= lw + 8 * s;
                double kh = 28 * s, kw = PromptWidth(keys[i], fs, kh, pad);
                x -= kw;
                DrawPrompt(dc, keys[i], new Rect(x, y + (th - kh) / 2, kw, kh), fs, 1, pad);
                x -= 26 * s;
            }
        }

        // ===================== LAPISAN ATAS =====================
        void DrawTop(DrawingContext dc)
        {
            double W = surfTop.ActualWidth, H = surfTop.ActualHeight;
            if (W < 20 || H < 20) return;
            if (mode == Mode.Photo) DrawPhoto(dc, W, H);
            if (mode == Mode.Video) DrawVideoOsd(dc, W, H);
            if (mode == Mode.Profile) DrawProfile(dc, W, H);
            if (mode == Mode.Details) DrawDetails(dc, W, H);
            if (optAnim > 0.01) DrawOptions(dc, W, H);
            if (confirmOpen) DrawConfirm(dc, W, H);
            DrawToast(dc, W, H);
            DrawTrophyPopups(dc, W, H);
            if (saverOn) DrawScreensaver(dc, W, H);
        }

        void DrawOptions(DrawingContext dc, double W, double H)
        {
            double s = S;
            double pw = 480 * s;
            double x = W - pw * optAnim;
            dc.PushOpacity(optAnim);
            dc.DrawRectangle(brPanel, null, new Rect(x, 0, pw + 2, H));
            dc.DrawRectangle(null, penLine, new Rect(x, -2, 0.1, H + 4));
            double top = H * 0.16;
            Txt(dc, optTitle, 17 * s, 0, x + 36 * s, top, 0.6, pw - 60 * s, 0, false);
            double rowH = 58 * s;
            double listTop = top + 44 * s;
            double listH = H * 0.78 - listTop;
            int maxRows = Math.Max(3, (int)(listH / rowH));
            double first = Math.Max(0, Math.Min(optScroll - maxRows / 2.0 + 0.5, opts.Count - maxRows));
            double fs = 23 * s;
            double tH = TextH(fs);
            for (int i = 0; i < opts.Count; i++)
            {
                double y = listTop + (i - first) * rowH;
                if (y < listTop - rowH * 0.6 || y > listTop + listH) continue;
                double edge = 1;
                if (y < listTop) edge = 1 - (listTop - y) / (rowH * 0.6);
                bool sel = i == optIndex;
                if (sel)
                {
                    Rect hr = new Rect(x + 14 * s, y + 4 * s, pw - 28 * s, rowH - 8 * s);
                    dc.DrawRoundedRectangle(brHiOpt, null, hr, 6 * s, 6 * s);
                }
                string txt = (opts[i].Checked ? "\u2713  " : "     ") + opts[i].Text;
                Txt(dc, txt, fs, 0, x + 30 * s, y + (rowH - tH) / 2, (sel ? 1 : 0.7) * edge, pw - 60 * s, 0, false);
            }
            dc.Pop();
        }

        void DrawConfirm(DrawingContext dc, double W, double H)
        {
            double s = S;
            dc.DrawRectangle(BlackA(0.6), null, new Rect(0, 0, W, H));
            Rect box = new Rect(W * 0.25, H * 0.36, W * 0.5, H * 0.28);
            dc.DrawRectangle(null, penLine, new Rect(box.X, box.Y, box.Width, 0.1));
            dc.DrawRectangle(null, penLine, new Rect(box.X, box.Bottom, box.Width, 0.1));
            Txt(dc, confirmText, 27 * s, 0, W / 2, box.Y + box.Height * 0.28 - TextH(27 * s) / 2, 1, box.Width - 40 * s, 1, true);
            string[] lab = { "Ya", "Tidak" };
            for (int i = 0; i < 2; i++)
            {
                Rect br = new Rect(W / 2 - 230 * s + i * 250 * s, box.Bottom - 90 * s, 210 * s, 56 * s);
                if (i == confirmIdx)
                {
                    dc.DrawRoundedRectangle(WhiteA(0.25), null, br, 8 * s, 8 * s);
                }
                Txt(dc, lab[i], 23 * s, 0, br.X + br.Width / 2, br.Y + (br.Height - TextH(23 * s)) / 2, i == confirmIdx ? 1 : 0.6, 0, 1, false);
            }
        }

        void DrawToast(DrawingContext dc, double W, double H)
        {
            if (toastText == null) return;
            double age = T - toastAt;
            if (age > 3.2) { toastText = null; return; }
            double a = age < 0.2 ? age / 0.2 : age > 2.7 ? Math.Max(0, (3.2 - age) / 0.5) : 1;
            double s = S;
            double fs = 21 * s;
            FormattedText ft = FT(toastText, fs, 0, false, W * 0.75);
            double w = Math.Min(W * 0.8, ft.Width + 60 * s), h = ft.Height + 26 * s;
            Rect r = new Rect((W - w) / 2, 110 * s, w, h);
            dc.PushOpacity(a);
            dc.DrawRoundedRectangle(brToast, penThin, r, h / 2, h / 2);
            dc.DrawText(ft, new Point(r.X + (w - ft.Width) / 2, r.Y + (h - ft.Height) / 2));
            dc.Pop();
        }

        void DrawPhoto(DrawingContext dc, double W, double H)
        {
            double s = S;
            dc.DrawRectangle(brBlack, null, new Rect(0, 0, W, H));
            BitmapSource bs = photoImg as BitmapSource;
            if (bs != null)
            {
                double sc = Math.Min(W / bs.PixelWidth, H / bs.PixelHeight);
                double w = bs.PixelWidth * sc, h = bs.PixelHeight * sc;
                double fade = Math.Min(1, (T - photoShownAt) / 0.35);
                dc.PushOpacity(fade);
                dc.DrawImage(bs, new Rect((W - w) / 2, (H - h) / 2, w, h));
                dc.Pop();
            }
            if (T - slideAt < 2.5 || !slideshow)
            {
                string info = (photos.Count > 0 ? Path.GetFileName(photos[photoIdx]) : "") + "     " + (photoIdx + 1) + " / " + photos.Count +
                    (slideshow ? "     [Slideshow]" : "") + "     Kiri/Kanan: Ganti   Enter: Slideshow   Tab: Opsi   Esc: Kembali";
                dc.DrawRectangle(brOsd, null, new Rect(0, H - 56 * s, W, 56 * s));
                Txt(dc, info, 17 * s, 0, 30 * s, H - 28 * s - TextH(17 * s) / 2, 0.9, W - 60 * s, 0, false);
            }
        }

        void DrawVideoOsd(DrawingContext dc, double W, double H)
        {
            if (T > osdUntil && !videoPaused) return;
            double s = S;
            double h = 96 * s;
            dc.DrawRectangle(brOsd, null, new Rect(0, H - h, W, h));
            string name = videoIdx < videoList.Count ? Path.GetFileNameWithoutExtension(videoList[videoIdx]) : "";
            int pos = VideoPosMs(), len = VideoLenMs();
            Txt(dc, (videoPaused ? "[JEDA]  " : "") + name, 22 * s, 0, 40 * s, H - h + 14 * s, 1, W * 0.6, 0, false);
            Txt(dc, MediaExt.FormatTime(pos) + " / " + MediaExt.FormatTime(len) + "     Volume " + (int)Math.Round(videoFull.Volume * 100) + "%", 18 * s, 0, W - 40 * s, H - h + 16 * s, 0.85, 0, 2, false);
            double prog = len > 0 ? Math.Min(1, (double)pos / len) : 0;
            dc.DrawRectangle(WhiteA(0.25), null, new Rect(40 * s, H - h + 54 * s, W - 80 * s, 4 * s));
            dc.DrawRectangle(brWhite, null, new Rect(40 * s, H - h + 54 * s, (W - 80 * s) * prog, 4 * s));
            Txt(dc, "Enter: Jeda    Kiri/Kanan: -/+10 dtk    Atas/Bawah: Volume    LB/RB: Video lain    Esc: Kembali", 15 * s, 0, 40 * s, H - h + 66 * s, 0.6, W - 80 * s, 0, false);
        }
    }
}
