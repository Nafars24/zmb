using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Media;

namespace ZMB
{
    class AchvSrc
    {
        public string Key;
        public Func<AchvSet> Fetch;
        public Action<AchvSet> Done;
        public string Label;
    }

    public partial class ZmbWindow
    {
        // ===================== CACHE ASINKRON =====================
        Dictionary<string, object> asyncVals = new Dictionary<string, object>();
        Dictionary<string, DateTime> asyncTimes = new Dictionary<string, DateTime>();
        Dictionary<string, string> asyncErr = new Dictionary<string, string>();
        HashSet<string> asyncPending = new HashSet<string>();
        double lastNavAt = 0;

        // achievement yang dipantau saat game berjalan
        AchvSrc watchSrc = null;
        int watchBefore = -1;
        HashSet<string> watchIds = null;

        T Peek<T>(string key) where T : class
        {
            object v;
            return asyncVals.TryGetValue(key, out v) ? v as T : null;
        }

        string AsyncError(string key)
        {
            string e;
            return asyncErr.TryGetValue(key, out e) ? e : null;
        }

        void InvalidateAsync(string key) { asyncTimes.Remove(key); }

        // Ambil data di thread lain. Mengembalikan data lama/cache (atau null) dan memperbarui saat selesai.
        T GetAsync<T>(string key, Func<T> fetch, double maxAgeMin, Action<T> done, bool refreshUi) where T : class
        {
            object v;
            bool have = asyncVals.TryGetValue(key, out v);
            DateTime t;
            bool fresh = asyncTimes.TryGetValue(key, out t) && (DateTime.Now - t).TotalMinutes < maxAgeMin;
            if (!fresh && !asyncPending.Contains(key))
            {
                asyncPending.Add(key);
                ThreadPool.QueueUserWorkItem(delegate
                {
                    T r = null; string err = null;
                    try { r = fetch(); }
                    catch (Exception ex) { err = ex.Message; }
                    Dispatcher.BeginInvoke((Action)delegate
                    {
                        asyncPending.Remove(key);
                        asyncTimes[key] = DateTime.Now;
                        if (r != null) { asyncVals[key] = r; asyncErr.Remove(key); }
                        else asyncErr[key] = err ?? Lang.T("Gagal memuat");
                        if (done != null) { try { done(r); } catch (Exception ex2) { Log(ex2.ToString()); } }
                        if (refreshUi) RefreshItemsOnly();
                    });
                });
            }
            return have ? v as T : null;
        }

        bool NavSettled { get { return T - lastNavAt > 0.7; } }

        // ===================== SUMBER ACHIEVEMENT PER GAME =====================
        string EffectiveSteamId()
        {
            return string.IsNullOrEmpty(cfg.SteamId) ? Achievements.DetectSteamId() : cfg.SteamId.Trim();
        }

        bool SteamReady { get { return !string.IsNullOrEmpty(cfg.SteamApiKey) && !string.IsNullOrEmpty(EffectiveSteamId()); } }
        bool RaReady { get { return !string.IsNullOrEmpty(cfg.RaUser) && !string.IsNullOrEmpty(cfg.RaApiKey); } }

        AchvSrc SourceFor(GameEntry g)
        {
            if (g == null) return null;
            AchvSrc src = new AchvSrc();
            if (g.Platform == "STEAM" || (g.Platform == "WIN" && !string.IsNullOrEmpty(g.SteamAppId)))
            {
                if (!SteamReady) return null;
                string app = g.Platform == "STEAM" ? g.Path.Substring(g.Path.LastIndexOf('/') + 1) : g.SteamAppId.Trim();
                string k = cfg.SteamApiKey.Trim(), sid = EffectiveSteamId();
                src.Key = "steam:" + app;
                src.Label = "Achievement Steam";
                src.Fetch = delegate { return Achievements.Steam(app, k, sid); };
                return src;
            }
            PlatformCfg p = cfg.FindPlatform(g.Platform);
            if (p == null) return null;
            bool ps3 = p.Short == "PS3" || (!string.IsNullOrEmpty(p.Emulator) && Path.GetFileName(p.Emulator).ToLowerInvariant().Contains("rpcs3"));
            if (ps3)
            {
                string root = Achievements.Rpcs3Root(cfg);
                if (root == null) return null;
                string path = g.Path, name = g.Name, np = g.NpCommId;
                src.Key = "rpcs3:" + path;
                src.Label = Lang.T("Trofi PS3");
                src.Fetch = delegate { return Achievements.Rpcs3ForGame(path, name, root, np); };
                return src;
            }
            int cid = Achievements.RaConsoleFor(p, g.Path);
            if (cid <= 0 || !RaReady) return null;
            string rom = g.Path, nm = g.Name, ru = cfg.RaUser.Trim(), rk = cfg.RaApiKey.Trim();
            int known = g.RaGameId;
            src.Key = "ra:" + rom;
            src.Label = "RetroAchievements";
            src.Fetch = delegate { return Achievements.RetroForRom(rom, nm, cid, known, ru, rk); };
            GameEntry ge = g;
            src.Done = delegate (AchvSet s)
            {
                if (s != null && s.ResolvedRaId > 0 && ge.RaGameId != s.ResolvedRaId)
                {
                    ge.RaGameId = s.ResolvedRaId;
                    cfg.EnsureStored(ge);
                    cfg.Save();
                }
            };
            return src;
        }

        // Teks singkat untuk subjudul game: "Trofi 12/40 (30%)"
        string AchvBrief(GameEntry g)
        {
            AchvSrc src = SourceFor(g);
            if (src == null) return "";
            AchvSet s = NavSettled ? GetAsync<AchvSet>(src.Key, src.Fetch, 10, src.Done, false) : Peek<AchvSet>(src.Key);
            if (s == null) return NavSettled && asyncPending.Contains(src.Key) ? Lang.T("  \u2022  Memuat trofi...") : "";
            if (s.Error != null || s.Total == 0) return "";
            return Lang.T("  \u2022  Trofi ") + s.Unlocked + "/" + s.Total + " (" + s.Percent + "%)";
        }

        void OpenAchvFor(GameEntry g)
        {
            AchvSrc src = SourceFor(g);
            if (src == null)
            {
                Sfx.Error();
                ShowToast(Lang.T("Atur akun dulu di Pengaturan > Akun Trofi & Achievement"));
                return;
            }
            XmbItem it = MakeItem(g.Name, src.Label, "#TROPHY", null);
            it.Children = delegate { return AchvLevel(src.Key, src.Fetch, src.Done); };
            PushLevel(it);
        }

        List<XmbItem> AchvLevel(string key, Func<AchvSet> fetch, Action<AchvSet> done)
        {
            AchvSet s = GetAsync<AchvSet>(key, fetch, 10, done, true);
            if (s == null)
            {
                string err = AsyncError(key);
                if (err != null && !asyncPending.Contains(key)) return ErrorItems(err, key);
                return LoadingItems();
            }
            if (s.Error != null) return ErrorItems(s.Error, key);
            return AchvItems(s, key);
        }

        List<XmbItem> LoadingItems()
        {
            List<XmbItem> l = new List<XmbItem>();
            l.Add(MakeItem(Lang.T("Memuat..."), Lang.T("Mengambil data trofi / achievement"), "\uE895", null));
            return l;
        }

        List<XmbItem> ErrorItems(string msg, string key)
        {
            List<XmbItem> l = new List<XmbItem>();
            l.Add(MakeItem(Lang.T("Tidak bisa memuat"), msg, "\uE783", delegate { InvalidateAsync(key); RefreshItemsOnly(); }));
            l.Add(MakeItem(Lang.T("Coba Lagi"), Lang.T("Muat ulang data"), "\uE72C", delegate { InvalidateAsync(key); RefreshItemsOnly(); }));
            return l;
        }

        static readonly Color GradeP = Color.FromRgb(150, 175, 215), GradeG = Color.FromRgb(205, 165, 40),
            GradeS = Color.FromRgb(160, 165, 175), GradeB = Color.FromRgb(160, 95, 50);

        static string GradeName(string g)
        {
            switch (g) { case "P": return "Platinum"; case "G": return Lang.T("Emas"); case "S": return Lang.T("Perak"); default: return Lang.T("Perunggu"); }
        }

        static Color GradeColor(string g)
        {
            switch (g) { case "P": return GradeP; case "G": return GradeG; case "S": return GradeS; default: return GradeB; }
        }

        static Func<ImageSource> IconLoader(string src, int size)
        {
            if (string.IsNullOrEmpty(src)) return null;
            if (src.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return delegate { return ImgConv.LoadUrl(src, size); };
            return delegate { return ImgConv.LoadFile(src, size); };
        }

        string SetSummary(AchvSet s)
        {
            if (s.Source == "RPCS3")
                return "Platinum " + s.CountGrade("P", true) + Lang.T("  \u2022  Emas ") + s.CountGrade("G", true) + "/" + s.CountGrade("G", false) +
                    Lang.T("  \u2022  Perak ") + s.CountGrade("S", true) + "/" + s.CountGrade("S", false) + Lang.T("  \u2022  Perunggu ") + s.CountGrade("B", true) + "/" + s.CountGrade("B", false);
            if (s.Source == "RetroAchievements")
            {
                int pts = s.Items.Sum(a => a.Points), got = s.Items.Where(a => a.Unlocked).Sum(a => a.Points);
                int hc = s.Items.Count(a => a.Hardcore);
                return s.Extra + Lang.T("  \u2022  Poin ") + got + " / " + pts + (hc > 0 ? "  \u2022  Hardcore " + hc : "");
            }
            return s.Title;
        }

        List<XmbItem> AchvItems(AchvSet s, string key)
        {
            List<XmbItem> l = new List<XmbItem>();
            XmbItem head = MakeItem(Lang.T("Progres: ") + s.Unlocked + " / " + s.Total + "  (" + s.Percent + "%)", SetSummary(s), "#TROPHY", null);
            head.Progress = s.Total > 0 ? (double)s.Unlocked / s.Total : 0;
            head.AchSet = s;
            if (!string.IsNullOrEmpty(s.Icon)) { head.ImageKey = "ach:" + s.Icon; head.ImageLoader = IconLoader(s.Icon, 460); }
            head.Options = delegate
            {
                List<OptionItem> o = new List<OptionItem>();
                o.Add(new OptionItem(Lang.T("Muat Ulang"), delegate { InvalidateAsync(key); RefreshItemsOnly(); }));
                return o;
            };
            l.Add(head);

            IEnumerable<Achv> ordered = s.Items.Where(a => a.Unlocked).OrderByDescending(a => a.When)
                .Concat(s.Items.Where(a => !a.Unlocked).OrderBy(a => a.Order));
            foreach (Achv a in ordered)
            {
                bool secret = a.Hidden && !a.Unlocked;
                XmbItem it = MakeItem(secret ? (s.Source == "RPCS3" ? Lang.T("Trofi Tersembunyi") : Lang.T("Achievement Tersembunyi")) : a.Name, "", null, null);
                List<string> parts = new List<string>();
                if (s.Source == "RPCS3") parts.Add(GradeName(a.Grade));
                if (!secret && a.Desc.Length > 0) parts.Add(a.Desc);
                if (a.Unlocked) parts.Add(a.When > DateTime.MinValue ? Lang.T("Terbuka ") + a.When.ToString("dd MMM yyyy, HH:mm") : Lang.T("Terbuka"));
                else parts.Add(Lang.T("Terkunci"));
                if (a.Points > 0 && s.Source == "RetroAchievements") parts.Add(a.Points + Lang.T(" poin") + (a.Hardcore ? " (Hardcore)" : ""));
                if (a.Rarity >= 0) parts.Add(Lang.T("Dimiliki ") + a.Rarity.ToString("0.0") + "% pemain");
                it.Subtitle = string.Join("  \u2022  ", parts.ToArray());

                string icon = a.Unlocked ? a.Icon : (a.IconLocked ?? a.Icon);
                bool needDim = !a.Unlocked && string.IsNullOrEmpty(a.IconLocked);
                if (secret && s.Source == "RPCS3") icon = null;
                if (!string.IsNullOrEmpty(icon)) { it.ImageKey = "ach:" + icon; it.ImageLoader = IconLoader(icon, 160); }
                it.Dim = needDim;
                it.Ach = a;
                it.AchSet = s;
                if (s.Source == "RPCS3") { it.Badge = a.Grade; it.BadgeColor = GradeColor(a.Grade); }
                else it.Glyph = a.Unlocked ? "#TROPHY" : "\uE72E";
                Achv aa = a;
                it.Options = delegate
                {
                    List<OptionItem> o = new List<OptionItem>();
                    if (aa.Unlocked) o.Add(new OptionItem(Lang.T("Putar Animasi Trofi"), delegate { EnqueueTrophyPopup(aa, s); }));
                    o.Add(new OptionItem(Lang.T("Muat Ulang"), delegate { InvalidateAsync(key); RefreshItemsOnly(); }));
                    return o;
                };
                l.Add(it);
            }
            return l;
        }

        // ===================== KOLEKSI TROFI =====================
        List<XmbItem> BuildTrophyHub()
        {
            List<XmbItem> l = new List<XmbItem>();

            XmbItem ps3 = MakeItem("PlayStation 3 (RPCS3)", "", null, null);
            ps3.Badge = "PS3"; ps3.BadgeColor = Color.FromRgb(30, 30, 34);
            ps3.DynSub = delegate
            {
                string root = Achievements.Rpcs3Root(cfg);
                if (root == null) return Lang.T("Atur emulator RPCS3 di platform PS3, atau folder RPCS3 di Pengaturan");
                List<AchvSet> all = Peek<List<AchvSet>>("rpcs3:all");
                if (all == null) return Lang.T("Trofi lokal dari ") + root;
                int plat = all.Sum(x => x.CountGrade("P", true)), gold = all.Sum(x => x.CountGrade("G", true));
                int silver = all.Sum(x => x.CountGrade("S", true)), bronze = all.Sum(x => x.CountGrade("B", true));
                double lf;
                int lvl = PsnLevel(plat * 180 + gold * 90 + silver * 30 + bronze * 15, out lf);
                return "Level " + lvl + " (" + (int)(lf * 100) + "%)  \u2022  " + all.Count + Lang.T(" game  \u2022  Platinum ") + plat + Lang.T("  \u2022  Emas ") + gold + Lang.T("  \u2022  Perak ") + silver + Lang.T("  \u2022  Perunggu ") + bronze;
            };
            ps3.Children = BuildTrophyRpcs3;
            l.Add(ps3);

            XmbItem st = MakeItem("Steam", "", null, null);
            st.Badge = "STEAM"; st.BadgeColor = Color.FromRgb(27, 40, 56);
            st.DynSub = delegate { return SteamReady ? Lang.T("Akun Steam: ") + EffectiveSteamId() : Lang.T("Atur Steam Web API Key di Pengaturan > Akun Trofi"); };
            st.Children = BuildTrophySteam;
            l.Add(st);

            XmbItem ra = MakeItem("RetroAchievements", "", null, null);
            ra.Badge = "RA"; ra.BadgeColor = Color.FromRgb(20, 90, 170);
            ra.DynSub = delegate
            {
                if (!RaReady) return Lang.T("Atur username & Web API Key di Pengaturan > Akun Trofi");
                string u = cfg.RaUser.Trim(), k = cfg.RaApiKey.Trim();
                string prof = NavSettled ? GetAsync<string>("ra:profile", delegate { return Achievements.RetroProfile(u, k); }, 15, null, false) : Peek<string>("ra:profile");
                return u + (prof != null ? "  \u2022  " + prof : "");
            };
            ra.Children = BuildTrophyRA;
            l.Add(ra);

            XmbItem acc = MakeItem(Lang.T("Pengaturan Akun"), Lang.T("Steam API Key, akun RetroAchievements, folder RPCS3"), "\uE713", null);
            acc.Children = BuildAchvAccount;
            l.Add(acc);
            return l;
        }

        XmbItem SetItem(AchvSet s, string key)
        {
            XmbItem it = MakeItem(s.Title, "", "#TROPHY", null);
            it.Subtitle = s.Unlocked + " / " + s.Total + " (" + s.Percent + "%)  \u2022  " + SetSummary(s);
            it.Progress = s.Total > 0 ? (double)s.Unlocked / s.Total : 0;
            it.AchSet = s;
            if (!string.IsNullOrEmpty(s.Icon)) { it.ImageKey = "ach:" + s.Icon; it.ImageLoader = IconLoader(s.Icon, 400); }
            AchvSet ss = s;
            it.Children = delegate { return AchvItems(ss, key); };
            return it;
        }

        List<XmbItem> BuildTrophyRpcs3()
        {
            string root = Achievements.Rpcs3Root(cfg);
            List<XmbItem> l = new List<XmbItem>();
            if (root == null)
            {
                l.Add(MakeItem(Lang.T("Atur Folder RPCS3"), Lang.T("Pilih folder tempat rpcs3.exe berada"), "\uE8B7", PickRpcs3Folder));
                return l;
            }
            List<AchvSet> all = GetAsync<List<AchvSet>>("rpcs3:all", delegate { return Achievements.Rpcs3All(root); }, 1, null, true);
            if (all == null) return LoadingItems();
            if (all.Count == 0)
            {
                l.Add(MakeItem(Lang.T("Belum ada trofi"), Lang.T("Data trofi muncul setelah game dimainkan di RPCS3 (") + root + ")", "\uE7BA", null));
                return l;
            }
            foreach (AchvSet s in all.OrderByDescending(x => x.Items.Where(a => a.Unlocked).Select(a => a.When).DefaultIfEmpty(DateTime.MinValue).Max()))
                l.Add(SetItem(s, "rpcs3:all"));
            return l;
        }

        XmbItem RefItem(AchvGameRef r)
        {
            XmbItem it = MakeItem(r.Title, r.Sub, "#TROPHY", null);
            if (!string.IsNullOrEmpty(r.Icon)) { it.ImageKey = "ach:" + r.Icon; it.ImageLoader = IconLoader(r.Icon, 400); }
            AchvGameRef rr = r;
            it.AchKey = r.Key;
            if (r.Total > 0) it.Progress = Math.Max(0, (double)r.Got / r.Total);
            it.DynSub = delegate
            {
                AchvSet s = Peek<AchvSet>(rr.Key);
                if (s != null && s.Error == null) return s.Unlocked + " / " + s.Total + " (" + s.Percent + "%)  \u2022  " + rr.Sub;
                if (rr.Total > 0) return rr.Got + " / " + rr.Total + " (" + (int)Math.Round(100.0 * rr.Got / rr.Total) + "%)  \u2022  " + rr.Sub;
                if (NavSettled) GetAsync<AchvSet>(rr.Key, rr.Fetch, 10, null, false);
                return rr.Sub;
            };
            it.Children = delegate { return AchvLevel(rr.Key, rr.Fetch, null); };
            return it;
        }

        List<XmbItem> BuildTrophySteam()
        {
            List<XmbItem> l = new List<XmbItem>();
            if (!SteamReady)
            {
                l.Add(MakeItem(Lang.T("Atur Steam Web API Key"), Lang.T("Buat gratis di steamcommunity.com/dev/apikey"), "\uE8D7", EditSteamKey));
                return l;
            }
            string k = cfg.SteamApiKey.Trim(), sid = EffectiveSteamId();
            List<AchvGameRef> games = GetAsync<List<AchvGameRef>>("steam:owned", delegate { return Achievements.SteamOwned(k, sid); }, 30, null, true);
            if (games == null)
            {
                string err = AsyncError("steam:owned");
                return (err != null && !asyncPending.Contains("steam:owned")) ? ErrorItems(err, "steam:owned") : LoadingItems();
            }
            if (games.Count == 0) { l.Add(MakeItem(Lang.T("Tidak ada game dengan achievement"), "", "\uE7BA", null)); return l; }
            foreach (AchvGameRef r in games) l.Add(RefItem(r));
            return l;
        }

        List<XmbItem> BuildTrophyRA()
        {
            List<XmbItem> l = new List<XmbItem>();
            if (!RaReady)
            {
                l.Add(MakeItem(Lang.T("Atur Akun RetroAchievements"), "Username + Web API Key (retroachievements.org/settings)", "\uE77B", EditRaUser));
                return l;
            }
            string u = cfg.RaUser.Trim(), k = cfg.RaApiKey.Trim();
            List<AchvGameRef> games = GetAsync<List<AchvGameRef>>("ra:played", delegate { return Achievements.RetroPlayed(u, k); }, 10, null, true);
            if (games == null)
            {
                string err = AsyncError("ra:played");
                return (err != null && !asyncPending.Contains("ra:played")) ? ErrorItems(err, "ra:played") : LoadingItems();
            }
            if (games.Count == 0) { l.Add(MakeItem(Lang.T("Belum ada game yang dimainkan"), Lang.T("Mainkan game dengan RetroArch / emulator yang mendukung RetroAchievements"), "\uE7BA", null)); return l; }
            foreach (AchvGameRef r in games) l.Add(RefItem(r));
            return l;
        }

        // ===================== AKUN =====================
        static string Mask(string s)
        {
            if (string.IsNullOrEmpty(s)) return Lang.T("Belum diatur");
            s = s.Trim();
            return s.Length <= 4 ? "****" : new string('*', Math.Min(12, s.Length - 4)) + s.Substring(s.Length - 4);
        }

        void EditSteamKey()
        {
            string v = Dialogs.Input(Owner32, "Steam Web API Key",
                Lang.T("Tempel Steam Web API Key kamu.\nBuat gratis di https://steamcommunity.com/dev/apikey (domain boleh diisi: localhost)."), cfg.SteamApiKey);
            if (v == null) return;
            cfg.SteamApiKey = v.Trim();
            ClearAchvCache();
            SaveAndRefresh();
        }

        void EditSteamId()
        {
            string v = Dialogs.Input(Owner32, "Steam ID",
                Lang.T("SteamID64 (17 digit, contoh 7656119...). Kosongkan untuk deteksi otomatis dari Steam di laptop ini."), cfg.SteamId);
            if (v == null) return;
            cfg.SteamId = v.Trim();
            ClearAchvCache();
            SaveAndRefresh();
        }

        void EditRaUser()
        {
            string v = Dialogs.Input(Owner32, "RetroAchievements", "Username RetroAchievements:", cfg.RaUser);
            if (v == null) return;
            cfg.RaUser = v.Trim();
            if (string.IsNullOrEmpty(cfg.RaApiKey))
            {
                string k = Dialogs.Input(Owner32, "RetroAchievements", Lang.T("Web API Key (ada di retroachievements.org/settings, bagian 'Keys'):"), cfg.RaApiKey);
                if (k != null) cfg.RaApiKey = k.Trim();
            }
            ClearAchvCache();
            SaveAndRefresh();
        }

        void EditRaKey()
        {
            string v = Dialogs.Input(Owner32, "RetroAchievements", Lang.T("Web API Key (ada di retroachievements.org/settings, bagian 'Keys'):"), cfg.RaApiKey);
            if (v == null) return;
            cfg.RaApiKey = v.Trim();
            ClearAchvCache();
            SaveAndRefresh();
        }

        void PickRpcs3Folder()
        {
            string f = Dialogs.PickFolder(Owner32, Lang.T("Pilih folder RPCS3 (yang berisi rpcs3.exe dan dev_hdd0)"), cfg.Rpcs3Folder);
            if (f == null) return;
            if (!Directory.Exists(Path.Combine(f, "dev_hdd0"))) ShowToast(Lang.T("Folder ini tidak punya dev_hdd0, pastikan folder RPCS3 yang benar"));
            cfg.Rpcs3Folder = f;
            Achievements.ResetRootCache();
            ClearAchvCache();
            SaveAndRefresh();
        }

        void ClearAchvCache()
        {
            asyncVals.Clear(); asyncTimes.Clear(); asyncErr.Clear();
        }

        List<XmbItem> BuildAchvAccount()
        {
            List<XmbItem> l = new List<XmbItem>();
            XmbItem sk = MakeItem("Steam Web API Key", "", "\uE8D7", EditSteamKey);
            sk.DynSub = delegate { return Mask(cfg.SteamApiKey); };
            sk.Options = delegate
            {
                List<OptionItem> o = new List<OptionItem>();
                o.Add(new OptionItem(Lang.T("Ubah Key..."), EditSteamKey));
                o.Add(new OptionItem(Lang.T("Buka Halaman Pembuatan Key"), delegate { OpenDefault("https://steamcommunity.com/dev/apikey"); }));
                o.Add(new OptionItem(Lang.T("Buka Pengaturan Privasi Steam"), delegate { OpenDefault("https://steamcommunity.com/my/edit/settings"); }));
                o.Add(new OptionItem(Lang.T("Hapus Key"), delegate { cfg.SteamApiKey = ""; ClearAchvCache(); SaveAndRefresh(); }));
                return o;
            };
            l.Add(sk);

            XmbItem sid = MakeItem("Steam ID", "", "\uE77B", EditSteamId);
            sid.DynSub = delegate
            {
                if (!string.IsNullOrEmpty(cfg.SteamId)) return cfg.SteamId;
                string d = Achievements.DetectSteamId();
                return d.Length > 0 ? Lang.T("Otomatis: ") + d : Lang.T("Tidak terdeteksi, isi manual");
            };
            l.Add(sid);

            XmbItem ru = MakeItem("RetroAchievements: Username", "", "\uE77B", EditRaUser);
            ru.DynSub = delegate { return string.IsNullOrEmpty(cfg.RaUser) ? Lang.T("Belum diatur") : cfg.RaUser; };
            l.Add(ru);

            XmbItem rk = MakeItem("RetroAchievements: Web API Key", "", "\uE8D7", EditRaKey);
            rk.DynSub = delegate { return Mask(cfg.RaApiKey); };
            rk.Options = delegate
            {
                List<OptionItem> o = new List<OptionItem>();
                o.Add(new OptionItem(Lang.T("Ubah Key..."), EditRaKey));
                o.Add(new OptionItem(Lang.T("Buka retroachievements.org/settings"), delegate { OpenDefault("https://retroachievements.org/settings"); }));
                o.Add(new OptionItem(Lang.T("Hapus Key"), delegate { cfg.RaApiKey = ""; ClearAchvCache(); SaveAndRefresh(); }));
                return o;
            };
            l.Add(rk);

            XmbItem rp = MakeItem(Lang.T("Folder RPCS3"), "", "\uE8B7", PickRpcs3Folder);
            rp.DynSub = delegate
            {
                if (!string.IsNullOrEmpty(cfg.Rpcs3Folder)) return cfg.Rpcs3Folder;
                string r = Achievements.Rpcs3Root(cfg);
                return r != null ? Lang.T("Otomatis: ") + r : Lang.T("Belum diatur (atau atur emulator platform PS3)");
            };
            l.Add(rp);

            l.Add(MakeItem(Lang.T("Tes Koneksi"), Lang.T("Cek akun Steam & RetroAchievements"), "\uE895", TestAchvAccounts));
            l.Add(MakeItem(Lang.T("Hapus Cache Trofi"), Lang.T("Hapus data & gambar trofi yang tersimpan"), "\uE74D", delegate
            {
                ClearAchvCache();
                Achievements.ClearCache();
                RefreshItemsOnly();
                ShowToast(Lang.T("Cache trofi dihapus"));
            }));
            return l;
        }

        void TestAchvAccounts()
        {
            string sk = cfg.SteamApiKey.Trim(), sid = EffectiveSteamId(), ru = cfg.RaUser.Trim(), rk = cfg.RaApiKey.Trim();
            ShowToast(Lang.T("Mengecek akun..."));
            ThreadPool.QueueUserWorkItem(delegate
            {
                List<string> res = new List<string>();
                if (sk.Length > 0 && sid.Length > 0)
                {
                    try { List<AchvGameRef> g = Achievements.SteamOwned(sk, sid); res.Add("Steam OK (" + g.Count + Lang.T(" game)")); }
                    catch (Exception ex) { res.Add("Steam: " + ex.Message); }
                }
                else res.Add(Lang.T("Steam belum diatur"));
                if (ru.Length > 0 && rk.Length > 0)
                {
                    try { Achievements.RetroProfile(ru, rk); res.Add("RetroAchievements OK"); }
                    catch (Exception ex) { res.Add("RA: " + ex.Message); }
                }
                else res.Add(Lang.T("RA belum diatur"));
                Dispatcher.BeginInvoke((Action)delegate { ShowToast(string.Join("   |   ", res.ToArray())); });
            });
        }

        // ===================== PANTAU TROFI BARU =====================
        void AchvWatchStart(GameEntry g)
        {
            StopAchvPolling();
            watchSrc = SourceFor(g);
            watchBefore = -1;
            if (watchSrc == null) return;
            StartAchvPolling();
            AchvSet s = Peek<AchvSet>(watchSrc.Key);
            watchIds = null;
            if (s != null && s.Error == null) { watchBefore = s.Unlocked; watchIds = new HashSet<string>(s.Items.Where(a => a.Unlocked).Select(a => a.Id)); return; }
            AchvSrc w = watchSrc;
            InvalidateAsync(w.Key);
            GetAsync<AchvSet>(w.Key, w.Fetch, 0, delegate (AchvSet r)
            {
                if (watchSrc == w && r != null && r.Error == null)
                {
                    watchBefore = r.Unlocked;
                    watchIds = new HashSet<string>(r.Items.Where(a => a.Unlocked).Select(a => a.Id));
                }
            }, false);
        }

        void AchvWatchCheck()
        {
            if (watchSrc == null || watchBefore < 0) return;
            AchvSrc w = watchSrc;
            int before = watchBefore;
            HashSet<string> ids = watchIds;
            watchSrc = null;
            StopAchvPolling();
            InvalidateAsync(w.Key);
            GetAsync<AchvSet>(w.Key, w.Fetch, 0, delegate (AchvSet r)
            {
                if (w.Done != null) w.Done(r);
                if (r == null || r.Error != null) return;
                List<Achv> fresh = ids != null
                    ? r.Items.Where(a => a.Unlocked && !ids.Contains(a.Id)).OrderBy(a => a.When).ToList()
                    : new List<Achv>();
                int gained = Math.Max(fresh.Count, r.Unlocked - before);
                if (gained > 0)
                {
                    AnnounceTrophies(fresh, r, before, false);
                    if (fresh.Count == 0) Sfx.Ok();
                    ShowToast(Lang.T("Selamat! ") + gained + Lang.T(" trofi baru di ") + r.Title);
                }
            }, false);
        }
    }
}
