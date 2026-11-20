using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ZMB
{
    public static class ShellThumb
    {
        [ComImport]
        [Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IShellItemImageFactory
        {
            [PreserveSig]
            int GetImage(SIZE size, int flags, out IntPtr phbm);
        }

        [StructLayout(LayoutKind.Sequential)]
        struct SIZE { public int cx; public int cy; }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
        static extern void SHCreateItemFromParsingName(
            [MarshalAs(UnmanagedType.LPWStr)] string path, IntPtr pbc,
            [MarshalAs(UnmanagedType.LPStruct)] Guid riid,
            [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory ppv);

        [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr h);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern bool SystemParametersInfo(int uAction, int uParam, string lpvParam, int fuWinIni);

        // Thumbnail dari Windows Explorer (video, foto, album art musik)
        public static Bitmap Get(string path, int size)
        {
            IShellItemImageFactory f = null;
            IntPtr hbm = IntPtr.Zero;
            try
            {
                if (!File.Exists(path)) return null;
                SHCreateItemFromParsingName(path, IntPtr.Zero, typeof(IShellItemImageFactory).GUID, out f);
                SIZE s; s.cx = size; s.cy = size;
                int hr = f.GetImage(s, 0x8, out hbm); // SIIGBF_THUMBNAILONLY
                if (hr != 0 || hbm == IntPtr.Zero) return null;
                Bitmap b = Image.FromHbitmap(hbm);
                return b;
            }
            catch { return null; }
            finally
            {
                if (hbm != IntPtr.Zero) DeleteObject(hbm);
                if (f != null) { try { Marshal.ReleaseComObject(f); } catch { } }
            }
        }

        public static bool SetWallpaper(string path)
        {
            try { return SystemParametersInfo(20, 0, path, 3); } catch { return false; }
        }
    }

    public static class Dialogs
    {
        public static string Input(IWin32Window owner, string title, string prompt, string def)
        {
            using (Form f = new Form())
            {
                f.Text = title;
                f.FormBorderStyle = FormBorderStyle.FixedDialog;
                f.StartPosition = FormStartPosition.CenterScreen;
                f.ClientSize = new Size(600, 190);
                f.MinimizeBox = false; f.MaximizeBox = false;
                f.ShowInTaskbar = false;
                f.TopMost = true;
                f.BackColor = Color.FromArgb(22, 28, 44);
                f.ForeColor = Color.White;
                f.Font = new Font("Segoe UI", 10.5f);

                Label l = new Label();
                l.Text = prompt; l.Left = 16; l.Top = 12; l.Width = 568; l.Height = 64;
                TextBox t = new TextBox();
                t.Left = 16; t.Top = 84; t.Width = 568; t.Text = def ?? "";
                t.BackColor = Color.FromArgb(40, 48, 70); t.ForeColor = Color.White; t.BorderStyle = BorderStyle.FixedSingle;
                Button ok = new Button();
                ok.Text = "OK"; ok.DialogResult = DialogResult.OK; ok.Left = 384; ok.Top = 136; ok.Width = 95; ok.Height = 36;
                ok.FlatStyle = FlatStyle.Flat; ok.BackColor = Color.FromArgb(40, 90, 180);
                Button cancel = new Button();
                cancel.Text = Lang.T("Batal"); cancel.DialogResult = DialogResult.Cancel; cancel.Left = 489; cancel.Top = 136; cancel.Width = 95; cancel.Height = 36;
                cancel.FlatStyle = FlatStyle.Flat; cancel.BackColor = Color.FromArgb(60, 60, 70);
                f.Controls.Add(l); f.Controls.Add(t); f.Controls.Add(ok); f.Controls.Add(cancel);
                f.AcceptButton = ok; f.CancelButton = cancel;
                f.Shown += delegate { t.Focus(); t.SelectAll(); };
                return f.ShowDialog(owner) == DialogResult.OK ? t.Text : null;
            }
        }

        public static string PickFile(IWin32Window owner, string title, string filter, string initialDir)
        {
            using (OpenFileDialog d = new OpenFileDialog())
            {
                d.Title = title; d.Filter = filter;
                if (!string.IsNullOrEmpty(initialDir) && Directory.Exists(initialDir)) d.InitialDirectory = initialDir;
                return d.ShowDialog(owner) == DialogResult.OK ? d.FileName : null;
            }
        }

        public static string[] PickFiles(IWin32Window owner, string title, string filter)
        {
            using (OpenFileDialog d = new OpenFileDialog())
            {
                d.Title = title; d.Filter = filter; d.Multiselect = true;
                return d.ShowDialog(owner) == DialogResult.OK ? d.FileNames : null;
            }
        }

        public static string PickFolder(IWin32Window owner, string desc, string current)
        {
            using (FolderBrowserDialog d = new FolderBrowserDialog())
            {
                d.Description = desc; d.ShowNewFolderButton = true;
                if (!string.IsNullOrEmpty(current) && Directory.Exists(current)) d.SelectedPath = current;
                return d.ShowDialog(owner) == DialogResult.OK ? d.SelectedPath : null;
            }
        }
    }
}
