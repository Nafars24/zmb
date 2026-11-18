using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;

namespace ZMB
{
    // Helper performa: transparansi lewat kuas (bukan PushOpacity yang membuat lapisan offscreen),
    // dan ikon yang dirender sekali menjadi bitmap.
    public partial class ZmbWindow
    {
        SolidColorBrush[] whiteLv = new SolidColorBrush[65];
        SolidColorBrush[] blackLv = new SolidColorBrush[65];
        Dictionary<Brush, Brush[]> brushLv = new Dictionary<Brush, Brush[]>();
        Dictionary<ImageSource, Brush[]> imgBrushLv = new Dictionary<ImageSource, Brush[]>();
        Dictionary<string, ImageSource> iconBmp = new Dictionary<string, ImageSource>();

        static int Lv(double a)
        {
            if (a <= 0) return 0;
            if (a >= 1) return 64;
            return (int)Math.Round(a * 64);
        }

        Brush WhiteA(double a)
        {
            int l = Lv(a);
            if (whiteLv[l] == null)
            {
                SolidColorBrush b = new SolidColorBrush(Color.FromArgb((byte)(l * 255 / 64), 255, 255, 255));
                b.Freeze();
                whiteLv[l] = b;
            }
            return whiteLv[l];
        }

        Brush BlackA(double a)
        {
            int l = Lv(a);
            if (blackLv[l] == null)
            {
                SolidColorBrush b = new SolidColorBrush(Color.FromArgb((byte)(l * 255 / 64), 0, 0, 0));
                b.Freeze();
                blackLv[l] = b;
            }
            return blackLv[l];
        }

        Brush BrushA(Brush b, double a)
        {
            if (b == null) return null;
            if (a >= 0.995) return b;
            int l = Lv(a);
            Brush[] arr;
            if (!brushLv.TryGetValue(b, out arr))
            {
                if (brushLv.Count > 600) brushLv.Clear();
                arr = new Brush[65];
                brushLv[b] = arr;
            }
            if (arr[l] == null)
            {
                Brush c = b.Clone();
                c.Opacity = b.Opacity * l / 64.0;
                c.Freeze();
                arr[l] = c;
            }
            return arr[l];
        }

        Brush ImageBrushA(ImageSource img, double a)
        {
            int l = Lv(a);
            Brush[] arr;
            if (!imgBrushLv.TryGetValue(img, out arr))
            {
                if (imgBrushLv.Count > 1500) imgBrushLv.Clear();
                arr = new Brush[65];
                imgBrushLv[img] = arr;
            }
            if (arr[l] == null)
            {
                ImageBrush ib = new ImageBrush(img);
                ib.Stretch = Stretch.Fill;
                ib.Opacity = l / 64.0;
                RenderOptions.SetBitmapScalingMode(ib, BitmapScalingMode.Linear);
                ib.Freeze();
                arr[l] = ib;
            }
            return arr[l];
        }

        void DrawImageA(DrawingContext dc, ImageSource img, Rect r, double a)
        {
            if (img == null || a <= 0.01 || r.Width <= 0 || r.Height <= 0) return;
            if (a >= 0.995) dc.DrawImage(img, r);
            else dc.DrawRectangle(ImageBrushA(img, a), null, r);
        }

        // Ikon dirender sekali (dengan bayangan blur asli) lalu dipakai ulang sebagai bitmap
        const double IconPad = 22, IconScale = 2.2;  // bitmap 264px untuk kotak ikon 100 unit
        const int IconPx = 264;

        ImageSource IconBitmap(string name, Color tint, bool useTint)
        {
            string key = useTint ? name + "|" + tint.ToString() : name;
            ImageSource bmp;
            if (iconBmp.TryGetValue(key, out bmp)) return bmp;
            bmp = null;
            Geometry g = IconGeo(name);
            if (g != null)
            {
                try
                {
                    Brush fill;
                    if (useTint)
                    {
                        LinearGradientBrush tb = new LinearGradientBrush(Mix(tint, Colors.White, 0.35), Mix(tint, Colors.Black, 0.18), 90);
                        tb.Freeze();
                        fill = tb;
                    }
                    else
                    {
                        LinearGradientBrush lb = new LinearGradientBrush(Color.FromRgb(255, 255, 255), Color.FromRgb(208, 214, 226), 90);
                        lb.Freeze();
                        fill = lb;
                    }
                    DrawingVisual v = new DrawingVisual();
                    using (DrawingContext d = v.RenderOpen())
                    {
                        d.PushTransform(new MatrixTransform(IconScale, 0, 0, IconScale, IconPad, IconPad));
                        d.DrawGeometry(fill, null, g);
                        d.Pop();
                    }
                    DropShadowEffect fx = new DropShadowEffect();
                    fx.Color = Colors.Black;
                    fx.BlurRadius = 10;
                    fx.ShadowDepth = 4;
                    fx.Direction = 290;
                    fx.Opacity = 0.42;
                    v.Effect = fx;
                    ContainerVisual cv = new ContainerVisual();
                    cv.Children.Add(v);
                    RenderTargetBitmap rtb = new RenderTargetBitmap(IconPx, IconPx, 96, 96, PixelFormats.Pbgra32);
                    rtb.Render(cv);
                    rtb.Freeze();
                    bmp = rtb;
                }
                catch { bmp = null; }
            }
            iconBmp[key] = bmp;
            return bmp;
        }

        void DrawImageFill(DrawingContext dc, ImageSource img, Rect target, double a)
        {
            BitmapSource bs = img as BitmapSource;
            double iw = bs != null ? bs.PixelWidth : img.Width, ih = bs != null ? bs.PixelHeight : img.Height;
            if (iw <= 0 || ih <= 0 || target.Width <= 0 || target.Height <= 0 || a <= 0.01) return;
            double sc = Math.Max(target.Width / iw, target.Height / ih);
            double w = iw * sc, h = ih * sc;
            dc.PushClip(new RectangleGeometry(target));
            DrawImageA(dc, img, new Rect(target.X + (target.Width - w) / 2, target.Y + (target.Height - h) / 2, w, h), a);
            dc.Pop();
        }
    }
}
