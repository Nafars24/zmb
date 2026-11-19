using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ZMB
{
    // Jewel case & spine PS1 dengan lapisan plastik transparan asli (assets/ps1_case.png, assets/ps1_spine.png)
    public partial class ZmbWindow
    {
        ImageSource ps1CaseImg, ps1SpineImg;
        bool ps1CaseTried, ps1SpineTried;
        Dictionary<string, string> spinePathCache = new Dictionary<string, string>();

        // Posisi jendela bening (dalam pecahan ukuran gambar), diukur dari gambar aslinya
        const double CaseWinX0 = 0.100, CaseWinX1 = 0.970, CaseWinY0 = 0.020, CaseWinY1 = 0.978;
        const double SpineWinX0 = 0.150, SpineWinX1 = 0.862, SpineWinY0 = 0.022, SpineWinY1 = 0.978;

        static ImageSource LoadAsset(string name)
        {
            try
            {
                Stream st = Assembly.GetExecutingAssembly().GetManifestResourceStream("ZMB." + name);
                if (st == null)
                {
                    string f = Path.Combine(Path.Combine(Config.BaseDir, "assets"), name);
                    if (!File.Exists(f)) return null;
                    st = new MemoryStream(File.ReadAllBytes(f));
                }
                using (st)
                {
                    BitmapImage bi = new BitmapImage();
                    bi.BeginInit();
                    bi.CacheOption = BitmapCacheOption.OnLoad;
                    bi.StreamSource = st;
                    bi.EndInit();
                    bi.Freeze();
                    return bi;
                }
            }
            catch { return null; }
        }

        ImageSource Ps1CaseImg()
        {
            if (!ps1CaseTried) { ps1CaseTried = true; ps1CaseImg = LoadAsset("ps1_case.png"); }
            return ps1CaseImg;
        }

        ImageSource Ps1SpineImg()
        {
            if (!ps1SpineTried) { ps1SpineTried = true; ps1SpineImg = LoadAsset("ps1_spine.png"); }
            return ps1SpineImg;
        }

        string SpinePath(GameEntry g)
        {
            if (g == null) return null;
            string key = g.Platform + "|" + g.Path;
            string p;
            if (spinePathCache.TryGetValue(key, out p)) return p;
            try { p = GameScanner.FindSpine(g); } catch { p = null; }
            spinePathCache[key] = p;
            return p;
        }

        void DrawPs1Case(DrawingContext dc, XmbItem it, CaseStyle cs, Rect r)
        {
            Rect win = new Rect(r.X + r.Width * CaseWinX0, r.Y + r.Height * CaseWinY0,
                                r.Width * (CaseWinX1 - CaseWinX0), r.Height * (CaseWinY1 - CaseWinY0));
            dc.DrawRectangle(SolidOf(Rgb(12, 12, 14)), null, win);
            DrawCoverArea(dc, it, cs, win);
            dc.DrawImage(Ps1CaseImg(), r);
        }

        void DrawPs1Spine(DrawingContext dc, XmbItem it, Rect r)
        {
            Rect win = new Rect(r.X + r.Width * SpineWinX0, r.Y + r.Height * SpineWinY0,
                                r.Width * (SpineWinX1 - SpineWinX0), r.Height * (SpineWinY1 - SpineWinY0));
            string sp = SpinePath(it.Game);
            ImageSource art = null;
            if (sp != null)
            {
                string spp = sp;
                art = GetImage("spine:" + spp, delegate { return ImgConv.LoadFile(spp, 1400); });
            }
            if (art != null) DrawSpineArt(dc, art, win);
            else DrawPs1AutoSpine(dc, it, win);
            dc.DrawImage(Ps1SpineImg(), r);
        }

        // Gambar spine kustom; kalau gambarnya mendatar (lebar > tinggi), diputar 90 derajat
        void DrawSpineArt(DrawingContext dc, ImageSource art, Rect win)
        {
            BitmapSource bs = art as BitmapSource;
            double iw = bs != null ? bs.PixelWidth : art.Width, ih = bs != null ? bs.PixelHeight : art.Height;
            if (iw > ih)
            {
                double cx = win.X + win.Width / 2, cy = win.Y + win.Height / 2;
                Rect rot = new Rect(cx - win.Height / 2, cy - win.Width / 2, win.Height, win.Width);
                dc.PushClip(new RectangleGeometry(win));
                dc.PushTransform(new RotateTransform(90, cx, cy));
                DrawImageFill(dc, art, rot, 1.0);
                dc.Pop();
                dc.Pop();
            }
            else DrawImageFill(dc, art, win, 1.0);
        }

        // Spine otomatis ala PS1: hitam, label platform di atas, judul vertikal, tanda warna di bawah
        void DrawPs1AutoSpine(DrawingContext dc, XmbItem it, Rect win)
        {
            LinearGradientBrush bg = new LinearGradientBrush(Rgb(22, 22, 26), Rgb(8, 8, 10), 0);
            bg.Freeze();
            dc.DrawRectangle(bg, null, win);
            double w = win.Width;
            double topH = Math.Min(win.Height * 0.14, w * 1.5);
            dc.DrawRectangle(SolidOf(Rgb(36, 36, 41)), null, new Rect(win.X, win.Y, w, topH));
            dc.DrawRectangle(WhiteA(0.25), null, new Rect(win.X, win.Y + topH, w, Math.Max(0.6, w * 0.02)));

            FormattedText lt = FT("PS1", Math.Max(5, w * 0.32), 2, false, topH * 0.8);
            dc.PushTransform(new MatrixTransform(0, 1, -1, 0, win.X + w / 2 + lt.Height / 2, win.Y + (topH - lt.Width) / 2));
            lt.SetForegroundBrush(WhiteA(0.85));
            dc.DrawText(lt, new Point(0, 0));
            lt.SetForegroundBrush(brWhite);
            dc.Pop();

            // tanda warna (diambil dari warna cover) di bawah, seperti logo penerbit
            ImageSource cover = BoxImage(it);
            Color mark = (cover != null && !it.BoxIsIcon) ? AvgColor(it.BoxKey ?? it.ImageKey, cover, Rgb(200, 60, 40)) : Rgb(200, 60, 40);
            double ms = w * 0.56;
            Rect mr = new Rect(win.X + (w - ms) / 2, win.Bottom - ms - w * 0.3, ms, ms);
            dc.DrawRectangle(SolidOf(mark), null, mr);

            double fsz = Math.Max(6, Math.Min(w * 0.5, 30 * S));
            double pad = w * 0.35;
            double maxLen = Math.Max(10, win.Height - topH - (mr.Height + w * 0.3) - pad * 3);
            FormattedText ft = FT(it.Title.ToUpperInvariant(), fsz, 2, false, maxLen);
            double tx = win.X + w / 2 + ft.Height / 2, ty = win.Y + topH + pad;
            dc.PushTransform(new MatrixTransform(0, 1, -1, 0, tx, ty));
            dc.DrawText(ft, new Point(0, 0));
            dc.Pop();
        }
    }
}
