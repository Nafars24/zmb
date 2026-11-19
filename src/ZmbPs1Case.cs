using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ZMB
{
    // Case & spine dengan lapisan plastik transparan asli per platform
    // (assets/ps1_case.png + ps1_spine.png untuk PS1, assets/ps3_case.png + ps3_spine.png untuk PS3)
    public class CaseOverlay
    {
        public string Name, Label;
        public double Aspect, SpineRatio;
        // posisi jendela bening (pecahan ukuran gambar), diukur dari gambar aslinya
        public double CX0, CX1, CY0, CY1, SX0, SX1, SY0, SY1;
        public ImageSource CaseImg, SpineImg;
        public bool Tried;

        public static readonly CaseOverlay Ps1 = new CaseOverlay("ps1", "PS1", 1.101, 0.103,
            0.100, 0.970, 0.020, 0.978, 0.150, 0.862, 0.022, 0.978);
        public static readonly CaseOverlay Ps3 = new CaseOverlay("ps3", "PS3", 0.900, 0.1123,
            0.0523, 0.9486, 0.1182, 0.9579, 0.183, 0.822, 0.111, 0.953);

        public CaseOverlay(string name, string label, double aspect, double spine,
            double cx0, double cx1, double cy0, double cy1, double sx0, double sx1, double sy0, double sy1)
        {
            Name = name; Label = label; Aspect = aspect; SpineRatio = spine;
            CX0 = cx0; CX1 = cx1; CY0 = cy0; CY1 = cy1; SX0 = sx0; SX1 = sx1; SY0 = sy0; SY1 = sy1;
        }

        public bool Ready
        {
            get
            {
                if (!Tried)
                {
                    Tried = true;
                    CaseImg = ZmbWindow.LoadAsset(Name + "_case.png");
                    SpineImg = ZmbWindow.LoadAsset(Name + "_spine.png");
                }
                return CaseImg != null && SpineImg != null;
            }
        }

        public Rect CaseWindow(Rect r) { return new Rect(r.X + r.Width * CX0, r.Y + r.Height * CY0, r.Width * (CX1 - CX0), r.Height * (CY1 - CY0)); }
        public Rect SpineWindow(Rect r) { return new Rect(r.X + r.Width * SX0, r.Y + r.Height * SY0, r.Width * (SX1 - SX0), r.Height * (SY1 - SY0)); }
    }

    public partial class ZmbWindow
    {
        Dictionary<string, string> spinePathCache = new Dictionary<string, string>();

        // Aset bawaan: dari resource di dalam exe, kalau tidak ada dari folder assets di sebelah ZMB.exe
        static byte[] LoadAssetBytes(string name)
        {
            try
            {
                using (Stream rs = Assembly.GetExecutingAssembly().GetManifestResourceStream("ZMB." + name))
                {
                    if (rs != null)
                    {
                        MemoryStream ms = new MemoryStream();
                        rs.CopyTo(ms);
                        return ms.ToArray();
                    }
                }
                string f = Path.Combine(Path.Combine(Config.BaseDir, "assets"), name);
                return File.Exists(f) ? File.ReadAllBytes(f) : null;
            }
            catch { return null; }
        }

        internal static ImageSource LoadAsset(string name)
        {
            try
            {
                byte[] data = LoadAssetBytes(name);
                if (data == null) return null;
                using (Stream st = new MemoryStream(data))
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

        void DrawOverlayCase(DrawingContext dc, XmbItem it, CaseStyle cs, Rect r)
        {
            CaseOverlay ov = cs.Overlay;
            Rect win = ov.CaseWindow(r);
            dc.DrawRectangle(SolidOf(Rgb(12, 12, 14)), null, win);
            DrawCoverArea(dc, it, cs, win);
            dc.DrawImage(ov.CaseImg, r);
        }

        void DrawOverlaySpine(DrawingContext dc, XmbItem it, CaseOverlay ov, Rect r)
        {
            Rect win = ov.SpineWindow(r);
            string sp = SpinePath(it.Game);
            ImageSource art = null;
            if (sp != null)
            {
                string spp = sp;
                art = GetImage("spine:" + spp, delegate { return ImgConv.LoadFile(spp, 1400); });
            }
            if (art != null) DrawSpineArt(dc, art, win);
            else DrawAutoSpine(dc, it, win, ov.Label);
            dc.DrawImage(ov.SpineImg, r);
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
        void DrawAutoSpine(DrawingContext dc, XmbItem it, Rect win, string label)
        {
            LinearGradientBrush bg = new LinearGradientBrush(Rgb(22, 22, 26), Rgb(8, 8, 10), 0);
            bg.Freeze();
            dc.DrawRectangle(bg, null, win);
            double w = win.Width;
            double topH = Math.Min(win.Height * 0.14, w * 1.5);
            dc.DrawRectangle(SolidOf(Rgb(36, 36, 41)), null, new Rect(win.X, win.Y, w, topH));
            dc.DrawRectangle(WhiteA(0.25), null, new Rect(win.X, win.Y + topH, w, Math.Max(0.6, w * 0.02)));

            FormattedText lt = FT(label, Math.Max(5, w * 0.32), 2, false, topH * 0.8);
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
