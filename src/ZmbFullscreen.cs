using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace ZMB
{
    // Auto layar penuh untuk game emulator:
    // 1) tambah argumen fullscreen yang dikenal emulatornya, 2) cadangan: jendela game dipaksa borderless menutup monitor.
    static class AutoFullscreen
    {
        // nama exe (awalan, huruf kecil) -> argumen fullscreen yang resmi didukung emulator itu
        static readonly string[][] Flags = {
            new[] { "duckstation", "-fullscreen" },
            new[] { "pcsx2", "-fullscreen" },
            new[] { "ppsspp", "--fullscreen" },
            new[] { "rpcs3", "--fullscreen" },
            new[] { "dolphin", "-C Dolphin.Display.Fullscreen=True" },
            new[] { "melonds", "-f" },
            new[] { "mgba", "-f" },
            new[] { "retroarch", "-f" },
            new[] { "snes9x", "-fullscreen" },
            new[] { "mesen", "--fullscreen" },
            new[] { "fusion", "-fullscreen" },
            new[] { "cemu", "-f" },
            new[] { "ryujinx", "--fullscreen" },
            new[] { "xenia", "--fullscreen" },
            new[] { "eden", "-f" },
            new[] { "citron", "-f" },
            new[] { "sudachi", "-f" },
            new[] { "yuzu", "-f" },
        };

        public static string AddFlag(string emulatorPath, string args)
        {
            if (string.IsNullOrEmpty(emulatorPath)) return args;
            string exe = Path.GetFileNameWithoutExtension(emulatorPath).ToLowerInvariant();
            string a = args ?? "";
            foreach (string[] f in Flags)
            {
                if (!exe.StartsWith(f[0])) continue;
                if (HasFlag(a, f[1])) return a;
                return f[1] + " " + a;
            }
            return a;
        }

        static bool HasFlag(string args, string flag)
        {
            string low = args.ToLowerInvariant();
            if (flag.IndexOf("fullscreen", StringComparison.OrdinalIgnoreCase) >= 0 && low.Contains("fullscreen")) return true;
            foreach (string tok in low.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries))
                if (tok == flag.ToLowerInvariant() || tok == "--fullscreen" || tok == "-fullscreen") return true;
            return false;
        }

        // ===== cadangan: paksa jendela borderless menutup seluruh monitor =====
        [StructLayout(LayoutKind.Sequential)] struct RECT { public int L, T, R, B; }
        [StructLayout(LayoutKind.Sequential)] struct MONITORINFO { public int cbSize; public RECT rcMonitor, rcWork; public int dwFlags; }
        delegate bool EnumProc(IntPtr h, IntPtr l);
        [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc cb, IntPtr l);
        [DllImport("user32.dll")] static extern int GetWindowThreadProcessId(IntPtr h, out int pid);
        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
        [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr h, int cmd);
        [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
        [DllImport("user32.dll")] static extern IntPtr MonitorFromWindow(IntPtr h, int flags);
        [DllImport("user32.dll")] static extern bool GetMonitorInfo(IntPtr m, ref MONITORINFO mi);
        [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr h, int i);
        [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr h, int i, int v);
        [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int w, int hh, int flags);
        [DllImport("user32.dll")] static extern bool IsIconic(IntPtr h);

        const int GWL_STYLE = -16, GW_OWNER = 4;
        const int WS_CAPTION = 0x00C00000, WS_THICKFRAME = 0x00040000, WS_MINIMIZEBOX = 0x00020000, WS_MAXIMIZEBOX = 0x00010000, WS_SYSMENU = 0x00080000;
        const int SWP_FRAMECHANGED = 0x0020, SWP_SHOWWINDOW = 0x0040, SWP_NOOWNERZORDER = 0x0200;

        // Tunggu jendela game muncul; kalau setelah beberapa detik belum menutup layar, jadikan borderless fullscreen.
        public static void WatchAndForce(Process proc)
        {
            if (proc == null) return;
            int pid;
            try { pid = proc.Id; } catch { return; }
            Thread t = new Thread(delegate ()
            {
                try { Watch(proc, pid); } catch { }
            });
            t.IsBackground = true;
            t.Start();
        }

        static void Watch(Process proc, int pid)
        {
            DateTime start = DateTime.Now, firstSeen = DateTime.MinValue;
            while ((DateTime.Now - start).TotalSeconds < 40)
            {
                Thread.Sleep(500);
                try { if (proc.HasExited) return; } catch { return; }
                IntPtr w = MainWindowOf(pid);
                if (w == IntPtr.Zero) { firstSeen = DateTime.MinValue; continue; }
                if (firstSeen == DateTime.MinValue) firstSeen = DateTime.Now;
                // beri waktu emulator memasang fullscreen-nya sendiri dulu
                if ((DateTime.Now - firstSeen).TotalSeconds < 3.5) continue;
                if (IsIconic(w)) continue;
                RECT mon;
                if (!MonitorRect(w, out mon)) return;
                RECT r;
                if (!GetWindowRect(w, out r)) return;
                if (r.L <= mon.L + 2 && r.T <= mon.T + 2 && r.R >= mon.R - 2 && r.B >= mon.B - 2) return; // sudah penuh
                int st = GetWindowLong(w, GWL_STYLE);
                st &= ~(WS_CAPTION | WS_THICKFRAME | WS_MINIMIZEBOX | WS_MAXIMIZEBOX | WS_SYSMENU);
                SetWindowLong(w, GWL_STYLE, st);
                SetWindowPos(w, IntPtr.Zero, mon.L, mon.T, mon.R - mon.L, mon.B - mon.T, SWP_FRAMECHANGED | SWP_SHOWWINDOW | SWP_NOOWNERZORDER);
                return;
            }
        }

        static bool MonitorRect(IntPtr w, out RECT r)
        {
            r = new RECT();
            IntPtr m = MonitorFromWindow(w, 2); // MONITOR_DEFAULTTONEAREST
            MONITORINFO mi = new MONITORINFO();
            mi.cbSize = Marshal.SizeOf(typeof(MONITORINFO));
            if (m == IntPtr.Zero || !GetMonitorInfo(m, ref mi)) return false;
            r = mi.rcMonitor;
            return true;
        }

        // Jendela utama proses: terlihat, tidak punya owner, ukuran wajar; ambil yang paling besar
        static IntPtr MainWindowOf(int pid)
        {
            IntPtr best = IntPtr.Zero;
            long bestArea = 0;
            EnumWindows(delegate (IntPtr h, IntPtr l)
            {
                int wp;
                GetWindowThreadProcessId(h, out wp);
                if (wp != pid || !IsWindowVisible(h) || GetWindow(h, GW_OWNER) != IntPtr.Zero) return true;
                RECT r;
                if (!GetWindowRect(h, out r)) return true;
                long w = r.R - r.L, hh = r.B - r.T;
                if (w < 320 || hh < 240) return true;
                if (w * hh > bestArea) { bestArea = w * hh; best = h; }
                return true;
            }, IntPtr.Zero);
            return best;
        }
    }
}
