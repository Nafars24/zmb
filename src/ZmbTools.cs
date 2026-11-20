using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace ZMB
{
    public partial class ZmbWindow
    {
        bool launchResume = false;

        // ===================== SAVE & MEMORY CARD =====================
        void LaunchResume(GameEntry g)
        {
            launchResume = true;
            try { Launch(g, false); } finally { launchResume = false; }
        }

        List<XmbItem> BuildSaveItems(PlatformCfg p, GameEntry g)
        {
            List<XmbItem> l = new List<XmbItem>();
            List<SaveLoc> locs = SaveManager.Locations(p);
            if (locs.Count == 0)
            {
                l.Add(MakeItem(Lang.T("Folder save tidak ditemukan"), Lang.T("Atur emulator dulu, atau isi folder save manual lewat Opsi"), "\uE783", null));
            }
            l.Add(MakeItem(Lang.T("Cadangkan Sekarang"), Lang.T("Simpan semua save ") + p.Short + Lang.T(" ke file ZIP di folder backups"), "\uE896", delegate
            {
                if (locs.Count == 0) { ShowToast(Lang.T("Tidak ada folder save untuk dicadangkan")); return; }
                ShowToast(Lang.T("Mencadangkan save ") + p.Short + " ...");
                ThreadPool.QueueUserWorkItem(delegate
                {
                    string res = null, err = null;
                    try { res = SaveManager.Backup(p, locs); } catch (Exception ex) { err = ex.Message; }
                    Dispatcher.BeginInvoke((Action)delegate
                    {
                        if (err != null) ShowToast(Lang.T("Gagal mencadangkan: ") + err);
                        else if (res == null) ShowToast(Lang.T("Tidak ada file save untuk dicadangkan"));
                        else { Sfx.Ok(); ShowToast(Lang.T("Cadangan dibuat: ") + Path.GetFileName(res)); RefreshItemsOnly(); }
                    });
                });
            }));
            XmbItem rest = MakeItem(Lang.T("Pulihkan dari Cadangan"), "", "\uE777", null);
            rest.DynSub = delegate { int n = SaveManager.Backups(p).Count; return n == 0 ? Lang.T("Belum ada cadangan") : n + Lang.T(" cadangan tersedia"); };
            rest.Children = delegate
            {
                List<XmbItem> bl = new List<XmbItem>();
                foreach (string zip in SaveManager.Backups(p))
                {
                    string z = zip;
                    XmbItem it = MakeItem(File.GetLastWriteTime(zip).ToString("dd MMM yyyy, HH:mm"), Path.GetFileName(zip) + "  \u2022  " + FormatSize(new FileInfo(zip).Length), "\uE7B8", null);
                    it.Activate = delegate
                    {
                        Confirm(Lang.T("Pulihkan save dari cadangan ini? Save sekarang akan dicadangkan dulu."), delegate
                        {
                            ThreadPool.QueueUserWorkItem(delegate
                            {
                                string err = null; int n = 0;
                                try { if (locs.Count > 0) SaveManager.Backup(p, locs); n = SaveManager.Restore(z); }
                                catch (Exception ex) { err = ex.Message; }
                                Dispatcher.BeginInvoke((Action)delegate { ShowToast(err != null ? Lang.T("Gagal: ") + err : n + Lang.T(" file save dipulihkan")); });
                            });
                        });
                    };
                    it.Options = delegate
                    {
                        List<OptionItem> o = new List<OptionItem>();
                        o.Add(new OptionItem(Lang.T("Buka Lokasi File"), delegate { ShowInExplorer(z); }));
                        o.Add(new OptionItem(Lang.T("Hapus Cadangan Ini"), delegate { Confirm(Lang.T("Hapus cadangan ini?"), delegate { try { File.Delete(z); } catch { } RefreshItemsOnly(); }); }));
                        return o;
                    };
                    bl.Add(it);
                }
                return bl;
            };
            l.Add(rest);

            string filter = g != null ? Achievements.Norm(Path.GetFileNameWithoutExtension(g.Path)) : null;
            if (g != null && p.Short == "PS3")
            {
                // save RPCS3 dinamai dengan Title ID (mis. BLUS30443)
                string tid = null;
                try { tid = Ps3Meta.Get(g.Path).TitleId; } catch { }
                filter = string.IsNullOrEmpty(tid) ? null : Achievements.Norm(tid);
            }
            foreach (SaveLoc loc in locs)
            {
                SaveLoc lc = loc;
                XmbItem f = MakeItem(loc.Label, loc.Dir, "\uE8B7", null);
                f.Children = delegate
                {
                    List<XmbItem> fl = new List<XmbItem>();
                    List<string> files = (filter != null && filter.Length >= 3) ? SaveManager.Files(lc, filter) : new List<string>();
                    bool filtered = files.Count > 0;
                    if (!filtered) files = SaveManager.Files(lc, null);
                    if (g != null && !filtered && files.Count > 0)
                        fl.Add(MakeItem(Lang.T("Menampilkan semua save"), Lang.T("Emulator ini menamai save dengan kode game, jadi tidak bisa difilter per judul"), "\uE946", null));
                    foreach (string file in files.Take(200))
                    {
                        string ff = file;
                        DateTime t = File.GetLastWriteTime(file);
                        XmbItem it = MakeItem(Path.GetFileName(file), t.ToString("dd MMM yyyy, HH:mm") + "  \u2022  " + FormatSize(new FileInfo(file).Length) + "  \u2022  " + Path.GetDirectoryName(file).Substring(lc.Dir.TrimEnd('\\').Length).TrimStart('\\'), "\uE74E", delegate { ShowInExplorer(ff); });
                        it.Options = delegate
                        {
                            List<OptionItem> o = new List<OptionItem>();
                            o.Add(new OptionItem(Lang.T("Buka Lokasi File"), delegate { ShowInExplorer(ff); }));
                            o.Add(new OptionItem(Lang.T("Pindahkan ke Sampah ZMB"), delegate
                            {
                                Confirm(Lang.T("Pindahkan ") + Path.GetFileName(ff) + Lang.T(" ke folder backups\\_dihapus?"), delegate
                                {
                                    try
                                    {
                                        string dest = Path.Combine(Path.Combine(SaveManager.BackupDir(p), "_dihapus"), DateTime.Now.ToString("yyyyMMdd_HHmmss") + "_" + Path.GetFileName(ff));
                                        Directory.CreateDirectory(Path.GetDirectoryName(dest));
                                        File.Move(ff, dest);
                                        RefreshItemsOnly();
                                    }
                                    catch (Exception ex) { ShowToast(Lang.T("Gagal: ") + ex.Message); }
                                });
                            }));
                            return o;
                        };
                        fl.Add(it);
                    }
                    if (fl.Count == 0) fl.Add(MakeItem(Lang.T("Belum ada file save"), lc.Dir, "\uE7BA", null));
                    return fl;
                };
                f.Options = delegate
                {
                    List<OptionItem> o = new List<OptionItem>();
                    o.Add(new OptionItem(Lang.T("Buka di Explorer"), delegate { ShowInExplorer(lc.Dir); }));
                    return o;
                };
                l.Add(f);
            }

            XmbItem man = MakeItem(Lang.T("Folder Save Manual"), "", "\uE70F", delegate
            {
                string v = Dialogs.Input(Owner32, Lang.T("Folder Save"), Lang.T("Folder tambahan yang berisi save/memory card ") + p.Name + Lang.T(". Pisahkan dengan titik koma (;):"), p.SaveDirs);
                if (v != null) { p.SaveDirs = v.Trim(); SaveAndRefresh(); }
            });
            man.DynSub = delegate { return string.IsNullOrEmpty(p.SaveDirs) ? Lang.T("Otomatis dari emulator") : p.SaveDirs; };
            l.Add(man);

            XmbItem ra = MakeItem(Lang.T("Argumen Lanjutkan (Save State)"), "", "\uE768", delegate { EditResumeArgs(p); });
            ra.DynSub = delegate { string r = SaveManager.ResumeArgsFor(p); return r == null ? Lang.T("Belum diatur (DuckStation otomatis)") : r; };
            l.Add(ra);
            return l;
        }

        void EditResumeArgs(PlatformCfg p)
        {
            string v = Dialogs.Input(Owner32, Lang.T("Argumen Lanjutkan"),
                Lang.T("Argumen untuk membuka game langsung ke save state terakhir. {args} = argumen biasa.\nDuckStation: -resume {args}   (otomatis)\nCek dokumentasi emulator lain untuk opsi load state lewat command line."), p.ResumeArgs);
            if (v != null) { p.ResumeArgs = v.Trim(); SaveAndRefresh(); }
        }

        void OpenSaves(PlatformCfg p, GameEntry g)
        {
            XmbItem it = MakeItem("Save & Memory Card", p.Name, "\uE74E", null);
            it.Children = delegate { return BuildSaveItems(p, g); };
            PushLevel(it);
        }

        // ===================== DETEKSI EMULATOR =====================
        bool detecting = false;

        void DetectEmulators()
        {
            if (detecting) { ShowToast(Lang.T("Sedang mencari emulator...")); return; }
            detecting = true;
            ShowToast(Lang.T("Mencari emulator di laptop (bisa sampai 1 menit)..."));
            ThreadPool.QueueUserWorkItem(delegate
            {
                List<EmuFound> found = null; string err = null;
                try { found = EmuDetect.Detect(); } catch (Exception ex) { err = ex.Message; }
                Dispatcher.BeginInvoke((Action)delegate
                {
                    detecting = false;
                    if (err != null) { ShowToast(Lang.T("Gagal: ") + err); return; }
                    ShowEmuResults(found);
                });
            });
        }

        void ApplyEmu(EmuFound f)
        {
            PlatformCfg p = cfg.FindPlatform(f.Platform);
            if (p == null) return;
            p.Emulator = f.Exe;
            p.Args = f.Args;
        }

        void ShowEmuResults(List<EmuFound> found)
        {
            if (found == null || found.Count == 0) { Sfx.Error(); ShowToast(Lang.T("Tidak ada emulator yang dikenali. Atur manual lewat Opsi platform.")); return; }
            Dictionary<string, EmuFound> best = EmuDetect.BestPerPlatform(found);
            List<EmuFound> missing = best.Values.Where(f => { PlatformCfg p = cfg.FindPlatform(f.Platform); return p != null && (string.IsNullOrEmpty(p.Emulator) || !File.Exists(p.Emulator)); }).ToList();
            List<OptionItem> o = new List<OptionItem>();
            o.Add(new OptionItem(Lang.T("Pasang ke platform yang belum diatur (") + missing.Count + ")", delegate
            {
                foreach (EmuFound f in missing) ApplyEmu(f);
                Achievements.ResetRootCache();
                SaveAndRefresh();
                ShowToast(missing.Count + Lang.T(" emulator dipasang"));
            }));
            o.Add(new OptionItem(Lang.T("Pasang & timpa semua (") + best.Count + ")", delegate
            {
                Confirm(Lang.T("Timpa pengaturan emulator semua platform dengan hasil deteksi?"), delegate
                {
                    foreach (EmuFound f in best.Values) ApplyEmu(f);
                    Achievements.ResetRootCache();
                    SaveAndRefresh();
                    ShowToast(best.Count + Lang.T(" emulator dipasang"));
                });
            }));
            foreach (EmuFound f in best.Values.OrderBy(x => x.Platform))
            {
                EmuFound ff = f;
                PlatformCfg p = cfg.FindPlatform(f.Platform);
                string cur = (p != null && !string.IsNullOrEmpty(p.Emulator)) ? Lang.T(" (sekarang: ") + Path.GetFileNameWithoutExtension(p.Emulator) + ")" : Lang.T(" (belum diatur)");
                o.Add(new OptionItem(f.Platform + "  \u2190  " + f.Name + cur, delegate { ApplyEmu(ff); Achievements.ResetRootCache(); SaveAndRefresh(); ShowToast(ff.Platform + ": " + ff.Name); }));
            }
            OpenOptions(Lang.T("Ditemukan ") + found.Count + Lang.T(" emulator"), o);
        }

        // ===================== PEMERIKSA ROM =====================
        List<RomCheckInput> RomCheckInputs()
        {
            List<RomCheckInput> l = new List<RomCheckInput>();
            foreach (PlatformCfg p in cfg.Platforms)
            {
                if (p.Hidden) continue;
                foreach (string rom in GameScanner.ListRoms(p))
                {
                    GameEntry g = cfg.GetMeta(rom, p.Short, GameScanner.RomDisplayName(p, rom));
                    RomCheckInput ri = new RomCheckInput();
                    ri.Game = g; ri.Platform = p;
                    ri.HasCover = HasCover(g, p);
                    string box = null; try { box = GameScanner.FindBoxArt(g, p); } catch { }
                    ri.HasBox = box != null;
                    bool crisp = false; string bg = null;
                    try { bg = GameScanner.FindBackground(g, p, out crisp); } catch { }
                    ri.HasBg = bg != null && crisp;
                    l.Add(ri);
                }
            }
            return l;
        }

        List<XmbItem> BuildRomCheck()
        {
            List<XmbItem> l = new List<XmbItem>();
            List<RomIssue> res = Peek<List<RomIssue>>("romcheck");
            if (res == null && !asyncPending.Contains("romcheck"))
            {
                l.Add(MakeItem(Lang.T("Mulai Periksa"), Lang.T("Cek file rusak, duplikat, dan media yang belum ada"), "\uE721", StartRomCheck));
                return l;
            }
            if (res == null) return LoadingItems();
            l.Add(MakeItem(Lang.T("Periksa Ulang"), Lang.T("Total masalah: ") + res.Count, "\uE72C", StartRomCheck));
            string[] kinds = { "rusak", "duplikat", "cover", "box", "latar" };
            string[] names = { Lang.T("File Rusak / Bermasalah"), Lang.T("Duplikat"), Lang.T("Tanpa Cover"), Lang.T("Tanpa Box Art"), Lang.T("Tanpa Latar HD") };
            string[] glyphs = { "\uE783", "\uE8C8", "\uE91B", "\uE8F1", "\uE8B9" };
            for (int i = 0; i < kinds.Length; i++)
            {
                string kd = kinds[i];
                List<RomIssue> items = res.Where(x => x.Kind == kd).ToList();
                XmbItem f = MakeItem(names[i] + " (" + items.Count + ")", items.Count == 0 ? Lang.T("Aman, tidak ada masalah") : Lang.T("Tekan untuk melihat daftar"), glyphs[i], null);
                if (items.Count > 0)
                {
                    f.Children = delegate
                    {
                        List<XmbItem> il = new List<XmbItem>();
                        if (kd == "cover" || kd == "box" || kd == "latar")
                            il.Add(MakeItem(Lang.T("Unduh Media untuk Semua (") + items.Count + ")", "Pakai SteamGridDB / ScreenScraper", "\uE896", delegate { StartScrape(items.Select(x => x.Game).Distinct().ToList(), false); }));
                        if (kd == "duplikat")
                            il.Add(MakeItem(Lang.T("Pindahkan Semua Duplikat"), Lang.T("Ke subfolder _duplikat di folder ROM masing-masing"), "\uE8DE", delegate
                            {
                                Confirm(Lang.T("Pindahkan ") + items.Count + Lang.T(" file duplikat ke folder _duplikat?"), delegate
                                {
                                    int moved = 0;
                                    foreach (RomIssue x in items)
                                    {
                                        try
                                        {
                                            string dir = Path.Combine(Path.GetDirectoryName(x.Path), "_duplikat");
                                            Directory.CreateDirectory(dir);
                                            File.Move(x.Path, Path.Combine(dir, Path.GetFileName(x.Path)));
                                            moved++;
                                        }
                                        catch { }
                                    }
                                    ShowToast(moved + Lang.T(" file dipindahkan"));
                                    InvalidateAsync("romcheck");
                                    SaveAndRefresh();
                                });
                            }));
                        foreach (RomIssue x in items.Take(500))
                        {
                            RomIssue xx = x;
                            XmbItem it = MakeItem(x.Name, x.Platform + "  \u2022  " + x.Detail + "  \u2022  " + Path.GetFileName(x.Path), null, delegate { ShowInExplorer(xx.Path); });
                            it.Badge = x.Platform;
                            PlatformCfg pp = cfg.FindPlatform(x.Platform);
                            if (pp != null) it.BadgeColor = ParseColor(pp.Color);
                            it.Options = delegate
                            {
                                List<OptionItem> o = new List<OptionItem>();
                                o.Add(new OptionItem(Lang.T("Buka Lokasi File"), delegate { ShowInExplorer(xx.Path); }));
                                if (xx.Game != null) o.Add(new OptionItem(Lang.T("Unduh Media Game Ini"), delegate { StartScrape(new List<GameEntry> { xx.Game }, true); }));
                                return o;
                            };
                            il.Add(it);
                        }
                        return il;
                    };
                }
                l.Add(f);
            }
            return l;
        }

        void StartRomCheck()
        {
            List<RomCheckInput> input = RomCheckInputs();
            InvalidateAsync("romcheck");
            ShowToast(Lang.T("Memeriksa ") + input.Count + Lang.T(" file game..."));
            GetAsync<List<RomIssue>>("romcheck", delegate { return RomChecker.Check(input); }, 1e9, delegate (List<RomIssue> r)
            {
                if (r != null) ShowToast(Lang.T("Pemeriksaan selesai: ") + r.Count(x => x.Kind == "rusak") + Lang.T(" rusak, ") + r.Count(x => x.Kind == "duplikat") + Lang.T(" duplikat"));
            }, true);
            RefreshItemsOnly();
        }

        // ===================== UPDATE ZMB =====================
        bool updating = false;

        void InstallUpdate(string zip) { InstallUpdate(zip, true); }

        void InstallUpdate(string zip, bool askFirst)
        {
            if (updating) return;
            Version v = Updater.ZipVersion(zip);
            if (v == null) { Sfx.Error(); ShowToast(Lang.T("ZIP ini bukan paket ZMB")); return; }
            Action build = delegate
            {
                updating = true;
                ShowToast(Lang.T("Membangun ZMB ") + v + Lang.T(" ... (sekitar 10-30 detik)"));
                ThreadPool.QueueUserWorkItem(delegate
                {
                    string log = null, root = null, exe = null, err = null;
                    try { exe = Updater.Build(zip, out log, out root); } catch (Exception ex) { err = ex.Message; }
                    Dispatcher.BeginInvoke((Action)delegate
                    {
                        updating = false;
                        if (exe == null)
                        {
                            Sfx.Error();
                            string msg = err ?? log ?? Lang.T("tidak diketahui");
                            try { File.WriteAllText(Path.Combine(Config.BaseDir, "zmb_update_error.log"), msg); } catch { }
                            ShowToast(Lang.T("Update gagal dibangun. Detail di zmb_update_error.log (kirim ke Claude)"));
                            return;
                        }
                        string r2 = root, e2 = exe;
                        Confirm(Lang.T("Update siap. Restart ZMB sekarang?"), delegate
                        {
                            cfg.Save();
                            try { Updater.InstallAndRestart(e2, r2); System.Windows.Application.Current.Shutdown(); }
                            catch (Exception ex) { ShowToast(Lang.T("Gagal memasang: ") + ex.Message); }
                        });
                    });
                });
            };
            if (askFirst) Confirm(Lang.T("Pasang ZMB versi ") + v + Lang.T(" (sekarang ") + Updater.Current + ")?", build);
            else build();
        }

        // ===================== UPDATE ONLINE =====================
        bool checkingOnline = false;
        double lastOnlineCheck = -1e9;

        void CheckOnlineUpdate(bool silent)
        {
            string repo = Updater.NormalizeRepo(cfg.UpdateRepo);
            if (repo.Length == 0) { if (!silent) ShowToast(Lang.T("Isi dulu Repositori Update (GitHub) di Pengaturan > Update ZMB")); return; }
            if (checkingOnline || updating) return;
            checkingOnline = true;
            lastOnlineCheck = T;
            if (!silent) ShowToast(Lang.T("Mengecek update di GitHub..."));
            ThreadPool.QueueUserWorkItem(delegate
            {
                Updater.OnlineRelease rel = null; string err = null;
                try { rel = Updater.CheckGitHub(repo); } catch (Exception ex) { err = ex.Message; }
                Dispatcher.BeginInvoke((Action)delegate
                {
                    checkingOnline = false;
                    if (err != null) { if (!silent) ShowToast(err); return; }
                    if (rel.Version <= Updater.Current) { if (!silent) ShowToast(Lang.T("ZMB sudah versi terbaru (") + Updater.Current + ")"); return; }
                    string note = rel.Notes.Split('\n').Select(x => x.Trim().TrimStart('-', '*', ' ')).FirstOrDefault(x => x.Length > 0) ?? "";
                    if (note.Length > 90) note = note.Substring(0, 90) + "...";
                    if (confirmOpen || mode != Mode.Xmb) { ShowToast("Update ZMB " + rel.Version + Lang.T(" tersedia. Buka Pengaturan > Update ZMB")); return; }
                    Updater.OnlineRelease r = rel;
                    Confirm("Update ZMB " + r.Version + Lang.T(" tersedia") + (note.Length > 0 ? ": " + note : "") + Lang.T(". Unduh & pasang sekarang?"), delegate
                    {
                        ShowToast(Lang.T("Mengunduh ZMB ") + r.Version + " ...");
                        updating = true;
                        ThreadPool.QueueUserWorkItem(delegate
                        {
                            string zip = null, derr = null;
                            try { zip = Updater.DownloadRelease(r); } catch (Exception ex) { derr = ex.Message; }
                            Dispatcher.BeginInvoke((Action)delegate
                            {
                                updating = false;
                                if (zip == null) { Sfx.Error(); ShowToast(Lang.T("Gagal mengunduh update: ") + derr); return; }
                                InstallUpdate(zip, false);
                            });
                        });
                    });
                });
            });
        }

        void CheckDownloadsForUpdate(bool silentIfNone)
        {
            ThreadPool.QueueUserWorkItem(delegate
            {
                Version v; string f = Updater.FindNewerInDownloads(out v);
                Dispatcher.BeginInvoke((Action)delegate
                {
                    if (f == null) { if (!silentIfNone) ShowToast(Lang.T("Tidak ada update baru di folder Downloads (versi sekarang ") + Updater.Current + ")"); return; }
                    if (confirmOpen || mode != Mode.Xmb) { ShowToast("Update ZMB " + v + Lang.T(" ada di Downloads. Pasang lewat Pengaturan > Update ZMB")); return; }
                    InstallUpdate(f);
                });
            });
        }

        List<XmbItem> BuildUpdateItems()
        {
            List<XmbItem> l = new List<XmbItem>();
            l.Add(MakeItem(Lang.T("Versi Sekarang: ") + Updater.Current, Lang.T("ZMB menyusun ulang dirinya sendiri dari file ZIP, tanpa BUILD.bat"), "\uE946", null));
            l.Add(MakeItem(Lang.T("Cek Folder Downloads"), Lang.T("Cari file ZMB*.zip yang lebih baru di folder Downloads"), "\uE896", delegate { CheckDownloadsForUpdate(false); }));
            l.Add(MakeItem(Lang.T("Pasang dari File ZIP..."), Lang.T("Pilih file ZMB.zip versi baru"), "\uE8E5", delegate
            {
                string f = Dialogs.PickFile(Owner32, Lang.T("Pilih ZMB.zip versi baru"), "ZIP|*.zip", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"));
                if (f != null) InstallUpdate(f);
            }));
            l.Add(Toggle(Lang.T("Cek Otomatis Saat Mulai"), Lang.T("Tawarkan update kalau ada ZMB.zip baru di Downloads"), "\uE72C", delegate { return cfg.CheckUpdateDownloads; }, delegate (bool v) { cfg.CheckUpdateDownloads = v; }));

            XmbItem repo = MakeItem(Lang.T("Repositori Update (GitHub)"), "", "\uE774", delegate
            {
                string r = Dialogs.Input(Owner32, "Repositori Update", Lang.T("Nama repositori GitHub tempat rilis ZMB (format: namakamu/zmb, atau tempel link github.com/...):"), cfg.UpdateRepo);
                if (r == null) return;
                string n = Updater.NormalizeRepo(r);
                if (r.Trim().Length > 0 && n.Length == 0) { Sfx.Error(); ShowToast(Lang.T("Format repositori tidak dikenali")); return; }
                cfg.UpdateRepo = n; cfg.Save();
                if (n.Length > 0) CheckOnlineUpdate(false);
            });
            repo.DynSub = delegate { return string.IsNullOrEmpty(cfg.UpdateRepo) ? Lang.T("Belum diatur") : "github.com/" + cfg.UpdateRepo; };
            l.Add(repo);
            l.Add(MakeItem(Lang.T("Cek Update Online Sekarang"), Lang.T("Ambil versi terbaru dari GitHub"), "\uE895", delegate { CheckOnlineUpdate(false); }));
            l.Add(Toggle(Lang.T("Update Otomatis dari Internet"), Lang.T("Cek GitHub saat ZMB dibuka dan tiap 6 jam"), "\uE774", delegate { return cfg.AutoUpdateOnline; }, delegate (bool v) { cfg.AutoUpdateOnline = v; }));
            return l;
        }
    }
}
