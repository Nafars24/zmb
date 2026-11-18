using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace ZMB
{
    public class SaveLoc
    {
        public string Label;
        public string Dir;
        public SaveLoc(string label, string dir) { Label = label; Dir = dir; }
    }

    public static class SaveManager
    {
        static string Docs { get { return Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments); } }
        static string AppData { get { return Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData); } }

        static string ExeName(PlatformCfg p)
        {
            return string.IsNullOrEmpty(p.Emulator) ? "" : Path.GetFileName(p.Emulator).ToLowerInvariant();
        }

        static bool Portable(string emuDir)
        {
            return File.Exists(Path.Combine(emuDir, "portable.txt")) || File.Exists(Path.Combine(emuDir, "portable.ini"));
        }

        public static List<SaveLoc> Locations(PlatformCfg p)
        {
            List<SaveLoc> all = new List<SaveLoc>();
            if (!string.IsNullOrEmpty(p.SaveDirs))
            {
                int n = 1;
                foreach (string d in p.SaveDirs.Split(';'))
                    if (d.Trim().Length > 0) all.Add(new SaveLoc("Folder " + (n++), d.Trim()));
            }
            string exe = ExeName(p);
            string emuDir = string.IsNullOrEmpty(p.Emulator) ? null : Path.GetDirectoryName(p.Emulator);
            if (emuDir != null)
            {
                if (exe.Contains("duckstation"))
                {
                    string b = Portable(emuDir) ? emuDir : Path.Combine(Docs, "DuckStation");
                    all.Add(new SaveLoc("Save State", Path.Combine(b, "savestates")));
                    all.Add(new SaveLoc("Memory Card", Path.Combine(b, "memcards")));
                }
                else if (exe.Contains("pcsx2"))
                {
                    string b = Portable(emuDir) ? emuDir : Path.Combine(Docs, "PCSX2");
                    all.Add(new SaveLoc("Save State", Path.Combine(b, "sstates")));
                    all.Add(new SaveLoc("Memory Card", Path.Combine(b, "memcards")));
                }
                else if (exe.Contains("ppsspp"))
                {
                    string b = Directory.Exists(Path.Combine(emuDir, @"memstick\PSP")) ? Path.Combine(emuDir, @"memstick\PSP") : Path.Combine(Docs, @"PPSSPP\PSP");
                    all.Add(new SaveLoc("Save State", Path.Combine(b, "PPSSPP_STATE")));
                    all.Add(new SaveLoc("Save Data", Path.Combine(b, "SAVEDATA")));
                }
                else if (exe.Contains("rpcs3"))
                {
                    all.Add(new SaveLoc("Save Data", Path.Combine(emuDir, @"dev_hdd0\home\00000001\savedata")));
                    all.Add(new SaveLoc("Save State", Path.Combine(emuDir, "savestates")));
                }
                else if (exe.Contains("retroarch"))
                {
                    all.Add(new SaveLoc("Save (SRAM)", Path.Combine(emuDir, "saves")));
                    all.Add(new SaveLoc("Save State", Path.Combine(emuDir, "states")));
                }
                else if (exe.Contains("dolphin"))
                {
                    string b = Directory.Exists(Path.Combine(emuDir, "User")) ? Path.Combine(emuDir, "User") : Path.Combine(Docs, "Dolphin Emulator");
                    all.Add(new SaveLoc("Save State", Path.Combine(b, "StateSaves")));
                    all.Add(new SaveLoc("Memory Card GC", Path.Combine(b, "GC")));
                    all.Add(new SaveLoc("Save Wii", Path.Combine(b, @"Wii\title")));
                }
                else if (exe.Contains("vita3k"))
                {
                    all.Add(new SaveLoc("Save Data", Path.Combine(AppData, @"Vita3K\Vita3K\ux0\user\00\savedata")));
                }
                else if (exe.Contains("azahar") || exe.Contains("citra") || exe.Contains("lime3ds"))
                {
                    string n = exe.Contains("azahar") ? "Azahar" : exe.Contains("lime") ? "Lime3DS" : "Citra";
                    string b = Directory.Exists(Path.Combine(emuDir, "user")) ? Path.Combine(emuDir, "user") : Path.Combine(AppData, n);
                    all.Add(new SaveLoc("Save Data", Path.Combine(b, "sdmc")));
                    all.Add(new SaveLoc("Save State", Path.Combine(b, "states")));
                }
                else if (exe.Contains("project64"))
                {
                    all.Add(new SaveLoc("Save", Path.Combine(emuDir, "Save")));
                }
                else
                {
                    // emulator lain biasanya menyimpan di folder emulator
                    foreach (string sub in new string[] { "saves", "save", "states", "savestates", "battery", "sav" })
                        all.Add(new SaveLoc(sub, Path.Combine(emuDir, sub)));
                }
            }
            // save di samping ROM (melonDS, mGBA, dll)
            if (!string.IsNullOrEmpty(p.RomFolder) && (exe.Contains("melonds") || exe.Contains("mgba") || exe.Contains("snes9x") || exe.Contains("mesen") || exe.Contains("desmume")))
                all.Add(new SaveLoc("Save di folder ROM", p.RomFolder));
            return all.Where(l => { try { return Directory.Exists(l.Dir); } catch { return false; } }).ToList();
        }

        static readonly string[] SaveExt = { ".sav", ".srm", ".state", ".mcd", ".mcr", ".ps2", ".p2s", ".ppst", ".sav2", ".dsv", ".gci", ".raw", ".sta", ".ss", ".eep", ".fla", ".mpk", ".bin", ".dat", ".sfo", ".png", ".auto", ".zst", ".st0", ".st1", ".st2" };

        static bool IsSaveFile(string f, bool romFolder)
        {
            string e = Path.GetExtension(f).ToLowerInvariant();
            if (romFolder) return e == ".sav" || e == ".srm" || e == ".dsv" || e == ".eep" || e == ".fla";
            return true;
        }

        public static List<string> Files(SaveLoc loc, string filter)
        {
            List<string> res = new List<string>();
            bool romFolder = loc.Label.StartsWith("Save di folder ROM");
            try
            {
                foreach (string f in Directory.GetFiles(loc.Dir, "*", SearchOption.AllDirectories))
                {
                    if (!IsSaveFile(f, romFolder)) continue;
                    if (filter != null && !Achievements.Norm(Path.GetFileName(f)).Contains(filter) && !Achievements.Norm(f).Contains(filter)) continue;
                    res.Add(f);
                    if (res.Count > 3000) break;
                }
            }
            catch { }
            return res.OrderByDescending(f => { try { return File.GetLastWriteTime(f); } catch { return DateTime.MinValue; } }).ToList();
        }

        public static string BackupDir(PlatformCfg p)
        {
            string d = Path.Combine(Config.BaseDir, "backups", GameScanner.SafeName(p.Short));
            Directory.CreateDirectory(d);
            return d;
        }

        public static string Backup(PlatformCfg p, List<SaveLoc> locs)
        {
            string file = Path.Combine(BackupDir(p), p.Short + "_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".zip");
            int count = 0;
            using (FileStream fs = File.Create(file))
            using (ZipArchive z = new ZipArchive(fs, ZipArchiveMode.Create))
            {
                for (int i = 0; i < locs.Count; i++)
                {
                    SaveLoc l = locs[i];
                    bool romFolder = l.Label.StartsWith("Save di folder ROM");
                    string prefix = i.ToString("00") + "_" + GameScanner.SafeName(l.Label) + "/";
                    foreach (string f in Directory.GetFiles(l.Dir, "*", SearchOption.AllDirectories))
                    {
                        if (!IsSaveFile(f, romFolder)) continue;
                        string rel = f.Substring(l.Dir.TrimEnd('\\').Length).TrimStart('\\').Replace('\\', '/');
                        ZipArchiveEntry e = z.CreateEntry(prefix + rel, CompressionLevel.Optimal);
                        using (Stream es = e.Open())
                        using (FileStream src = new FileStream(f, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                            src.CopyTo(es);
                        count++;
                    }
                }
                // catatan lokasi asli untuk pemulihan
                ZipArchiveEntry info = z.CreateEntry("zmb_locations.txt");
                using (StreamWriter w = new StreamWriter(info.Open()))
                    for (int i = 0; i < locs.Count; i++) w.WriteLine(i.ToString("00") + "|" + locs[i].Label + "|" + locs[i].Dir);
            }
            if (count == 0) { try { File.Delete(file); } catch { } return null; }
            return file;
        }

        public static List<string> Backups(PlatformCfg p)
        {
            try
            {
                return Directory.GetFiles(BackupDir(p), "*.zip").OrderByDescending(f => File.GetLastWriteTime(f)).ToList();
            }
            catch { return new List<string>(); }
        }

        public static int Restore(string zip)
        {
            int count = 0;
            using (FileStream fs = File.OpenRead(zip))
            using (ZipArchive z = new ZipArchive(fs, ZipArchiveMode.Read))
            {
                Dictionary<string, string> map = new Dictionary<string, string>();
                ZipArchiveEntry info = z.GetEntry("zmb_locations.txt");
                if (info == null) throw new Exception("File cadangan tidak dikenali");
                using (StreamReader r = new StreamReader(info.Open()))
                {
                    string line;
                    while ((line = r.ReadLine()) != null)
                    {
                        string[] parts = line.Split('|');
                        if (parts.Length >= 3) map[parts[0]] = parts[2];
                    }
                }
                foreach (ZipArchiveEntry e in z.Entries)
                {
                    if (e.FullName == "zmb_locations.txt" || e.FullName.EndsWith("/")) continue;
                    int slash = e.FullName.IndexOf('/');
                    if (slash < 3) continue;
                    string idx = e.FullName.Substring(0, 2);
                    string dir;
                    if (!map.TryGetValue(idx, out dir)) continue;
                    string rel = e.FullName.Substring(slash + 1).Replace('/', '\\');
                    if (rel.Contains("..")) continue;
                    string dest = Path.Combine(dir, rel);
                    Directory.CreateDirectory(Path.GetDirectoryName(dest));
                    using (Stream es = e.Open())
                    using (FileStream o = File.Create(dest))
                        es.CopyTo(o);
                    count++;
                }
            }
            return count;
        }

        // Argumen untuk "lanjutkan dari save state terakhir" ({args} = argumen biasa)
        public static string ResumeArgsFor(PlatformCfg p)
        {
            if (!string.IsNullOrEmpty(p.ResumeArgs)) return p.ResumeArgs;
            string exe = ExeName(p);
            if (exe.Contains("duckstation")) return "-resume {args}";
            return null;
        }
    }
}
