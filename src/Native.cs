using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Media;
using System.Runtime.InteropServices;
using System.Text;

namespace ZMB
{
    public static class Native
    {
        [DllImport("winmm.dll", CharSet = CharSet.Unicode)]
        static extern int mciSendString(string command, StringBuilder ret, int retLen, IntPtr hwnd);

        public static int LastMciError = 0;

        public static string Mci(string cmd)
        {
            StringBuilder sb = new StringBuilder(512);
            try { LastMciError = mciSendString(cmd, sb, 512, IntPtr.Zero); }
            catch { LastMciError = -1; }
            return sb.ToString();
        }

        [DllImport("user32.dll")] public static extern bool LockWorkStation();
        [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
        [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
        [DllImport("user32.dll")] public static extern bool DestroyIcon(IntPtr hIcon);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        static extern int SHDefExtractIcon(string pszIconFile, int iIndex, uint uFlags, out IntPtr phiconLarge, out IntPtr phiconSmall, uint nIconSize);

        // Ambil ikon resolusi tinggi dari exe (hingga 256px)
        public static Bitmap ExtractBigIcon(string file)
        {
            try
            {
                string target = file;
                string ext = Path.GetExtension(file).ToLowerInvariant();
                if (ext == ".lnk")
                {
                    string t = ResolveShortcut(file);
                    if (!string.IsNullOrEmpty(t) && File.Exists(t)) target = t;
                }
                if (!File.Exists(target)) return null;
                int[] sizes = new int[] { 256, 128, 64, 48 };
                foreach (int s in sizes)
                {
                    IntPtr large, small;
                    int hr = SHDefExtractIcon(target, 0, 0, out large, out small, (uint)(s | (16 << 16)));
                    if (small != IntPtr.Zero) DestroyIcon(small);
                    if (hr == 0 && large != IntPtr.Zero)
                    {
                        Bitmap bmp;
                        using (Icon ic = Icon.FromHandle(large)) bmp = ic.ToBitmap();
                        DestroyIcon(large);
                        return bmp;
                    }
                }
                using (Icon ic2 = Icon.ExtractAssociatedIcon(target))
                {
                    if (ic2 != null) return ic2.ToBitmap();
                }
            }
            catch { }
            return null;
        }

        public static string ResolveShortcut(string lnk)
        {
            try
            {
                Type t = Type.GetTypeFromProgID("WScript.Shell");
                object shell = Activator.CreateInstance(t);
                object sc = t.InvokeMember("CreateShortcut", System.Reflection.BindingFlags.InvokeMethod, null, shell, new object[] { lnk });
                object target = sc.GetType().InvokeMember("TargetPath", System.Reflection.BindingFlags.GetProperty, null, sc, null);
                Marshal.ReleaseComObject(sc);
                Marshal.ReleaseComObject(shell);
                return target as string;
            }
            catch { return null; }
        }

        public static Image LoadImageScaled(string path, int maxSize)
        {
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
                byte[] data = File.ReadAllBytes(path);
                using (MemoryStream ms = new MemoryStream(data))
                using (Image src = Image.FromStream(ms))
                {
                    float sc = Math.Min(1f, Math.Min((float)maxSize / src.Width, (float)maxSize / src.Height));
                    int w = Math.Max(1, (int)(src.Width * sc));
                    int h = Math.Max(1, (int)(src.Height * sc));
                    Bitmap b = new Bitmap(w, h);
                    using (Graphics g = Graphics.FromImage(b))
                    {
                        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                        g.DrawImage(src, 0, 0, w, h);
                    }
                    return b;
                }
            }
            catch { return null; }
        }
    }

    // ===================== GAMEPAD (XInput) =====================
    public enum XKey { None, Up, Down, Left, Right, Ok, Back, Options, Start, Select, PrevTrack, NextTrack, PageUp, PageDown }

    public static class Gamepad
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct XINPUT_GAMEPAD
        {
            public ushort wButtons;
            public byte bLeftTrigger;
            public byte bRightTrigger;
            public short sThumbLX;
            public short sThumbLY;
            public short sThumbRX;
            public short sThumbRY;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct XINPUT_STATE
        {
            public uint dwPacketNumber;
            public XINPUT_GAMEPAD Gamepad;
        }

        [DllImport("xinput1_4.dll", EntryPoint = "XInputGetState")]
        static extern int XInputGetState14(int idx, ref XINPUT_STATE s);
        [DllImport("xinput9_1_0.dll", EntryPoint = "XInputGetState")]
        static extern int XInputGetState910(int idx, ref XINPUT_STATE s);

        static int api = 0; // 0 = belum dicek, 1 = 1_4, 2 = 9_1_0, -1 = tidak ada
        static bool[] connected = new bool[4];
        static DateTime lastScan = DateTime.MinValue;
        static ushort prevButtons = 0, prevSony = 0, prevXbox = 0;
        public static DateTime LastPressAt = DateTime.MinValue;
        static Dictionary<XKey, DateTime> holdStart = new Dictionary<XKey, DateTime>();
        static Dictionary<XKey, DateTime> lastRepeat = new Dictionary<XKey, DateTime>();
        public static bool AnyConnected = false;
        public static ushort CurrentButtons = 0;
        // Jenis stik untuk ikon tombol di layar: 1 = Xbox (A B X Y), 2 = PlayStation (silang, bulat, kotak, segitiga)
        public const int StyleXbox = 1, StyleSony = 2;
        public static int Style = StyleXbox;
        public static bool XboxConnected, SonyConnected;

        // ---- Stik Sony (DualShock 4 / DualSense) lewat winmm, tanpa DS4Windows ----
        [StructLayout(LayoutKind.Sequential)]
        struct JOYINFOEX
        {
            public int dwSize, dwFlags, dwXpos, dwYpos, dwZpos, dwRpos, dwUpos, dwVpos, dwButtons, dwButtonNumber, dwPOV, dwReserved1, dwReserved2;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct JOYCAPS
        {
            public ushort wMid, wPid;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szPname;
            public uint wXmin, wXmax, wYmin, wYmax, wZmin, wZmax, wNumButtons, wPeriodMin, wPeriodMax,
                wRmin, wRmax, wUmin, wUmax, wVmin, wVmax, wCaps, wMaxAxes, wNumAxes, wMaxButtons;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szRegKey;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szOEMVxD;
        }

        [DllImport("winmm.dll")] static extern int joyGetPosEx(int id, ref JOYINFOEX ji);
        [DllImport("winmm.dll", EntryPoint = "joyGetDevCapsW", CharSet = CharSet.Unicode)] static extern int joyGetDevCaps(IntPtr id, ref JOYCAPS caps, int size);

        const int SonyVid = 0x054C;
        static List<int> sonyIds = new List<int>();
        static DateTime lastSonyScan = DateTime.MinValue;
        static bool winmmOk = true;

        static void ScanSony()
        {
            sonyIds.Clear();
            for (int i = 0; i < 16; i++)
            {
                JOYINFOEX ji = new JOYINFOEX();
                ji.dwSize = Marshal.SizeOf(typeof(JOYINFOEX));
                ji.dwFlags = 0xFF;
                if (joyGetPosEx(i, ref ji) != 0) continue;
                JOYCAPS c = new JOYCAPS();
                if (joyGetDevCaps(new IntPtr(i), ref c, Marshal.SizeOf(typeof(JOYCAPS))) != 0) continue;
                if (c.wMid == SonyVid) sonyIds.Add(i);
            }
        }

        // Tombol Sony dipetakan ke susunan bit XInput: silang=A, bulat=B, kotak=X, segitiga=Y
        static ushort ReadSony()
        {
            if (!winmmOk) return 0;
            ushort b = 0;
            try
            {
                if ((DateTime.Now - lastSonyScan).TotalSeconds > 3) { lastSonyScan = DateTime.Now; ScanSony(); }
                foreach (int id in sonyIds.ToArray())
                {
                    JOYINFOEX ji = new JOYINFOEX();
                    ji.dwSize = Marshal.SizeOf(typeof(JOYINFOEX));
                    ji.dwFlags = 0xFF;
                    if (joyGetPosEx(id, ref ji) != 0) { sonyIds.Remove(id); continue; }
                    int bt = ji.dwButtons;
                    if ((bt & 0x0001) != 0) b |= X;      // kotak
                    if ((bt & 0x0002) != 0) b |= A;      // silang
                    if ((bt & 0x0004) != 0) b |= B;      // bulat
                    if ((bt & 0x0008) != 0) b |= Y;      // segitiga
                    if ((bt & 0x0010) != 0) b |= LB;     // L1
                    if ((bt & 0x0020) != 0) b |= RB;     // R1
                    if ((bt & 0x0040) != 0) b |= 0x0400; // L2
                    if ((bt & 0x0080) != 0) b |= 0x0800; // R2
                    if ((bt & 0x0100) != 0) b |= BACK;   // Share / Create
                    if ((bt & 0x0200) != 0) b |= START;  // Options
                    if ((bt & 0x2000) != 0) b |= BACK;   // touchpad
                    int pov = ji.dwPOV;
                    if (pov >= 0 && pov < 36000)
                    {
                        if (pov >= 31500 || pov <= 4500) b |= UP;
                        if (pov >= 4500 && pov <= 13500) b |= RIGHT;
                        if (pov >= 13500 && pov <= 22500) b |= DOWN;
                        if (pov >= 22500 && pov <= 31500) b |= LEFT;
                    }
                    if (ji.dwYpos < 12000) b |= UP;
                    if (ji.dwYpos > 53500) b |= DOWN;
                    if (ji.dwXpos < 12000) b |= LEFT;
                    if (ji.dwXpos > 53500) b |= RIGHT;
                }
            }
            catch { winmmOk = false; }
            SonyConnected = sonyIds.Count > 0;
            return b;
        }

        static int GetState(int i, ref XINPUT_STATE s)
        {
            if (api == 0)
            {
                try { XINPUT_STATE t = new XINPUT_STATE(); XInputGetState14(0, ref t); api = 1; }
                catch
                {
                    try { XINPUT_STATE t = new XINPUT_STATE(); XInputGetState910(0, ref t); api = 2; }
                    catch { api = -1; }
                }
            }
            if (api == 1) return XInputGetState14(i, ref s);
            if (api == 2) return XInputGetState910(i, ref s);
            return 1167;
        }

        public const ushort UP = 0x0001, DOWN = 0x0002, LEFT = 0x0004, RIGHT = 0x0008,
            START = 0x0010, BACK = 0x0020, LB = 0x0100, RB = 0x0200, A = 0x1000, B = 0x2000, X = 0x4000, Y = 0x8000;

        // Mengembalikan daftar tombol yang baru ditekan (dengan auto-repeat untuk arah)
        public static List<XKey> Poll()
        {
            List<XKey> result = new List<XKey>();
            ushort buttons = 0;
            ushort sony = ReadSony();
            bool rescan = (DateTime.Now - lastScan).TotalSeconds > 2;
            if (rescan) lastScan = DateTime.Now;
            AnyConnected = false;
            for (int i = 0; i < 4; i++)
            {
                if (!connected[i] && !rescan) continue;
                if (api == -1) break;
                XINPUT_STATE s = new XINPUT_STATE();
                int r;
                try { r = GetState(i, ref s); } catch { api = -1; break; }
                connected[i] = (r == 0);
                if (r != 0) continue;
                AnyConnected = true;
                ushort b = s.Gamepad.wButtons;
                if (s.Gamepad.sThumbLY > 16000) b |= UP;
                if (s.Gamepad.sThumbLY < -16000) b |= DOWN;
                if (s.Gamepad.sThumbLX < -16000) b |= LEFT;
                if (s.Gamepad.sThumbLX > 16000) b |= RIGHT;
                if (s.Gamepad.bLeftTrigger > 100) b |= 0x0400;  // LT
                if (s.Gamepad.bRightTrigger > 100) b |= 0x0800; // RT
                buttons |= b;
            }
            XboxConnected = AnyConnected;
            AnyConnected = XboxConnected || SonyConnected;
            // ikon tombol mengikuti stik yang terakhir ditekan (atau satu-satunya yang tercolok)
            ushort xbox = buttons;
            if ((sony & ~prevSony) != 0) Style = StyleSony;
            else if ((xbox & ~prevXbox) != 0) Style = StyleXbox;
            else if (SonyConnected && !XboxConnected) Style = StyleSony;
            else if (XboxConnected && !SonyConnected) Style = StyleXbox;
            prevSony = sony; prevXbox = xbox;
            buttons |= sony;
            if ((buttons & ~prevButtons) != 0) LastPressAt = DateTime.Now;
            CurrentButtons = buttons;
            CheckRepeat(result, buttons, UP, XKey.Up);
            CheckRepeat(result, buttons, DOWN, XKey.Down);
            CheckRepeat(result, buttons, LEFT, XKey.Left);
            CheckRepeat(result, buttons, RIGHT, XKey.Right);
            CheckRepeat(result, buttons, 0x0400, XKey.PageUp);
            CheckRepeat(result, buttons, 0x0800, XKey.PageDown);
            CheckPress(result, buttons, A, XKey.Ok);
            CheckPress(result, buttons, B, XKey.Back);
            CheckPress(result, buttons, Y, XKey.Options);
            CheckPress(result, buttons, X, XKey.Select);
            CheckPress(result, buttons, START, XKey.Start);
            CheckPress(result, buttons, LB, XKey.PrevTrack);
            CheckPress(result, buttons, RB, XKey.NextTrack);
            prevButtons = buttons;
            return result;
        }

        static void CheckPress(List<XKey> res, ushort now, ushort mask, XKey k)
        {
            if ((now & mask) != 0 && (prevButtons & mask) == 0) res.Add(k);
        }

        static void CheckRepeat(List<XKey> res, ushort now, ushort mask, XKey k)
        {
            DateTime t = DateTime.Now;
            if ((now & mask) != 0)
            {
                if ((prevButtons & mask) == 0)
                {
                    res.Add(k); holdStart[k] = t; lastRepeat[k] = t;
                }
                else if ((t - holdStart[k]).TotalMilliseconds > 380 && (t - lastRepeat[k]).TotalMilliseconds > 85)
                {
                    res.Add(k); lastRepeat[k] = t;
                }
            }
        }
    }

    // ===================== EFEK SUARA (disintesis) =====================
    public static class Sfx
    {
        static byte[] tick, ok, back, boot, err, trophy;
        static SoundPlayer player = new SoundPlayer();
        public static bool Enabled = true;

        static byte[] MakeWav(int ms, Func<double, double> wave)
        {
            int rate = 22050;
            int n = rate * ms / 1000;
            MemoryStream m = new MemoryStream();
            BinaryWriter w = new BinaryWriter(m);
            w.Write(Encoding.ASCII.GetBytes("RIFF")); w.Write(36 + n * 2);
            w.Write(Encoding.ASCII.GetBytes("WAVE")); w.Write(Encoding.ASCII.GetBytes("fmt "));
            w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(rate); w.Write(rate * 2); w.Write((short)2); w.Write((short)16);
            w.Write(Encoding.ASCII.GetBytes("data")); w.Write(n * 2);
            for (int i = 0; i < n; i++)
            {
                double t = (double)i / rate;
                double v = wave(t);
                if (v > 1) v = 1; if (v < -1) v = -1;
                w.Write((short)(v * 32000));
            }
            w.Flush();
            return m.ToArray();
        }

        static double Env(double t, double attack, double len)
        {
            if (t < attack) return t / attack;
            return Math.Max(0, 1 - (t - attack) / (len - attack));
        }

        public static void Init()
        {
            tick = MakeWav(45, t => 0.22 * Env(t, 0.002, 0.045) * Math.Sin(2 * Math.PI * 1850 * t));
            ok = MakeWav(160, t => 0.25 * Env(t, 0.004, 0.16) * (Math.Sin(2 * Math.PI * 880 * t) * 0.6 + Math.Sin(2 * Math.PI * 1320 * t) * 0.4));
            back = MakeWav(120, t => 0.22 * Env(t, 0.004, 0.12) * Math.Sin(2 * Math.PI * (700 - 1500 * t) * t));
            trophy = MakeWav(900, t =>
            {
                double[] f = new double[] { 784, 988, 1175, 1568 };
                double v = 0;
                for (int i = 0; i < f.Length; i++)
                {
                    double st = i * 0.09;
                    if (t < st) continue;
                    double tt = t - st;
                    v += Math.Sin(2 * Math.PI * f[i] * tt) * Math.Exp(-tt * 4.5) * (i == 3 ? 0.9 : 0.6);
                }
                return 0.16 * v;
            });
            err = MakeWav(200, t => 0.22 * Env(t, 0.004, 0.2) * Math.Sin(2 * Math.PI * 220 * t) * (Math.Sin(2 * Math.PI * 30 * t) > 0 ? 1 : 0.5));
            boot = MakeWav(2600, t =>
            {
                double e = t < 0.8 ? t / 0.8 : Math.Max(0, 1 - (t - 0.8) / 1.8);
                double[] f = new double[] { 130.81, 196.0, 261.63, 329.63, 392.0, 523.25 };
                double v = 0;
                for (int i = 0; i < f.Length; i++) v += Math.Sin(2 * Math.PI * f[i] * t + i) * (1.0 - i * 0.12);
                return 0.09 * e * v * (1 + 0.15 * Math.Sin(2 * Math.PI * 5 * t));
            });
        }

        static Dictionary<string, byte[]> custom = new Dictionary<string, byte[]>();

        public static void ResetCustom() { custom.Clear(); }

        // Suara kustom dari paket tema (tick.wav, ok.wav, back.wav, error.wav, boot.wav, trophy.wav)
        public static void LoadCustom(string dir)
        {
            custom.Clear();
            try
            {
                if (!Directory.Exists(dir)) return;
                foreach (string n in new string[] { "tick", "ok", "back", "error", "boot", "trophy" })
                {
                    string f = Path.Combine(dir, n + ".wav");
                    if (File.Exists(f)) custom[n] = File.ReadAllBytes(f);
                }
            }
            catch { }
        }

        static byte[] Pick(string name, byte[] def)
        {
            byte[] b;
            return custom.TryGetValue(name, out b) ? b : def;
        }

        static void Play(byte[] d)
        {
            if (!Enabled || d == null) return;
            try { player.Stop(); player.Stream = new MemoryStream(d); player.Play(); } catch { }
        }

        public static void Tick() { Play(Pick("tick", tick)); }
        public static void Ok() { Play(Pick("ok", ok)); }
        public static void Back() { Play(Pick("back", back)); }
        public static void Error() { Play(Pick("error", err)); }
        public static void Boot() { Play(Pick("boot", boot)); }
        public static void Trophy() { Play(Pick("trophy", trophy)); }
    }
}
