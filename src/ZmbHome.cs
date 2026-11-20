using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace ZMB
{
    public partial class ZmbWindow
    {
        [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
        [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr hWnd, int id);
        const int HomeHotkeyId = 0x5A4D;
        GameEntry homeLastGame = null;
        bool hotkeyRegistered = false;

        void InitHotkey()
        {
            try
            {
                IntPtr h = new WindowInteropHelper(this).Handle;
                HwndSource src = HwndSource.FromHwnd(h);
                if (src != null) src.AddHook(HomeWndProc);
                if (cfg.HomeHotkey) hotkeyRegistered = RegisterHotKey(h, HomeHotkeyId, 0x0002 | 0x0004, 0x24); // Ctrl+Shift+Home
            }
            catch { }
        }

        void ReleaseHotkey()
        {
            try { if (hotkeyRegistered) UnregisterHotKey(new WindowInteropHelper(this).Handle, HomeHotkeyId); } catch { }
        }

        IntPtr HomeWndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == 0x0312 && wParam.ToInt32() == HomeHotkeyId)
            {
                handled = true;
                OpenHomeMenu();
            }
            return IntPtr.Zero;
        }

        static bool ProcAlive(Process p)
        {
            if (p == null) return false;
            try { return !p.HasExited; } catch { return false; }
        }

        // Menu seperti tombol PS: muncul saat BACK+START ditahan / Ctrl+Shift+Home
        void OpenHomeMenu()
        {
            BringToFrontZmb();
            Sfx.Ok();
            lastInputAt = T;
            saverOn = false;
            List<OptionItem> o = new List<OptionItem>();
            GameEntry g = runningGame ?? homeLastGame;
            bool alive = ProcAlive(runningProc);
            string title = "Menu Home";
            if (alive && g != null)
            {
                title = Lang.T("Sedang dimainkan: ") + g.Name;
                o.Add(new OptionItem(Lang.T("Kembali ke Game"), ReturnToGame));
                o.Add(new OptionItem(Lang.T("Tutup Game"), delegate { CloseRunningGame(false); }));
                o.Add(new OptionItem(Lang.T("Tutup Paksa"), delegate
                {
                    Confirm(Lang.T("Tutup paksa ") + g.Name + Lang.T("? Progres yang belum disimpan akan hilang."), delegate { CloseRunningGame(true); });
                }));
            }
            else if (g != null && T - launchedAt < 6 * 3600)
            {
                title = Lang.T("Terakhir dijalankan: ") + g.Name;
                o.Add(new OptionItem(Lang.T("Kembali ke Game (sembunyikan ZMB)"), delegate { WindowState = WindowState.Minimized; }));
            }
            o.Add(new OptionItem(music.Playing && !music.Paused ? Lang.T("Jeda Musik") : Lang.T("Putar Musik"), delegate { music.TogglePause(); ShowMusicToast(); }));
            o.Add(new OptionItem(Lang.T("Lagu Berikutnya"), delegate { music.Next(); ShowMusicToast(); }));
            o.Add(new OptionItem(Lang.T("Tetap di ZMB"), delegate { }));
            OpenOptions(title, o);
        }

        void ReturnToGame()
        {
            try
            {
                if (ProcAlive(runningProc))
                {
                    runningProc.Refresh();
                    IntPtr h = runningProc.MainWindowHandle;
                    WindowState = WindowState.Minimized;
                    if (h != IntPtr.Zero)
                    {
                        Native.ShowWindow(h, 9); // SW_RESTORE
                        Native.SetForegroundWindow(h);
                    }
                    return;
                }
            }
            catch { }
            WindowState = WindowState.Minimized;
        }

        void CloseRunningGame(bool force)
        {
            Process p = runningProc;
            if (!ProcAlive(p)) { ShowToast(Lang.T("Game sudah tidak berjalan")); return; }
            string name = runningGame != null ? runningGame.Name : "game";
            if (!force)
            {
                bool sent = false;
                try { sent = p.CloseMainWindow(); } catch { sent = false; }
                if (sent)
                {
                    ShowToast(Lang.T("Menutup ") + name + " ...");
                    DispatcherTimer t = new DispatcherTimer();
                    t.Interval = TimeSpan.FromSeconds(6);
                    t.Tick += delegate
                    {
                        t.Stop();
                        if (ProcAlive(p) && p == runningProc)
                            Confirm(name + Lang.T(" belum tertutup. Tutup paksa?"), delegate { CloseRunningGame(true); });
                    };
                    t.Start();
                    return;
                }
            }
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo("taskkill", "/PID " + p.Id + " /T /F");
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                Process.Start(psi);
                ShowToast(name + Lang.T(" ditutup paksa"));
            }
            catch (Exception ex)
            {
                try { p.Kill(); ShowToast(name + Lang.T(" ditutup paksa")); }
                catch { ShowToast(Lang.T("Gagal menutup: ") + ex.Message); }
            }
        }

        // ===================== LOG SESI MAIN =====================
        void RecordSession(GameEntry g, double minutes, DateTime start)
        {
            if (g == null || minutes < 0.5) return;
            PlaySession ps = new PlaySession();
            ps.Start = start;
            ps.Minutes = Math.Round(minutes, 1);
            ps.Name = g.Name;
            ps.Platform = g.Platform;
            ps.Key = g.Path;
            cfg.Sessions.Add(ps);
        }
    }
}
