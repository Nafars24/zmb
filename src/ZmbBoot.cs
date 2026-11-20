using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace ZMB
{
    // Animasi boot 3D sebelum game PS1 jalan:
    // konsol muncul -> tutup CD terbuka -> disc game masuk -> tutup -> tombol power -> layar hitam -> game.
    // Model 3D: "SONY PlayStation One" oleh Vladimir_Sofyanov (Sketchfab), lisensi CC-BY-4.0.
    public partial class ZmbWindow
    {
        class BootModel
        {
            public Dictionary<int, MeshGeometry3D> Parts = new Dictionary<int, MeshGeometry3D>();
            public Dictionary<int, int> PartTex = new Dictionary<int, int>();
            public List<ImageSource> Texs = new List<ImageSource>();
            public int LedPart = -1;
            public ImageSource LedTex;
        }

        static Dictionary<string, BootModel> bootModels = new Dictionary<string, BootModel>();
        static Dictionary<string, string> bootModelErrors = new Dictionary<string, string>();
        static Dictionary<double, MeshGeometry3D> discMeshes = new Dictionary<double, MeshGeometry3D>();
        string bootKind = "ps1";
        Point3D bootCamPos, bootCamTarget;

        // PS3: disc masuk lewat slot depan (model sudah diputar agar sisi depan menghadap +X)
        static readonly Point3D Ps3DiscSeat = new Point3D(-0.15, 0.265, -0.80);
        static readonly Point3D Ps3DiscFront = new Point3D(1.95, 0.27, -0.80);
        static readonly Point3D Ps3DiscStart = new Point3D(2.6, 1.9, -2.2);

        // PS2: laci (tray) keluar dari depan, disc diletakkan, laci masuk lagi
        const int Ps2TrayFront = 10, Ps2Eject = 11, Ps2Reset = 12, Ps2TrayLogo = 13, Ps2TrayBody = 14;
        const double Ps2TrayOut = 1.40;
        static readonly Point3D Ps2DiscSeat = new Point3D(0.14, 0.136, -0.57); // di atas laci saat tertutup
        static readonly Point3D Ps2DiscStart = new Point3D(2.6, 1.8, -2.4);
        TranslateTransform3D bootTrayT;

        // PSP: pintu UMD di belakang terbuka (engsel bawah), UMD masuk, pintu tertutup, PSP berputar ke depan, layar menyala
        const int PspDoor = 100, PspDoorLogo = 16, PspLatch = 15, PspPower = 101;
        const double PspHingeY = -0.37, PspHingeZ = -0.19;
        static readonly Point3D PspUmdSeat = new Point3D(-0.005, 0.0, -0.150);
        static readonly Point3D PspUmdStart = new Point3D(-1.4, 1.3, -1.6);
        AxisAngleRotation3D bootDoorRot;

        // Bagian model (lihat assets/ps1_console.bin)
        const int PartLid = 1, PartReset = 5, PartLed = 13, PartPower = 40, PartOpen = 41;
        const double HingeX = -0.90, HingeY = 0.19;          // poros engsel tutup CD
        const double LidOpenDeg = 72;
        static readonly Point3D DiscSeat = new Point3D(0, 0.160, -0.015);
        static readonly Point3D DiscStart = new Point3D(1.25, 1.75, 0.55);

        // state animasi
        Grid bootLayer;
        Viewport3D bootVp;
        Rectangle bootDim, bootFade;
        TextBlock bootHint;
        PerspectiveCamera bootCam;
        AxisAngleRotation3D bootLidRot, bootYaw, bootDiscSpin, bootDiscTilt;
        TranslateTransform3D bootOpenT, bootPowerT, bootDiscT;
        ScaleTransform3D bootScale;
        SolidColorBrush bootLedBrush;
        ImageBrush bootLedImg;
        Stopwatch bootClock;
        double bootTime, bootLast;
        bool bootClockStarted;
        EventHandler bootTick;
        DispatcherTimer bootHoldTimer;
        GameEntry bootGame;
        bool bootAdmin, bootResume, bootActive, bootSndOpen, bootSndClose, bootSndPower;
        static string BootKindFor(GameEntry g)
        {
            if (g == null) return null;
            if (string.Equals(g.Platform, "PS1", StringComparison.OrdinalIgnoreCase)) return "ps1";
            if (string.Equals(g.Platform, "PS3", StringComparison.OrdinalIgnoreCase)) return "ps3";
            if (string.Equals(g.Platform, "PS2", StringComparison.OrdinalIgnoreCase)) return "ps2";
            if (string.Equals(g.Platform, "PSP", StringComparison.OrdinalIgnoreCase)) return "psp";
            if (string.Equals(g.Platform, "SWITCH", StringComparison.OrdinalIgnoreCase)) return "switch";
            return null;
        }

        bool BootAnimFor(GameEntry g)
        {
            if (!cfg.Ps1BootAnim || g == null) return false;
            string kind = BootKindFor(g);
            if (kind == null) return false;
            PlatformCfg p = cfg.FindPlatform(g.Platform);
            if (p == null || string.IsNullOrEmpty(p.Emulator) || !File.Exists(p.Emulator) || !File.Exists(g.Path)) return false;
            if (ConsoleModel(kind) == null)
            {
                string why;
                if (!bootModelErrors.TryGetValue(kind, out why) || why == null) why = "file assets\\" + kind + "_console.bin tidak ditemukan";
                Log("Boot anim dilewati: " + why);
                ShowToast(Lang.T("Animasi boot ") + kind.ToUpperInvariant() + Lang.T(" dilewati: ") + why);
                return false;
            }
            return true;
        }

        static readonly object bootModelLock = new object();

        static BootModel ConsoleModel(string kind)
        {
            lock (bootModelLock)
            {
                BootModel m;
                if (bootModels.TryGetValue(kind, out m)) return m;
                try { m = LoadConsoleModel(kind + "_console"); }
                catch (Exception ex) { m = null; bootModelErrors[kind] = ex.Message; }
                bootModels[kind] = m;
                return m;
            }
        }

        // Muat model di belakang layar supaya animasi tidak tersendat di awal
        void PreloadBootModel()
        {
            if (!cfg.Ps1BootAnim) return;
            System.Threading.ThreadPool.QueueUserWorkItem(delegate { ConsoleModel("ps1"); ConsoleModel("ps2"); ConsoleModel("ps3"); ConsoleModel("psp"); ConsoleModel("switch"); });
        }

        static BootModel LoadConsoleModel(string baseName)
        {
            byte[] bin = LoadAssetBytes(baseName + ".bin");
            if (bin == null) return null;
            BootModel m = new BootModel();
            using (BinaryReader br = new BinaryReader(new MemoryStream(bin)))
            {
                if (new string(br.ReadChars(4)) != "ZMB3") return null;
                int ver = br.ReadInt32();
                if (ver >= 2)
                {
                    // v2: beberapa tekstur + bagian yang punya lampu
                    int nt = br.ReadInt32();
                    for (int i = 0; i < nt; i++) m.Texs.Add(LoadAsset(ReadStr(br)));
                    m.LedPart = br.ReadInt32();
                    string led = ReadStr(br);
                    if (led.Length > 0) m.LedTex = LoadAsset(led);
                }
                else m.Texs.Add(LoadAsset(baseName + ".jpg"));
                int count = br.ReadInt32();
                for (int i = 0; i < count; i++)
                {
                    int id = br.ReadInt32();
                    m.PartTex[id] = ver >= 2 ? br.ReadInt32() : 0;
                    int nv = br.ReadInt32();
                    Point3DCollection pos = new Point3DCollection(nv);
                    Vector3DCollection nor = new Vector3DCollection(nv);
                    PointCollection uv = new PointCollection(nv);
                    for (int k = 0; k < nv; k++)
                    {
                        pos.Add(new Point3D(br.ReadSingle(), br.ReadSingle(), br.ReadSingle()));
                        nor.Add(new Vector3D(br.ReadSingle(), br.ReadSingle(), br.ReadSingle()));
                        uv.Add(new Point(br.ReadSingle(), br.ReadSingle()));
                    }
                    int ni = br.ReadInt32();
                    Int32Collection idx = new Int32Collection(ni);
                    for (int k = 0; k < ni; k++) idx.Add(br.ReadInt32());
                    MeshGeometry3D mesh = new MeshGeometry3D();
                    mesh.Positions = pos; mesh.Normals = nor; mesh.TextureCoordinates = uv; mesh.TriangleIndices = idx;
                    mesh.Freeze();
                    m.Parts[id] = mesh;
                }
            }
            return m;
        }

        static string ReadStr(BinaryReader br)
        {
            int n = br.ReadInt32();
            return System.Text.Encoding.ASCII.GetString(br.ReadBytes(n));
        }

        // Cincin disc (atas menghadap +Y), UV memetakan gambar persegi ke seluruh piringan
        static MeshGeometry3D DiscMesh(double R)
        {
            MeshGeometry3D cached;
            if (discMeshes.TryGetValue(R, out cached)) return cached;
            const double r = 0.075;
            const int seg = 96;
            MeshGeometry3D m = new MeshGeometry3D();
            Point3DCollection pos = new Point3DCollection();
            Vector3DCollection nor = new Vector3DCollection();
            PointCollection uv = new PointCollection();
            Int32Collection idx = new Int32Collection();
            for (int k = 0; k <= seg; k++)
            {
                double a = 2 * Math.PI * k / seg, c = Math.Cos(a), s = Math.Sin(a);
                pos.Add(new Point3D(r * c, 0, r * s)); uv.Add(new Point(0.5 + 0.5 * r / R * c, 0.5 + 0.5 * r / R * s)); nor.Add(new Vector3D(0, 1, 0));
                pos.Add(new Point3D(R * c, 0, R * s)); uv.Add(new Point(0.5 + 0.5 * c, 0.5 + 0.5 * s)); nor.Add(new Vector3D(0, 1, 0));
            }
            for (int k = 0; k < seg; k++)
            {
                int a = 2 * k;
                // urutan berlawanan jarum jam dilihat dari atas -> sisi depan menghadap +Y
                idx.Add(a); idx.Add(a + 3); idx.Add(a + 1);
                idx.Add(a); idx.Add(a + 2); idx.Add(a + 3);
            }
            m.Positions = pos; m.Normals = nor; m.TextureCoordinates = uv; m.TriangleIndices = idx;
            m.Freeze();
            discMeshes[R] = m;
            return m;
        }

        // Gambar disc: kustom / folder discs / unduhan, kalau tidak ada dibuat dari cover game
        ImageSource DiscArt(GameEntry g)
        {
            string f = null;
            try { f = GameScanner.FindDisc(g); } catch { }
            ImageSource src = null;
            bool generated = false;
            if (f != null) { try { src = ImgConv.LoadFile(f, 1024); } catch { } }
            if (src == null)
            {
                generated = true;
                try
                {
                    string cv = GameScanner.FindCover(cfg.FindPlatform(g.Platform), g.Path, g.Cover);
                    if (cv != null) src = ImgConv.LoadFile(cv, 1024);
                }
                catch { }
            }
            const int N = 768;
            DrawingVisual dv = new DrawingVisual();
            using (DrawingContext dc = dv.RenderOpen())
            {
                Rect full = new Rect(0, 0, N, N);
                Point c = new Point(N / 2.0, N / 2.0);
                dc.PushClip(new EllipseGeometry(c, N / 2.0, N / 2.0));
                if (src != null) DrawImageFill(dc, src, full, 1.0);
                else
                {
                    dc.DrawRectangle(SolidOf(Rgb(214, 216, 222)), null, full);
                    FormattedText t = FT(g.Name, N * 0.07, 2, true, N * 0.7);
                    dc.DrawText(t, new Point(N * 0.15, N * 0.68));
                }
                if (generated)
                {
                    // area tengah bening seperti CD asli
                    dc.DrawEllipse(SolidOf(Color.FromArgb(235, 190, 196, 204)), new Pen(SolidOf(Color.FromArgb(160, 120, 126, 134)), N * 0.004), c, N * 0.22, N * 0.22);
                    dc.DrawEllipse(SolidOf(Color.FromArgb(255, 158, 164, 172)), null, c, N * 0.15, N * 0.15);
                    dc.DrawEllipse(null, new Pen(SolidOf(Color.FromArgb(120, 0, 0, 0)), N * 0.006), c, N / 2.0 - N * 0.004, N / 2.0 - N * 0.004);
                }
                // kilau plastik
                LinearGradientBrush sheen = new LinearGradientBrush();
                sheen.StartPoint = new Point(0, 0); sheen.EndPoint = new Point(1, 1);
                sheen.GradientStops.Add(new GradientStop(Color.FromArgb(0, 255, 255, 255), 0.30));
                sheen.GradientStops.Add(new GradientStop(Color.FromArgb(70, 255, 255, 255), 0.45));
                sheen.GradientStops.Add(new GradientStop(Color.FromArgb(0, 255, 255, 255), 0.60));
                dc.DrawRectangle(sheen, null, full);
                dc.Pop();
            }
            RenderTargetBitmap rtb = new RenderTargetBitmap(N, N, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(dv);
            rtb.Freeze();
            return rtb;
        }

        void StartBoot(GameEntry g, bool forceAdmin)
        {
            bootKind = BootKindFor(g) ?? "ps1";
            BootModel bm = ConsoleModel(bootKind);
            bool ps3 = bootKind == "ps3", ps2 = bootKind == "ps2", psp = bootKind == "psp", sw = bootKind == "switch";
            if (ps3) { bootCamPos = new Point3D(5.0, 2.25, 2.6); bootCamTarget = new Point3D(0.25, 0.0, 0.15); }
            else if (ps2) { bootCamPos = new Point3D(4.6, 2.3, 2.4); bootCamTarget = new Point3D(0.55, 0.0, -0.25); }
            else if (psp) { bootCamPos = new Point3D(0.9, 0.6, 3.2); bootCamTarget = new Point3D(0, 0, -0.1); }
            else if (sw) { bootCamPos = SwCamClose; bootCamTarget = SwTgtClose; }
            else { bootCamPos = new Point3D(4.2, 3.4, 2.85); bootCamTarget = new Point3D(0, 0.35, 0); }
            bootTrayT = new TranslateTransform3D();
            bootGame = g; bootAdmin = forceAdmin; bootResume = launchResume;
            bootSndOpen = bootSndClose = bootSndPower = false;
            StopTrailer(true);
            StopThemeMusic();

            // satu material per tekstur; PS3 hitam piano lebih mengilap
            List<Material> mats = new List<Material>();
            foreach (ImageSource tx in bm.Texs)
            {
                ImageBrush texBrush = new ImageBrush(tx);
                texBrush.ViewportUnits = BrushMappingMode.Absolute;
                texBrush.Viewport = new Rect(0, 0, 1, 1);
                texBrush.TileMode = TileMode.Tile;
                texBrush.Freeze();
                MaterialGroup mg = new MaterialGroup();
                mg.Children.Add(new DiffuseMaterial(texBrush));
                mg.Children.Add(ps3 ? new SpecularMaterial(SolidOf(Color.FromArgb(120, 255, 255, 255)), 45)
                              : ps2 ? new SpecularMaterial(SolidOf(Color.FromArgb(80, 255, 255, 255)), 32)
                                    : new SpecularMaterial(SolidOf(Color.FromArgb(60, 255, 255, 255)), 28));
                mg.Freeze();
                mats.Add(mg);
            }
            bootLedImg = null;

            Model3DGroup console = new Model3DGroup();
            foreach (KeyValuePair<int, MeshGeometry3D> kv in bm.Parts)
            {
                int ti;
                if (!bm.PartTex.TryGetValue(kv.Key, out ti) || ti < 0 || ti >= mats.Count) ti = 0;
                Material mat = mats.Count > 0 ? mats[ti] : new DiffuseMaterial(Brushes.Gray);
                GeometryModel3D gm = new GeometryModel3D(kv.Value, mat);
                gm.BackMaterial = mat;
                if (ps3 || ps2 || psp || sw)
                {
                    if (sw && kv.Key == SwFlap)
                    {
                        if (bootDoorRot == null) bootDoorRot = new AxisAngleRotation3D(new Vector3D(1, 0, 0), 0);
                        gm.Transform = new RotateTransform3D(bootDoorRot, 0, SwHingeY, SwHingeZ);
                    }
                    if (psp)
                    {
                        if (kv.Key == PspDoor || kv.Key == PspDoorLogo)
                        {
                            if (bootDoorRot == null) bootDoorRot = new AxisAngleRotation3D(new Vector3D(1, 0, 0), 0);
                            gm.Transform = new RotateTransform3D(bootDoorRot, 0, PspHingeY, PspHingeZ);
                        }
                        else if (kv.Key == PspLatch) { bootOpenT = new TranslateTransform3D(); gm.Transform = bootOpenT; }
                        else if (kv.Key == PspPower) { bootPowerT = new TranslateTransform3D(); gm.Transform = bootPowerT; }
                    }
                    if (ps2)
                    {
                        if (kv.Key == Ps2TrayFront || kv.Key == Ps2TrayLogo || kv.Key == Ps2TrayBody) gm.Transform = bootTrayT;
                        else if (kv.Key == Ps2Eject) { bootOpenT = new TranslateTransform3D(); gm.Transform = bootOpenT; }
                        else if (kv.Key == Ps2Reset) { bootPowerT = new TranslateTransform3D(); gm.Transform = bootPowerT; }
                    }
                    if (kv.Key == bm.LedPart && bm.LedTex != null)
                    {
                        ImageSource ledTex = bm.LedTex;
                        if (psp) { ImageSource scr = PspScreenArt(g); if (scr != null) ledTex = scr; }
                        if (sw) { ImageSource scr = ScreenArt(g, 0, 1); if (scr != null) ledTex = scr; }
                        bootLedImg = new ImageBrush(ledTex);
                        bootLedImg.ViewportUnits = BrushMappingMode.Absolute;
                        bootLedImg.Viewport = new Rect(0, 0, 1, 1);
                        bootLedImg.Opacity = 0;
                        MaterialGroup lm = new MaterialGroup();
                        lm.Children.Add(mat);
                        lm.Children.Add(new EmissiveMaterial(bootLedImg));
                        gm.Material = lm; gm.BackMaterial = lm;
                    }
                }
                else if (kv.Key == PartLid)
                {
                    bootLidRot = new AxisAngleRotation3D(new Vector3D(0, 0, 1), 0);
                    gm.Transform = new RotateTransform3D(bootLidRot, HingeX, HingeY, 0);
                }
                else if (kv.Key == PartOpen) { bootOpenT = new TranslateTransform3D(); gm.Transform = bootOpenT; }
                else if (kv.Key == PartPower) { bootPowerT = new TranslateTransform3D(); gm.Transform = bootPowerT; }
                else if (kv.Key == PartLed)
                {
                    bootLedBrush = new SolidColorBrush(Color.FromArgb(0, 60, 255, 90));
                    MaterialGroup lm = new MaterialGroup();
                    lm.Children.Add(mat);
                    lm.Children.Add(new EmissiveMaterial(bootLedBrush));
                    gm.Material = lm; gm.BackMaterial = lm;
                }
                console.Children.Add(gm);
            }
            if (bootOpenT == null) bootOpenT = new TranslateTransform3D();
            if (bootPowerT == null) bootPowerT = new TranslateTransform3D();
            if (bootLidRot == null) bootLidRot = new AxisAngleRotation3D(new Vector3D(0, 0, 1), 0);
            if (bootLedBrush == null) bootLedBrush = new SolidColorBrush(Colors.Transparent);

            // disc game
            ImageBrush discBrush = new ImageBrush(psp ? UmdArt(g) : sw ? CartArt(g) : DiscArt(g));
            discBrush.ViewportUnits = BrushMappingMode.Absolute;
            discBrush.Viewport = new Rect(0, 0, 1, 1);
            discBrush.Freeze();
            MaterialGroup dTop = new MaterialGroup();
            dTop.Children.Add(new DiffuseMaterial(discBrush));
            dTop.Children.Add(new SpecularMaterial(SolidOf(Color.FromArgb(140, 255, 255, 255)), 60));
            MaterialGroup dBot = new MaterialGroup(); // sisi data: PS1 hitam mengilap, Blu-ray perak kebiruan
            dBot.Children.Add(new DiffuseMaterial(SolidOf(ps3 ? Rgb(150, 160, 185) : ps2 ? Rgb(60, 80, 165) : Rgb(16, 16, 20))));
            dBot.Children.Add(new SpecularMaterial(SolidOf(Color.FromArgb(200, 170, 150, 255)), 40));
            if (psp) { dBot = new MaterialGroup(); dBot.Children.Add(new DiffuseMaterial(SolidOf(Rgb(120, 122, 130)))); }
            if (sw) dBot = dTop; // kotak cartridge: semua sisi memakai tekstur yang sama
            GeometryModel3D disc = new GeometryModel3D(psp ? UmdMesh() : sw ? CartMesh() : DiscMesh(ps3 || ps2 ? 0.6 : 0.72), dTop);
            disc.BackMaterial = dBot;
            bootDiscSpin = new AxisAngleRotation3D(new Vector3D(0, 1, 0), 0);
            bootDiscTilt = new AxisAngleRotation3D(new Vector3D(0, 0, 1), 0);
            bootDiscT = new TranslateTransform3D(0, 100, 0);
            Transform3DGroup dt = new Transform3DGroup();
            dt.Children.Add(new RotateTransform3D(bootDiscSpin));
            dt.Children.Add(new RotateTransform3D(bootDiscTilt));
            dt.Children.Add(bootDiscT);
            disc.Transform = dt;
            console.Children.Add(disc);

            bootScale = new ScaleTransform3D(1, 1, 1);
            bootYaw = new AxisAngleRotation3D(new Vector3D(0, 1, 0), 0);
            Transform3DGroup ct = new Transform3DGroup();
            ct.Children.Add(bootScale);
            ct.Children.Add(new RotateTransform3D(bootYaw));
            console.Transform = ct;

            Model3DGroup scene = new Model3DGroup();
            scene.Children.Add(new AmbientLight(Rgb(78, 80, 88)));
            scene.Children.Add(new DirectionalLight(Rgb(235, 232, 225), new Vector3D(-0.45, -1, -0.35)));
            scene.Children.Add(new DirectionalLight(Rgb(90, 100, 125), new Vector3D(0.7, -0.25, 0.9)));
            scene.Children.Add(console);

            bootCam = new PerspectiveCamera(bootCamPos, bootCamTarget - bootCamPos, new Vector3D(0, 1, 0), 46);
            bootCam.NearPlaneDistance = 0.05; bootCam.FarPlaneDistance = 60;

            bootVp = new Viewport3D();
            bootVp.Camera = bootCam;
            bootVp.IsHitTestVisible = false;
            ModelVisual3D mv = new ModelVisual3D();
            mv.Content = scene;
            bootVp.Children.Add(mv);

            bootLayer = new Grid();
            bootLayer.IsHitTestVisible = false;
            bootDim = new Rectangle();
            RadialGradientBrush dimBr = new RadialGradientBrush(Color.FromArgb(235, 14, 16, 22), Color.FromArgb(255, 0, 0, 0));
            dimBr.Freeze();
            bootDim.Fill = dimBr; bootDim.Opacity = 0;
            bootFade = new Rectangle();
            bootFade.Fill = Brushes.Black; bootFade.Opacity = 0;
            bootHint = new TextBlock();
            bootHint.Text = Lang.T("Tekan tombol apa saja untuk melewati");
            bootHint.Foreground = new SolidColorBrush(Color.FromArgb(150, 255, 255, 255));
            bootHint.FontSize = Math.Max(12, 15 * S);
            bootHint.HorizontalAlignment = HorizontalAlignment.Center;
            bootHint.VerticalAlignment = VerticalAlignment.Bottom;
            bootHint.Margin = new Thickness(0, 0, 0, 38 * S);
            bootHint.Opacity = 0;
            bootLayer.Children.Add(bootDim);
            bootLayer.Children.Add(bootVp);
            bootLayer.Children.Add(bootHint);
            bootLayer.Children.Add(bootFade);
            Panel.SetZIndex(bootLayer, 100); // di atas semua lapisan XMB (ZIndex 1-6)
            root.Children.Add(bootLayer);

            bootActive = true;
            bootTime = 0; bootLast = 0; bootClockStarted = false;
            UpdateBoot(0);
            bootClock = new Stopwatch();
            bootTick = delegate
            {
                if (!bootActive) return;
                // jam mulai di frame pertama yang benar-benar tampil; langkah dibatasi supaya
                // frame yang tersendat (mis. saat tekstur diunggah ke GPU) tidak membuat gerakan melompat
                if (!bootClockStarted) { bootClockStarted = true; bootClock.Start(); bootLast = 0; return; }
                double now = bootClock.Elapsed.TotalSeconds;
                double step = now - bootLast;
                bootLast = now;
                if (step > 1.0 / 30) step = 1.0 / 30;
                if (step < 0) step = 0;
                bootTime += step;
                UpdateBoot(bootTime);
            };
            CompositionTarget.Rendering += bootTick;
        }

        static double Clamp01(double v) { return v < 0 ? 0 : (v > 1 ? 1 : v); }
        static double Seg(double t, double a, double b) { return Clamp01((t - a) / (b - a)); }
        static double Smooth(double u) { u = Clamp01(u); return u * u * u * (u * (u * 6 - 15) + 10); } // smootherstep
        static double EaseOut3(double u) { u = 1 - Clamp01(u); return 1 - u * u * u; }
        static double EaseInOut(double u) { u = Clamp01(u); return u < 0.5 ? 4 * u * u * u : 1 - Math.Pow(-2 * u + 2, 3) / 2; }
        static double EaseOutBack(double u) { const double c1 = 0.9, c3 = c1 + 1; double v = Clamp01(u) - 1; return 1 + c3 * v * v * v + c1 * v * v; }
        static double Pulse(double t, double a, double b) { double u = Seg(t, a, b); return (u <= 0 || u >= 1) ? 0 : Math.Sin(Math.PI * u); }
        static double Bez(double a, double b, double c, double d, double u)
        {
            double v = 1 - u;
            return v * v * v * a + 3 * v * v * u * b + 3 * v * u * u * c + u * u * u * d;
        }

        void UpdateBoot(double t)
        {
            // 1. konsol muncul; kamera berputar pelan sepanjang animasi (tidak pernah berhenti mendadak)
            double appear = EaseOut3(Seg(t, 0, 0.9));
            bootDim.Opacity = Smooth(t / 0.6);
            bootVp.Opacity = Smooth(t / 0.7);
            bootHint.Opacity = Smooth(Seg(t, 0.9, 1.5)) * (1 - Smooth(Seg(t, 3.2, 3.6)));
            double sc = 0.86 + 0.14 * appear;
            bootScale.ScaleX = sc; bootScale.ScaleY = sc; bootScale.ScaleZ = sc;
            bootYaw.Angle = -26 + 22 * EaseInOut(t / 4.3);

            if (bootKind == "switch") UpdateBootSwitch(t);
            else if (bootKind == "psp") UpdateBootPsp(t);
            else if (bootKind == "ps3") UpdateBootPs3(t);
            else if (bootKind == "ps2") UpdateBootPs2(t);
            else UpdateBootPs1(t);

            // 5. kamera mendekat, layar memudar ke hitam
            double te = bootKind == "psp" || bootKind == "switch" ? t - 0.6 : t; // PSP/Switch sedikit lebih panjang
            double push = EaseInOut(Seg(te, 3.35, 4.3));
            Point3D cp = bootCamPos + (bootCamTarget - bootCamPos) * (0.30 * push);
            bootCam.Position = cp;
            bootCam.LookDirection = bootCamTarget - cp;
            bootFade.Opacity = Smooth(Seg(te, 3.55, 4.25));

            if (te >= 4.3) FinishBoot(false);
        }

        // PS3: disc melayang ke depan slot, lalu ditarik masuk; lampu power menyala hijau
        void UpdateBootPs3(double t)
        {
            if (t < 1.0) bootDiscT.OffsetY = 100;
            else if (t < 2.0)
            {
                double u = EaseInOut(Seg(t, 1.0, 1.95));
                Point3D a = Ps3DiscStart, f = Ps3DiscFront;
                bootDiscT.OffsetX = Bez(a.X, a.X * 0.7 + f.X * 0.3, f.X + 0.6, f.X, u);
                bootDiscT.OffsetY = Bez(a.Y, a.Y * 0.7 + f.Y * 0.3 + 0.5, f.Y + 0.15, f.Y, u);
                bootDiscT.OffsetZ = Bez(a.Z, a.Z * 0.7 + f.Z * 0.3, f.Z, f.Z, u);
                bootDiscTilt.Angle = 40 * (1 - Smooth(Seg(t, 1.0, 1.9)));
                bootDiscSpin.Angle = -300 * EaseOut3(Seg(t, 1.0, 2.0));
            }
            else
            {
                double u = Seg(t, 2.05, 2.75);
                u = u * u * (3 - 2 * u);
                bootDiscT.OffsetX = Ps3DiscFront.X + (Ps3DiscSeat.X - Ps3DiscFront.X) * u;
                bootDiscT.OffsetY = Ps3DiscFront.Y + (Ps3DiscSeat.Y - Ps3DiscFront.Y) * u;
                bootDiscT.OffsetZ = Ps3DiscFront.Z;
                bootDiscTilt.Angle = 0;
                bootDiscSpin.Angle = -300;
            }
            if (t >= 2.08 && !bootSndOpen) { bootSndOpen = true; Sfx.Tick(); }   // slot menarik disc
            if (t >= 2.75 && !bootSndClose) { bootSndClose = true; Sfx.Back(); } // disc terpasang
            if (t >= 3.0 && !bootSndPower) { bootSndPower = true; Sfx.Ok(); }    // power
            if (bootLedImg != null) bootLedImg.Opacity = Smooth(Seg(t, 3.02, 3.3));
        }

        // PS2: tombol eject, laci keluar, disc diletakkan di laci, laci masuk, tombol reset/power, lampu hijau
        void UpdateBootPs2(double t)
        {
            bootOpenT.OffsetX = -0.012 * Pulse(t, 0.55, 0.8);
            if (t >= 0.62 && !bootSndOpen) { bootSndOpen = true; Sfx.Tick(); }
            double tray = t < 2.35 ? Ps2TrayOut * EaseOut3(Seg(t, 0.75, 1.45)) : Ps2TrayOut * (1 - EaseInOut(Seg(t, 2.35, 3.0)));
            bootTrayT.OffsetX = tray;
            if (t >= 3.0 && !bootSndClose) { bootSndClose = true; Sfx.Back(); }

            if (t < 1.2) bootDiscT.OffsetY = 100;
            else
            {
                double u = EaseInOut(Seg(t, 1.2, 2.2));
                Point3D a = Ps2DiscStart;
                Point3D e = new Point3D(Ps2DiscSeat.X + Ps2TrayOut, Ps2DiscSeat.Y, Ps2DiscSeat.Z);
                bootDiscT.OffsetX = t >= 2.35 ? Ps2DiscSeat.X + tray : Bez(a.X, a.X * 0.6 + e.X * 0.4, e.X, e.X, u);
                bootDiscT.OffsetY = Bez(a.Y, a.Y * 0.6 + e.Y * 0.4 + 0.4, e.Y + 0.4, e.Y, u);
                bootDiscT.OffsetZ = Bez(a.Z, a.Z * 0.6 + e.Z * 0.4, e.Z, e.Z, u);
                bootDiscTilt.Angle = 42 * (1 - Smooth(Seg(t, 1.2, 2.15)));
                bootDiscSpin.Angle = -360 * EaseOut3(Seg(t, 1.2, 2.4));
            }

            bootPowerT.OffsetX = -0.012 * Pulse(t, 3.05, 3.3);
            if (t >= 3.15 && !bootSndPower) { bootSndPower = true; Sfx.Ok(); }
            if (bootLedImg != null) bootLedImg.Opacity = Smooth(Seg(t, 3.18, 3.45));
        }

        // ===== Nintendo Switch: tutup slot game card terbuka, cartridge masuk, kamera mundur ke layar yang menyala =====
        const int SwFlap = 10;
        const double SwHingeY = 0.4577, SwHingeZ = -0.0342;
        static readonly Point3D SwCardSeat = new Point3D(0.579, 0.30, 0.002);
        static readonly Point3D SwCardAbove = new Point3D(0.579, 0.62, 0.002);
        static readonly Point3D SwCardStart = new Point3D(0.82, 1.15, 0.45);
        static readonly Point3D SwCamClose = new Point3D(0.95, 0.75, 0.75), SwTgtClose = new Point3D(0.55, 0.42, 0.0);
        static readonly Point3D SwCamFront = new Point3D(0.35, 0.35, 2.5), SwTgtFront = new Point3D(0, 0, 0);

        void UpdateBootSwitch(double t)
        {
            bootYaw.Angle = 0;
            double cu = EaseInOut(Seg(t, 2.9, 3.7));
            bootCamPos = SwCamClose + (SwCamFront - SwCamClose) * cu;
            bootCamTarget = SwTgtClose + (SwTgtFront - SwTgtClose) * cu;

            if (t >= 0.6 && !bootSndOpen) { bootSndOpen = true; Sfx.Tick(); }
            double flap = t < 2.55 ? -100 * EaseOutBack(Seg(t, 0.6, 1.2)) : -100 * (1 - EaseInOut(Seg(t, 2.55, 2.95)));
            if (bootDoorRot != null) bootDoorRot.Angle = flap;
            if (t >= 2.95 && !bootSndClose) { bootSndClose = true; Sfx.Back(); }

            if (t < 1.0) bootDiscT.OffsetY = 100;
            else if (t < 2.05)
            {
                double u = EaseInOut(Seg(t, 1.0, 2.0));
                Point3D a = SwCardStart, e = SwCardAbove;
                bootDiscT.OffsetX = Bez(a.X, a.X * 0.4 + e.X * 0.6, e.X, e.X, u);
                bootDiscT.OffsetY = Bez(a.Y, a.Y * 0.4 + e.Y * 0.6 + 0.1, e.Y + 0.12, e.Y, u);
                bootDiscT.OffsetZ = Bez(a.Z, a.Z * 0.4 + e.Z * 0.6, e.Z, e.Z, u);
                bootDiscTilt.Angle = -35 * (1 - Smooth(Seg(t, 1.0, 1.95)));
                bootDiscSpin.Angle = 0;
            }
            else
            {
                double u = Seg(t, 2.1, 2.5);
                u = u * u;
                bootDiscT.OffsetX = SwCardSeat.X;
                bootDiscT.OffsetY = SwCardAbove.Y + (SwCardSeat.Y - SwCardAbove.Y) * u;
                bootDiscT.OffsetZ = SwCardSeat.Z;
                bootDiscTilt.Angle = 0;
            }
            if (t >= 3.5 && !bootSndPower) { bootSndPower = true; Sfx.Ok(); }
            if (bootLedImg != null) bootLedImg.Opacity = Smooth(Seg(t, 3.55, 3.9)); // layar menyala
        }

        // Cartridge Switch: kotak tipis, muka depan & belakang memakai gambar cartridge, sisi abu gelap
        static MeshGeometry3D cartMesh;
        static MeshGeometry3D CartMesh()
        {
            if (cartMesh != null) return cartMesh;
            const double x = 0.085, y = 0.125, z = 0.015, e = 0.02;
            Point3D[] pts = {
                new Point3D(-x, -y, z), new Point3D(x, -y, z), new Point3D(x, y, z), new Point3D(-x, y, z),     // depan
                new Point3D(x, -y, -z), new Point3D(-x, -y, -z), new Point3D(-x, y, -z), new Point3D(x, y, -z), // belakang
            };
            Point[] fuv = { new Point(0, 1), new Point(1, 1), new Point(1, 0), new Point(0, 0) };
            Point[] buv = { new Point(0, 1), new Point(1, 1), new Point(1, 0), new Point(0, 0) };
            MeshGeometry3D m = new MeshGeometry3D();
            Point3DCollection P = new Point3DCollection(); Vector3DCollection N = new Vector3DCollection(); PointCollection U = new PointCollection(); Int32Collection I = new Int32Collection();
            Action<Point3D, Point3D, Point3D, Point3D, Vector3D, Point[]> quad = delegate (Point3D a, Point3D b, Point3D c, Point3D d, Vector3D n, Point[] uv)
            {
                int k = P.Count;
                P.Add(a); P.Add(b); P.Add(c); P.Add(d);
                for (int i = 0; i < 4; i++) { N.Add(n); U.Add(uv[i]); }
                I.Add(k); I.Add(k + 1); I.Add(k + 2); I.Add(k); I.Add(k + 2); I.Add(k + 3);
            };
            Point[] side = { new Point(e, e), new Point(e, e), new Point(e, e), new Point(e, e) };
            quad(pts[0], pts[1], pts[2], pts[3], new Vector3D(0, 0, 1), fuv);
            quad(pts[4], pts[5], pts[6], pts[7], new Vector3D(0, 0, -1), buv);
            quad(pts[5], pts[0], pts[3], pts[6], new Vector3D(-1, 0, 0), side);
            quad(pts[1], pts[4], pts[7], pts[2], new Vector3D(1, 0, 0), side);
            quad(pts[3], pts[2], pts[7], pts[6], new Vector3D(0, 1, 0), side);
            quad(pts[5], pts[4], pts[1], pts[0], new Vector3D(0, -1, 0), side);
            m.Positions = P; m.Normals = N; m.TextureCoordinates = U; m.TriangleIndices = I;
            m.Freeze();
            cartMesh = m;
            return m;
        }

        // Gambar cartridge: gambar pilihan kamu / ScreenScraper (support-2D) dipasang utuh; kalau tidak ada,
        // dibuat cartridge abu gelap dengan label dari cover game
        ImageSource CartArt(GameEntry g)
        {
            string f = null;
            try { f = GameScanner.FindDisc(g); } catch { }
            ImageSource art = null;
            bool custom = false;
            if (f != null) { try { art = ImgConv.LoadFile(f, 768); custom = art != null; } catch { } }
            if (art == null)
            {
                try
                {
                    string cv = GameScanner.FindCover(cfg.FindPlatform(g.Platform), g.Path, g.Cover);
                    if (cv != null) art = ImgConv.LoadFile(cv, 768);
                }
                catch { }
            }
            const int W = 340, H = 500;
            DrawingVisual dv = new DrawingVisual();
            using (DrawingContext dc = dv.RenderOpen())
            {
                dc.DrawRectangle(SolidOf(Rgb(46, 47, 52)), null, new Rect(0, 0, W, H));
                if (custom) DrawIconImage(dc, art, new Rect(0, 0, W, H), 1.0);
                else
                {
                    // cangkang: bagian atas bertakik, label besar di tengah
                    dc.DrawRectangle(SolidOf(Rgb(58, 59, 64)), null, new Rect(0, 0, W, 46));
                    for (int i = 0; i < 5; i++) dc.DrawRectangle(SolidOf(Rgb(38, 39, 43)), null, new Rect(40 + i * 56, 14, 36, 18));
                    Rect lab = new Rect(26, 70, W - 52, H - 100);
                    dc.DrawRectangle(SolidOf(Rgb(220, 30, 36)), null, new Rect(lab.X, lab.Y, lab.Width, 34));
                    Rect pic = new Rect(lab.X, lab.Y + 34, lab.Width, lab.Height - 34);
                    if (art != null) { dc.PushClip(new RectangleGeometry(pic)); DrawImageFill(dc, art, pic, 1.0); dc.Pop(); }
                    else dc.DrawRectangle(SolidOf(Rgb(230, 230, 234)), null, pic);
                    dc.DrawRectangle(null, new Pen(SolidOf(Rgb(30, 30, 34)), 3), lab);
                }
            }
            RenderTargetBitmap rtb = new RenderTargetBitmap(W, H, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(dv);
            rtb.Freeze();
            return rtb;
        }

        void UpdateBootPsp(double t)
        {
            // mulai dari sisi belakang, lalu berputar ke depan setelah pintu tertutup
            bootYaw.Angle = t < 2.75 ? 160 + 15 * EaseInOut(Seg(t, 0, 2.6)) : 175 + (348 - 175) * EaseInOut(Seg(t, 2.75, 3.6));
            bootOpenT.OffsetX = -0.02 * Pulse(t, 0.5, 0.75);
            if (t >= 0.6 && !bootSndOpen) { bootSndOpen = true; Sfx.Tick(); }
            double door = t < 2.15 ? -72 * EaseOutBack(Seg(t, 0.7, 1.35)) : -72 * (1 - EaseInOut(Seg(t, 2.15, 2.6)));
            if (bootDoorRot != null) bootDoorRot.Angle = door;
            if (t >= 2.6 && !bootSndClose) { bootSndClose = true; Sfx.Back(); }

            if (t < 1.05) bootDiscT.OffsetY = 100;
            else
            {
                double u = EaseInOut(Seg(t, 1.05, 2.05));
                Point3D a = PspUmdStart, e = PspUmdSeat;
                bootDiscT.OffsetX = Bez(a.X, a.X * 0.5 + e.X * 0.5, e.X, e.X, u);
                bootDiscT.OffsetY = Bez(a.Y, a.Y * 0.5 + e.Y * 0.5 + 0.3, e.Y, e.Y, u);
                bootDiscT.OffsetZ = Bez(a.Z, a.Z * 0.5 + e.Z * 0.5, e.Z - 0.5, e.Z, u);
                bootDiscTilt.Angle = -30 * (1 - Smooth(Seg(t, 1.05, 2.0)));
                bootDiscSpin.Angle = 0;
            }

            bootPowerT.OffsetY = 0.025 * Pulse(t, 3.4, 3.7);
            if (t >= 3.5 && !bootSndPower) { bootSndPower = true; Sfx.Ok(); }
            if (bootLedImg != null) bootLedImg.Opacity = Smooth(Seg(t, 3.6, 3.95)); // layar menyala
        }

        // Layar PSP menyala memakai background game (atau cover kalau tidak ada background)
        ImageSource PspScreenArt(GameEntry g) { return ScreenArt(g, 0.439, 0.561); }

        // Layar menyala dengan background game; pita tekstur (v0, tinggi) yang dipakai UV layar
        ImageSource ScreenArt(GameEntry g, double v0, double vh)
        {
            try
            {
                PlatformCfg p = cfg.FindPlatform(g.Platform);
                string f = null;
                bool crisp;
                try { f = GameScanner.FindBackground(g, p, out crisp); } catch { }
                if (string.IsNullOrEmpty(f) || !File.Exists(f)) f = !string.IsNullOrEmpty(g.Background) && File.Exists(g.Background) ? g.Background : null;
                if (f == null) { try { f = GameScanner.FindCover(p, g.Path, g.Cover); } catch { } }
                if (f == null || !File.Exists(f)) return null;
                ImageSource img = ImgConv.LoadFile(f, 1280);
                if (img == null) return null;
                // UV layar memakai pita bawah tekstur (v 0.439 - 1.0), lebar penuh, tidak tercermin
                const int N = 1024;
                DrawingVisual dv = new DrawingVisual();
                using (DrawingContext dc = dv.RenderOpen())
                {
                    dc.DrawRectangle(Brushes.Black, null, new Rect(0, 0, N, N));
                    Rect scr = new Rect(0, N * v0, N, N * vh);
                    dc.PushClip(new RectangleGeometry(scr));
                    DrawImageFill(dc, img, scr, 1.0);
                    dc.Pop();
                }
                RenderTargetBitmap rtb = new RenderTargetBitmap(N, N, 96, 96, PixelFormats.Pbgra32);
                rtb.Render(dv);
                rtb.Freeze();
                return rtb;
            }
            catch { return null; }
        }

        // UMD: persegi (bidang XY, menghadap -Z) dengan gambar cangkang UMD bertransparansi
        static MeshGeometry3D umdMesh;
        static MeshGeometry3D UmdMesh()
        {
            if (umdMesh != null) return umdMesh;
            const double w = 0.29, h = 0.30;
            MeshGeometry3D m = new MeshGeometry3D();
            m.Positions = new Point3DCollection(new Point3D[] { new Point3D(-w, -h, 0), new Point3D(w, -h, 0), new Point3D(w, h, 0), new Point3D(-w, h, 0) });
            m.Normals = new Vector3DCollection(new Vector3D[] { new Vector3D(0, 0, -1), new Vector3D(0, 0, -1), new Vector3D(0, 0, -1), new Vector3D(0, 0, -1) });
            // dilihat dari -Z, sumbu X terbalik -> U dibalik supaya gambar tidak tercermin
            m.TextureCoordinates = new PointCollection(new Point[] { new Point(1, 1), new Point(0, 1), new Point(0, 0), new Point(1, 0) });
            m.TriangleIndices = new Int32Collection(new int[] { 0, 2, 1, 0, 3, 2 });
            m.Freeze();
            umdMesh = m;
            return m;
        }

        ImageSource UmdArt(GameEntry g)
        {
            ImageSource disc = DiscArt(g);
            const int N = 512;
            DrawingVisual dv = new DrawingVisual();
            using (DrawingContext dc = dv.RenderOpen())
            {
                Rect shell = new Rect(10, 30, N - 20, N - 40);
                LinearGradientBrush sb = new LinearGradientBrush(Rgb(196, 198, 206), Rgb(140, 142, 150), 90);
                sb.Freeze();
                dc.DrawRoundedRectangle(sb, new Pen(SolidOf(Rgb(100, 102, 110)), 4), shell, 58, 58);
                // tonjolan atas UMD
                dc.DrawRoundedRectangle(sb, null, new Rect(N * 0.30, 14, N * 0.40, 40), 16, 16);
                Point c = new Point(N / 2.0, N / 2.0 + 18);
                double r = N * 0.40;
                dc.PushClip(new EllipseGeometry(c, r, r));
                dc.DrawImage(disc, new Rect(c.X - r, c.Y - r, 2 * r, 2 * r));
                dc.Pop();
                dc.DrawEllipse(null, new Pen(SolidOf(Color.FromArgb(160, 255, 255, 255)), 3), c, r, r);
                dc.DrawEllipse(SolidOf(Rgb(170, 172, 180)), new Pen(SolidOf(Rgb(110, 112, 120)), 2), c, N * 0.06, N * 0.06);
            }
            RenderTargetBitmap rtb = new RenderTargetBitmap(N, N, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(dv);
            rtb.Freeze();
            return rtb;
        }

        void UpdateBootPs1(double t)
        {
            // 2. tombol OPEN ditekan, tutup terbuka dengan sedikit pantulan
            bootOpenT.OffsetY = -0.014 * Pulse(t, 0.55, 0.8);
            if (t >= 0.62 && !bootSndOpen) { bootSndOpen = true; Sfx.Tick(); }
            double lid;
            if (t < 2.35) lid = LidOpenDeg * EaseOutBack(Seg(t, 0.72, 1.55));
            else lid = LidOpenDeg * (1 - EaseInOut(Seg(t, 2.35, 2.85)));
            if (t >= 2.85) lid += 2.2 * Math.Sin(Math.PI * Seg(t, 2.85, 3.05)) ; // tutup memantul kecil
            bootLidRot.Angle = lid;
            if (t >= 2.85 && !bootSndClose) { bootSndClose = true; Sfx.Back(); }

            // 3. disc melayang turun mengikuti lengkung halus lalu duduk di spindle
            if (t < 1.1) bootDiscT.OffsetY = 100;
            else
            {
                double u = EaseInOut(Seg(t, 1.1, 2.3));
                bootDiscT.OffsetX = Bez(DiscStart.X, DiscStart.X * 0.55, DiscSeat.X + 0.25, DiscSeat.X, u);
                bootDiscT.OffsetY = Bez(DiscStart.Y, DiscStart.Y + 0.35, DiscSeat.Y + 0.55, DiscSeat.Y, u);
                bootDiscT.OffsetZ = Bez(DiscStart.Z, DiscStart.Z * 0.6, DiscSeat.Z + 0.05, DiscSeat.Z, u);
                bootDiscTilt.Angle = 46 * (1 - Smooth(Seg(t, 1.1, 2.25)));
                bootDiscSpin.Angle = -380 * EaseOut3(Seg(t, 1.1, 2.6));
            }

            // 4. tombol POWER, lampu menyala perlahan
            bootPowerT.OffsetY = -0.014 * Pulse(t, 3.05, 3.3);
            if (t >= 3.15 && !bootSndPower) { bootSndPower = true; Sfx.Ok(); }
            double led = Smooth(Seg(t, 3.18, 3.45));
            bootLedBrush.Color = Color.FromArgb((byte)(255 * led), 60, 255, 90);
        }

        // Selesai (atau dilewati): jalankan game, layar tetap hitam sebentar sampai emulator muncul
        void FinishBoot(bool skipped)
        {
            if (!bootActive) return;
            bootActive = false;
            CompositionTarget.Rendering -= bootTick;
            bootTick = null;
            if (bootVp != null) { bootLayer.Children.Remove(bootVp); bootVp = null; }
            bootHint.Opacity = 0;
            bootDim.Opacity = 1;
            bootFade.Opacity = 1;

            GameEntry g = bootGame;
            bootGame = null;
            bool prevResume = launchResume;
            launchResume = bootResume;
            try { LaunchNow(g, bootAdmin); }
            finally { launchResume = prevResume; }

            bootHoldTimer = new DispatcherTimer();
            bootHoldTimer.Interval = TimeSpan.FromSeconds(2.5);
            bootHoldTimer.Tick += delegate { EndBootLayer(); };
            bootHoldTimer.Start();
        }

        void EndBootLayer()
        {
            if (bootHoldTimer != null) { bootHoldTimer.Stop(); bootHoldTimer = null; }
            if (bootLayer != null) { root.Children.Remove(bootLayer); bootLayer = null; }
            bootLidRot = null; bootOpenT = null; bootPowerT = null; bootLedBrush = null; bootLedImg = null; bootTrayT = null; bootDoorRot = null;
        }

        // dipanggil dari HandleKey: true kalau input dipakai oleh animasi boot
        bool BootConsumeKey()
        {
            if (bootActive)
            {
                if (bootTime < 0.8) return true; // abaikan sisa tekanan tombol yang membuka game
                FinishBoot(true);
                return true;
            }
            if (bootLayer != null) { EndBootLayer(); return true; }
            return false;
        }
    }
}
