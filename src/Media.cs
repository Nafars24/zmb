using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Media;

namespace ZMB
{
    public static class MediaExt
    {
        public static readonly string[] Music = { ".mp3", ".wav", ".wma", ".m4a", ".aac", ".mid", ".midi", ".flac", ".ogg", ".opus" };
        public static readonly string[] Video = { ".mp4", ".avi", ".wmv", ".mkv", ".mov", ".mpg", ".mpeg", ".m4v", ".webm", ".flv", ".3gp", ".ts" };
        public static readonly string[] Photo = { ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tif", ".tiff" };

        public static bool Is(string file, string[] exts)
        {
            string e = Path.GetExtension(file).ToLowerInvariant();
            return exts.Contains(e);
        }

        public static List<string> Files(string dir, string[] exts, bool recursive)
        {
            List<string> res = new List<string>();
            try
            {
                if (!Directory.Exists(dir)) return res;
                foreach (string f in Directory.GetFiles(dir))
                    if (Is(f, exts)) res.Add(f);
                if (recursive)
                    foreach (string d in Directory.GetDirectories(dir))
                    {
                        try
                        {
                            FileAttributes a = File.GetAttributes(d);
                            if ((a & FileAttributes.Hidden) != 0 || (a & FileAttributes.System) != 0) continue;
                        }
                        catch { continue; }
                        res.AddRange(Files(d, exts, true));
                    }
            }
            catch { }
            res.Sort(StringComparer.OrdinalIgnoreCase);
            return res;
        }

        public static string FormatTime(int ms)
        {
            if (ms < 0) ms = 0;
            int s = ms / 1000;
            if (s >= 3600) return string.Format("{0}:{1:00}:{2:00}", s / 3600, (s / 60) % 60, s % 60);
            return string.Format("{0}:{1:00}", s / 60, s % 60);
        }
    }

    // ===================== PEMUTAR MUSIK (WPF MediaPlayer) =====================
    public class MusicPlayer
    {
        public List<string> Queue = new List<string>();
        public int Pos = -1;
        public bool Playing = false;
        public bool Paused = false;
        public bool Shuffle = false;
        public bool Repeat = true;
        public int Volume = 80;
        public Action<string> OnMessage;
        MediaPlayer mp = new MediaPlayer();
        Random rnd = new Random();
        int failStreak = 0;
        double duck = 1.0;

        public string Current { get { return (Pos >= 0 && Pos < Queue.Count) ? Queue[Pos] : null; } }

        public MusicPlayer()
        {
            mp.MediaEnded += delegate { Next(); };
            mp.MediaOpened += delegate { failStreak = 0; };
            mp.MediaFailed += delegate (object s, ExceptionEventArgs e)
            {
                failStreak++;
                Msg(Lang.T("Format tidak didukung: ") + Path.GetFileName(Current ?? ""));
                if (Queue.Count > 1 && failStreak < Math.Min(Queue.Count, 15)) PlayIndex((Pos + 1) % Queue.Count);
                else { Stop(); failStreak = 0; }
            };
        }

        void Msg(string s) { if (OnMessage != null) OnMessage(s); }

        public void PlayList(List<string> files, int start)
        {
            if (files == null || files.Count == 0) { Msg(Lang.T("Tidak ada lagu di folder ini")); return; }
            Queue = new List<string>(files);
            if (Shuffle)
            {
                string first = (start >= 0 && start < Queue.Count) ? Queue[start] : null;
                for (int i = Queue.Count - 1; i > 0; i--)
                {
                    int j = rnd.Next(i + 1);
                    string t = Queue[i]; Queue[i] = Queue[j]; Queue[j] = t;
                }
                if (first != null) { Queue.Remove(first); Queue.Insert(0, first); }
                start = 0;
            }
            failStreak = 0;
            PlayIndex(Math.Max(0, start));
        }

        public void PlayIndex(int i)
        {
            if (i < 0 || i >= Queue.Count) { Stop(); return; }
            Pos = i;
            try
            {
                mp.Open(new Uri(Queue[i], UriKind.Absolute));
                ApplyVolume();
                mp.Play();
                Playing = true; Paused = false;
            }
            catch (Exception ex) { Msg(Lang.T("Gagal memutar: ") + ex.Message); Playing = false; }
        }

        public void ApplyVolume() { mp.Volume = Math.Max(0, Math.Min(1, Volume / 100.0 * duck)); }

        // Kecilkan musik saat trailer bersuara
        public void SetDuck(bool on) { duck = on ? 0.2 : 1.0; ApplyVolume(); }

        public void TogglePause()
        {
            if (!Playing) { if (Queue.Count > 0) PlayIndex(Math.Max(0, Pos)); return; }
            if (Paused) Resume(); else Pause();
        }

        public void Pause() { if (Playing && !Paused) { mp.Pause(); Paused = true; } }
        public void Resume() { if (Playing && Paused) { mp.Play(); Paused = false; } }

        public void Next()
        {
            if (Queue.Count == 0) return;
            int n = Pos + 1;
            if (n >= Queue.Count) { if (!Repeat) { Stop(); return; } n = 0; }
            PlayIndex(n);
        }

        public void Prev()
        {
            if (Queue.Count == 0) return;
            if (PositionMs() > 4000) { PlayIndex(Pos); return; }
            int n = Pos - 1;
            if (n < 0) n = Queue.Count - 1;
            PlayIndex(n);
        }

        public void Stop()
        {
            try { mp.Stop(); mp.Close(); } catch { }
            Playing = false; Paused = false;
        }

        public int PositionMs() { return Playing ? (int)mp.Position.TotalMilliseconds : 0; }

        public int LengthMs()
        {
            if (!Playing || !mp.NaturalDuration.HasTimeSpan) return 0;
            return (int)mp.NaturalDuration.TimeSpan.TotalMilliseconds;
        }

        public void Seek(int deltaMs)
        {
            if (!Playing) return;
            int len = LengthMs();
            int p = Math.Max(0, PositionMs() + deltaMs);
            if (len > 0) p = Math.Min(len - 500, p);
            mp.Position = TimeSpan.FromMilliseconds(Math.Max(0, p));
        }

        public string Title()
        {
            string c = Current;
            return c == null ? "" : Path.GetFileNameWithoutExtension(c);
        }
    }
}
