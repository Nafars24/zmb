using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace ZMB
{
    public partial class ZmbWindow
    {
        void BuildCategories()
        {
            cats.Clear();
            cats.Add(new Category(Lang.T("Pengguna"), "\uE77B", BuildUserItems));
            cats.Add(new Category(Lang.T("Pengaturan"), "\uE713", BuildSettingsItems));
            cats.Add(new Category(Lang.T("Foto"), "\uEB9F", BuildPhotoItems));
            cats.Add(new Category(Lang.T("Musik"), "\uEC4F", BuildMusicItems));
            cats.Add(new Category(Lang.T("Video"), "\uE714", BuildVideoItems));
            cats.Add(new Category(Lang.T("Game"), "\uE7FC", BuildGameItems));
            for (int ci = 0; ci < cats.Count && ci < CatKeys.Length; ci++) cats[ci].IconKey = CatKeys[ci];
            foreach (Category c in cats) c.Items = SafeBuild(c.Build);
            Category game = cats[5];
            for (int i = 0; i < game.Items.Count; i++) if (game.Items[i].Title == "Windows") { game.Index = i; game.Anim = i; break; }
        }

        // ===================== PENGGUNA / SISTEM =====================
        List<XmbItem> BuildUserItems()
        {
            List<XmbItem> l = new List<XmbItem>();
            int h = DateTime.Now.Hour;
            string greet = h < 11 ? Lang.T("Selamat pagi") : h < 15 ? Lang.T("Selamat siang") : h < 18 ? Lang.T("Selamat sore") : Lang.T("Selamat malam");
            XmbItem me = MakeItem(Environment.UserName, "", "\uE77B", null);
            me.DynSub = delegate
            {
                int hh = DateTime.Now.Hour;
                string gg = hh < 11 ? Lang.T("Selamat pagi") : hh < 15 ? Lang.T("Selamat siang") : hh < 18 ? Lang.T("Selamat sore") : Lang.T("Selamat malam");
                return gg + "! " + DateTime.Now.ToString("dddd, dd MMMM yyyy");
            };
            me.Activate = OpenProfile;
            me.Subtitle = Lang.T("Profil & statistik main");
            l.Add(me);
            l.Add(MakeItem(Lang.T("Profil Saya"), Lang.T("Total jam main, game favorit, grafik mingguan, level & trofi"), "\uE77B", OpenProfile));
            l.Add(MakeItem(Lang.T("Matikan Laptop"), "Shutdown Windows", "\uE7E8", delegate
            {
                Confirm(Lang.T("Matikan laptop sekarang?"), delegate { cfg.Save(); RunQuiet("shutdown", "/s /t 0"); });
            }));
            l.Add(MakeItem("Restart", Lang.T("Mulai ulang Windows"), "\uE777", delegate
            {
                Confirm(Lang.T("Restart laptop sekarang?"), delegate { cfg.Save(); RunQuiet("shutdown", "/r /t 0"); });
            }));
            l.Add(MakeItem("Sleep", Lang.T("Mode tidur"), "\uE708", delegate
            {
                Confirm(Lang.T("Masuk mode sleep?"), delegate { System.Windows.Forms.Application.SetSuspendState(System.Windows.Forms.PowerState.Suspend, false, false); });
            }));
            l.Add(MakeItem(Lang.T("Kunci Layar"), "Lock Windows", "\uE72E", delegate { Native.LockWorkStation(); }));
            l.Add(MakeItem("Minimize Launcher", Lang.T("Kembali ke desktop (gamepad: tahan BACK+START untuk kembali)"), "\uE921", delegate { WindowState = WindowState.Minimized; }));
            l.Add(MakeItem(Lang.T("Keluar dari ZMB"), Lang.T("Tutup ZMB"), "\uE711", delegate
            {
                Confirm(Lang.T("Keluar dari ZMB?"), delegate { Close(); });
            }));
            l.Add(MakeItem("File Explorer", Lang.T("Buka File Explorer"), "\uE838", delegate { RunQuiet("explorer.exe", ""); }));
            l.Add(MakeItem(Lang.T("Browser Internet"), Lang.T("Buka browser default"), "\uE774", delegate { OpenDefault("https://www.google.com"); }));
            l.Add(MakeItem(Lang.T("Pengaturan Windows"), Lang.T("Buka Settings Windows"), "\uE713", delegate { OpenDefault("ms-settings:"); }));
            l.Add(MakeItem("Task Manager", Lang.T("Lihat program yang berjalan"), "\uE9D9", delegate { RunQuiet("taskmgr.exe", ""); }));
            return l;
        }

        void RunQuiet(string file, string args)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(file, args);
                psi.UseShellExecute = true;
                if (file == "shutdown") { psi.UseShellExecute = false; psi.CreateNoWindow = true; }
                Process.Start(psi);
            }
            catch (Exception ex) { ShowToast(Lang.T("Gagal: ") + ex.Message); }
        }

        // ===================== PENGATURAN =====================
        XmbItem Toggle(string title, string desc, string glyph, Func<bool> get, Action<bool> set)
        {
            XmbItem it = MakeItem(title, "", glyph, null);
            it.DynSub = delegate { return (get() ? Lang.T("Aktif") : Lang.T("Nonaktif")) + (string.IsNullOrEmpty(desc) ? "" : "  \u2022  " + desc); };
            it.Activate = delegate { set(!get()); cfg.Save(); };
            it.Options = delegate
            {
                List<OptionItem> o = new List<OptionItem>();
                o.Add(new OptionItem(Lang.T("Aktif"), delegate { set(true); cfg.Save(); }, get()));
                o.Add(new OptionItem(Lang.T("Nonaktif"), delegate { set(false); cfg.Save(); }, !get()));
                return o;
            };
            return it;
        }

        XmbItem FolderSetting(string title, string glyph, Func<string> get, Action<string> set)
        {
            XmbItem it = MakeItem(title, "", glyph, null);
            it.DynSub = delegate { return get(); };
            it.Activate = delegate
            {
                string f = Dialogs.PickFolder(Owner32, Lang.T("Pilih ") + title, get());
                if (f != null) { set(f); SaveAndRefresh(); ShowToast(title + Lang.T(" diganti")); }
            };
            it.Options = delegate
            {
                List<OptionItem> o = new List<OptionItem>();
                o.Add(new OptionItem(Lang.T("Ganti Folder..."), it.Activate));
                o.Add(new OptionItem(Lang.T("Buka di Explorer"), delegate { ShowInExplorer(get()); }));
                return o;
            };
            return it;
        }

        // Pengaturan dikelompokkan supaya tidak memanjang ke bawah; isi tiap grup diambil dari daftar lengkap
        static readonly string[][] SettingGroups = {
            new[] { Lang.T("Tampilan"), "\uE790", Lang.T("Mode, tema, latar, gelombang, partikel, ukuran, layar penuh"),
                    Lang.T("Mode Tampilan"), Lang.T("Paket Tema"), Lang.T("Tema Warna"), Lang.T("Latar Belakang"), Lang.T("Gelombang"), Lang.T("Partikel"), Lang.T("Ukuran Tampilan"), Lang.T("Layar Penuh"), Lang.T("Jam 24 Jam"), Lang.T("Animasi Pembuka"), "Screensaver" },
            new[] { Lang.T("Suara & Musik"), "\uE767", Lang.T("Efek suara, volume, musik tema game"),
                    Lang.T("Efek Suara Menu"), Lang.T("Volume Musik & Video"), Lang.T("Musik Tema Game"), Lang.T("Jeda Musik Saat Game Jalan") },
            new[] { Lang.T("Latar & Trailer Game"), "\uE714", Lang.T("Gambar latar dan trailer saat game dipilih"),
                    Lang.T("Latar Gambar Game"), Lang.T("Trailer di Latar"), "Trailer Online (Steam)", Lang.T("Jeda Sebelum Trailer") },
            new[] { Lang.T("Saat Main Game"), "\uE7FC", Lang.T("Layar penuh, animasi boot konsol, notifikasi trofi"),
                    Lang.T("Auto Layar Penuh (Emulator)"), Lang.T("Animasi Boot Konsol"), Lang.T("Notifikasi Trofi Saat Main"), Lang.T("Minimize Saat Game Jalan"), Lang.T("Tombol Home Keyboard") },
            new[] { Lang.T("Game & Emulator"), "\uE7FC", Lang.T("Platform, emulator, Steam, media otomatis, akun trofi, pemeriksa ROM"),
                    Lang.T("Emulator & Platform"), Lang.T("Tampilkan Game Steam"), Lang.T("Unduh Media Otomatis"), Lang.T("Akun Trofi & Achievement"), Lang.T("Pemeriksa ROM") },
            new[] { Lang.T("Folder Media"), "\uEC4F", Lang.T("Folder musik, video, foto, durasi slideshow"),
                    Lang.T("Folder Musik"), Lang.T("Folder Video"), Lang.T("Folder Foto"), Lang.T("Durasi Slideshow") },
            new[] { Lang.T("Sistem"), "\uE713", Lang.T("Update, jalan saat Windows menyala, bantuan, tentang"),
                    Lang.T("Bahasa / Language"), "Update ZMB", Lang.T("Jalan Saat Windows Menyala"), Lang.T("Kontrol & Bantuan"), Lang.T("Tentang ZMB") },
        };

        List<XmbItem> BuildSettingsItems()
        {
            List<XmbItem> l = new List<XmbItem>();
            HashSet<string> grouped = new HashSet<string>();
            foreach (string[] g in SettingGroups)
            {
                string[] grp = g;
                for (int k = 3; k < grp.Length; k++) grouped.Add(grp[k]);
                XmbItem gi = MakeItem(grp[0], grp[2], grp[1], null);
                gi.Children = delegate
                {
                    List<XmbItem> all = BuildSettingsFlat();
                    List<XmbItem> sub = new List<XmbItem>();
                    for (int k = 3; k < grp.Length; k++)
                    {
                        XmbItem f = all.FirstOrDefault(x => x.Title == grp[k]);
                        if (f != null) sub.Add(f);
                    }
                    return sub;
                };
                l.Add(gi);
            }
            // menu yang belum masuk grup tetap tampil di bawah (supaya tidak ada yang hilang)
            foreach (XmbItem x in BuildSettingsFlat())
                if (!grouped.Contains(x.Title)) l.Add(x);
            return l;
        }

        // Ganti bahasa: simpan, lalu tawarkan buka ulang ZMB (teks menu dibuat saat program mulai)
        void SetLanguage(string code)
        {
            if (cfg.Language == code) return;
            cfg.Language = code;
            cfg.Save();
            string msg = code == "en" ? Lang.T("Restart ZMB now to switch to English?") : "Buka ulang ZMB sekarang untuk memakai Bahasa Indonesia?";
            Confirm(msg, delegate
            {
                try
                {
                    System.Diagnostics.Process.Start(System.Reflection.Assembly.GetExecutingAssembly().Location, "--restart");
                    System.Windows.Application.Current.Shutdown();
                }
                catch (Exception ex) { ShowToast(ex.Message); }
            });
        }

        List<XmbItem> BuildSettingsFlat()
        {
            List<XmbItem> l = new List<XmbItem>();
            XmbItem lang = MakeItem(Lang.T("Bahasa / Language"), "", "\uE774", null);
            lang.DynSub = delegate { return cfg.Language == "en" ? "English" : "Bahasa Indonesia"; };
            lang.Options = delegate
            {
                List<OptionItem> o = new List<OptionItem>();
                o.Add(new OptionItem("Bahasa Indonesia", delegate { SetLanguage("id"); }, cfg.Language != "en"));
                o.Add(new OptionItem("English", delegate { SetLanguage("en"); }, cfg.Language == "en"));
                return o;
            };
            lang.Activate = delegate { OpenOptions(Lang.T("Bahasa / Language"), lang.Options()); };
            l.Add(lang);

            XmbItem vm = MakeItem(Lang.T("Mode Tampilan"), "", "\uE8A9", null);
            vm.DynSub = delegate { return (cfg.ViewMode == 1 ? Lang.T("Aurora (dashboard Xbox 360 + rak coverflow)") : Lang.T("XMB (ala PS3)")) + Lang.T("  \u2022  F2 untuk ganti cepat"); };
            vm.Options = delegate
            {
                List<OptionItem> o = new List<OptionItem>();
                o.Add(new OptionItem(Lang.T("XMB (ala PS3)"), delegate { if (cfg.ViewMode != 0) ToggleViewMode(); }, cfg.ViewMode == 0));
                o.Add(new OptionItem(Lang.T("Coverflow (ala Aurora Xbox 360)"), delegate { if (cfg.ViewMode != 1) ToggleViewMode(); }, cfg.ViewMode == 1));
                return o;
            };
            vm.Activate = delegate { OpenOptions(Lang.T("Mode Tampilan"), vm.Options()); };
            l.Add(vm);

            XmbItem theme = MakeItem(Lang.T("Tema Warna"), "", "\uE790", null);
            theme.DynSub = delegate { return ThemeNames[Math.Max(0, Math.Min(ThemeNames.Length - 1, cfg.Theme))]; };
            theme.Options = delegate
            {
                List<OptionItem> o = new List<OptionItem>();
                for (int i = 0; i < ThemeNames.Length; i++)
                {
                    int ti = i;
                    o.Add(new OptionItem(ThemeNames[i], delegate { cfg.Theme = ti; cfg.Save(); backImg = null; backKey = ""; }, cfg.Theme == i));
                }
                return o;
            };
            theme.Activate = delegate { OpenOptions(Lang.T("Tema Warna"), theme.Options()); };
            l.Add(theme);

            XmbItem bgi = MakeItem(Lang.T("Latar Belakang"), "", "\uE91B", null);
            bgi.DynSub = delegate { return string.IsNullOrEmpty(cfg.BackgroundImage) ? Lang.T("Gradasi warna tema (klasik)") : Path.GetFileName(cfg.BackgroundImage); };
            bgi.Options = delegate
            {
                List<OptionItem> o = new List<OptionItem>();
                o.Add(new OptionItem(Lang.T("Klasik (warna tema)"), delegate { cfg.BackgroundImage = ""; cfg.Save(); backImg = null; backKey = ""; }, string.IsNullOrEmpty(cfg.BackgroundImage)));
                o.Add(new OptionItem(Lang.T("Pilih Gambar..."), delegate
                {
                    string f = Dialogs.PickFile(Owner32, Lang.T("Pilih gambar latar"), Lang.T("Gambar|*.jpg;*.jpeg;*.png;*.bmp"), cfg.PhotoFolder);
                    if (f != null) { cfg.BackgroundImage = f; cfg.Save(); backImg = null; backKey = ""; }
                }));
                return o;
            };
            bgi.Activate = delegate { OpenOptions(Lang.T("Latar Belakang"), bgi.Options()); };
            l.Add(bgi);

            l.Add(Toggle(Lang.T("Efek Suara Menu"), "", "\uE767", delegate { return cfg.Sounds; }, delegate (bool v) { cfg.Sounds = v; Sfx.Enabled = v; }));

            XmbItem vol = MakeItem(Lang.T("Volume Musik & Video"), "", "\uE767", null);
            vol.DynSub = delegate { return cfg.Volume + "%"; };
            vol.Options = delegate
            {
                List<OptionItem> o = new List<OptionItem>();
                for (int v = 100; v >= 0; v -= 10)
                {
                    int vv = v;
                    o.Add(new OptionItem(v + "%", delegate { cfg.Volume = vv; music.Volume = vv; music.ApplyVolume(); cfg.Save(); }, cfg.Volume == v));
                }
                return o;
            };
            vol.Activate = delegate { OpenOptions("Volume", vol.Options()); };
            l.Add(vol);

            XmbItem scale = MakeItem(Lang.T("Ukuran Tampilan"), "", "\uE740", null);
            scale.DynSub = delegate { return (int)Math.Round(cfg.UiScale * 100) + "%"; };
            scale.Options = delegate
            {
                List<OptionItem> o = new List<OptionItem>();
                int[] vals = { 75, 85, 100, 110, 125, 140 };
                foreach (int v in vals)
                {
                    int vv = v;
                    o.Add(new OptionItem(v + "%", delegate { cfg.UiScale = vv / 100f; cfg.Save(); textCache.Clear(); }, (int)Math.Round(cfg.UiScale * 100) == v));
                }
                return o;
            };
            scale.Activate = delegate { OpenOptions(Lang.T("Ukuran Tampilan"), scale.Options()); };
            l.Add(scale);

            l.Add(Toggle(Lang.T("Layar Penuh"), Lang.T("F11 untuk ganti cepat"), "\uE740", delegate { return cfg.Fullscreen; }, delegate (bool v) { cfg.Fullscreen = v; ApplyFullscreen(); }));
            l.Add(Toggle(Lang.T("Animasi Pembuka"), Lang.T("Intro saat launcher dibuka"), "\uE768", delegate { return cfg.BootAnimation; }, delegate (bool v) { cfg.BootAnimation = v; }));
            l.Add(Toggle(Lang.T("Jam 24 Jam"), "", "\uE823", delegate { return cfg.Clock24; }, delegate (bool v) { cfg.Clock24 = v; }));
            l.Add(Toggle(Lang.T("Minimize Saat Game Jalan"), Lang.T("Launcher otomatis kembali setelah game ditutup"), "\uE921", delegate { return cfg.MinimizeOnLaunch; }, delegate (bool v) { cfg.MinimizeOnLaunch = v; }));
            l.Add(Toggle(Lang.T("Jeda Musik Saat Game Jalan"), "", "\uE769", delegate { return cfg.PauseMusicOnLaunch; }, delegate (bool v) { cfg.PauseMusicOnLaunch = v; }));
            l.Add(Toggle(Lang.T("Tampilkan Game Steam"), Lang.T("Deteksi otomatis library Steam"), "\uE7FC", delegate { return cfg.ShowSteam; }, delegate (bool v) { cfg.ShowSteam = v; RefreshAll(); }));
            l.Add(Toggle(Lang.T("Latar Gambar Game"), Lang.T("Tampilkan gambar game di belakang menu saat dipilih"), "\uE91B", delegate { return cfg.GameBackground; }, delegate (bool v) { cfg.GameBackground = v; focusKey = null; }));
            XmbItem trl = MakeItem(Lang.T("Trailer di Latar"), "", "\uE714", null);
            trl.DynSub = delegate { return !cfg.Trailers ? Lang.T("Mati") : cfg.TrailerSound ? Lang.T("Aktif, dengan suara (musik dikecilkan)") : Lang.T("Aktif, tanpa suara"); };
            trl.Options = delegate
            {
                List<OptionItem> o = new List<OptionItem>();
                o.Add(new OptionItem(Lang.T("Aktif, dengan suara"), delegate { cfg.Trailers = true; cfg.TrailerSound = true; cfg.Save(); }, cfg.Trailers && cfg.TrailerSound));
                o.Add(new OptionItem(Lang.T("Aktif, tanpa suara"), delegate { cfg.Trailers = true; cfg.TrailerSound = false; cfg.Save(); }, cfg.Trailers && !cfg.TrailerSound));
                o.Add(new OptionItem(Lang.T("Mati"), delegate { cfg.Trailers = false; StopTrailer(true); cfg.Save(); }, !cfg.Trailers));
                return o;
            };
            trl.Activate = delegate { OpenOptions(Lang.T("Trailer di Latar"), trl.Options()); };
            l.Add(trl);
            l.Add(Toggle("Trailer Online (Steam)", Lang.T("Ambil trailer game Steam dari internet jika tidak ada file lokal"), "\uE774", delegate { return cfg.OnlineTrailers; }, delegate (bool v) { cfg.OnlineTrailers = v; }));
            XmbItem tdel = MakeItem(Lang.T("Jeda Sebelum Trailer"), "", "\uE916", null);
            tdel.DynSub = delegate { return cfg.TrailerDelay.ToString("0.0") + Lang.T(" detik setelah game dipilih"); };
            tdel.Options = delegate
            {
                List<OptionItem> o = new List<OptionItem>();
                float[] vals = { 0.5f, 1f, 1.5f, 2.5f, 4f };
                foreach (float v in vals) { float vv = v; o.Add(new OptionItem(v.ToString("0.0") + Lang.T(" detik"), delegate { cfg.TrailerDelay = vv; cfg.Save(); }, Math.Abs(cfg.TrailerDelay - v) < 0.01)); }
                return o;
            };
            tdel.Activate = delegate { OpenOptions(Lang.T("Jeda Sebelum Trailer"), tdel.Options()); };
            l.Add(tdel);
            l.Add(Toggle(Lang.T("Jalan Saat Windows Menyala"), Lang.T("Buka launcher otomatis saat login"), "\uE7E8", delegate { return GetAutoStart(); }, delegate (bool v) { SetAutoStart(v); }));

            l.Add(FolderSetting(Lang.T("Folder Musik"), "\uEC4F", delegate { return cfg.MusicFolder; }, delegate (string v) { cfg.MusicFolder = v; }));
            l.Add(FolderSetting(Lang.T("Folder Video"), "\uE714", delegate { return cfg.VideoFolder; }, delegate (string v) { cfg.VideoFolder = v; }));
            l.Add(FolderSetting(Lang.T("Folder Foto"), "\uEB9F", delegate { return cfg.PhotoFolder; }, delegate (string v) { cfg.PhotoFolder = v; }));

            XmbItem slide = MakeItem(Lang.T("Durasi Slideshow"), "", "\uE786", null);
            slide.DynSub = delegate { return cfg.SlideSeconds + Lang.T(" detik per foto"); };
            slide.Options = delegate
            {
                List<OptionItem> o = new List<OptionItem>();
                int[] vals = { 3, 5, 8, 10, 15, 30 };
                foreach (int v in vals) { int vv = v; o.Add(new OptionItem(v + Lang.T(" detik"), delegate { cfg.SlideSeconds = vv; cfg.Save(); }, cfg.SlideSeconds == v)); }
                return o;
            };
            slide.Activate = delegate { OpenOptions(Lang.T("Durasi Slideshow"), slide.Options()); };
            l.Add(slide);

            XmbItem emu = MakeItem(Lang.T("Emulator & Platform"), Lang.T("Atur emulator, folder ROM, argumen"), "\uE7FC", null);
            emu.Children = BuildPlatformManager;
            l.Add(emu);

            XmbItem tp = MakeItem(Lang.T("Paket Tema"), "", "\uE790", null);
            tp.DynSub = delegate { return string.IsNullOrEmpty(cfg.ThemePack) ? (Directory.Exists(Path.Combine(Config.BaseDir, "theme")) ? Lang.T("Bawaan + folder theme") : Lang.T("Bawaan")) : cfg.ThemePack; };
            tp.Options = delegate
            {
                List<OptionItem> o = new List<OptionItem>();
                o.Add(new OptionItem(Lang.T("Bawaan"), delegate { cfg.ThemePack = ""; cfg.Save(); ApplyThemePack(); }, string.IsNullOrEmpty(cfg.ThemePack)));
                foreach (string pk in ThemePacks()) { string pp = pk; o.Add(new OptionItem(pk, delegate { cfg.ThemePack = pp; cfg.Save(); ApplyThemePack(); }, cfg.ThemePack == pk)); }
                o.Add(new OptionItem(Lang.T("Buka Folder Tema Kustom"), EnsureThemeFolder));
                o.Add(new OptionItem(Lang.T("Muat Ulang Tema"), delegate { ApplyThemePack(); ShowToast(Lang.T("Tema dimuat ulang")); }));
                return o;
            };
            tp.Activate = delegate { OpenOptions(Lang.T("Paket Tema"), tp.Options()); };
            l.Add(tp);

            XmbItem ws = MakeItem(Lang.T("Gelombang"), "", "\uE706", null);
            ws.DynSub = delegate { return !cfg.ShowWaves ? Lang.T("Mati") : cfg.WaveStyle == 1 ? Lang.T("Garis tipis") : Lang.T("Klasik PS3"); };
            ws.Options = delegate
            {
                List<OptionItem> o = new List<OptionItem>();
                o.Add(new OptionItem(Lang.T("Klasik PS3"), delegate { cfg.ShowWaves = true; cfg.WaveStyle = 0; cfg.Save(); }, cfg.ShowWaves && cfg.WaveStyle == 0));
                o.Add(new OptionItem(Lang.T("Garis tipis"), delegate { cfg.ShowWaves = true; cfg.WaveStyle = 1; cfg.Save(); }, cfg.ShowWaves && cfg.WaveStyle == 1));
                o.Add(new OptionItem(Lang.T("Mati"), delegate { cfg.ShowWaves = false; cfg.Save(); }, !cfg.ShowWaves));
                return o;
            };
            ws.Activate = delegate { OpenOptions(Lang.T("Gelombang"), ws.Options()); };
            l.Add(ws);

            XmbItem pt = MakeItem(Lang.T("Partikel"), "", "\uE9CA", null);
            string[] pNames = { Lang.T("Otomatis (salju di bulan Desember)"), Lang.T("Kilau"), Lang.T("Salju"), Lang.T("Mati") };
            pt.DynSub = delegate { return pNames[Math.Max(0, Math.Min(3, cfg.Particles))]; };
            pt.Options = delegate
            {
                List<OptionItem> o = new List<OptionItem>();
                for (int i = 0; i < 4; i++) { int ii = i; o.Add(new OptionItem(pNames[i], delegate { cfg.Particles = ii; cfg.Save(); }, cfg.Particles == i)); }
                return o;
            };
            pt.Activate = delegate { OpenOptions(Lang.T("Partikel"), pt.Options()); };
            l.Add(pt);

            XmbItem ss = MakeItem("Screensaver", "", "\uE708", null);
            ss.DynSub = delegate { return cfg.ScreensaverMin <= 0 ? Lang.T("Mati") : Lang.T("Setelah ") + cfg.ScreensaverMin + Lang.T(" menit tanpa input (slideshow latar game)"); };
            ss.Options = delegate
            {
                List<OptionItem> o = new List<OptionItem>();
                int[] vals = { 0, 2, 5, 10, 20, 30 };
                foreach (int v in vals) { int vv = v; o.Add(new OptionItem(v == 0 ? Lang.T("Mati") : v + Lang.T(" menit"), delegate { cfg.ScreensaverMin = vv; cfg.Save(); }, cfg.ScreensaverMin == v)); }
                o.Add(new OptionItem(Lang.T("Coba Sekarang"), delegate { StartScreensaver(); }));
                return o;
            };
            ss.Activate = delegate { OpenOptions("Screensaver", ss.Options()); };
            l.Add(ss);

            l.Add(Toggle(Lang.T("Auto Layar Penuh (Emulator)"), Lang.T("Game konsol selalu dibuka layar penuh: argumen fullscreen emulator, atau jendela dipaksa borderless menutup layar"), "\uE7FC", delegate { return cfg.AutoFullscreen; }, delegate (bool v) { cfg.AutoFullscreen = v; }));
            l.Add(Toggle(Lang.T("Notifikasi Trofi Saat Main"), Lang.T("Popup trofi ala PlayStation di kanan bawah layar, di atas game yang sedang jalan"), "\uE7FC", delegate { return cfg.TrophyOverlay; }, delegate (bool v) { cfg.TrophyOverlay = v; if (!v) StopAchvPolling(); }));
            l.Add(Toggle(Lang.T("Animasi Boot Konsol"), Lang.T("Konsol 3D PS1/PS2/PS3/PSP/Switch memasukkan disc/UMD/cartridge game lalu menyala sebelum game jalan"), "\uE7FC", delegate { return cfg.Ps1BootAnim; }, delegate (bool v) { cfg.Ps1BootAnim = v; }));
            l.Add(Toggle(Lang.T("Musik Tema Game"), Lang.T("Putar musik tema saat game dipilih (file di folder music)"), "\uE8D6", delegate { return cfg.ThemeMusicOn; }, delegate (bool v) { cfg.ThemeMusicOn = v; if (!v) StopThemeMusic(); focusKey = null; }));
            l.Add(Toggle(Lang.T("Tombol Home Keyboard"), Lang.T("Ctrl+Shift+Home membuka Menu Home dari dalam game"), "\uE80F", delegate { return cfg.HomeHotkey; }, delegate (bool v) { cfg.HomeHotkey = v; ShowToast(Lang.T("Berlaku setelah ZMB dibuka ulang")); }));

            XmbItem rc = MakeItem(Lang.T("Pemeriksa ROM"), Lang.T("Cari file rusak, duplikat, dan game tanpa cover/box art/latar"), "\uE9D9", null);
            rc.Children = BuildRomCheck;
            l.Add(rc);

            XmbItem up = MakeItem("Update ZMB", "", "\uE895", null);
            up.DynSub = delegate { return Lang.T("Versi ") + Updater.Current + Lang.T("  \u2022  pasang versi baru dari file ZIP tanpa BUILD.bat"); };
            up.Children = BuildUpdateItems;
            l.Add(up);

            XmbItem scr = MakeItem(Lang.T("Unduh Media Otomatis"), Lang.T("Cover, latar, logo & video dari SteamGridDB dan ScreenScraper"), "\uE896", null);
            scr.Children = BuildScrapeMenu;
            l.Add(scr);

            XmbItem accs = MakeItem(Lang.T("Akun Trofi & Achievement"), Lang.T("Steam API Key, RetroAchievements, folder RPCS3"), "#TROPHY", null);
            accs.Children = BuildAchvAccount;
            l.Add(accs);

            XmbItem help = MakeItem(Lang.T("Kontrol & Bantuan"), Lang.T("Cara pakai keyboard, mouse, dan gamepad"), "\uE946", null);
            help.Children = BuildHelpItems;
            l.Add(help);

            XmbItem about = MakeItem(Lang.T("Tentang ZMB"), Lang.T("Versi ") + typeof(ZmbWindow).Assembly.GetName().Version.ToString(3) + Lang.T("  \u2022  Model 3D PS one: Vladimir_Sofyanov (Sketchfab, CC-BY 4.0)  \u2022  Enter: buka folder data"), "\uE946", delegate { ShowInExplorer(Config.FilePath); });
            l.Add(about);
            return l;
        }

        List<XmbItem> BuildHelpItems()
        {
            List<XmbItem> l = new List<XmbItem>();
            l.Add(MakeItem(Lang.T("Navigasi"), Lang.T("Panah / WASD / D-Pad / Stik kiri / Scroll mouse"), "\uE759", null));
            l.Add(MakeItem(Lang.T("Pilih"), Lang.T("Enter / Tombol A (Xbox) / Silang (PlayStation) / Klik kiri"), "\uE73E", null));
            l.Add(MakeItem(Lang.T("Kembali"), Lang.T("Esc / Backspace / Tombol B (Xbox) / Bulat (PlayStation) / Klik kanan di video"), "\uE72B", null));
            l.Add(MakeItem(Lang.T("Menu Opsi (Segitiga)"), Lang.T("Tab / T / Tombol Y / Klik kanan"), "\uE712", null));
            l.Add(MakeItem(Lang.T("Musik: Putar/Jeda"), Lang.T("Spasi / Tombol START / Tombol media"), "\uE768", null));
            l.Add(MakeItem(Lang.T("Musik: Lagu Sebelum/Berikut"), Lang.T("[ dan ]  /  LB dan RB"), "\uE893", null));
            l.Add(MakeItem(Lang.T("Lompat Cepat"), Lang.T("Page Up / Page Down  /  LT dan RT"), "\uE74A", null));
            l.Add(MakeItem(Lang.T("Kembali ke Launcher dari Game"), Lang.T("Tahan BACK + START di gamepad selama 1 detik"), "\uE7FC", null));
            l.Add(MakeItem(Lang.T("Layar Penuh / Jendela"), "F11", "\uE740", null));
            l.Add(MakeItem(Lang.T("Ganti Mode XMB / Coverflow"), Lang.T("F2  \u2022  di Coverflow: Kiri/Kanan pindah game, Atas/Bawah ganti kategori"), "\uE8A9", null));
            l.Add(MakeItem(Lang.T("Refresh Daftar"), Lang.T("F5 (scan ulang Steam & ROM)"), "\uE72C", null));
            l.Add(MakeItem(Lang.T("Trofi & Achievement"), Lang.T("Isi akun di Pengaturan > Akun Trofi. Lihat trofi per game: Tab > Lihat Trofi"), "#TROPHY", null));
            l.Add(MakeItem(Lang.T("Trailer & Latar Game"), Lang.T("Taruh video/gambar bernama sama dengan game di folder trailers / backgrounds (lihat PANDUAN)"), "\uE714", null));
            l.Add(MakeItem("Gamepad PS4/PS5", Lang.T("Pakai DS4Windows atau Steam agar terbaca sebagai XInput"), "\uE7FC", null));
            return l;
        }

        // ===================== FOTO / MUSIK / VIDEO =====================
        List<XmbItem> FolderItems(string dir, string[] exts, string kind, Action<List<string>, int> onOpen)
        {
            List<XmbItem> res = new List<XmbItem>();
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
            {
                res.Add(MakeItem(Lang.T("Folder tidak ditemukan"), dir ?? "", "\uE783", null));
                return res;
            }
            string[] dirs;
            try { dirs = Directory.GetDirectories(dir); } catch { dirs = new string[0]; }
            Array.Sort(dirs, StringComparer.OrdinalIgnoreCase);
            foreach (string d in dirs)
            {
                try
                {
                    FileAttributes at = File.GetAttributes(d);
                    if ((at & FileAttributes.Hidden) != 0 || (at & FileAttributes.System) != 0) continue;
                }
                catch { continue; }
                string dd = d;
                XmbItem f = MakeItem(Path.GetFileName(d), "Folder", "\uE8B7", null);
                f.Children = delegate { return FolderItems(dd, exts, kind, onOpen); };
                f.Options = delegate
                {
                    List<OptionItem> o = new List<OptionItem>();
                    if (kind == "music")
                    {
                        o.Add(new OptionItem(Lang.T("Putar Semua Isi Folder"), delegate { music.PlayList(MediaExt.Files(dd, exts, true), 0); ShowMusicToast(); }));
                        o.Add(new OptionItem(Lang.T("Putar Acak Isi Folder"), delegate { PlayShuffled(MediaExt.Files(dd, exts, true)); }));
                    }
                    if (kind == "photo") o.Add(new OptionItem(Lang.T("Slideshow Isi Folder"), delegate { OpenPhotos(MediaExt.Files(dd, exts, true), 0, true); }));
                    if (kind == "video") o.Add(new OptionItem(Lang.T("Putar Semua Video"), delegate { List<string> v = MediaExt.Files(dd, exts, true); if (v.Count > 0) PlayVideo(v, 0); }));
                    o.Add(new OptionItem(Lang.T("Buka di Explorer"), delegate { ShowInExplorer(dd); }));
                    return o;
                };
                res.Add(f);
            }

            List<string> files = MediaExt.Files(dir, exts, false);
            string glyph = kind == "music" ? "\uE8D6" : kind == "video" ? "\uE8B2" : "\uE91B";
            for (int i = 0; i < files.Count; i++)
            {
                int idx = i;
                string path = files[i];
                string sub = Path.GetExtension(path).TrimStart('.').ToUpperInvariant();
                try { sub += "  \u2022  " + FormatSize(new FileInfo(path).Length); } catch { }
                XmbItem it = MakeItem(Path.GetFileNameWithoutExtension(path), sub, glyph, delegate { onOpen(files, idx); });
                it.Kind = kind;
                it.ImageKey = "thumb:" + path;
                if (kind == "photo")
                    it.ImageLoader = delegate { return ImgConv.FromDrawing(ShellThumb.Get(path, 320)) ?? ImgConv.LoadFile(path, 360); };
                else
                    it.ImageLoader = delegate { return ImgConv.FromDrawing(ShellThumb.Get(path, 320)); };
                it.Options = delegate
                {
                    List<OptionItem> o = new List<OptionItem>();
                    o.Add(new OptionItem(kind == "photo" ? Lang.T("Lihat") : Lang.T("Putar"), delegate { onOpen(files, idx); }));
                    if (kind == "music") o.Add(new OptionItem(Lang.T("Putar Acak Folder Ini"), delegate { PlayShuffled(files); }));
                    if (kind == "photo")
                    {
                        o.Add(new OptionItem(Lang.T("Jadikan Latar ZMB"), delegate { cfg.BackgroundImage = path; cfg.Save(); backImg = null; backKey = ""; }));
                        o.Add(new OptionItem(Lang.T("Jadikan Wallpaper Desktop"), delegate { ShowToast(ShellThumb.SetWallpaper(path) ? Lang.T("Wallpaper diganti") : Lang.T("Gagal")); }));
                    }
                    o.Add(new OptionItem(Lang.T("Buka dengan Aplikasi Windows"), delegate { OpenDefault(path); }));
                    o.Add(new OptionItem(Lang.T("Buka Lokasi File"), delegate { ShowInExplorer(path); }));
                    return o;
                };
                res.Add(it);
            }
            if (res.Count == 0) res.Add(MakeItem(Lang.T("Folder kosong"), dir, "\uE7BA", null));
            return res;
        }

        static string FormatSize(long b)
        {
            if (b > 1L << 30) return (b / (double)(1L << 30)).ToString("0.0") + " GB";
            if (b > 1L << 20) return (b / (double)(1L << 20)).ToString("0.0") + " MB";
            return (b / 1024) + " KB";
        }

        void PlayShuffled(List<string> files)
        {
            bool old = music.Shuffle;
            music.Shuffle = true;
            music.PlayList(files, new Random().Next(Math.Max(1, files.Count)));
            music.Shuffle = old;
            ShowMusicToast();
        }

        List<XmbItem> BuildPhotoItems()
        {
            List<XmbItem> l = new List<XmbItem>();
            l.Add(MakeItem(Lang.T("Slideshow Semua Foto"), cfg.PhotoFolder, "\uE786", delegate
            {
                OpenPhotos(MediaExt.Files(cfg.PhotoFolder, MediaExt.Photo, true), 0, true);
            }));
            l.AddRange(FolderItems(cfg.PhotoFolder, MediaExt.Photo, "photo", delegate (List<string> f, int i) { OpenPhotos(f, i, false); }));
            return l;
        }

        List<XmbItem> BuildMusicItems()
        {
            List<XmbItem> l = new List<XmbItem>();
            XmbItem now = MakeItem(Lang.T("Sedang Diputar"), "", "\uE768", null);
            now.DynSub = delegate
            {
                if (!music.Playing) return Lang.T("Tidak ada lagu yang diputar");
                return (music.Paused ? Lang.T("Dijeda  \u2022  ") : "") + music.Title() + "  (" + (music.Pos + 1) + "/" + music.Queue.Count + ")";
            };
            now.Activate = delegate { music.TogglePause(); ShowMusicToast(); };
            now.Options = delegate
            {
                List<OptionItem> o = new List<OptionItem>();
                o.Add(new OptionItem(music.Playing && !music.Paused ? Lang.T("Jeda") : Lang.T("Putar"), delegate { music.TogglePause(); }));
                o.Add(new OptionItem(Lang.T("Lagu Berikutnya"), delegate { music.Next(); ShowMusicToast(); }));
                o.Add(new OptionItem(Lang.T("Lagu Sebelumnya"), delegate { music.Prev(); ShowMusicToast(); }));
                o.Add(new OptionItem(Lang.T("Maju 30 detik"), delegate { music.Seek(30000); }));
                o.Add(new OptionItem(Lang.T("Mundur 30 detik"), delegate { music.Seek(-30000); }));
                o.Add(new OptionItem(Lang.T("Acak (Shuffle)"), delegate { music.Shuffle = !music.Shuffle; cfg.Shuffle = music.Shuffle; cfg.Save(); }, music.Shuffle));
                o.Add(new OptionItem(Lang.T("Ulangi Daftar (Repeat)"), delegate { music.Repeat = !music.Repeat; cfg.Repeat = music.Repeat; cfg.Save(); }, music.Repeat));
                o.Add(new OptionItem("Stop", delegate { music.Stop(); }));
                return o;
            };
            l.Add(now);
            l.Add(MakeItem(Lang.T("Putar Semua (Acak)"), cfg.MusicFolder, "\uE8B1", delegate { PlayShuffled(MediaExt.Files(cfg.MusicFolder, MediaExt.Music, true)); }));
            l.Add(MakeItem(Lang.T("Putar Semua (Urut)"), cfg.MusicFolder, "\uE8EE", delegate
            {
                bool old = music.Shuffle; music.Shuffle = false;
                music.PlayList(MediaExt.Files(cfg.MusicFolder, MediaExt.Music, true), 0);
                music.Shuffle = old; ShowMusicToast();
            }));
            l.AddRange(FolderItems(cfg.MusicFolder, MediaExt.Music, "music", delegate (List<string> f, int i)
            {
                music.PlayList(f, i); ShowMusicToast();
            }));
            return l;
        }

        List<XmbItem> BuildVideoItems()
        {
            List<XmbItem> l = new List<XmbItem>();
            l.AddRange(FolderItems(cfg.VideoFolder, MediaExt.Video, "video", delegate (List<string> f, int i) { PlayVideo(f, i); }));
            return l;
        }

        // ===================== GAME =====================
        List<XmbItem> BuildGameItems()
        {
            List<XmbItem> l = new List<XmbItem>();
            List<GameEntry> recent = cfg.AllKnown().Where(g => g.PlayCount > 0 && g.LastPlayed > DateTime.MinValue).OrderByDescending(g => g.LastPlayed).Take(10).ToList();
            if (recent.Count > 0)
            {
                XmbItem r = MakeItem(Lang.T("Terakhir Dimainkan"), recent[0].Name, "\uE81C", null);
                r.Children = delegate
                {
                    return cfg.AllKnown().Where(g => g.PlayCount > 0).OrderByDescending(g => g.LastPlayed).Take(10).Select(g => GameItem(g, null)).ToList();
                };
                l.Add(r);
            }
            if (cfg.AllKnown().Any(g => g.Favorite))
            {
                XmbItem f = MakeItem(Lang.T("Favorit"), Lang.T("Game yang kamu tandai favorit"), "\uE735", null);
                f.Children = delegate { return cfg.AllKnown().Where(g => g.Favorite).OrderBy(g => g.Name).Select(g => GameItem(g, null)).ToList(); };
                l.Add(f);
            }

            XmbItem tro = MakeItem(Lang.T("Koleksi Trofi"), Lang.T("Steam, RetroAchievements, dan trofi PS3 (RPCS3)"), "#TROPHY", null);
            tro.Children = BuildTrophyHub;
            l.Add(tro);

            XmbItem win = MakeItem("Windows", "", "#WIN", null);
            win.DynSub = delegate
            {
                int n = cfg.WindowsGames.Count + (cfg.ShowSteam ? GameScanner.SteamGames(false).Count : 0);
                return n + Lang.T(" game PC  \u2022  Steam terdeteksi otomatis");
            };
            win.Children = BuildWindowsGames;
            win.Options = delegate
            {
                List<OptionItem> o = new List<OptionItem>();
                o.Add(new OptionItem(Lang.T("Tambah Game (.exe / shortcut)..."), AddWindowsGames));
                o.Add(new OptionItem(Lang.T("Scan Folder Game..."), ScanGameFolder));
                o.Add(new OptionItem(Lang.T("Scan Ulang Steam"), delegate { GameScanner.SteamGames(true); RefreshAll(); ShowToast(Lang.T("Steam di-scan ulang")); }));
                return o;
            };
            l.Add(win);

            foreach (PlatformCfg p in cfg.Platforms)
            {
                if (p.Hidden) continue;
                PlatformCfg pp = p;
                XmbItem it = MakeItem(p.Name, "", null, null);
                it.Badge = p.Short;
                it.BadgeColor = ParseColor(p.Color);
                ApplyPlatformIcon(it, p);
                it.DynSub = delegate
                {
                    if (string.IsNullOrEmpty(pp.Emulator)) return Lang.T("Emulator belum diatur  \u2022  saran: ") + pp.Hint;
                    if (string.IsNullOrEmpty(pp.RomFolder)) return Path.GetFileNameWithoutExtension(pp.Emulator) + Lang.T("  \u2022  folder ROM belum diatur");
                    return Path.GetFileNameWithoutExtension(pp.Emulator) + "  \u2022  " + pp.RomFolder;
                };
                it.Children = delegate { return BuildRomItems(pp); };
                it.Options = delegate { return PlatformOptions(pp); };
                l.Add(it);
            }

            XmbItem manage = MakeItem(Lang.T("Kelola Platform & Emulator"), Lang.T("Tambah, sembunyikan, atur emulator"), "\uE713", null);
            manage.Children = BuildPlatformManager;
            l.Add(manage);
            foreach (XmbItem gi in l) gi.HideSub = true; // daftar platform tampil bersih tanpa keterangan
            return l;
        }

        static Color ParseColor(string hex)
        {
            try { return (Color)ColorConverter.ConvertFromString(hex); } catch { return Color.FromRgb(50, 80, 150); }
        }

        List<XmbItem> BuildWindowsGames()
        {
            List<XmbItem> l = new List<XmbItem>();
            l.Add(MakeItem(Lang.T("Tambah Game"), Lang.T("Pilih file .exe atau shortcut game"), "\uE710", AddWindowsGames));
            l.Add(MakeItem(Lang.T("Scan Folder Game"), Lang.T("Pilih folder berisi banyak game (misal D:\\Games)"), "\uE721", ScanGameFolder));

            List<XmbItem> games = new List<XmbItem>();
            foreach (GameEntry g in cfg.WindowsGames) games.Add(GameItem(g, null));
            if (cfg.ShowSteam)
            {
                foreach (SteamGame sg in GameScanner.SteamGames(false))
                {
                    GameEntry g = cfg.GetMeta("steam://rungameid/" + sg.AppId, "STEAM", sg.Name);
                    games.Add(GameItem(g, sg.Icon));
                }
            }
            games.Sort((a, b) => string.Compare(a.Title, b.Title, StringComparison.OrdinalIgnoreCase));
            l.AddRange(games);
            return l;
        }

        XmbItem GameItem(GameEntry g, string fallbackImage)
        {
            XmbItem it = new XmbItem();
            it.Title = g.Name;
            it.Game = g;
            it.Kind = "game";
            it.DynSub = delegate { return GameSubtitle(g); };
            it.Glyph = "\uE7FC";
            PlatformCfg p = (g.Platform == "WIN" || g.Platform == "STEAM") ? null : cfg.FindPlatform(g.Platform);

            string cover = null;
            if (!string.IsNullOrEmpty(g.Cover) && File.Exists(g.Cover)) cover = g.Cover;
            else if (g.Platform == "STEAM")
            {
                cover = fallbackImage;
                if (cover == null)
                {
                    string id = g.Path.Substring(g.Path.LastIndexOf('/') + 1);
                    foreach (SteamGame sg in GameScanner.SteamGames(false)) if (sg.AppId == id) { cover = sg.Icon; break; }
                }
            }
            else if (p != null) cover = GameScanner.FindCover(p, g.Path, g.Cover);

            if (cover != null)
            {
                string c = cover;
                it.ImageKey = "img:" + c;
                it.ImageLoader = delegate { return ImgConv.LoadFile(c, 440); };
            }
            else if (g.Platform == "WIN")
            {
                it.ImageKey = "ico:" + g.Path;
                it.ImageLoader = delegate { return ImgConv.FromDrawing(Native.ExtractBigIcon(g.Path)); };
            }

            string box = null;
            try { box = GameScanner.FindBoxArt(g, p); } catch { }
            if (box != null)
            {
                string bx = box;
                it.BoxKey = "box:" + bx;
                it.BoxLoader = delegate { return ImgConv.LoadFile(bx, 720); };
            }
            else if (it.ImageKey != null)
            {
                it.BoxKey = it.ImageKey;
                it.BoxLoader = it.ImageLoader;
                it.BoxIsIcon = it.ImageKey.StartsWith("ico:");
            }

            if (g.Platform == "STEAM") { it.Badge = "STEAM"; it.BadgeColor = Color.FromRgb(27, 40, 56); }
            else if (p != null) { it.Badge = p.Short; it.BadgeColor = ParseColor(p.Color); }

            it.Activate = delegate { Launch(g, false); };
            it.Options = delegate { return GameOptions(g); };
            it.HideSub = true; // judul game saja, tanpa "PS1 \u2022 Terakhir: ..."
            return it;
        }

        string GameSubtitle(GameEntry g)
        {
            string fav = g.Favorite ? Lang.T("Favorit  \u2022  ") : "";
            string plat = g.Platform == "WIN" ? "PC" : g.Platform == "STEAM" ? "Steam" : g.Platform;
            if (g.PlayCount == 0) return fav + plat + Lang.T("  \u2022  Belum pernah dimainkan") + AchvBrief(g);
            string s = fav + plat + Lang.T("  \u2022  Terakhir: ") + g.LastPlayed.ToString("dd MMM yyyy, HH:mm");
            if (g.Minutes >= 1) s += "  \u2022  Total " + FormatPlay(g.Minutes);
            return s + AchvBrief(g);
        }

        List<OptionItem> GameOptions(GameEntry g)
        {
            List<OptionItem> o = new List<OptionItem>();
            o.Add(new OptionItem(Lang.T("Mainkan"), delegate { Launch(g, false); }));
            o.Add(new OptionItem(Lang.T("Detail Game"), delegate { OpenDetails(g); }));
            PlatformCfg sp = (g.Platform == "WIN" || g.Platform == "STEAM") ? null : cfg.FindPlatform(g.Platform);
            if (sp != null && SaveManager.ResumeArgsFor(sp) != null)
                o.Add(new OptionItem(Lang.T("Lanjutkan dari Save State Terakhir"), delegate { LaunchResume(g); }));
            if (sp != null) o.Add(new OptionItem("Save & Memory Card", delegate { OpenSaves(sp, g); }));
            o.Add(new OptionItem(Lang.T("Pilih Musik Tema..."), delegate
            {
                string f = Dialogs.PickFile(Owner32, Lang.T("Pilih musik tema untuk ") + g.Name, "Audio|*.mp3;*.wma;*.wav;*.m4a", cfg.MusicFolder);
                if (f != null) { g.ThemeMusic = f; cfg.EnsureStored(g); cfg.Save(); focusKey = null; ShowToast(Lang.T("Musik tema diatur")); }
            }));
            if (!string.IsNullOrEmpty(g.ThemeMusic)) o.Add(new OptionItem(Lang.T("Hapus Musik Tema"), delegate { g.ThemeMusic = ""; cfg.Save(); StopThemeMusic(); }));
            o.Add(new OptionItem(Lang.T("Lihat Trofi / Achievement"), delegate { OpenAchvFor(g); }));
            o.Add(new OptionItem(Lang.T("Unduh Media Game Ini (cover, latar, logo, video)"), delegate { StartScrape(new List<GameEntry> { g }, true); }));
            if (g.Platform == "WIN")
                o.Add(new OptionItem(Lang.T("Steam AppID untuk Achievement") + (string.IsNullOrEmpty(g.SteamAppId) ? "" : " (" + g.SteamAppId + ")"), delegate
                {
                    string a = Dialogs.Input(Owner32, "Steam AppID", Lang.T("Kalau game ini juga ada di Steam, isi AppID-nya (angka di link store.steampowered.com/app/XXXX) agar achievement Steam tampil:"), g.SteamAppId);
                    if (a != null) { g.SteamAppId = a.Trim(); cfg.EnsureStored(g); SaveAndRefresh(); }
                }));
            else if (g.Platform != "STEAM")
            {
                PlatformCfg gp = cfg.FindPlatform(g.Platform);
                if (gp != null && gp.Short == "PS3")
                    o.Add(new OptionItem(Lang.T("NP Comm ID Trofi") + (string.IsNullOrEmpty(g.NpCommId) ? Lang.T(" (otomatis)") : " (" + g.NpCommId + ")"), delegate
                    {
                        string a = Dialogs.Input(Owner32, "NP Communication ID", Lang.T("ID folder trofi RPCS3, contoh NPWR01234_00. Kosongkan untuk otomatis:"), g.NpCommId);
                        if (a != null) { g.NpCommId = a.Trim(); cfg.EnsureStored(g); InvalidateAsync("rpcs3:" + g.Path); SaveAndRefresh(); }
                    }));
                else
                    o.Add(new OptionItem(Lang.T("ID Game RetroAchievements") + (g.RaGameId > 0 ? " (" + g.RaGameId + ")" : Lang.T(" (otomatis)")), delegate
                    {
                        string a = Dialogs.Input(Owner32, Lang.T("ID Game RetroAchievements"), Lang.T("Angka di link retroachievements.org/game/XXXX. Kosongkan / 0 untuk otomatis:"), g.RaGameId > 0 ? g.RaGameId.ToString() : "");
                        if (a == null) return;
                        int id; int.TryParse(a.Trim(), out id);
                        g.RaGameId = Math.Max(0, id); cfg.EnsureStored(g); InvalidateAsync("ra:" + g.Path); SaveAndRefresh();
                    }));
            }
            if (g.Platform != "STEAM") o.Add(new OptionItem(Lang.T("Mainkan sebagai Administrator"), delegate { Launch(g, true); }));
            o.Add(new OptionItem(g.Favorite ? Lang.T("Hapus dari Favorit") : Lang.T("Tambah ke Favorit"), delegate
            {
                g.Favorite = !g.Favorite; cfg.EnsureStored(g); SaveAndRefresh();
                ShowToast(g.Favorite ? Lang.T("Ditambahkan ke Favorit") : Lang.T("Dihapus dari Favorit"));
            }));
            o.Add(new OptionItem(Lang.T("Ganti Nama"), delegate
            {
                string n = Dialogs.Input(Owner32, Lang.T("Ganti Nama"), Lang.T("Nama baru untuk game ini:"), g.Name);
                if (!string.IsNullOrEmpty(n)) { g.Name = n.Trim(); cfg.EnsureStored(g); SaveAndRefresh(); }
            }));
            o.Add(new OptionItem(Lang.T("Pilih Gambar Cover..."), delegate { PickCover(g); }));
            if (g.Platform != "WIN" && g.Platform != "STEAM")
            {
                o.Add(new OptionItem(Lang.T("Pilih Gambar Spine (Coverflow)..."), delegate
                {
                    string f = Dialogs.PickFile(Owner32, Lang.T("Pilih gambar punggung case (spine) untuk ") + g.Name, Lang.T("Gambar|*.jpg;*.jpeg;*.png;*.bmp"), null);
                    if (f != null) { g.Spine = f; cfg.EnsureStored(g); SaveAndRefresh(); ShowToast(Lang.T("Spine diganti")); }
                }));
                if (!string.IsNullOrEmpty(g.Spine))
                    o.Add(new OptionItem(Lang.T("Hapus Spine Kustom"), delegate { ForgetImage("spine:" + g.Spine); g.Spine = ""; SaveAndRefresh(); }));
                if (g.Platform == "PS1" || g.Platform == "PS2" || g.Platform == "PS3" || g.Platform == "PSP" || g.Platform == "SWITCH")
                {
                    bool cart = g.Platform == "SWITCH";
                    o.Add(new OptionItem(cart ? Lang.T("Pilih Gambar Cartridge (Animasi Boot)...") : Lang.T("Pilih Gambar Disc (Animasi Boot)..."), delegate
                    {
                        string f = Dialogs.PickFile(Owner32, (cart ? Lang.T("Pilih gambar cartridge untuk ") : Lang.T("Pilih gambar disc untuk ")) + g.Name, Lang.T("Gambar|*.jpg;*.jpeg;*.png;*.bmp"), null);
                        if (f != null) { g.Disc = f; cfg.EnsureStored(g); cfg.Save(); ShowToast(Lang.T("Disc diganti")); }
                    }));
                    if (!string.IsNullOrEmpty(g.Disc))
                        o.Add(new OptionItem(Lang.T("Hapus Disc Kustom"), delegate { g.Disc = ""; cfg.Save(); ShowToast(Lang.T("Disc kustom dihapus")); }));
                }
            }
            o.Add(new OptionItem(Lang.T("Pilih Box Art (Coverflow)..."), delegate
            {
                string f = Dialogs.PickFile(Owner32, Lang.T("Pilih box art portrait untuk ") + g.Name, Lang.T("Gambar|*.jpg;*.jpeg;*.png;*.bmp"), null);
                if (f != null) { g.BoxArt = f; cfg.EnsureStored(g); SaveAndRefresh(); ShowToast(Lang.T("Box art diganti")); }
            }));
            if (!string.IsNullOrEmpty(g.Cover))
                o.Add(new OptionItem(Lang.T("Hapus Cover Kustom"), delegate { ForgetImage("img:" + g.Cover); g.Cover = ""; SaveAndRefresh(); }));
            o.Add(new OptionItem(Lang.T("Pilih Gambar Latar..."), delegate
            {
                string f = Dialogs.PickFile(Owner32, Lang.T("Pilih gambar latar untuk ") + g.Name + Lang.T(" (disarankan 1920x1080)"), Lang.T("Gambar|*.jpg;*.jpeg;*.png;*.bmp"), null);
                if (f != null) { g.Background = f; cfg.EnsureStored(g); SaveAndRefresh(); ShowToast(Lang.T("Latar game diganti")); }
            }));
            if (!string.IsNullOrEmpty(g.Background))
                o.Add(new OptionItem(Lang.T("Hapus Latar Kustom"), delegate { g.Background = ""; SaveAndRefresh(); }));
            o.Add(new OptionItem(Lang.T("Pilih Video Trailer..."), delegate
            {
                string f = Dialogs.PickFile(Owner32, Lang.T("Pilih video trailer untuk ") + g.Name, Lang.T("Video|*.mp4;*.wmv;*.avi;*.m4v;*.mov;*.mkv|Semua file|*.*"), cfg.VideoFolder);
                if (f != null) { g.Trailer = f; cfg.EnsureStored(g); SaveAndRefresh(); ShowToast(Lang.T("Trailer diatur")); }
            }));
            o.Add(new OptionItem(Lang.T("Trailer dari Link (URL .mp4)..."), delegate
            {
                string u = Dialogs.Input(Owner32, Lang.T("Trailer dari Link"), Lang.T("Tempel link langsung ke file video .mp4 (bukan link YouTube):"), g.Trailer.Contains("://") ? g.Trailer : "");
                if (!string.IsNullOrEmpty(u)) { g.Trailer = u.Trim(); cfg.EnsureStored(g); SaveAndRefresh(); }
            }));
            if (!string.IsNullOrEmpty(g.Trailer))
                o.Add(new OptionItem(Lang.T("Hapus Trailer Kustom"), delegate { g.Trailer = ""; SaveAndRefresh(); }));
            if (g.Platform == "WIN")
            {
                o.Add(new OptionItem(Lang.T("Argumen Peluncuran") + (string.IsNullOrEmpty(g.Args) ? "" : " (" + g.Args + ")"), delegate
                {
                    string a = Dialogs.Input(Owner32, Lang.T("Argumen"), Lang.T("Argumen tambahan saat menjalankan game (contoh: -windowed -nosplash):"), g.Args);
                    if (a != null) { g.Args = a; SaveAndRefresh(); }
                }));
                o.Add(new OptionItem(Lang.T("Selalu Jalankan sebagai Admin"), delegate { g.Admin = !g.Admin; SaveAndRefresh(); }, g.Admin));
            }
            if (g.Platform != "STEAM") o.Add(new OptionItem(Lang.T("Buka Lokasi File"), delegate { ShowInExplorer(g.Path); }));
            if (g.Platform == "STEAM")
            {
                string id = g.Path.Substring(g.Path.LastIndexOf('/') + 1);
                o.Add(new OptionItem(Lang.T("Buka di Library Steam"), delegate { OpenDefault("steam://nav/games/details/" + id); }));
            }
            o.Add(new OptionItem(Lang.T("Reset Statistik Main"), delegate { g.PlayCount = 0; g.Minutes = 0; g.LastPlayed = DateTime.MinValue; SaveAndRefresh(); }));
            if (g.Platform == "WIN")
            {
                o.Add(new OptionItem(Lang.T("Hapus dari Daftar"), delegate
                {
                    Confirm(Lang.T("Hapus \"") + g.Name + Lang.T("\" dari daftar? (file game tidak dihapus)"), delegate
                    {
                        cfg.WindowsGames.Remove(g); SaveAndRefresh(); ShowToast(Lang.T("Dihapus dari daftar"));
                    });
                }));
            }
            else if (cfg.Meta.Contains(g) && g.Platform != "STEAM")
            {
                o.Add(new OptionItem(Lang.T("Hapus Data Game Ini"), delegate { cfg.Meta.Remove(g); SaveAndRefresh(); }));
            }
            return o;
        }

        void PickCover(GameEntry g)
        {
            string f = Dialogs.PickFile(Owner32, Lang.T("Pilih gambar cover untuk ") + g.Name, Lang.T("Gambar|*.jpg;*.jpeg;*.png;*.bmp"), null);
            if (f == null) return;
            try
            {
                string plat = string.IsNullOrEmpty(g.Platform) ? "MISC" : g.Platform;
                string dir = Path.Combine(Config.BaseDir, "covers", plat);
                Directory.CreateDirectory(dir);
                string safe = string.Join("_", g.Name.Split(Path.GetInvalidFileNameChars()));
                string dest = Path.Combine(dir, safe + "_" + DateTime.Now.Ticks.ToString().Substring(10) + Path.GetExtension(f).ToLowerInvariant());
                File.Copy(f, dest, true);
                if (!string.IsNullOrEmpty(g.Cover)) ForgetImage("img:" + g.Cover);
                g.Cover = dest;
                cfg.EnsureStored(g);
                SaveAndRefresh();
                ShowToast(Lang.T("Cover diganti"));
            }
            catch (Exception ex) { ShowToast(Lang.T("Gagal menyalin cover: ") + ex.Message); }
        }

        void AddWindowsGames()
        {
            string[] files = Dialogs.PickFiles(Owner32, Lang.T("Pilih game (.exe / shortcut)"), Lang.T("Game|*.exe;*.lnk;*.url|Semua file|*.*"));
            if (files == null) return;
            int added = 0;
            foreach (string f in files)
            {
                if (cfg.WindowsGames.Any(x => string.Equals(x.Path, f, StringComparison.OrdinalIgnoreCase))) continue;
                GameEntry g = new GameEntry();
                g.Path = f;
                g.Platform = "WIN";
                g.Name = Path.GetExtension(f).ToLowerInvariant() == ".exe" ? GameScanner.NiceName(f) : Path.GetFileNameWithoutExtension(f);
                cfg.WindowsGames.Add(g);
                added++;
            }
            SaveAndRefresh();
            ShowToast(added + Lang.T(" game ditambahkan"));
        }

        void ScanGameFolder()
        {
            string dir = Dialogs.PickFolder(Owner32, Lang.T("Pilih folder yang berisi folder-folder game (contoh: D:\\Games). Tiap subfolder dianggap 1 game."), null);
            if (dir == null) return;
            Mouse.OverrideCursor = Cursors.Wait;
            List<GameEntry> found = GameScanner.ScanGameFolder(dir);
            Mouse.OverrideCursor = null;
            int added = 0;
            foreach (GameEntry g in found)
            {
                if (cfg.WindowsGames.Any(x => string.Equals(x.Path, g.Path, StringComparison.OrdinalIgnoreCase))) continue;
                cfg.WindowsGames.Add(g);
                added++;
            }
            SaveAndRefresh();
            ShowToast(added + Lang.T(" game ditemukan & ditambahkan") + (added > 0 ? Lang.T(" (cek & hapus yang salah via menu Opsi)") : ""));
        }

        // ===================== EMULATOR / ROM =====================
        List<XmbItem> BuildRomItems(PlatformCfg p)
        {
            List<XmbItem> l = new List<XmbItem>();
            if (string.IsNullOrEmpty(p.Emulator) || !File.Exists(p.Emulator))
                l.Add(MakeItem(Lang.T("Atur Emulator ") + p.Short, Lang.T("Pilih file .exe emulator  \u2022  saran: ") + p.Hint, "\uE713", delegate { PickEmulator(p); }));
            if (string.IsNullOrEmpty(p.RomFolder) || !Directory.Exists(p.RomFolder))
                l.Add(MakeItem(Lang.T("Atur Folder Game"), Lang.T("Pilih folder berisi file game ") + p.Name + " (" + p.Extensions + ")", "\uE8B7", delegate { PickRomFolder(p); }));

            List<string> roms = GameScanner.ListRoms(p);
            HashSet<string> set = new HashSet<string>(roms, StringComparer.OrdinalIgnoreCase);
            foreach (GameEntry m in cfg.Meta)
                if (m.Platform == p.Short && !set.Contains(m.Path) && File.Exists(m.Path)) { roms.Add(m.Path); set.Add(m.Path); }
            roms = GameScanner.CleanRomList(roms); // entri lama (disc 2, track 02, ...) ikut dirapikan

            List<XmbItem> games = new List<XmbItem>();
            foreach (string rom in roms)
            {
                GameEntry g = cfg.GetMeta(rom, p.Short, GameScanner.RomDisplayName(p, rom));
                if (GameScanner.HasDiscOrTrackLabel(g.Name)) g.Name = GameScanner.CleanRomName(g.Name); // nama tersimpan dari versi lama
                games.Add(GameItem(g, null));
            }
            games.Sort((a, b) => string.Compare(a.Title, b.Title, StringComparison.OrdinalIgnoreCase));
            l.AddRange(games);

            if (games.Count == 0 && !string.IsNullOrEmpty(p.RomFolder) && Directory.Exists(p.RomFolder))
                l.Add(MakeItem(Lang.T("Belum ada game"), Lang.T("Tidak ada file ") + p.Extensions + " di " + p.RomFolder, "\uE7BA", null));
            l.Add(MakeItem(Lang.T("Tambah Game Manual"), Lang.T("Pilih file game satu per satu"), "\uE710", delegate { AddRomManual(p); }));
            return l;
        }

        // Ikon kustom platform: pilihan sendiri, atau file ZMB\icons\<SINGKATAN>.png/.ico/.jpg
        static string PlatformIconPath(PlatformCfg p)
        {
            if (p == null) return null;
            if (!string.IsNullOrEmpty(p.Icon) && File.Exists(p.Icon)) return p.Icon;
            string dir = Path.Combine(Config.BaseDir, "icons");
            foreach (string ext in new[] { ".png", ".ico", ".jpg", ".jpeg" })
            {
                string f = Path.Combine(dir, p.Short + ext);
                if (File.Exists(f)) return f;
            }
            return null;
        }

        void ApplyPlatformIcon(XmbItem it, PlatformCfg p)
        {
            string f = PlatformIconPath(p);
            if (f == null) return;
            string key = "picon:" + f + "|" + File.GetLastWriteTimeUtc(f).Ticks;
            it.ImageKey = key;
            it.ImageLoader = delegate { return ImgConv.LoadFile(f, 256); };
            it.Kind = "platform";
        }

        List<OptionItem> PlatformOptions(PlatformCfg p)
        {
            List<OptionItem> o = new List<OptionItem>();
            o.Add(new OptionItem(Lang.T("Pilih Emulator (.exe)...") + (string.IsNullOrEmpty(p.Emulator) ? "" : "  [" + Path.GetFileName(p.Emulator) + "]"), delegate { PickEmulator(p); }));
            o.Add(new OptionItem(Lang.T("Pilih Folder Game..."), delegate { PickRomFolder(p); }));
            o.Add(new OptionItem(Lang.T("Pilih Ikon Platform..."), delegate
            {
                string f = Dialogs.PickFile(Owner32, Lang.T("Pilih ikon untuk ") + p.Name + Lang.T(" (PNG transparan paling bagus)"), Lang.T("Gambar|*.png;*.ico;*.jpg;*.jpeg;*.bmp"), null);
                if (f != null) { p.Icon = f; SaveAndRefresh(); ShowToast(Lang.T("Ikon ") + p.Short + Lang.T(" diganti")); }
            }));
            if (!string.IsNullOrEmpty(p.Icon))
                o.Add(new OptionItem(Lang.T("Kembalikan Ikon Bawaan"), delegate { p.Icon = ""; SaveAndRefresh(); ShowToast(Lang.T("Ikon bawaan dipakai lagi")); }));
            o.Add(new OptionItem(Lang.T("Argumen Emulator: ") + p.Args, delegate
            {
                string a = Dialogs.Input(Owner32, Lang.T("Argumen Emulator ") + p.Name,
                    Lang.T("Gunakan {rom} untuk path game. Lainnya: {romname}, {romdir}, {emudir}.\nContoh RetroArch: -L \"cores\\swanstation_libretro.dll\" \"{rom}\"   |   DuckStation: -fullscreen \"{rom}\""), p.Args);
                if (a != null) { p.Args = a; SaveAndRefresh(); }
            }));
            o.Add(new OptionItem(Lang.T("Ekstensi File: ") + p.Extensions, delegate
            {
                string a = Dialogs.Input(Owner32, Lang.T("Ekstensi File"), Lang.T("Ekstensi file game, pisahkan dengan titik koma (contoh: iso;bin;cue):"), p.Extensions);
                if (a != null) { p.Extensions = a; SaveAndRefresh(); }
            }));
            o.Add(new OptionItem(Lang.T("Tambah Game Manual..."), delegate { AddRomManual(p); }));
            o.Add(new OptionItem("Save & Memory Card", delegate { OpenSaves(p, null); }));
            o.Add(new OptionItem(Lang.T("Argumen Lanjutkan (Save State)"), delegate { EditResumeArgs(p); }));
            o.Add(new OptionItem(Lang.T("Unduh Media Semua Game ") + p.Short, delegate { StartScrape(GamesOf(p), false); }));
            int ssS = MediaScraper.SsSystemFor(p, null);
            o.Add(new OptionItem(Lang.T("ID Sistem ScreenScraper: ") + (ssS > 0 ? ssS.ToString() : Lang.T("tidak ada")), delegate
            {
                string a = Dialogs.Input(Owner32, Lang.T("ID Sistem ScreenScraper"), Lang.T("ID sistem di ScreenScraper (contoh PS1=57, PS2=58, PSP=61, NES=3, SNES=4, N64=14, GBA=12, NDS=15, Megadrive=1, MAME=75). Kosong untuk bawaan:"), p.SsSystem >= 0 ? p.SsSystem.ToString() : "");
                if (a == null) return;
                int v;
                p.SsSystem = int.TryParse(a.Trim(), out v) ? v : -1;
                SaveAndRefresh();
            }));
            int raC = Achievements.RaConsoleFor(p, null);
            o.Add(new OptionItem(Lang.T("ID Konsol RetroAchievements: ") + (raC > 0 ? raC.ToString() : Lang.T("tidak ada")), delegate
            {
                string a = Dialogs.Input(Owner32, Lang.T("ID Konsol RetroAchievements"), Lang.T("ID konsol di RetroAchievements (contoh PS1=12, PS2=21, PSP=41, NES=7, SNES=3, N64=2, GBA=5, NDS=18, Genesis=1). Isi 0 untuk mematikan, kosong untuk bawaan:"), p.RaConsole >= 0 ? p.RaConsole.ToString() : "");
                if (a == null) return;
                int c;
                p.RaConsole = int.TryParse(a.Trim(), out c) ? c : -1;
                SaveAndRefresh();
            }));
            if (!string.IsNullOrEmpty(p.Emulator) && File.Exists(p.Emulator))
            {
                o.Add(new OptionItem(Lang.T("Buka Emulator Saja"), delegate
                {
                    try
                    {
                        ProcessStartInfo psi = new ProcessStartInfo(p.Emulator);
                        psi.WorkingDirectory = Path.GetDirectoryName(p.Emulator);
                        Process.Start(psi);
                        if (cfg.MinimizeOnLaunch) launchMinimizeAt = T + 1f;
                    }
                    catch (Exception ex) { ShowToast(Lang.T("Gagal: ") + ex.Message); }
                }));
            }
            if (!string.IsNullOrEmpty(p.RomFolder)) o.Add(new OptionItem(Lang.T("Buka Folder Game"), delegate { ShowInExplorer(p.RomFolder); }));
            o.Add(new OptionItem(Lang.T("Ganti Nama Platform"), delegate
            {
                string n = Dialogs.Input(Owner32, Lang.T("Nama Platform"), Lang.T("Nama platform:"), p.Name);
                if (!string.IsNullOrEmpty(n)) { p.Name = n; SaveAndRefresh(); }
            }));
            o.Add(new OptionItem(Lang.T("Ganti Label Ikon (") + p.Short + ")", delegate
            {
                string n = Dialogs.Input(Owner32, Lang.T("Label Ikon"), Lang.T("Label singkat (maks 5 huruf), contoh PS1:"), p.Short);
                if (!string.IsNullOrEmpty(n))
                {
                    string newShort = n.Trim().ToUpperInvariant();
                    if (newShort.Length > 6) newShort = newShort.Substring(0, 6);
                    foreach (GameEntry m in cfg.Meta) if (m.Platform == p.Short) m.Platform = newShort;
                    p.Short = newShort; SaveAndRefresh();
                }
            }));
            o.Add(new OptionItem(Lang.T("Warna Ikon: ") + p.Color, delegate
            {
                using (System.Windows.Forms.ColorDialog cd = new System.Windows.Forms.ColorDialog())
                {
                    Color cur = ParseColor(p.Color);
                    cd.Color = System.Drawing.Color.FromArgb(cur.R, cur.G, cur.B); cd.FullOpen = true;
                    if (cd.ShowDialog(Owner32) == System.Windows.Forms.DialogResult.OK)
                    {
                        p.Color = string.Format("#{0:X2}{1:X2}{2:X2}", cd.Color.R, cd.Color.G, cd.Color.B);
                        badgeBrushes.Clear(); SaveAndRefresh();
                    }
                }
            }));
            o.Add(new OptionItem(p.Hidden ? Lang.T("Tampilkan di Menu Game") : Lang.T("Sembunyikan dari Menu Game"), delegate { p.Hidden = !p.Hidden; SaveAndRefresh(); }));
            o.Add(new OptionItem(Lang.T("Hapus Platform"), delegate
            {
                Confirm(Lang.T("Hapus platform ") + p.Name + "?", delegate { cfg.Platforms.Remove(p); SaveAndRefresh(); });
            }));
            return o;
        }

        List<XmbItem> BuildPlatformManager()
        {
            List<XmbItem> l = new List<XmbItem>();
            l.Add(MakeItem(Lang.T("Deteksi Emulator Otomatis"), Lang.T("Cari DuckStation, PCSX2, PPSSPP, RPCS3, RetroArch, Dolphin, dll lalu atur otomatis"), "\uE721", DetectEmulators));
            l.Add(MakeItem(Lang.T("Tambah Platform Baru"), Lang.T("Contoh: Sega Saturn, Dreamcast, Xbox, dll"), "\uE710", AddPlatform));
            foreach (PlatformCfg p in cfg.Platforms)
            {
                PlatformCfg pp = p;
                XmbItem it = MakeItem(p.Name, "", null, null);
                it.Badge = p.Short;
                it.BadgeColor = ParseColor(p.Color);
                ApplyPlatformIcon(it, p);
                it.DynSub = delegate
                {
                    return (pp.Hidden ? Lang.T("[Disembunyikan]  ") : "") +
                        (string.IsNullOrEmpty(pp.Emulator) ? Lang.T("Emulator: belum diatur") : "Emulator: " + Path.GetFileName(pp.Emulator)) +
                        "  \u2022  " + (string.IsNullOrEmpty(pp.RomFolder) ? Lang.T("Folder: belum diatur") : "Folder: " + pp.RomFolder);
                };
                it.Activate = delegate { OpenOptions(pp.Name, PlatformOptions(pp)); };
                it.Options = delegate { return PlatformOptions(pp); };
                l.Add(it);
            }
            l.Add(MakeItem(Lang.T("Kembalikan Platform Bawaan"), Lang.T("Tambahkan lagi platform default yang terhapus"), "\uE777", delegate
            {
                int added = 0;
                foreach (PlatformCfg d in Config.DefaultPlatforms())
                    if (cfg.FindPlatform(d.Short) == null) { cfg.Platforms.Add(d); added++; }
                SaveAndRefresh();
                ShowToast(added + Lang.T(" platform dikembalikan"));
            }));
            return l;
        }

        void AddPlatform()
        {
            string name = Dialogs.Input(Owner32, Lang.T("Platform Baru"), Lang.T("Nama platform (contoh: Sega Dreamcast):"), "");
            if (string.IsNullOrEmpty(name)) return;
            string shrt = Dialogs.Input(Owner32, Lang.T("Platform Baru"), Lang.T("Label singkat untuk ikon (contoh: DC):"), name.Length > 4 ? name.Substring(0, 4).ToUpperInvariant() : name.ToUpperInvariant());
            if (string.IsNullOrEmpty(shrt)) return;
            shrt = shrt.Trim().ToUpperInvariant();
            if (cfg.FindPlatform(shrt) != null) { ShowToast("Label " + shrt + Lang.T(" sudah dipakai")); return; }
            string ext = Dialogs.Input(Owner32, Lang.T("Platform Baru"), Lang.T("Ekstensi file game (contoh: cdi;gdi;chd):"), "iso;chd;zip");
            PlatformCfg p = new PlatformCfg(name.Trim(), shrt, ext ?? "", "#3A6EA5", "");
            cfg.Platforms.Add(p);
            SaveAndRefresh();
            ShowToast("Platform " + p.Name + Lang.T(" ditambahkan, sekarang atur emulatornya"));
            OpenOptions(p.Name, PlatformOptions(p));
        }

        void PickEmulator(PlatformCfg p)
        {
            string f = Dialogs.PickFile(Owner32, Lang.T("Pilih emulator untuk ") + p.Name + Lang.T(" (saran: ") + p.Hint + ")", "Program (*.exe)|*.exe", null);
            if (f == null) return;
            p.Emulator = f;
            SaveAndRefresh();
            ShowToast("Emulator " + p.Short + ": " + Path.GetFileName(f));
        }

        void PickRomFolder(PlatformCfg p)
        {
            string f = Dialogs.PickFolder(Owner32, Lang.T("Pilih folder berisi game ") + p.Name, p.RomFolder);
            if (f == null) return;
            p.RomFolder = f;
            SaveAndRefresh();
            ShowToast(GameScanner.ListRoms(p).Count + Lang.T(" game ") + p.Short + Lang.T(" ditemukan"));
        }

        void AddRomManual(PlatformCfg p)
        {
            string exts = string.Join(";", p.ExtList().Select(e => "*" + e).ToArray());
            if (exts.Length == 0) exts = "*.*";
            string[] files = Dialogs.PickFiles(Owner32, Lang.T("Pilih file game ") + p.Name, "Game " + p.Short + "|" + exts + Lang.T("|Semua file|*.*"));
            if (files == null) return;
            foreach (string f in files)
            {
                GameEntry g = cfg.GetMeta(f, p.Short, GameScanner.RomDisplayName(p, f));
                cfg.EnsureStored(g);
            }
            SaveAndRefresh();
            ShowToast(files.Length + Lang.T(" game ditambahkan ke ") + p.Short);
        }
    }
}
