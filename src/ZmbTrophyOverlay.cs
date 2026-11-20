using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace ZMB
{
    // Jendela transparan di atas game (kanan bawah layar) untuk notifikasi trofi saat bermain.
    // Tidak bisa diklik dan tidak mengambil fokus dari game.
    class TrophyOverlayWindow : Window
    {
        [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr h, int i);
        [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr h, int i, int v);
        const int GWL_EXSTYLE = -20, WS_EX_TRANSPARENT = 0x20, WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x08000000;

        public Surface Surf;

        public TrophyOverlayWindow(Action<DrawingContext> draw)
        {
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            Topmost = true;
            ShowInTaskbar = false;
            ShowActivated = false;
            ResizeMode = ResizeMode.NoResize;
            Focusable = false;
            Title = "ZMB Trophy";
            Surf = new Surface(draw);
            Content = Surf;
            SourceInitialized += delegate
            {
                try
                {
                    IntPtr h = new WindowInteropHelper(this).Handle;
                    SetWindowLong(h, GWL_EXSTYLE, GetWindowLong(h, GWL_EXSTYLE) | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);
                }
                catch { }
            };
        }
    }

    public partial class ZmbWindow
    {
        TrophyOverlayWindow trophyWin;
        List<TrophyPopup> overlayPopups = new List<TrophyPopup>();
        DispatcherTimer overlayTimer, achvPollTimer;
        double overlayScale = 1;
        bool achvPollBusy;

        void ShowOverlayPopup(TrophyPopup p)
        {
            overlayPopups.Add(p);
            try
            {
                if (trophyWin == null)
                {
                    trophyWin = new TrophyOverlayWindow(DrawOverlay);
                }
                // ukur ulang tiap kali (resolusi bisa berubah saat game jalan)
                double sw = SystemParameters.PrimaryScreenWidth, sh = SystemParameters.PrimaryScreenHeight;
                overlayScale = Math.Max(0.75, Math.Min(2.0, sh / 1080.0));
                double ww = 540 * overlayScale, wh = 150 * overlayScale;
                trophyWin.Width = ww; trophyWin.Height = wh;
                trophyWin.Left = sw - ww - 24 * overlayScale;
                trophyWin.Top = sh - wh - 40 * overlayScale;
                if (!trophyWin.IsVisible) trophyWin.Show();
                trophyWin.Topmost = false; trophyWin.Topmost = true; // naik lagi ke atas game
                if (overlayTimer == null)
                {
                    overlayTimer = new DispatcherTimer(DispatcherPriority.Render);
                    overlayTimer.Interval = TimeSpan.FromMilliseconds(15);
                    overlayTimer.Tick += delegate { OverlayTick(); };
                }
                overlayTimer.Start();
            }
            catch (Exception ex)
            {
                Log(Lang.T("Overlay trofi: ") + ex);
                overlayPopups.Remove(p);
                p.Overlay = false;
                popups.Add(p);
            }
        }

        void OverlayTick()
        {
            if (overlayPopups.Count == 0)
            {
                overlayTimer.Stop();
                if (trophyWin != null) trophyWin.Hide();
                return;
            }
            if (trophyWin != null) trophyWin.Surf.InvalidateVisual();
        }

        void DrawOverlay(DrawingContext dc)
        {
            if (overlayPopups.Count == 0 || trophyWin == null) return;
            TrophyPopup p = overlayPopups[0];
            double now = T;
            if (p.Start < 0) { p.Start = now; Sfx.Trophy(); }
            double age = now - p.Start;
            if (age > TrophyDur) { overlayPopups.RemoveAt(0); return; }
            double s = overlayScale;
            double w = 460 * s, h = 104 * s;
            DrawTrophyCard(dc, p, trophyWin.Width - w - 16 * s, trophyWin.Height - h - 18 * s, s, age);
        }

        // ===== Pantau trofi baru selama game berjalan =====
        void StartAchvPolling()
        {
            StopAchvPolling();
            if (!cfg.TrophyOverlay || watchSrc == null) return;
            double secs = 30;
            if (watchSrc.Key.StartsWith("rpcs3:")) secs = 6;       // file lokal, murah
            else if (watchSrc.Key.StartsWith("steam:")) secs = 15;
            achvPollTimer = new DispatcherTimer();
            achvPollTimer.Interval = TimeSpan.FromSeconds(secs);
            achvPollTimer.Tick += delegate { AchvPollTick(); };
            achvPollTimer.Start();
        }

        void StopAchvPolling()
        {
            if (achvPollTimer != null) { achvPollTimer.Stop(); achvPollTimer = null; }
            achvPollBusy = false;
        }

        void AchvPollTick()
        {
            if (watchSrc == null) { StopAchvPolling(); return; }
            if (watchBefore < 0 || achvPollBusy) return;
            AchvSrc w = watchSrc;
            achvPollBusy = true;
            InvalidateAsync(w.Key);
            GetAsync<AchvSet>(w.Key, w.Fetch, 0, delegate (AchvSet r)
            {
                achvPollBusy = false;
                if (watchSrc != w || r == null || r.Error != null || watchIds == null) return;
                List<Achv> fresh = r.Items.Where(a => a.Unlocked && !watchIds.Contains(a.Id)).OrderBy(a => a.When).ToList();
                if (fresh.Count == 0) return;
                int before = watchBefore;
                foreach (Achv a in fresh) watchIds.Add(a.Id);
                watchBefore = r.Unlocked;
                AnnounceTrophies(fresh, r, before, true);
            }, false);
        }

        // Tampilkan trofi baru (+ platinum kalau semua sudah terbuka)
        void AnnounceTrophies(List<Achv> fresh, AchvSet r, int before, bool overlay)
        {
            bool hasPlat = false;
            foreach (Achv a in fresh.Take(8))
            {
                EnqueueTrophyPopup(a, r, overlay);
                if (a.Grade == "P") hasPlat = true;
            }
            if (!hasPlat && r.Total > 0 && r.Unlocked == r.Total && before < r.Total && r.Source != "RPCS3")
                EnqueuePlatinum(r, overlay);
        }
    }
}
