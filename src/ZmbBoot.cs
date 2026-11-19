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
            public ImageSource Tex;
        }

        static BootModel ps1Model;
        static bool ps1ModelTried;
        static MeshGeometry3D discMesh;

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
        Stopwatch bootClock;
        EventHandler bootTick;
        DispatcherTimer bootHoldTimer;
        GameEntry bootGame;
        bool bootAdmin, bootResume, bootActive, bootSndOpen, bootSndClose, bootSndPower;
        static readonly Point3D BootCamPos = new Point3D(4.2, 3.4, 2.85);
        static readonly Point3D BootCamTarget = new Point3D(0, 0.35, 0);

        bool BootAnimFor(GameEntry g)
        {
            if (!cfg.Ps1BootAnim || g == null) return false;
            if (!string.Equals(g.Platform, "PS1", StringComparison.OrdinalIgnoreCase)) return false;
            PlatformCfg p = cfg.FindPlatform(g.Platform);
            if (p == null || string.IsNullOrEmpty(p.Emulator) || !File.Exists(p.Emulator) || !File.Exists(g.Path)) return false;
            return Ps1Model() != null;
        }

        static BootModel Ps1Model()
        {
            if (ps1ModelTried) return ps1Model;
            ps1ModelTried = true;
            try { ps1Model = LoadConsoleModel("ps1_console"); }
            catch { ps1Model = null; }
            return ps1Model;
        }

        static BootModel LoadConsoleModel(string baseName)
        {
            byte[] bin = LoadAssetBytes(baseName + ".bin");
            if (bin == null) return null;
            BootModel m = new BootModel();
            m.Tex = LoadAsset(baseName + ".jpg");
            using (BinaryReader br = new BinaryReader(new MemoryStream(bin)))
            {
                if (new string(br.ReadChars(4)) != "ZMB3") return null;
                br.ReadInt32(); // versi
                int count = br.ReadInt32();
                for (int i = 0; i < count; i++)
                {
                    int id = br.ReadInt32();
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

        // Cincin disc (atas menghadap +Y), UV memetakan gambar persegi ke seluruh piringan
        static MeshGeometry3D DiscMesh()
        {
            if (discMesh != null) return discMesh;
            const double R = 0.72, r = 0.075;
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
            discMesh = m;
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
            BootModel bm = Ps1Model();
            bootGame = g; bootAdmin = forceAdmin; bootResume = launchResume;
            bootSndOpen = bootSndClose = bootSndPower = false;
            StopTrailer(true);
            StopThemeMusic();

            ImageBrush texBrush = new ImageBrush(bm.Tex);
            texBrush.ViewportUnits = BrushMappingMode.Absolute;
            texBrush.Viewport = new Rect(0, 0, 1, 1);
            texBrush.TileMode = TileMode.Tile;
            texBrush.Freeze();
            MaterialGroup mat = new MaterialGroup();
            mat.Children.Add(new DiffuseMaterial(texBrush));
            mat.Children.Add(new SpecularMaterial(SolidOf(Color.FromArgb(60, 255, 255, 255)), 28));
            mat.Freeze();

            Model3DGroup console = new Model3DGroup();
            foreach (KeyValuePair<int, MeshGeometry3D> kv in bm.Parts)
            {
                GeometryModel3D gm = new GeometryModel3D(kv.Value, mat);
                gm.BackMaterial = mat;
                if (kv.Key == PartLid)
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
            ImageBrush discBrush = new ImageBrush(DiscArt(g));
            discBrush.ViewportUnits = BrushMappingMode.Absolute;
            discBrush.Viewport = new Rect(0, 0, 1, 1);
            discBrush.Freeze();
            MaterialGroup dTop = new MaterialGroup();
            dTop.Children.Add(new DiffuseMaterial(discBrush));
            dTop.Children.Add(new SpecularMaterial(SolidOf(Color.FromArgb(140, 255, 255, 255)), 60));
            MaterialGroup dBot = new MaterialGroup(); // sisi data PS1: hitam mengilap
            dBot.Children.Add(new DiffuseMaterial(SolidOf(Rgb(16, 16, 20))));
            dBot.Children.Add(new SpecularMaterial(SolidOf(Color.FromArgb(200, 170, 150, 255)), 40));
            GeometryModel3D disc = new GeometryModel3D(DiscMesh(), dTop);
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

            bootCam = new PerspectiveCamera(BootCamPos, BootCamTarget - BootCamPos, new Vector3D(0, 1, 0), 46);
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
            bootHint.Text = "Tekan tombol apa saja untuk melewati";
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
            root.Children.Add(bootLayer);

            bootActive = true;
            UpdateBoot(0);
            bootClock = Stopwatch.StartNew();
            bootTick = delegate { if (bootActive) UpdateBoot(bootClock.Elapsed.TotalSeconds); };
            CompositionTarget.Rendering += bootTick;
        }

        static double Clamp01(double v) { return v < 0 ? 0 : (v > 1 ? 1 : v); }
        static double Seg(double t, double a, double b) { return Clamp01((t - a) / (b - a)); }
        static double EaseOut3(double u) { u = 1 - u; return 1 - u * u * u; }
        static double EaseIn2(double u) { return u * u; }
        static double EaseInOut(double u) { return u < 0.5 ? 4 * u * u * u : 1 - Math.Pow(-2 * u + 2, 3) / 2; }
        static double EaseOutBack(double u) { const double c1 = 1.4, c3 = c1 + 1; double v = u - 1; return 1 + c3 * v * v * v + c1 * v * v; }
        static double Pulse(double t, double a, double b) { double u = Seg(t, a, b); return (u <= 0 || u >= 1) ? 0 : Math.Sin(Math.PI * u); }

        void UpdateBoot(double t)
        {
            // 1. konsol muncul
            double appear = EaseOut3(Seg(t, 0, 0.75));
            bootDim.Opacity = Clamp01(t / 0.45);
            bootVp.Opacity = appear;
            bootHint.Opacity = Seg(t, 0.8, 1.3) * (1 - Seg(t, 3.3, 3.6));
            double sc = 0.84 + 0.16 * appear;
            bootScale.ScaleX = sc; bootScale.ScaleY = sc; bootScale.ScaleZ = sc;
            bootYaw.Angle = -30 * (1 - appear) - 4 * Seg(t, 0.7, 4.2);

            // 2. tombol OPEN ditekan, tutup terbuka
            bootOpenT.OffsetY = -0.014 * Pulse(t, 0.55, 0.78);
            if (t >= 0.62 && !bootSndOpen) { bootSndOpen = true; Sfx.Tick(); }
            double lid = 0;
            if (t >= 0.75 && t < 1.45) lid = LidOpenDeg * EaseOutBack(Seg(t, 0.75, 1.45));
            else if (t >= 1.45 && t < 2.35) lid = LidOpenDeg;
            else if (t >= 2.35 && t < 2.78) lid = LidOpenDeg * (1 - EaseIn2(Seg(t, 2.35, 2.78)));
            else if (t >= 2.78 && t < 2.96) lid = 3.5 * Math.Sin(Math.PI * Seg(t, 2.78, 2.96));
            bootLidRot.Angle = lid;
            if (t >= 2.78 && !bootSndClose) { bootSndClose = true; Sfx.Back(); }

            // 3. disc masuk ke dalam konsol
            if (t < 1.2) bootDiscT.OffsetY = 100;
            else
            {
                double u = EaseInOut(Seg(t, 1.2, 2.25));
                double arc = Math.Sin(Math.PI * u) * 0.35 * (1 - u);
                bootDiscT.OffsetX = DiscStart.X + (DiscSeat.X - DiscStart.X) * u;
                bootDiscT.OffsetY = DiscStart.Y + (DiscSeat.Y - DiscStart.Y) * u + arc;
                bootDiscT.OffsetZ = DiscStart.Z + (DiscSeat.Z - DiscStart.Z) * u;
                bootDiscTilt.Angle = 48 * Math.Pow(1 - u, 1.6);
                bootDiscSpin.Angle = -420 * EaseOut3(Seg(t, 1.2, 2.4));
            }

            // 4. tombol POWER, lampu menyala
            bootPowerT.OffsetY = -0.014 * Pulse(t, 3.0, 3.22);
            if (t >= 3.1 && !bootSndPower) { bootSndPower = true; Sfx.Ok(); }
            double led = Seg(t, 3.12, 3.3);
            bootLedBrush.Color = Color.FromArgb((byte)(255 * led), 60, 255, 90);

            // 5. kamera mendekat, layar hitam
            double push = EaseIn2(Seg(t, 3.4, 4.15));
            Point3D cp = BootCamPos + (BootCamTarget - BootCamPos) * (0.28 * push);
            bootCam.Position = cp;
            bootCam.LookDirection = BootCamTarget - cp;
            bootFade.Opacity = EaseIn2(Seg(t, 3.45, 4.1));

            if (t >= 4.2) FinishBoot(false);
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
            bootLidRot = null; bootOpenT = null; bootPowerT = null; bootLedBrush = null;
        }

        // dipanggil dari HandleKey: true kalau input dipakai oleh animasi boot
        bool BootConsumeKey()
        {
            if (bootActive) { FinishBoot(true); return true; }
            if (bootLayer != null) { EndBootLayer(); return true; }
            return false;
        }
    }
}
