using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace ZMB
{
    public class Ps3Info
    {
        public string Title = "";
        public string TitleId = "";
        public string NpCommId = "";
        public string Icon;   // path PNG (hasil ekstrak / di folder game)
        public string Pic1;
    }

    // Membaca data game PS3 dari ISO (ISO 9660) atau folder game, tanpa mengekstrak seluruh isi
    public static class Ps3Meta
    {
        static Dictionary<string, Ps3Info> mem = new Dictionary<string, Ps3Info>(StringComparer.OrdinalIgnoreCase);
        static object lk = new object();

        class IsoEntry
        {
            public string Name;
            public long Lba;
            public long Size;
            public bool Dir;
        }

        static uint LE32(byte[] b, int o) { return (uint)(b[o] | b[o + 1] << 8 | b[o + 2] << 16 | b[o + 3] << 24); }

        static byte[] ReadAt(FileStream fs, long pos, int len)
        {
            byte[] buf = new byte[len];
            fs.Seek(pos, SeekOrigin.Begin);
            int got = 0;
            while (got < len)
            {
                int n = fs.Read(buf, got, len - got);
                if (n <= 0) break;
                got += n;
            }
            return buf;
        }

        static List<IsoEntry> ReadDir(FileStream fs, long lba, long size)
        {
            List<IsoEntry> res = new List<IsoEntry>();
            if (size <= 0 || size > 8 * 1024 * 1024) return res;
            byte[] d = ReadAt(fs, lba * 2048, (int)size);
            int p = 0;
            while (p < d.Length)
            {
                int len = d[p];
                if (len == 0)
                {
                    p = ((p / 2048) + 1) * 2048; // record tidak melewati batas sektor
                    continue;
                }
                if (p + 33 > d.Length) break;
                int nameLen = d[p + 32];
                if (p + 33 + nameLen > d.Length) break;
                IsoEntry e = new IsoEntry();
                e.Lba = LE32(d, p + 2);
                e.Size = LE32(d, p + 10);
                e.Dir = (d[p + 25] & 2) != 0;
                if (nameLen == 1 && (d[p + 33] == 0 || d[p + 33] == 1)) { p += len; continue; } // "." dan ".."
                string n = Encoding.ASCII.GetString(d, p + 33, nameLen);
                int semi = n.IndexOf(';');
                if (semi >= 0) n = n.Substring(0, semi);
                e.Name = n.TrimEnd('.');
                res.Add(e);
                p += len;
            }
            return res;
        }

        static IsoEntry Find(FileStream fs, IsoEntry root, string path)
        {
            IsoEntry cur = root;
            foreach (string part in path.Split('/'))
            {
                if (cur == null || !cur.Dir) return null;
                List<IsoEntry> list = ReadDir(fs, cur.Lba, cur.Size);
                cur = list.FirstOrDefault(x => string.Equals(x.Name, part, StringComparison.OrdinalIgnoreCase));
            }
            return cur;
        }

        static IsoEntry Root(FileStream fs)
        {
            byte[] pvd = ReadAt(fs, 16 * 2048, 2048);
            if (pvd[0] != 1 || Encoding.ASCII.GetString(pvd, 1, 5) != "CD001") return null;
            IsoEntry r = new IsoEntry();
            r.Lba = LE32(pvd, 156 + 2);
            r.Size = LE32(pvd, 156 + 10);
            r.Dir = true;
            r.Name = "";
            return r;
        }

        // PARAM.SFO -> TITLE, TITLE_ID
        public static void ParseSfo(byte[] b, Ps3Info info)
        {
            if (b == null || b.Length < 20 || b[0] != 0 || b[1] != 'P' || b[2] != 'S' || b[3] != 'F') return;
            int keyStart = (int)LE32(b, 8), dataStart = (int)LE32(b, 12), count = (int)LE32(b, 16);
            for (int i = 0; i < count && 20 + i * 16 + 16 <= b.Length; i++)
            {
                int o = 20 + i * 16;
                int keyOff = b[o] | b[o + 1] << 8;
                int fmt = b[o + 2] | b[o + 3] << 8;
                int len = (int)LE32(b, o + 4);
                int dataOff = (int)LE32(b, o + 12);
                int ks = keyStart + keyOff;
                if (ks >= b.Length) continue;
                int ke = ks;
                while (ke < b.Length && b[ke] != 0) ke++;
                string key = Encoding.ASCII.GetString(b, ks, ke - ks);
                if (fmt != 0x0204 && fmt != 0x0004) continue;
                int ds = dataStart + dataOff;
                if (ds < 0 || ds + len > b.Length || len <= 0) continue;
                string val = Encoding.UTF8.GetString(b, ds, len).TrimEnd('\0').Trim();
                if (key == "TITLE") info.Title = val.Replace("\n", " ");
                else if (key == "TITLE_ID") info.TitleId = val;
            }
        }

        static string CacheDir(string iso)
        {
            FileInfo fi = new FileInfo(iso);
            string sig = iso.ToLowerInvariant() + "|" + fi.Length + "|" + fi.LastWriteTimeUtc.Ticks;
            string h;
            using (MD5 m = MD5.Create()) h = BitConverter.ToString(m.ComputeHash(Encoding.UTF8.GetBytes(sig))).Replace("-", "").Substring(0, 16);
            return Path.Combine(Path.Combine(Path.Combine(Config.BaseDir, "cache"), "ps3"), h);
        }

        static bool IsPng(byte[] b) { return b != null && b.Length > 8 && b[0] == 0x89 && b[1] == 'P' && b[2] == 'N' && b[3] == 'G'; }

        static Ps3Info FromIso(string iso)
        {
            Ps3Info info = new Ps3Info();
            string dir = CacheDir(iso);
            string infoFile = Path.Combine(dir, "info.txt");
            if (File.Exists(infoFile))
            {
                string[] l = File.ReadAllLines(infoFile);
                if (l.Length >= 3) { info.Title = l[0]; info.TitleId = l[1]; info.NpCommId = l[2]; }
                string ic = Path.Combine(dir, "ICON0.PNG"), pc = Path.Combine(dir, "PIC1.PNG");
                if (File.Exists(ic)) info.Icon = ic;
                if (File.Exists(pc)) info.Pic1 = pc;
                return info;
            }
            try
            {
                using (FileStream fs = new FileStream(iso, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    IsoEntry root = Root(fs);
                    if (root != null)
                    {
                        Directory.CreateDirectory(dir);
                        IsoEntry sfo = Find(fs, root, "PS3_GAME/PARAM.SFO");
                        if (sfo != null && sfo.Size < 1024 * 1024) ParseSfo(ReadAt(fs, sfo.Lba * 2048, (int)sfo.Size), info);
                        foreach (string n in new string[] { "ICON0.PNG", "PIC1.PNG" })
                        {
                            IsoEntry e = Find(fs, root, "PS3_GAME/" + n);
                            if (e == null || e.Size <= 0 || e.Size > 16 * 1024 * 1024) continue;
                            byte[] png = ReadAt(fs, e.Lba * 2048, (int)e.Size);
                            if (!IsPng(png)) continue; // ISO terenkripsi: data tidak bisa dibaca
                            string outF = Path.Combine(dir, n);
                            File.WriteAllBytes(outF, png);
                            if (n == "ICON0.PNG") info.Icon = outF; else info.Pic1 = outF;
                        }
                        IsoEntry trop = Find(fs, root, "PS3_GAME/TROPDIR");
                        if (trop != null && trop.Dir)
                        {
                            IsoEntry first = ReadDir(fs, trop.Lba, trop.Size).FirstOrDefault(x => x.Dir);
                            if (first != null) info.NpCommId = first.Name;
                        }
                        File.WriteAllLines(infoFile, new string[] { info.Title, info.TitleId, info.NpCommId });
                    }
                }
            }
            catch { }
            return info;
        }

        static Ps3Info FromFolder(string eboot)
        {
            Ps3Info info = new Ps3Info();
            try
            {
                // .../PS3_GAME/USRDIR/EBOOT.BIN  atau  .../GAMEID/USRDIR/EBOOT.BIN (game PSN)
                string usr = Path.GetDirectoryName(eboot);
                string game = Path.GetDirectoryName(usr);
                string sfo = Path.Combine(game, "PARAM.SFO");
                if (File.Exists(sfo)) ParseSfo(File.ReadAllBytes(sfo), info);
                string ic = Path.Combine(game, "ICON0.PNG"), pc = Path.Combine(game, "PIC1.PNG");
                if (File.Exists(ic)) info.Icon = ic;
                if (File.Exists(pc)) info.Pic1 = pc;
                string trop = Path.Combine(game, "TROPDIR");
                if (Directory.Exists(trop))
                {
                    string[] subs = Directory.GetDirectories(trop);
                    if (subs.Length > 0) info.NpCommId = Path.GetFileName(subs[0]);
                }
            }
            catch { }
            return info;
        }

        public static bool IsIso(string path) { return path != null && path.EndsWith(".iso", StringComparison.OrdinalIgnoreCase); }

        public static Ps3Info Get(string path)
        {
            if (string.IsNullOrEmpty(path)) return new Ps3Info();
            lock (lk)
            {
                Ps3Info i;
                if (mem.TryGetValue(path, out i)) return i;
            }
            Ps3Info info = IsIso(path) ? FromIso(path) : FromFolder(path);
            lock (lk) mem[path] = info;
            return info;
        }
    }
}
