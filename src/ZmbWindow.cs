using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;

namespace ZMB
{
    public class OptionItem
    {
        public string Text;
        public Action Act;
        public bool Checked;
        public OptionItem(string text, Action act) { Text = text; Act = act; }
        public OptionItem(string text, Action act, bool chk) { Text = text; Act = act; Checked = chk; }
    }

    public class XmbItem
    {
        public string Title = "";
        public string Subtitle = "";
        public Func<string> DynSub;
        public string Glyph;
        public string Badge;
        public Color BadgeColor = Color.FromRgb(50, 80, 150);
        public string ImageKey;
        public Func<ImageSource> ImageLoader;
        public Action Activate;
        public Func<List<XmbItem>> Children;
        public Func<List<OptionItem>> Options;
        public GameEntry Game;
        public double Progress = -1;
        public bool Dim;
        public Achv Ach;
        public AchvSet AchSet;
        public string AchKey;
        public string Kind;          // game / music / video / photo
        public string BoxKey;        // gambar untuk mode Coverflow
        public Func<ImageSource> BoxLoader;
        public bool BoxIsIcon;

        public string Sub
        {
            get
            {
                if (DynSub != null) { try { return DynSub(); } catch { return ""; } }
                return Subtitle;
            }
        }
    }

    public class Category
    {
        public string Title;
        public string Glyph;
        public string IconKey;
        public Func<List<XmbItem>> Build;
        public List<XmbItem> Items = new List<XmbItem>();
        public int Index;
        public double Anim;
        public Category(string t, string g, Func<List<XmbItem>> b) { Title = t; Glyph = g; Build = b; }
    }

    public class Level
    {
        public string Title;
        public XmbItem Parent;
        public Func<List<XmbItem>> Source;
        public List<XmbItem> Items = new List<XmbItem>();
        public int Index;
        public double Anim;
    }

    enum Mode { Xmb, Video, Photo, Profile, Details }

    public partial class ZmbWindow : Window
    {
        Config cfg;
        MusicPlayer music = new MusicPlayer();
        List<Category> cats = new List<Category>();
        int catIndex;
        double catAnim;
        List<Level> stack = new List<Level>();
        double folderAnim = 0, levelSlide = 0;
        Mode mode = Mode.Xmb;

        bool optOpen = false;
        double optAnim = 0;
        List<OptionItem> opts = new List<OptionItem>();
        int optIndex = 0;
        double optScroll = 0;
        string optTitle = "";

        bool confirmOpen = false;
        string confirmText = "";
        Action confirmYes;
        int confirmIdx = 1;

        string toastText = null;
        double toastAt = -10;

        bool booting = false;
        double bootStart = 0;

        // lapisan tampilan
        Grid root;
        Surface surfBack, surfMain, surfTop;
        Image bgA, bgB;
        bool bgFrontA = true;
        double bgAOp = 0, bgBOp = 0, bgATarget = 0, bgBTarget = 0;
        MediaElement trailer;
        double trailerOp = 0, trailerTarget = 0;
        bool trailerActive = false;
        MediaElement videoFull;

        // latar game
        string focusKey = null;
        double focusSince = 0;
        string bgShownKey = null;
        bool bgRequested = false;
        bool trailerTried = false;
        bool trailerDucked = false;

        // video layar penuh
        List<string> videoList = new List<string>();
        int videoIdx = 0;
        bool videoPaused = false;
        double osdUntil = 0;
        bool pausedForVideo = false;

        // foto
        List<string> photos = new List<string>();
        int photoIdx = 0;
        ImageSource photoImg = null;
        bool slideshow = false;
        double slideAt = 0, photoShownAt = 0;

        // game berjalan
        Process runningProc = null;
        GameEntry untrackedGame = null;
        DateTime untrackedStart;
        GameEntry runningGame = null;
        DateTime runningStart;
        bool pausedForGame = false;
        double launchMinimizeAt = 0, launchedAt = -100, homeHoldStart = -1;

        int musicPos = 0, musicLen = 0;
        double musicPollAt = 0;

        Stopwatch clock = Stopwatch.StartNew();
        TimeSpan lastRenderTime = TimeSpan.Zero;
        double lastFrame = 0;
        DispatcherTimer bgTimer;
        string backKey = "";

        // cache gambar (UI thread)
        Dictionary<string, ImageSource> imgCache = new Dictionary<string, ImageSource>();
        Queue<string> imgOrder = new Queue<string>();
        HashSet<string> imgPending = new HashSet<string>();
        HashSet<string> imgFailed = new HashSet<string>();
        List<KeyValuePair<string, Func<ImageSource>>> jobs = new List<KeyValuePair<string, Func<ImageSource>>>();
        object jobLock = new object();
        AutoResetEvent jobEvt = new AutoResetEvent(false);
        ConcurrentQueue<KeyValuePair<string, ImageSource>> jobDone = new ConcurrentQueue<KeyValuePair<string, ImageSource>>();

        double T { get { return clock.Elapsed.TotalSeconds; } }

        public ZmbWindow()
        {
            cfg = Config.Load();
            Sfx.Init();
            Sfx.Enabled = cfg.Sounds;
            music.Shuffle = cfg.Shuffle;
            music.Repeat = cfg.Repeat;
            music.Volume = cfg.Volume;
            music.OnMessage = delegate (string s) { ShowToast(s); };
            Surface.OnError = delegate (Exception ex) { LogOnce(ex.ToString()); };

            Title = "ZMB";
            Background = Brushes.Black;
            UseLayoutRounding = false;
            try { Icon = ImgConv.FromDrawing(System.Drawing.Icon.ExtractAssociatedIcon(System.Reflection.Assembly.GetEntryAssembly().Location).ToBitmap()); } catch { }

            root = new Grid();
            root.Background = Brushes.Black;
            Content = root;

            surfBack = new Surface(DrawBack);
            root.Children.Add(surfBack);

            bgA = MakeBgImage(); root.Children.Add(bgA);
            bgB = MakeBgImage(); root.Children.Add(bgB);

            trailer = new MediaElement();
            trailer.LoadedBehavior = MediaState.Manual;
            trailer.UnloadedBehavior = MediaState.Manual;
            trailer.Stretch = Stretch.UniformToFill;
            trailer.Opacity = 0;
            trailer.IsHitTestVisible = false;
            trailer.MediaOpened += delegate { trailerTarget = 1; };
            trailer.MediaEnded += delegate { try { trailer.Position = TimeSpan.Zero; trailer.Play(); } catch { } };
            trailer.MediaFailed += delegate { StopTrailer(true); };
            root.Children.Add(trailer);

            surfMain = new Surface(DrawMain);
            root.Children.Add(surfMain);

            videoFull = new MediaElement();
            videoFull.LoadedBehavior = MediaState.Manual;
            videoFull.UnloadedBehavior = MediaState.Manual;
            videoFull.Stretch = Stretch.Uniform;
            videoFull.Visibility = Visibility.Collapsed;
            videoFull.IsHitTestVisible = false;
            videoFull.MediaEnded += delegate
            {
                if (videoIdx + 1 < videoList.Count) StartVideoAt(videoIdx + 1);
                else CloseVideo();
            };
            videoFull.MediaFailed += delegate (object s, ExceptionRoutedEventArgs e)
            {
                string f = (videoIdx < videoList.Count) ? videoList[videoIdx] : null;
                CloseVideo();
                if (f != null)
                {
                    ShowToast("Format ini dibuka di pemutar video Windows...");
                    OpenDefault(f);
                }
            };
            root.Children.Add(videoFull);

            surfTop = new Surface(DrawTop);
            root.Children.Add(surfTop);

            InitRender();
            BuildCategories();
            ApplyThemePack();
            catIndex = Math.Max(0, Math.Min(cats.Count - 1, cfg.LastCategory));
            catAnim = catIndex;

            ApplyFullscreen();
            StartImageWorker();

            PreviewKeyDown += OnKeyDownZmb;
            MouseWheel += delegate (object s, MouseWheelEventArgs e)
            {
                if (cfg.ViewMode == 1 && mode == Mode.Xmb && !optOpen && !confirmOpen) HandleKey(e.Delta > 0 ? XKey.Left : XKey.Right);
                else HandleKey(e.Delta > 0 ? XKey.Up : XKey.Down);
            };
            MouseDown += OnMouseDownZmb;
            Activated += OnActivatedZmb;
            SourceInitialized += delegate { InitHotkey(); };
            ContentRendered += delegate { PreloadBootModel(); };
            MouseMove += delegate (object s, MouseEventArgs e)
            {
                Point mp = e.GetPosition(this);
                if (lastMouse.X >= 0 && (Math.Abs(mp.X - lastMouse.X) > 4 || Math.Abs(mp.Y - lastMouse.Y) > 4))
                {
                    lastInputAt = T;
                    if (saverOn) ExitScreensaver();
                }
                lastMouse = mp;
            };
            Deactivated += delegate { StopTrailer(true); };
            Closing += OnClosingZmb;

            booting = cfg.BootAnimation;
            bootStart = 0;
            if (booting) Sfx.Boot();

            CompositionTarget.Rendering += OnFrame;

            if (cfg.CheckUpdateDownloads)
            {
                DispatcherTimer ut = new DispatcherTimer();
                ut.Interval = TimeSpan.FromSeconds(6);
                ut.Tick += delegate { ut.Stop(); CheckDownloadsForUpdate(true); };
                ut.Start();
            }

            // timer ringan untuk gamepad saat launcher tidak aktif (minimize / game berjalan)
            bgTimer = new DispatcherTimer(DispatcherPriority.Background);
            bgTimer.Interval = TimeSpan.FromMilliseconds(60);
            bgTimer.Tick += delegate { if (!IsActive || WindowState == WindowState.Minimized) PollHomeCombo(); };
            bgTimer.Start();
        }

        Image MakeBgImage()
        {
            Image im = new Image();
            im.Stretch = Stretch.UniformToFill;
            im.Opacity = 0;
            im.IsHitTestVisible = false;
            im.HorizontalAlignment = HorizontalAlignment.Center;
            im.VerticalAlignment = VerticalAlignment.Center;
            RenderOptions.SetBitmapScalingMode(im, BitmapScalingMode.HighQuality);
            return im;
        }

        System.Windows.Forms.IWin32Window Owner32
        {
            get { return new Win32Owner(new WindowInteropHelper(this).Handle); }
        }

        // ===================== DASAR =====================
        void ApplyFullscreen()
        {
            if (cfg.Fullscreen)
            {
                WindowState = WindowState.Normal;
                WindowStyle = WindowStyle.None;
                ResizeMode = ResizeMode.NoResize;
                WindowState = WindowState.Maximized;
            }
            else
            {
                WindowState = WindowState.Normal;
                WindowStyle = WindowStyle.SingleBorderWindow;
                ResizeMode = ResizeMode.CanResize;
                Rect wa = SystemParameters.WorkArea;
                Width = Math.Min(1280, wa.Width - 40);
                Height = Math.Min(720, wa.Height - 40);
                Left = wa.Left + (wa.Width - Width) / 2;
                Top = wa.Top + (wa.Height - Height) / 2;
            }
        }

        public static void Log(string s)
        {
            try { File.AppendAllText(Path.Combine(Config.BaseDir, "zmb_error.log"), DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + s + Environment.NewLine); } catch { }
        }

        static HashSet<string> logged = new HashSet<string>();
        static void LogOnce(string s)
        {
            string k = s.Length > 200 ? s.Substring(0, 200) : s;
            if (logged.Contains(k)) return;
            logged.Add(k);
            Log(s);
        }

        void ShowToast(string s) { toastText = s; toastAt = T; }

        void Confirm(string text, Action yes)
        {
            confirmText = text; confirmYes = yes; confirmIdx = 1; confirmOpen = true;
            Sfx.Ok();
        }

        void OpenOptions(string title, List<OptionItem> list)
        {
            if (list == null || list.Count == 0) return;
            opts = list; optIndex = 0; optTitle = title; optOpen = true;
            for (int i = 0; i < list.Count; i++) if (list[i].Checked) { optIndex = i; break; }
            optScroll = optIndex;
            Sfx.Ok();
        }

        List<XmbItem> SafeBuild(Func<List<XmbItem>> f)
        {
            try
            {
                List<XmbItem> l = f();
                return l ?? new List<XmbItem>();
            }
            catch (Exception ex)
            {
                Log(ex.ToString());
                List<XmbItem> l = new List<XmbItem>();
                l.Add(MakeItem("Terjadi kesalahan", ex.Message, "\uE783", null));
                return l;
            }
        }

        XmbItem MakeItem(string title, string sub, string glyph, Action act)
        {
            XmbItem it = new XmbItem();
            it.Title = title; it.Subtitle = sub ?? ""; it.Glyph = glyph; it.Activate = act;
            return it;
        }

        XmbItem EmptyItem() { return MakeItem("Kosong", "Tidak ada item di sini", "\uE7BA", null); }

        void RefreshAll()
        {
            RefreshItemsOnly();
            spinePathCache.Clear();
            focusKey = null; // paksa latar dihitung ulang
        }

        void RefreshItemsOnly()
        {
            foreach (Category c in cats)
            {
                c.Items = SafeBuild(c.Build);
                c.Index = Math.Max(0, Math.Min(c.Items.Count - 1, c.Index));
            }
            foreach (Level l in stack)
            {
                l.Items = SafeBuild(l.Source);
                if (l.Items.Count == 0) l.Items.Add(EmptyItem());
                l.Index = Math.Max(0, Math.Min(l.Items.Count - 1, l.Index));
                l.Anim = Math.Min(l.Anim, l.Items.Count - 1);
            }
        }

        void SaveAndRefresh() { cfg.Save(); RefreshAll(); }

        // ===================== WORKER GAMBAR =====================
        void StartImageWorker()
        {
            Thread th = new Thread(ImageWorkerLoop);
            th.IsBackground = true;
            th.SetApartmentState(ApartmentState.STA);
            th.Priority = ThreadPriority.BelowNormal;
            th.Start();
        }

        void ImageWorkerLoop()
        {
            while (true)
            {
                jobEvt.WaitOne();
                while (true)
                {
                    KeyValuePair<string, Func<ImageSource>> job;
                    lock (jobLock)
                    {
                        if (jobs.Count == 0) break;
                        job = jobs[jobs.Count - 1];
                        jobs.RemoveAt(jobs.Count - 1);
                    }
                    ImageSource im = null;
                    try { im = job.Value(); if (im != null && im.CanFreeze && !im.IsFrozen) im.Freeze(); } catch { im = null; }
                    jobDone.Enqueue(new KeyValuePair<string, ImageSource>(job.Key, im));
                }
            }
        }

        void RequestImage(string key, Func<ImageSource> loader)
        {
            if (key == null || loader == null) return;
            if (imgPending.Contains(key) || imgFailed.Contains(key) || imgCache.ContainsKey(key)) return;
            imgPending.Add(key);
            lock (jobLock)
            {
                jobs.Add(new KeyValuePair<string, Func<ImageSource>>(key, loader));
                while (jobs.Count > 40)
                {
                    imgPending.Remove(jobs[0].Key);
                    jobs.RemoveAt(0);
                }
            }
            jobEvt.Set();
        }

        ImageSource GetImage(string key, Func<ImageSource> loader)
        {
            if (key == null) return null;
            ImageSource im;
            if (imgCache.TryGetValue(key, out im)) return im;
            RequestImage(key, loader);
            return null;
        }

        void CollectImages()
        {
            KeyValuePair<string, ImageSource> kv;
            while (jobDone.TryDequeue(out kv))
            {
                imgPending.Remove(kv.Key);
                if (kv.Value == null) { imgFailed.Add(kv.Key); continue; }
                if (imgCache.ContainsKey(kv.Key)) continue;
                imgCache[kv.Key] = kv.Value;
                imgOrder.Enqueue(kv.Key);
            }
            while (imgCache.Count > 300 && imgOrder.Count > 0) imgCache.Remove(imgOrder.Dequeue());
        }

        void ForgetImage(string key)
        {
            if (key == null) return;
            imgCache.Remove(key);
            imgFailed.Remove(key);
        }

        // ===================== LOOP UTAMA (tiap frame layar / vsync) =====================
        void OnFrame(object sender, EventArgs e)
        {
            RenderingEventArgs re = e as RenderingEventArgs;
            if (re != null)
            {
                if (re.RenderingTime == lastRenderTime) return;
                lastRenderTime = re.RenderingTime;
            }
            double now = T;
            double dt = Math.Min(0.1, Math.Max(0.0, now - lastFrame));
            lastFrame = now;
            if (booting && bootStart == 0) bootStart = now;

            CollectImages();

            bool focused = IsActive && WindowState != WindowState.Minimized;
            if (focused)
            {
                List<XKey> padKeys = Gamepad.Poll();
                if (padKeys.Count > 0) promptKeyboard = false;
                foreach (XKey k in padKeys) HandleKey(k);
            }

            double a = 1 - Math.Exp(-dt * 14);
            catAnim += (catIndex - catAnim) * a;
            foreach (Category c in cats) c.Anim += (c.Index - c.Anim) * a;
            foreach (Level l in stack) l.Anim += (l.Index - l.Anim) * a;
            folderAnim += ((stack.Count > 0 ? 1 : 0) - folderAnim) * a;
            levelSlide += (0 - levelSlide) * a;
            optAnim += ((optOpen ? 1 : 0) - optAnim) * (1 - Math.Exp(-dt * 18));
            if (optOpen) optScroll += (optIndex - optScroll) * a;

            if (booting && now - bootStart > 3.2) booting = false;

            if (now >= musicPollAt)
            {
                musicPollAt = now + 0.25;
                musicPos = music.PositionMs();
                musicLen = music.LengthMs();
                string bk = BackKey();
                if (bk != backKey) { backKey = bk; surfBack.InvalidateVisual(); }
            }

            if (launchMinimizeAt > 0 && now >= launchMinimizeAt)
            {
                launchMinimizeAt = 0;
                WindowState = WindowState.Minimized;
            }

            if (mode == Mode.Photo && slideshow && now - slideAt > cfg.SlideSeconds)
            {
                slideAt = now;
                ShowPhoto(photoIdx + 1);
            }

            UpdateGameBackground(now, dt);
            UpdateScreensaver(now);
            if (cfg.AutoUpdateOnline && !string.IsNullOrEmpty(cfg.UpdateRepo) && now > 8 && now - lastOnlineCheck > 6 * 3600 && mode == Mode.Xmb && IsActive && !confirmOpen && !booting)
                CheckOnlineUpdate(true);

            if (WindowState != WindowState.Minimized)
            {
                surfMain.InvalidateVisual();
                surfTop.InvalidateVisual();
            }
        }

        void PollHomeCombo()
        {
            Gamepad.Poll();
            double now = T;
            ushort combo = (ushort)(Gamepad.BACK | Gamepad.START);
            if ((Gamepad.CurrentButtons & combo) == combo)
            {
                if (homeHoldStart < 0) homeHoldStart = now;
                else if (now - homeHoldStart > 1.0) { homeHoldStart = double.MaxValue; OpenHomeMenu(); }
            }
            else homeHoldStart = -1;
        }

        void BringToFrontZmb()
        {
            if (WindowState == WindowState.Minimized) WindowState = cfg.Fullscreen ? WindowState.Maximized : WindowState.Normal;
            Show();
            Activate();
            Topmost = true; Topmost = false;
            try { Native.SetForegroundWindow(new WindowInteropHelper(this).Handle); } catch { }
        }

        void OnActivatedZmb(object sender, EventArgs e)
        {
            if (untrackedGame != null && runningProc == null)
            {
                double mins = (DateTime.Now - untrackedStart).TotalMinutes;
                if (mins > 1 && mins < 24 * 60)
                {
                    untrackedGame.Minutes += mins;
                    RecordSession(untrackedGame, mins, untrackedStart);
                    cfg.Save();
                }
                untrackedGame = null;
            }
            if (pausedForGame && runningProc == null && T - launchedAt > 5)
            {
                pausedForGame = false;
                music.Resume();
            }
            if (runningProc == null && watchSrc != null && T - launchedAt > 30) AchvWatchCheck();
            focusKey = null;
        }

        // ===================== LATAR GAME & TRAILER =====================
        GameEntry FocusedGame()
        {
            if (mode == Mode.Details && detGame != null) return detGame;
            if (mode != Mode.Xmb || booting || saverOn) return null;
            List<XmbItem> items; int idx;
            GetCurrent(out items, out idx);
            if (idx < 0 || idx >= items.Count) return null;
            if (stack.Count == 0 && Math.Abs(catIndex - catAnim) > 0.4) return null;
            return items[idx].Game;
        }

        void UpdateGameBackground(double now, double dt)
        {
            GameEntry g = (cfg.GameBackground || cfg.Trailers || cfg.ThemeMusicOn) && IsActive ? FocusedGame() : null;
            string key = g == null ? null : g.Platform + "|" + g.Path;
            bool focusChanged = key != focusKey;
            if (key != focusKey)
            {
                focusKey = key;
                focusSince = now;
                bgRequested = false;
                trailerTried = false;
                focusLogo = null;
                StopTrailer(false);
                if (key == null || !cfg.GameBackground) { bgATarget = 0; bgBTarget = 0; bgShownKey = null; }
            }

            // gambar latar muncul setelah pilihan diam sebentar (biar tidak berkedip saat scroll cepat)
            if (g != null && cfg.GameBackground && !bgRequested && now - focusSince > 0.28)
            {
                bgRequested = true;
                focusLogo = FindLogo(g);
                bool crisp;
                PlatformCfg p = cfg.FindPlatform(g.Platform);
                string path = null;
                try { path = GameScanner.FindBackground(g, p, out crisp); } catch { crisp = true; }
                if (path == null) { bgATarget = 0; bgBTarget = 0; bgShownKey = null; }
                else
                {
                    string ikey = "bg:" + path;
                    string want = key;
                    bool cr = crisp;
                    ImageSource cached;
                    if (imgCache.TryGetValue(ikey, out cached)) ShowBg(cached, cr);
                    else
                    {
                        string pth = path;
                        ThreadPool.QueueUserWorkItem(delegate
                        {
                            BitmapSource bs = ImgConv.LoadFile(pth, cr ? 1920 : 480);
                            Dispatcher.BeginInvoke((Action)delegate
                            {
                                if (bs == null) return;
                                imgCache[ikey] = bs; imgOrder.Enqueue(ikey);
                                if (focusKey == want) ShowBg(bs, cr);
                            });
                        });
                    }
                }
            }

            // trailer setelah jeda
            if (g != null && cfg.Trailers && !trailerTried && now - focusSince > cfg.TrailerDelay)
            {
                trailerTried = true;
                PlatformCfg p = cfg.FindPlatform(g.Platform);
                string tr = null;
                try { tr = GameScanner.FindTrailer(g, p); } catch { }
                if (tr != null) StartTrailer(tr);
                else if (g.Platform == "STEAM" && cfg.OnlineTrailers)
                {
                    string appId = g.Path.Substring(g.Path.LastIndexOf('/') + 1);
                    string want = key;
                    string cachedUrl;
                    if (GameScanner.TryCachedSteamTrailer(appId, out cachedUrl)) { if (cachedUrl != null) StartTrailer(cachedUrl); }
                    else
                    {
                        ThreadPool.QueueUserWorkItem(delegate
                        {
                            string url = GameScanner.FetchSteamTrailer(appId);
                            Dispatcher.BeginInvoke((Action)delegate
                            {
                                if (url != null && focusKey == want && cfg.Trailers) StartTrailer(url);
                            });
                        });
                    }
                }
            }

            UpdateThemeMusic(g, now, focusChanged);

            // animasi crossfade
            double fa = 1 - Math.Exp(-dt * 5.5);
            bgAOp += (bgATarget - bgAOp) * fa;
            bgBOp += (bgBTarget - bgBOp) * fa;
            double tt = trailerActive ? trailerTarget : 0;
            trailerOp += (tt - trailerOp) * (1 - Math.Exp(-dt * (tt > trailerOp ? 3.0 : 8.0)));
            if (Math.Abs(bgA.Opacity - bgAOp) > 0.002) bgA.Opacity = bgAOp;
            if (Math.Abs(bgB.Opacity - bgBOp) > 0.002) bgB.Opacity = bgBOp;
            if (Math.Abs(trailer.Opacity - trailerOp) > 0.002) trailer.Opacity = trailerOp;
            if (bgAOp < 0.003 && bgATarget == 0 && bgA.Source != null) bgA.Source = null;
            if (bgBOp < 0.003 && bgBTarget == 0 && bgB.Source != null) bgB.Source = null;
            if (!trailerActive && trailerOp < 0.01 && trailer.Source != null)
            {
                try { trailer.Stop(); trailer.Close(); } catch { }
                trailer.Source = null;
            }
        }

        double GameBgVisibility()
        {
            return Math.Max(Math.Max(bgAOp, bgBOp), trailerOp);
        }

        void ShowBg(ImageSource src, bool crisp)
        {
            Image front = bgFrontA ? bgB : bgA; // gambar baru masuk ke lapisan yang sedang tersembunyi
            front.Source = src;
            front.Effect = crisp ? null : new BlurEffect { Radius = 38, KernelType = KernelType.Gaussian, RenderingBias = RenderingBias.Performance };
            front.Margin = crisp ? new Thickness(0) : new Thickness(-60);
            bgFrontA = !bgFrontA;
            if (bgFrontA) { bgATarget = 1; bgBTarget = 0; } else { bgBTarget = 1; bgATarget = 0; }
            Panel.SetZIndex(bgA, bgFrontA ? 2 : 1);
            Panel.SetZIndex(bgB, bgFrontA ? 1 : 2);
            Panel.SetZIndex(trailer, 3);
            Panel.SetZIndex(surfMain, 4);
            Panel.SetZIndex(videoFull, 5);
            Panel.SetZIndex(surfTop, 6);
        }

        void StartTrailer(string src)
        {
            try
            {
                Uri u = new Uri(src, UriKind.Absolute);
                bool musicOn = music.Playing && !music.Paused;
                trailer.IsMuted = !cfg.TrailerSound;
                trailer.Volume = cfg.Volume / 100.0;
                if (cfg.TrailerSound && musicOn) { trailerDucked = true; UpdateDuck(); }
                if (cfg.TrailerSound) StopThemeMusic();
                trailerTarget = 0; // jadi 1 saat MediaOpened
                trailer.Source = u;
                trailer.Play();
                trailerActive = true;
            }
            catch (Exception ex) { LogOnce("Trailer: " + ex.Message); trailerActive = false; }
        }

        void StopTrailer(bool hard)
        {
            if (trailerActive)
            {
                trailerActive = false;
                try { trailer.IsMuted = true; } catch { }
            }
            if (trailerDucked) { trailerDucked = false; UpdateDuck(); }
            if (hard)
            {
                try { trailer.Stop(); trailer.Close(); } catch { }
                trailer.Source = null;
                trailerOp = 0; trailer.Opacity = 0;
            }
        }

        // ===================== INPUT =====================
        void OnKeyDownZmb(object sender, KeyEventArgs e)
        {
            Key key = e.Key == Key.System ? e.SystemKey : e.Key;
            if ((Keyboard.Modifiers & (ModifierKeys.Alt | ModifierKeys.Control)) != 0) return;
            if (key == Key.F11)
            {
                cfg.Fullscreen = !cfg.Fullscreen; ApplyFullscreen(); cfg.Save();
                e.Handled = true; return;
            }
            if (key == Key.F2)
            {
                ToggleViewMode();
                e.Handled = true; return;
            }
            if (key == Key.F5)
            {
                GameScanner.SteamGames(true);
                RefreshAll(); ShowToast("Daftar diperbarui");
                e.Handled = true; return;
            }
            promptKeyboard = true;
            if (e.IsRepeat && (bootActive || bootLayer != null)) { e.Handled = true; return; }
            XKey k = MapKey(key);
            if (k != XKey.None) { HandleKey(k); e.Handled = true; }
        }

        static XKey MapKey(Key k)
        {
            switch (k)
            {
                case Key.Up: case Key.W: return XKey.Up;
                case Key.Down: case Key.S: return XKey.Down;
                case Key.Left: case Key.A: return XKey.Left;
                case Key.Right: case Key.D: return XKey.Right;
                case Key.Enter: case Key.X: return XKey.Ok;
                case Key.Escape: case Key.Back: case Key.C: return XKey.Back;
                case Key.Tab: case Key.Apps: case Key.T: return XKey.Options;
                case Key.I: return XKey.Select;
                case Key.Space: case Key.MediaPlayPause: return XKey.Start;
                case Key.MediaNextTrack: case Key.OemCloseBrackets: return XKey.NextTrack;
                case Key.MediaPreviousTrack: case Key.OemOpenBrackets: return XKey.PrevTrack;
                case Key.PageUp: return XKey.PageUp;
                case Key.PageDown: return XKey.PageDown;
            }
            return XKey.None;
        }

        void OnMouseDownZmb(object sender, MouseButtonEventArgs e)
        {
            promptKeyboard = true;
            if (e.ChangedButton == MouseButton.Left) HandleKey(mode == Mode.Photo ? XKey.Right : XKey.Ok);
            else if (e.ChangedButton == MouseButton.Right) HandleKey(mode == Mode.Video ? XKey.Back : XKey.Options);
            else if (e.ChangedButton == MouseButton.XButton1 || e.ChangedButton == MouseButton.Middle) HandleKey(XKey.Back);
        }

        void HandleKey(XKey k)
        {
            if (saverOn) { ExitScreensaver(); return; }
            lastInputAt = T;
            if (BootConsumeKey()) return;
            try { HandleKeyInner(k); }
            catch (Exception ex) { Log(ex.ToString()); ShowToast("Error: " + ex.Message); }
        }

        bool HandleOptionsKey(XKey k)
        {
            if (!optOpen) return false;
            if (k == XKey.Up && optIndex > 0) { optIndex--; Sfx.Tick(); }
            else if (k == XKey.Down && optIndex < opts.Count - 1) { optIndex++; Sfx.Tick(); }
            else if (k == XKey.PageUp) { optIndex = Math.Max(0, optIndex - 6); Sfx.Tick(); }
            else if (k == XKey.PageDown) { optIndex = Math.Min(opts.Count - 1, optIndex + 6); Sfx.Tick(); }
            else if (k == XKey.Ok)
            {
                optOpen = false;
                OptionItem o = opts[optIndex];
                Sfx.Ok();
                if (o.Act != null) o.Act();
            }
            else if (k == XKey.Back || k == XKey.Options || k == XKey.Left) { optOpen = false; Sfx.Back(); }
            return true;
        }

        void HandleKeyInner(XKey k)
        {
            if (booting)
            {
                if (k == XKey.Ok || k == XKey.Back || k == XKey.Start) booting = false;
                return;
            }
            lastNavAt = T;
            if (confirmOpen)
            {
                if (k == XKey.Left || k == XKey.Right || k == XKey.Up || k == XKey.Down) { confirmIdx = 1 - confirmIdx; Sfx.Tick(); }
                else if (k == XKey.Ok)
                {
                    confirmOpen = false;
                    if (confirmIdx == 0 && confirmYes != null) { Sfx.Ok(); confirmYes(); }
                    else Sfx.Back();
                }
                else if (k == XKey.Back) { confirmOpen = false; Sfx.Back(); }
                return;
            }
            if (mode == Mode.Video) { HandleVideoKey(k); return; }
            if (mode == Mode.Photo) { HandlePhotoKey(k); return; }
            if (mode == Mode.Profile) { HandleProfileKey(k); return; }
            if (mode == Mode.Details) { HandleDetailsKey(k); return; }

            if (HandleOptionsKey(k)) return;

            if (k == XKey.Start) { music.TogglePause(); ShowMusicToast(); return; }
            if (k == XKey.NextTrack) { if (music.Queue.Count > 0) { music.Next(); ShowMusicToast(); } return; }
            if (k == XKey.PrevTrack) { if (music.Queue.Count > 0) { music.Prev(); ShowMusicToast(); } return; }

            if (cfg.ViewMode == 1)
            {
                // Coverflow: kiri/kanan = pindah game, atas/bawah = kategori (atau kembali di dalam folder)
                if (k == XKey.Up)
                {
                    if (stack.Count > 0) PopLevel();
                    else if (catIndex > 0) { catIndex--; Sfx.Tick(); }
                    cfg.LastCategory = catIndex;
                    return;
                }
                if (k == XKey.Down)
                {
                    if (stack.Count == 0 && catIndex < cats.Count - 1) { catIndex++; Sfx.Tick(); }
                    cfg.LastCategory = catIndex;
                    return;
                }
                if (k == XKey.Left) k = XKey.Up;
                else if (k == XKey.Right) k = XKey.Down;
            }

            List<XmbItem> items; int idx;
            GetCurrent(out items, out idx);

            switch (k)
            {
                case XKey.Left:
                    if (stack.Count > 0) PopLevel();
                    else if (catIndex > 0) { catIndex--; Sfx.Tick(); }
                    break;
                case XKey.Right:
                    if (stack.Count == 0 && catIndex < cats.Count - 1) { catIndex++; Sfx.Tick(); }
                    break;
                case XKey.Up:
                    if (idx > 0) { SetIndex(idx - 1); Sfx.Tick(); }
                    break;
                case XKey.Down:
                    if (idx < items.Count - 1) { SetIndex(idx + 1); Sfx.Tick(); }
                    break;
                case XKey.PageUp:
                    if (idx > 0) { SetIndex(Math.Max(0, idx - 6)); Sfx.Tick(); }
                    break;
                case XKey.PageDown:
                    if (idx < items.Count - 1) { SetIndex(Math.Min(items.Count - 1, idx + 6)); Sfx.Tick(); }
                    break;
                case XKey.Ok:
                    if (idx >= 0 && idx < items.Count) ActivateItem(items[idx]);
                    break;
                case XKey.Back:
                    if (stack.Count > 0) PopLevel();
                    break;
                case XKey.Select:
                    if (idx >= 0 && idx < items.Count && items[idx].Game != null) { OpenDetails(items[idx].Game); break; }
                    if (idx >= 0 && idx < items.Count && items[idx].Options != null)
                        OpenOptions(items[idx].Title, items[idx].Options());
                    else Sfx.Error();
                    break;
                case XKey.Options:
                    if (idx >= 0 && idx < items.Count && items[idx].Options != null)
                        OpenOptions(items[idx].Title, items[idx].Options());
                    else Sfx.Error();
                    break;
            }
            cfg.LastCategory = catIndex;
        }

        void ToggleViewMode()
        {
            cfg.ViewMode = cfg.ViewMode == 1 ? 0 : 1;
            cfg.Save();
            Sfx.Ok();
            ShowToast(cfg.ViewMode == 1 ? "Mode Coverflow (ala Aurora)" : "Mode XMB (ala PS3)");
        }

        void GetCurrent(out List<XmbItem> items, out int idx)
        {
            if (stack.Count > 0)
            {
                Level l = stack[stack.Count - 1];
                items = l.Items; idx = l.Index;
            }
            else
            {
                items = cats[catIndex].Items; idx = cats[catIndex].Index;
            }
        }

        void SetIndex(int i)
        {
            if (stack.Count > 0) stack[stack.Count - 1].Index = i;
            else cats[catIndex].Index = i;
        }

        void ActivateItem(XmbItem it)
        {
            if (it.Children != null) { Sfx.Ok(); PushLevel(it); return; }
            if (it.Activate != null) { Sfx.Ok(); it.Activate(); return; }
            if (it.Options != null) { OpenOptions(it.Title, it.Options()); return; }
            Sfx.Error();
        }

        void PushLevel(XmbItem it)
        {
            Level l = new Level();
            l.Title = it.Title;
            l.Parent = it;
            l.Source = it.Children;
            l.Items = SafeBuild(it.Children);
            if (l.Items.Count == 0) l.Items.Add(EmptyItem());
            stack.Add(l);
            levelSlide = 1;
        }

        void PopLevel()
        {
            if (stack.Count == 0) return;
            Sfx.Back();
            stack.RemoveAt(stack.Count - 1);
            levelSlide = -1;
        }

        void ShowMusicToast()
        {
            if (!music.Playing) { ShowToast("Musik berhenti"); return; }
            ShowToast((music.Paused ? "Dijeda: " : "Memutar: ") + music.Title());
        }

        // ===================== VIDEO LAYAR PENUH =====================
        void PlayVideo(List<string> list, int idx)
        {
            videoList = list;
            StartVideoAt(idx);
        }

        void StartVideoAt(int idx)
        {
            if (idx < 0 || idx >= videoList.Count) return;
            StopTrailer(true);
            videoIdx = idx;
            if (music.Playing && !music.Paused) { music.Pause(); pausedForVideo = true; }
            try
            {
                videoFull.Source = new Uri(videoList[idx], UriKind.Absolute);
                videoFull.Volume = cfg.Volume / 100.0;
                videoFull.Visibility = Visibility.Visible;
                videoFull.Play();
                videoPaused = false;
                mode = Mode.Video;
                osdUntil = T + 3.5;
            }
            catch (Exception ex)
            {
                CloseVideo();
                ShowToast("Gagal memutar video: " + ex.Message);
            }
        }

        void CloseVideo()
        {
            try { videoFull.Stop(); videoFull.Close(); } catch { }
            videoFull.Source = null;
            videoFull.Visibility = Visibility.Collapsed;
            mode = Mode.Xmb;
            if (pausedForVideo) { music.Resume(); pausedForVideo = false; }
        }

        int VideoPosMs() { return (int)videoFull.Position.TotalMilliseconds; }
        int VideoLenMs() { return videoFull.NaturalDuration.HasTimeSpan ? (int)videoFull.NaturalDuration.TimeSpan.TotalMilliseconds : 0; }

        void VideoSeek(int delta)
        {
            int len = VideoLenMs();
            int p = Math.Max(0, VideoPosMs() + delta);
            if (len > 0) p = Math.Min(len - 1000, p);
            videoFull.Position = TimeSpan.FromMilliseconds(Math.Max(0, p));
        }

        void HandleVideoKey(XKey k)
        {
            switch (k)
            {
                case XKey.Ok:
                case XKey.Start:
                    if (videoPaused) videoFull.Play(); else videoFull.Pause();
                    videoPaused = !videoPaused;
                    break;
                case XKey.Left: VideoSeek(-10000); break;
                case XKey.Right: VideoSeek(10000); break;
                case XKey.PageUp: VideoSeek(-60000); break;
                case XKey.PageDown: VideoSeek(60000); break;
                case XKey.Up: videoFull.Volume = Math.Min(1, videoFull.Volume + 0.1); break;
                case XKey.Down: videoFull.Volume = Math.Max(0, videoFull.Volume - 0.1); break;
                case XKey.NextTrack: if (videoIdx + 1 < videoList.Count) StartVideoAt(videoIdx + 1); break;
                case XKey.PrevTrack: if (videoIdx > 0) StartVideoAt(videoIdx - 1); break;
                case XKey.Back: Sfx.Back(); CloseVideo(); return;
            }
            osdUntil = T + 3.5;
        }

        // ===================== FOTO =====================
        void OpenPhotos(List<string> list, int idx, bool slide)
        {
            if (list == null || list.Count == 0) { ShowToast("Tidak ada foto"); return; }
            StopTrailer(true);
            photos = list;
            photoImg = null;
            mode = Mode.Photo;
            slideshow = slide;
            slideAt = T;
            ShowPhoto(idx);
        }

        void ShowPhoto(int idx)
        {
            if (photos.Count == 0) return;
            if (idx < 0) idx = photos.Count - 1;
            if (idx >= photos.Count) idx = 0;
            photoIdx = idx;
            string path = photos[idx];
            int want = idx;
            List<string> list = photos;
            ThreadPool.QueueUserWorkItem(delegate
            {
                BitmapSource bs = ImgConv.LoadFile(path, 2560);
                Dispatcher.BeginInvoke((Action)delegate
                {
                    if (mode == Mode.Photo && photos == list && photoIdx == want)
                    {
                        photoImg = bs;
                        photoShownAt = T;
                    }
                });
            });
        }

        void ClosePhotos()
        {
            mode = Mode.Xmb;
            slideshow = false;
            photoImg = null;
        }

        void HandlePhotoKey(XKey k)
        {
            if (HandleOptionsKey(k)) return;
            switch (k)
            {
                case XKey.Left: case XKey.Up: ShowPhoto(photoIdx - 1); slideAt = T; Sfx.Tick(); break;
                case XKey.Right: case XKey.Down: ShowPhoto(photoIdx + 1); slideAt = T; Sfx.Tick(); break;
                case XKey.Ok:
                case XKey.Start:
                    slideshow = !slideshow; slideAt = T;
                    ShowToast(slideshow ? "Slideshow aktif" : "Slideshow berhenti");
                    break;
                case XKey.Back: Sfx.Back(); ClosePhotos(); break;
                case XKey.Options:
                case XKey.Select:
                    string p = photos[photoIdx];
                    List<OptionItem> l = new List<OptionItem>();
                    l.Add(new OptionItem(slideshow ? "Hentikan Slideshow" : "Mulai Slideshow", delegate { slideshow = !slideshow; slideAt = T; }));
                    l.Add(new OptionItem("Jadikan Latar ZMB", delegate { cfg.BackgroundImage = p; cfg.Save(); backImg = null; backKey = ""; ShowToast("Latar ZMB diganti"); }));
                    l.Add(new OptionItem("Jadikan Wallpaper Desktop", delegate { ShowToast(ShellThumb.SetWallpaper(p) ? "Wallpaper diganti" : "Gagal mengganti wallpaper"); }));
                    l.Add(new OptionItem("Buka dengan Aplikasi Lain", delegate { OpenDefault(p); }));
                    l.Add(new OptionItem("Buka Lokasi File", delegate { ShowInExplorer(p); }));
                    l.Add(new OptionItem("Kembali ke Menu", delegate { ClosePhotos(); }));
                    OpenOptions(Path.GetFileName(p), l);
                    break;
            }
        }

        // ===================== GAME =====================
        void Launch(GameEntry g, bool forceAdmin)
        {
            if (bootActive || bootLayer != null) return;
            if (BootAnimFor(g))
            {
                try { StartBoot(g, forceAdmin); return; }
                catch (Exception ex)
                {
                    Log("Boot anim: " + ex);
                    try { CompositionTarget.Rendering -= bootTick; EndBootLayer(); } catch { }
                    bootActive = false;
                    ShowToast("Animasi boot gagal: " + ex.Message);
                }
            }
            LaunchNow(g, forceAdmin);
        }

        void LaunchNow(GameEntry g, bool forceAdmin)
        {
            ProcessStartInfo psi;
            if (g.Platform == "WIN" || g.Platform == "STEAM")
            {
                bool isUrl = g.Path.Contains("://");
                if (!isUrl && !File.Exists(g.Path)) { Sfx.Error(); ShowToast("File game tidak ditemukan: " + g.Path); return; }
                psi = new ProcessStartInfo(g.Path);
                psi.UseShellExecute = true;
                if (!string.IsNullOrEmpty(g.Args)) psi.Arguments = g.Args;
                if (!isUrl) psi.WorkingDirectory = Path.GetDirectoryName(g.Path);
                if (g.Admin || forceAdmin) psi.Verb = "runas";
            }
            else
            {
                PlatformCfg p = cfg.FindPlatform(g.Platform);
                if (p == null) { Sfx.Error(); ShowToast("Platform tidak ditemukan"); return; }
                if (string.IsNullOrEmpty(p.Emulator) || !File.Exists(p.Emulator))
                {
                    Sfx.Error();
                    ShowToast("Emulator " + p.Name + " belum diatur");
                    OpenOptions(p.Name, PlatformOptions(p));
                    return;
                }
                if (!File.Exists(g.Path)) { Sfx.Error(); ShowToast("File game tidak ditemukan"); return; }
                string argStr = GameScanner.BuildArgs(p, g.Path);
                string resumeT = launchResume ? SaveManager.ResumeArgsFor(p) : null;
                if (resumeT != null)
                {
                    PlatformCfg tmp = new PlatformCfg();
                    tmp.Emulator = p.Emulator;
                    tmp.Args = resumeT.Replace("{args}", string.IsNullOrEmpty(p.Args) ? "\"{rom}\"" : p.Args);
                    argStr = GameScanner.BuildArgs(tmp, g.Path);
                }
                psi = new ProcessStartInfo(p.Emulator, argStr);
                psi.UseShellExecute = true;
                psi.WorkingDirectory = Path.GetDirectoryName(p.Emulator);
                if (forceAdmin) psi.Verb = "runas";
            }

            StopTrailer(true);
            AchvWatchStart(g);
            Process proc = null;
            try { proc = Process.Start(psi); }
            catch (Win32Exception ex)
            {
                if (ex.NativeErrorCode == 1223) ShowToast("Dibatalkan");
                else { Sfx.Error(); ShowToast("Gagal menjalankan: " + ex.Message); }
                return;
            }
            catch (Exception ex) { Sfx.Error(); ShowToast("Gagal menjalankan: " + ex.Message); return; }

            g.LastPlayed = DateTime.Now;
            g.PlayCount++;
            homeLastGame = g;
            if (proc == null) { untrackedGame = g; untrackedStart = DateTime.Now; }
            else untrackedGame = null;
            StopThemeMusic();
            if (mode == Mode.Details) CloseDetails();
            cfg.EnsureStored(g);
            cfg.Save();
            ShowToast("Menjalankan " + g.Name + " ...");
            launchedAt = T;

            if (cfg.PauseMusicOnLaunch && music.Playing && !music.Paused) { music.Pause(); pausedForGame = true; }
            if (proc != null)
            {
                runningProc = proc;
                runningGame = g;
                runningStart = DateTime.Now;
                try { proc.EnableRaisingEvents = true; proc.Exited += OnGameExited; } catch { runningProc = null; }
            }
            if (cfg.MinimizeOnLaunch) launchMinimizeAt = T + 1.3;
        }

        void OnGameExited(object sender, EventArgs e)
        {
            try
            {
                Dispatcher.BeginInvoke((Action)delegate
                {
                    if (sender != runningProc || runningGame == null) return;
                    double secs = (DateTime.Now - runningStart).TotalSeconds;
                    GameEntry g = runningGame;
                    runningProc = null; runningGame = null;
                    if (secs < 12) return; // kemungkinan launcher yang membuka proses lain
                    AchvWatchCheck();
                    g.Minutes += secs / 60.0;
                    RecordSession(g, secs / 60.0, runningStart);
                    cfg.Save();
                    BringToFrontZmb();
                    if (pausedForGame) { pausedForGame = false; music.Resume(); }
                    ShowToast("Kamu bermain " + g.Name + " selama " + FormatPlay(secs / 60.0));
                    RefreshAll();
                });
            }
            catch { }
        }

        static string FormatPlay(double minutes)
        {
            if (minutes < 1) return "kurang dari 1 menit";
            int m = (int)Math.Round(minutes);
            if (m < 60) return m + " menit";
            return (m / 60) + " jam " + (m % 60) + " menit";
        }

        // ===================== UTILITAS =====================
        void OpenDefault(string path)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(path);
                psi.UseShellExecute = true;
                Process.Start(psi);
            }
            catch (Exception ex) { ShowToast("Gagal membuka: " + ex.Message); }
        }

        void ShowInExplorer(string path)
        {
            try
            {
                if (File.Exists(path)) Process.Start("explorer.exe", "/select,\"" + path + "\"");
                else if (Directory.Exists(path)) Process.Start("explorer.exe", "\"" + path + "\"");
                else ShowToast("Lokasi tidak ditemukan");
            }
            catch { }
        }

        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

        bool GetAutoStart()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKey))
                    return k != null && k.GetValue("ZMB") != null;
            }
            catch { return false; }
        }

        void SetAutoStart(bool on)
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.CreateSubKey(RunKey))
                {
                    if (on) k.SetValue("ZMB", "\"" + System.Reflection.Assembly.GetEntryAssembly().Location + "\"");
                    else k.DeleteValue("ZMB", false);
                }
            }
            catch (Exception ex) { ShowToast("Gagal: " + ex.Message); }
        }

        void OnClosingZmb(object sender, CancelEventArgs e)
        {
            try { StopAchvPolling(); if (trophyWin != null) { trophyWin.Close(); trophyWin = null; } } catch { }
            try
            {
                CompositionTarget.Rendering -= OnFrame;
                ReleaseHotkey();
                StopThemeMusic();
                cfg.LastCategory = catIndex;
                cfg.Save();
                music.Stop();
                StopTrailer(true);
                CloseVideo();
            }
            catch { }
        }
    }
}
