using System;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SD = System.Drawing;

namespace ZMB
{
    public static class ImgConv
    {
        // Memuat gambar dari file sebagai bitmap WPF (aman dipanggil dari thread lain, hasilnya di-freeze)
        public static BitmapSource LoadFile(string path, int maxWidth)
        {
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
                byte[] data = File.ReadAllBytes(path);
                int srcW = 0;
                using (MemoryStream probe = new MemoryStream(data))
                {
                    BitmapFrame fr = BitmapFrame.Create(probe, BitmapCreateOptions.DelayCreation | BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.None);
                    srcW = fr.PixelWidth;
                }
                MemoryStream ms = new MemoryStream(data);
                BitmapImage bi = new BitmapImage();
                bi.BeginInit();
                bi.CacheOption = BitmapCacheOption.OnLoad;
                bi.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                if (maxWidth > 0 && srcW > maxWidth) bi.DecodePixelWidth = maxWidth;
                bi.StreamSource = ms;
                bi.EndInit();
                bi.Freeze();
                ms.Dispose();
                return bi;
            }
            catch { return null; }
        }

        // Unduh gambar dari internet (disimpan di cache\img agar tidak diunduh ulang)
        public static BitmapSource LoadUrl(string url, int maxWidth)
        {
            try
            {
                string dir = Path.Combine(Config.BaseDir, "cache", "img");
                Directory.CreateDirectory(dir);
                string ext = Path.GetExtension(new Uri(url).AbsolutePath).ToLowerInvariant();
                if (ext != ".png" && ext != ".jpg" && ext != ".jpeg" && ext != ".gif") ext = ".img";
                string name;
                using (System.Security.Cryptography.MD5 md5 = System.Security.Cryptography.MD5.Create())
                    name = BitConverter.ToString(md5.ComputeHash(System.Text.Encoding.UTF8.GetBytes(url))).Replace("-", "").ToLowerInvariant();
                string file = Path.Combine(dir, name + ext);
                if (!File.Exists(file))
                {
                    System.Net.ServicePointManager.SecurityProtocol |= (System.Net.SecurityProtocolType)3072;
                    using (System.Net.WebClient wc = new System.Net.WebClient())
                    {
                        wc.Headers[System.Net.HttpRequestHeader.UserAgent] = "ZMB/2.1";
                        byte[] data = wc.DownloadData(url);
                        if (data == null || data.Length < 16) return null;
                        File.WriteAllBytes(file, data);
                    }
                }
                return LoadFile(file, maxWidth);
            }
            catch { return null; }
        }

        public static BitmapSource FromDrawing(SD.Image img)
        {
            if (img == null) return null;
            try
            {
                using (MemoryStream ms = new MemoryStream())
                {
                    img.Save(ms, SD.Imaging.ImageFormat.Png);
                    ms.Position = 0;
                    BitmapImage bi = new BitmapImage();
                    bi.BeginInit();
                    bi.CacheOption = BitmapCacheOption.OnLoad;
                    bi.StreamSource = ms;
                    bi.EndInit();
                    bi.Freeze();
                    return bi;
                }
            }
            catch { return null; }
            finally { img.Dispose(); }
        }
    }

    // Permukaan gambar: OnRender dipanggil ulang tiap frame (dirender GPU oleh WPF)
    public class Surface : FrameworkElement
    {
        Action<DrawingContext> draw;
        public static Action<Exception> OnError;

        public Surface(Action<DrawingContext> d)
        {
            draw = d;
            IsHitTestVisible = false;
            Focusable = false;
            SnapsToDevicePixels = false;
            TextOptions.SetTextHintingMode(this, TextHintingMode.Animated);
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Ideal);
            RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.Linear);
            RenderOptions.SetEdgeMode(this, EdgeMode.Unspecified);
        }

        protected override void OnRender(DrawingContext dc)
        {
            try { draw(dc); }
            catch (Exception ex) { if (OnError != null) OnError(ex); }
        }
    }

    public class Win32Owner : System.Windows.Forms.IWin32Window
    {
        IntPtr h;
        public Win32Owner(IntPtr handle) { h = handle; }
        public IntPtr Handle { get { return h; } }
    }
}
