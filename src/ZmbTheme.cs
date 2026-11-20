using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ZMB
{
    public partial class ZmbWindow
    {
        // ===================== PAKET TEMA =====================
        Dictionary<string, ImageSource> themeIcons = new Dictionary<string, ImageSource>();
        Color? themePackColor = null;
        string themeBgPath = null;

        string ThemeDir()
        {
            if (!string.IsNullOrEmpty(cfg.ThemePack))
            {
                string d = Path.Combine(Path.Combine(Config.BaseDir, "themes"), cfg.ThemePack);
                if (Directory.Exists(d)) return d;
            }
            string def = Path.Combine(Config.BaseDir, "theme");
            return Directory.Exists(def) ? def : null;
        }

        static readonly string[] CatKeys = { "user", "settings", "photo", "music", "video", "game" };

        void ApplyThemePack()
        {
            themeIcons.Clear();
            themePackColor = null;
            themeBgPath = null;
            Sfx.ResetCustom();
            string dir = ThemeDir();
            if (dir == null) { backKey = ""; return; }
            string icons = Path.Combine(dir, "icons");
            foreach (string k in CatKeys)
            {
                foreach (string e in new string[] { ".png", ".jpg" })
                {
                    string f = Path.Combine(icons, k + e);
                    if (!File.Exists(f)) continue;
                    BitmapSource bs = ImgConv.LoadFile(f, 256);
                    if (bs != null) { themeIcons[k] = bs; break; }
                }
            }
            Sfx.LoadCustom(Path.Combine(dir, "sounds"));
            foreach (string e in new string[] { "background.jpg", "background.png" })
                if (File.Exists(Path.Combine(dir, e))) { themeBgPath = Path.Combine(dir, e); break; }
            string txt = Path.Combine(dir, "theme.txt");
            if (File.Exists(txt))
            {
                try
                {
                    foreach (string line in File.ReadAllLines(txt))
                    {
                        string[] kv = line.Split(new char[] { '=' }, 2);
                        if (kv.Length != 2) continue;
                        string key = kv[0].Trim().ToLowerInvariant(), val = kv[1].Trim();
                        if (key == "color") { try { themePackColor = (Color)ColorConverter.ConvertFromString(val); } catch { } }
                        else if (key == "waves") cfg.WaveStyle = val.ToLowerInvariant().StartsWith("line") ? 1 : 0;
                        else if (key == "particles") cfg.Particles = val.ToLowerInvariant() == "snow" ? 2 : val.ToLowerInvariant() == "off" ? 3 : 1;
                    }
                }
                catch { }
            }
            backKey = "";
            backImg = null;
        }

        void EnsureThemeFolder()
        {
            string d = Path.Combine(Config.BaseDir, "theme");
            try
            {
                Directory.CreateDirectory(Path.Combine(d, "icons"));
                Directory.CreateDirectory(Path.Combine(d, "sounds"));
                string readme = Path.Combine(d, "BACA_SAYA.txt");
                if (!File.Exists(readme))
                    File.WriteAllText(readme,
                        "TEMA KUSTOM ZMB\r\n\r\n" +
                        "icons\\   -> ikon kategori PNG transparan (256x256): user.png, settings.png, photo.png, music.png, video.png, game.png\r\n" +
                        "sounds\\  -> suara WAV: tick.wav (geser), ok.wav (pilih), back.wav (kembali), error.wav, boot.wav (pembuka), trophy.wav (trofi)\r\n" +
                        "background.jpg -> gambar latar menu\r\n" +
                        "theme.txt -> pengaturan, contoh:\r\n   color=#1A4FA0\r\n   waves=lines        (classic / lines)\r\n   particles=snow     (sparkle / snow / off)\r\n\r\n" +
                        "Paket tema lain: buat folder di  themes\\NamaTema\\  dengan isi yang sama, lalu pilih di Pengaturan > Paket Tema.\r\n");
            }
            catch { }
            ShowInExplorer(d);
        }

        List<string> ThemePacks()
        {
            List<string> l = new List<string>();
            try
            {
                string d = Path.Combine(Config.BaseDir, "themes");
                if (Directory.Exists(d)) foreach (string x in Directory.GetDirectories(d)) l.Add(Path.GetFileName(x));
            }
            catch { }
            return l;
        }

        bool SnowActive()
        {
            if (cfg.Particles == 2) return true;
            if (cfg.Particles == 0) return DateTime.Now.Month == 12 || (DateTime.Now.Month == 1 && DateTime.Now.Day <= 6);
            return false;
        }

        void DrawSnow(DrawingContext dc, double W, double H, double t, double alpha)
        {
            for (int i = 0; i < 90; i++)
            {
                double seed = (i * 7919 % 1000) / 1000.0, seed2 = (i * 104729 % 1000) / 1000.0, seed3 = (i * 15485863 % 1000) / 1000.0;
                double speed = 0.03 + seed2 * 0.05;
                double y = ((seed3 + t * speed) % 1.1 - 0.05) * H;
                double x = seed * W + Math.Sin(t * (0.4 + seed2) + i) * 26 * S;
                double r = (1.2 + 3.2 * seed2) * S;
                dc.DrawEllipse(WhiteA(alpha * (0.35 + 0.5 * seed)), null, new Point(x, y), r, r);
            }
        }

        // ===================== SCREENSAVER =====================
        double lastInputAt = 0;
        bool saverOn = false;
        double saverStart = 0;
        List<KeyValuePair<string, string>> saverItems = new List<KeyValuePair<string, string>>(); // path, judul
        Point lastMouse = new Point(-1, -1);

        void NoteInput() { lastInputAt = T; }

        void UpdateScreensaver(double now)
        {
            if (Gamepad.CurrentButtons != 0) lastInputAt = now;
            if (saverOn) return;
            if (cfg.ScreensaverMin <= 0 || !IsActive || mode != Mode.Xmb || optOpen || confirmOpen || booting) return;
            if (videoFull.Visibility == Visibility.Visible) return;
            if (now - lastInputAt < cfg.ScreensaverMin * 60) return;
            StartScreensaver();
        }

        void StartScreensaver()
        {
            saverItems.Clear();
            Random rnd = new Random();
            List<GameEntry> games = cfg.AllKnown().ToList();
            if (cfg.ShowSteam)
                foreach (SteamGame sg in GameScanner.SteamGames(false).Take(80))
                    games.Add(cfg.GetMeta("steam://rungameid/" + sg.AppId, "STEAM", sg.Name));
            foreach (GameEntry g in games.OrderBy(x => rnd.Next()).Take(80))
            {
                bool crisp;
                string bg = null;
                try { bg = GameScanner.FindBackground(g, cfg.FindPlatform(g.Platform), out crisp); } catch { crisp = false; }
                if (bg != null && crisp) saverItems.Add(new KeyValuePair<string, string>(bg, g.Name));
                if (saverItems.Count >= 40) break;
            }
            if (saverItems.Count < 3)
                foreach (string f in MediaExt.Files(cfg.PhotoFolder, MediaExt.Photo, true).OrderBy(x => rnd.Next()).Take(40))
                    saverItems.Add(new KeyValuePair<string, string>(f, ""));
            saverOn = true;
            saverStart = T;
            StopTrailer(true);
            StopThemeMusic();
        }

        void ExitScreensaver()
        {
            saverOn = false;
            lastInputAt = T;
        }

        void DrawScreensaver(DrawingContext dc, double W, double H)
        {
            double now = T;
            double age = now - saverStart;
            double a = Math.Min(1, age / 1.5);
            dc.PushOpacity(a);
            dc.DrawRectangle(brBlack, null, new Rect(0, 0, W, H));
            if (saverItems.Count > 0)
            {
                double slide = 9.0;
                int idx = (int)(age / slide);
                double local = age - idx * slide;
                for (int pass = 0; pass < 2; pass++)
                {
                    int ii = pass == 0 ? idx - 1 : idx;
                    if (ii < 0) continue;
                    double lt = pass == 0 ? local + slide : local;
                    double op = pass == 0 ? Math.Max(0, 1 - local / 1.5) : Math.Min(1, local / 1.5);
                    if (op <= 0.01) continue;
                    KeyValuePair<string, string> item = saverItems[ii % saverItems.Count];
                    string p = item.Key;
                    ImageSource img = GetImage("saver:" + p, delegate { return ImgConv.LoadFile(p, 1920); });
                    string nextP = saverItems[(ii + 1) % saverItems.Count].Key;
                    GetImage("saver:" + nextP, delegate { return ImgConv.LoadFile(nextP, 1920); });
                    if (img == null) continue;
                    // efek Ken Burns: zoom & geser pelan
                    double z = 1.04 + 0.10 * (lt / (slide + 1.5));
                    double dir = (ii % 2 == 0) ? 1 : -1;
                    double px = dir * (lt / slide - 0.5) * W * 0.04;
                    dc.PushOpacity(op);
                    dc.PushTransform(new MatrixTransform(z, 0, 0, z, W / 2 - W / 2 * z + px, H / 2 - H / 2 * z));
                    DrawImageFill(dc, img, new Rect(0, 0, W, H));
                    dc.Pop();
                    dc.Pop();
                    if (pass == 1 && item.Value.Length > 0)
                        Txt(dc, item.Value, 30 * S, 2, 70 * S, H - 110 * S, Math.Min(op, 0.9), W * 0.6, 0, true);
                }
                dc.DrawRectangle(BlackA(0.25), null, new Rect(0, 0, W, H));
            }
            Txt(dc, DateTime.Now.ToString(cfg.Clock24 ? "HH:mm" : "h:mm tt"), 64 * S, 1, W - 70 * S, H - 140 * S, 0.85, 0, 2, true);
            Txt(dc, DateTime.Now.ToString("dddd, dd MMMM"), 20 * S, 0, W - 70 * S, H - 60 * S, 0.7, 0, 2, true);
            dc.Pop();
        }

        // ===================== MUSIK TEMA GAME (ala SND0) =====================
        MediaPlayer themePlayer = null;
        bool themeTried = false;
        bool themePlaying = false;
        double themeStartAt = 0;
        bool themeDucked = false;

        string FindThemeMusic(GameEntry g)
        {
            if (g == null) return null;
            if (!string.IsNullOrEmpty(g.ThemeMusic) && File.Exists(g.ThemeMusic)) return g.ThemeMusic;
            string[] exts = { ".mp3", ".wma", ".wav", ".m4a" };
            string key = g.Platform == "STEAM" ? g.Path.Substring(g.Path.LastIndexOf('/') + 1) :
                         g.Platform == "WIN" ? GameScanner.SafeName(g.Name) : GameScanner.RomKey(g.Path);
            List<string> dirs = new List<string>();
            dirs.Add(Path.Combine(Path.Combine(Config.BaseDir, "music"), g.Platform));
            try
            {
                if (g.Platform == "WIN")
                {
                    string gd = Path.GetDirectoryName(g.Path);
                    foreach (string e in exts) { string f = Path.Combine(gd, "theme" + e); if (File.Exists(f)) return f; }
                }
                else if (g.Platform != "STEAM")
                {
                    string rd = Path.GetDirectoryName(g.Path);
                    dirs.Add(Path.Combine(rd, "music"));
                    dirs.Add(Path.Combine(rd, "themes"));
                }
            }
            catch { }
            foreach (string d in dirs)
                foreach (string e in exts)
                {
                    string f = Path.Combine(d, key + e);
                    if (File.Exists(f)) return f;
                }
            return null;
        }

        void UpdateDuck()
        {
            music.SetDuck(trailerDucked || themeDucked);
        }

        void StartThemeMusic(string file)
        {
            try
            {
                if (themePlayer == null)
                {
                    themePlayer = new MediaPlayer();
                    themePlayer.MediaEnded += delegate { try { themePlayer.Position = TimeSpan.Zero; themePlayer.Play(); } catch { } };
                    themePlayer.MediaFailed += delegate { StopThemeMusic(); };
                }
                themePlayer.Open(new Uri(file, UriKind.Absolute));
                themePlayer.Volume = 0;
                themePlayer.Play();
                themePlaying = true;
                themeStartAt = T;
                if (music.Playing && !music.Paused) { themeDucked = true; UpdateDuck(); }
            }
            catch { themePlaying = false; }
        }

        void StopThemeMusic()
        {
            if (themePlayer != null && themePlaying)
            {
                try { themePlayer.Stop(); themePlayer.Close(); } catch { }
            }
            themePlaying = false;
            if (themeDucked) { themeDucked = false; UpdateDuck(); }
        }

        void UpdateThemeMusic(GameEntry g, double now, bool focusChanged)
        {
            if (focusChanged) { StopThemeMusic(); themeTried = false; }
            if (themePlaying && themePlayer != null)
            {
                double target = cfg.Volume / 100.0 * 0.85;
                double v = Math.Min(target, target * (now - themeStartAt) / 1.2);
                if (Math.Abs(themePlayer.Volume - v) > 0.01) themePlayer.Volume = v;
                if (trailerActive && cfg.TrailerSound) StopThemeMusic(); // trailer bersuara lebih diutamakan
            }
            if (g == null || themeTried || !cfg.ThemeMusicOn || now - focusSince < 0.9) return;
            themeTried = true;
            if (trailerActive && cfg.TrailerSound) return;
            string f = FindThemeMusic(g);
            if (f != null) StartThemeMusic(f);
        }
    }
}
