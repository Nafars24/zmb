using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Serialization;

namespace ZMB
{
    public class GameEntry
    {
        public string Name = "";
        public string Path = "";        // exe / lnk / url / rom / steam://
        public string Args = "";
        public string Cover = "";
        public string Platform = "WIN"; // WIN, STEAM, atau Short platform emulator (PS1, PS2, ...)
        public DateTime LastPlayed = DateTime.MinValue;
        public int PlayCount = 0;
        public double Minutes = 0;
        public bool Admin = false;
        public bool Favorite = false;
        public string Background = "";
        public string Trailer = "";
        public int RaGameId = 0;
        public string SteamAppId = "";
        public string NpCommId = "";
        public string Logo = "";
        public string BoxArt = "";
        public string Spine = "";
        public string Disc = "";
        public string ThemeMusic = "";
        public string Desc = "";
        public string Developer = "";
        public string Publisher = "";
        public string Year = "";
        public string Genre = "";
        public string Players = "";
        public string Rating = "";
        public List<string> Shots = new List<string>();
        public bool MetaFetched = false;
    }

    public class PlaySession
    {
        public DateTime Start;
        public double Minutes;
        public string Name = "";
        public string Platform = "";
        public string Key = "";
    }

    public class PlatformCfg
    {
        public string Name = "";
        public string Short = "";
        public string Emulator = "";
        public string Args = "\"{rom}\"";
        public string RomFolder = "";
        public string Extensions = "";
        public string Color = "#3060C0";
        public bool Hidden = false;
        public string Hint = "";
        public int RaConsole = -1;
        public int SsSystem = -1;
        public string ResumeArgs = "";
        public string SaveDirs = "";
        public string Icon = "";          // ikon kustom (PNG/ICO/JPG)

        public PlatformCfg() { }
        public PlatformCfg(string name, string shrt, string ext, string color, string hint)
        {
            Name = name; Short = shrt; Extensions = ext; Color = color; Hint = hint;
        }

        public string[] ExtList()
        {
            return Extensions.Split(new char[] { ';', ',', ' ', '|' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(e => "." + e.Trim().TrimStart('*').TrimStart('.').ToLowerInvariant()).ToArray();
        }
    }

    public class Config
    {
        public List<GameEntry> WindowsGames = new List<GameEntry>();
        public List<GameEntry> Meta = new List<GameEntry>();
        public List<PlatformCfg> Platforms = new List<PlatformCfg>();
        public string MusicFolder = "";
        public string VideoFolder = "";
        public string PhotoFolder = "";
        public int Theme = 0;
        public bool Fullscreen = true;
        public bool Sounds = true;
        public int Volume = 80;
        public bool MinimizeOnLaunch = true;
        public bool PauseMusicOnLaunch = true;
        public bool ShowSteam = true;
        public bool ShowWaves = true;
        public bool BootAnimation = true;
        public bool Clock24 = true;
        public bool Shuffle = false;
        public bool Repeat = true;
        public int SlideSeconds = 5;
        public int LastCategory = 5;
        public string BackgroundImage = "";
        public float UiScale = 1f;
        public bool GameBackground = true;
        public bool Trailers = true;
        public bool TrailerSound = true;
        public bool OnlineTrailers = true;
        public float TrailerDelay = 1.5f;
        public string SteamApiKey = "";
        public string SteamId = "";
        public string RaUser = "";
        public string RaApiKey = "";
        public string Rpcs3Folder = "";
        public string SgdbKey = "";
        public string SsUser = "";
        public string SsPass = "";
        public string SsDevId = "";
        public string SsDevPass = "";
        public bool ScrapeVideos = true;
        public bool ShowLogo = true;
        public string ScrapeRegion = "wor";
        public int ViewMode = 0; // 0 = XMB (PS3), 1 = Coverflow (Aurora)
        public List<PlaySession> Sessions = new List<PlaySession>();
        public string ThemePack = "";
        public int WaveStyle = 0;      // 0 klasik, 1 garis tipis
        public int Particles = 0;      // 0 otomatis, 1 kilau, 2 salju, 3 mati
        public int ScreensaverMin = 5; // 0 = mati
        public bool ThemeMusicOn = true;
        public bool Ps1BootAnim = true;
        public bool TrophyOverlay = true;
        public bool AutoFullscreen = true;
        public bool HomeHotkey = true;
        public string AvatarPath = "";
        public bool CheckUpdateDownloads = true;
        public string UpdateRepo = "Nafars24/zmb";
        public bool AutoUpdateOnline = true;

        [XmlIgnore] public static string BaseDir = AppDomain.CurrentDomain.BaseDirectory;
        [XmlIgnore] public static string FilePath { get { return System.IO.Path.Combine(BaseDir, "zmb_config.xml"); } }

        public static Config Load()
        {
            Config c = null;
            try
            {
                if (File.Exists(FilePath))
                {
                    XmlSerializer xs = new XmlSerializer(typeof(Config));
                    using (FileStream fs = File.OpenRead(FilePath)) c = (Config)xs.Deserialize(fs);
                }
            }
            catch
            {
                try { File.Copy(FilePath, FilePath + ".rusak.bak", true); } catch { }
                c = null;
            }
            if (c == null) c = new Config();
            c.EnsureDefaults();
            return c;
        }

        public void Save()
        {
            try
            {
                XmlSerializer xs = new XmlSerializer(typeof(Config));
                string tmp = FilePath + ".tmp";
                using (FileStream fs = File.Create(tmp)) xs.Serialize(fs, this);
                if (File.Exists(FilePath)) File.Delete(FilePath);
                File.Move(tmp, FilePath);
            }
            catch { }
        }

        public static List<PlatformCfg> DefaultPlatforms()
        {
            List<PlatformCfg> l = new List<PlatformCfg>();
            l.Add(new PlatformCfg("PlayStation 1", "PS1", "cue;chd;pbp;iso;m3u;bin;img;ecm", "#8A8F99", "DuckStation / ePSXe / RetroArch"));
            l.Add(new PlatformCfg("PlayStation 2", "PS2", "iso;chd;cso;bin;gz", "#2B3FA8", "PCSX2"));
            l.Add(new PlatformCfg("PlayStation 3", "PS3", "iso;bin", "#1A1A1A", "RPCS3 (file .iso atau folder game)"));
            l.Add(new PlatformCfg("PlayStation Portable", "PSP", "iso;cso;pbp;chd", "#202020", "PPSSPP"));
            l.Add(new PlatformCfg("PlayStation Vita", "VITA", "vpk", "#1E4FA0", "Vita3K"));
            l.Add(new PlatformCfg("Nintendo (NES)", "NES", "nes;fds;zip", "#B22222", "Mesen / FCEUX / RetroArch"));
            l.Add(new PlatformCfg("Super Nintendo", "SNES", "sfc;smc;zip", "#6A4FB0", "Snes9x / bsnes / RetroArch"));
            l.Add(new PlatformCfg("Nintendo 64", "N64", "z64;n64;v64;zip", "#2E8B57", "Project64 / simple64"));
            l.Add(new PlatformCfg("Game Boy Advance", "GBA", "gba;gb;gbc;zip", "#5B3FA0", "mGBA / VisualBoyAdvance"));
            l.Add(new PlatformCfg("Nintendo DS", "NDS", "nds;zip", "#7A7A7A", "melonDS / DeSmuME"));
            l.Add(new PlatformCfg("Nintendo 3DS", "3DS", "3ds;cci;cxi;app", "#C0392B", "Azahar / Lime3DS"));
            l.Add(new PlatformCfg("GameCube / Wii", "GCN", "iso;gcm;rvz;wbfs;ciso;gcz", "#5A3E9A", "Dolphin"));
            l.Add(new PlatformCfg("Nintendo Switch", "SWITCH", "nsp;xci;nca;nro", "#E60012", "Ryujinx / Eden / Citron / Sudachi"));
            l.Add(new PlatformCfg("Xbox 360", "X360", "iso;xex;zar", "#107C10", "Xenia / Xenia Canary"));
            l.Add(new PlatformCfg("Sega Genesis", "MD", "md;gen;bin;smd;zip", "#111111", "Kega Fusion / RetroArch"));
            l.Add(new PlatformCfg("Arcade (MAME)", "ARC", "zip;7z", "#D35400", "MAME / FBNeo (argumen: {romname})"));
            return l;
        }

        public void EnsureDefaults()
        {
            if (Platforms == null) Platforms = new List<PlatformCfg>();
            if (WindowsGames == null) WindowsGames = new List<GameEntry>();
            if (Meta == null) Meta = new List<GameEntry>();
            if (Sessions == null) Sessions = new List<PlaySession>();
            if (Sessions.Count > 5000) Sessions.RemoveRange(0, Sessions.Count - 5000);
            if (Platforms.Count == 0) Platforms = DefaultPlatforms();
            if (string.IsNullOrEmpty(MusicFolder)) MusicFolder = Environment.GetFolderPath(Environment.SpecialFolder.MyMusic);
            if (string.IsNullOrEmpty(VideoFolder)) VideoFolder = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
            if (string.IsNullOrEmpty(PhotoFolder)) PhotoFolder = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
            if (Volume < 0) Volume = 0;
            if (Volume > 100) Volume = 100;
            if (SlideSeconds < 2) SlideSeconds = 5;
            if (string.IsNullOrEmpty(UpdateRepo)) UpdateRepo = "Nafars24/zmb";
            // platform baru di versi terbaru: tambahkan ke konfigurasi lama (setelah platform sebelumnya di daftar bawaan)
            List<PlatformCfg> defs = DefaultPlatforms();
            for (int di = 0; di < defs.Count; di++)
            {
                if (Platforms.Any(x => x.Short == defs[di].Short)) continue;
                int at = Platforms.Count;
                for (int pj = di - 1; pj >= 0; pj--)
                {
                    int found = Platforms.FindIndex(x => x.Short == defs[pj].Short);
                    if (found >= 0) { at = found + 1; break; }
                }
                Platforms.Insert(at, defs[di]);
            }
            // RPCS3 sekarang bisa membuka .iso: tambahkan ekstensi iso untuk konfigurasi lama
            foreach (PlatformCfg p in Platforms)
                if (p.Short == "PS3" && !p.ExtList().Contains(".iso"))
                {
                    p.Extensions = "iso;" + p.Extensions;
                    if (p.Hint.Contains("EBOOT")) p.Hint = "RPCS3 (file .iso atau folder game)";
                }
        }

        public PlatformCfg FindPlatform(string shrt)
        {
            foreach (PlatformCfg p in Platforms) if (string.Equals(p.Short, shrt, StringComparison.OrdinalIgnoreCase)) return p;
            return null;
        }

        // Meta untuk ROM & Steam (key = Path)
        public GameEntry GetMeta(string path, string platform, string defaultName)
        {
            foreach (GameEntry g in Meta)
                if (string.Equals(g.Path, path, StringComparison.OrdinalIgnoreCase)) return g;
            GameEntry n = new GameEntry();
            n.Path = path; n.Platform = platform; n.Name = defaultName;
            return n;
        }

        public void EnsureStored(GameEntry g)
        {
            if (g.Platform == "WIN") { if (!WindowsGames.Contains(g)) WindowsGames.Add(g); return; }
            if (!Meta.Contains(g)) Meta.Add(g);
        }

        public IEnumerable<GameEntry> AllKnown()
        {
            return WindowsGames.Concat(Meta);
        }
    }
}
