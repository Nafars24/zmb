using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Net;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace ZMB
{
    public static class Updater
    {
        public static Version Current { get { return Assembly.GetEntryAssembly().GetName().Version; } }
        public static string ExePath { get { return Assembly.GetEntryAssembly().Location; } }

        // Membaca versi dari src/Program.cs di dalam ZIP
        public static Version ZipVersion(string zip)
        {
            try
            {
                using (FileStream fs = File.OpenRead(zip))
                using (ZipArchive z = new ZipArchive(fs, ZipArchiveMode.Read))
                {
                    ZipArchiveEntry e = z.Entries.FirstOrDefault(x => x.FullName.Replace('\\', '/').EndsWith("src/Program.cs", StringComparison.OrdinalIgnoreCase));
                    if (e == null) return null;
                    string t;
                    using (StreamReader r = new StreamReader(e.Open())) t = r.ReadToEnd();
                    Match m = Regex.Match(t, "AssemblyVersion\\(\"([0-9.]+)\"\\)");
                    if (!m.Success) return null;
                    return new Version(m.Groups[1].Value);
                }
            }
            catch { return null; }
        }

        public static string FindNewerInDownloads(out Version ver)
        {
            ver = null;
            try
            {
                string dl = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
                if (!Directory.Exists(dl)) return null;
                foreach (string f in Directory.GetFiles(dl, "ZMB*.zip").OrderByDescending(x => File.GetLastWriteTime(x)).Take(6))
                {
                    Version v = ZipVersion(f);
                    if (v != null && v > Current) { ver = v; return f; }
                }
            }
            catch { }
            return null;
        }

        // ===================== UPDATE ONLINE (GitHub Releases) =====================
        public class OnlineRelease
        {
            public Version Version;
            public string ZipUrl;
            public string Notes = "";
            public string Tag = "";
        }

        public static string NormalizeRepo(string repo)
        {
            if (string.IsNullOrEmpty(repo)) return "";
            string r = repo.Trim().TrimEnd('/');
            Match m = Regex.Match(r, @"github\.com/([^/]+)/([^/?#]+)", RegexOptions.IgnoreCase);
            if (m.Success) r = m.Groups[1].Value + "/" + m.Groups[2].Value;
            if (r.EndsWith(".git", StringComparison.OrdinalIgnoreCase)) r = r.Substring(0, r.Length - 4);
            return Regex.IsMatch(r, @"^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$") ? r : "";
        }

        public static OnlineRelease CheckGitHub(string repo)
        {
            repo = NormalizeRepo(repo);
            if (repo.Length == 0) throw new Exception(Lang.T("Repositori update belum diatur (format: nama/repo)"));
            ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072;
            string json;
            using (WebClient wc = new WebClient())
            {
                wc.Headers[HttpRequestHeader.UserAgent] = "ZMB-Updater";
                wc.Headers[HttpRequestHeader.Accept] = "application/vnd.github+json";
                wc.Encoding = Encoding.UTF8;
                try { json = wc.DownloadString("https://api.github.com/repos/" + repo + "/releases/latest"); }
                catch (WebException ex)
                {
                    HttpWebResponse r = ex.Response as HttpWebResponse;
                    if (r != null && (int)r.StatusCode == 404) return CheckBranch(repo, "main"); // belum ada rilis: pakai cabang main
                    throw new Exception(Lang.T("Gagal cek update: ") + ex.Message);
                }
            }
            JavaScriptSerializer js = new JavaScriptSerializer();
            js.MaxJsonLength = int.MaxValue;
            Dictionary<string, object> d = js.DeserializeObject(json) as Dictionary<string, object>;
            if (d == null) throw new Exception(Lang.T("Respon GitHub tidak dikenali"));
            OnlineRelease rel = new OnlineRelease();
            object v;
            rel.Tag = d.TryGetValue("tag_name", out v) && v != null ? v.ToString() : "";
            rel.Notes = d.TryGetValue("body", out v) && v != null ? v.ToString() : "";
            Match vm = Regex.Match(rel.Tag, @"\d+(\.\d+){1,3}");
            if (!vm.Success) throw new Exception(Lang.T("Tag rilis tidak berisi nomor versi: ") + rel.Tag);
            rel.Version = new Version(vm.Value);
            object[] assets = d.TryGetValue("assets", out v) ? v as object[] : null;
            if (assets != null)
                foreach (object a in assets)
                {
                    Dictionary<string, object> ad = a as Dictionary<string, object>;
                    if (ad == null) continue;
                    object nm, url;
                    if (ad.TryGetValue("name", out nm) && nm != null && nm.ToString().EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
                        && ad.TryGetValue("browser_download_url", out url) && url != null)
                    { rel.ZipUrl = url.ToString(); break; }
                }
            if (rel.ZipUrl == null) throw new Exception(Lang.T("Rilis ") + rel.Tag + Lang.T(" tidak punya file ZIP"));
            return rel;
        }

        // Versi terbaru langsung dari cabang (dibaca dari src/Program.cs), unduh arsip ZIP cabang tsb
        public static OnlineRelease CheckBranch(string repo, string branch)
        {
            string prog = null;
            // 1) lewat API GitHub (isi file dalam base64)
            try
            {
                using (WebClient wc = new WebClient())
                {
                    wc.Headers[HttpRequestHeader.UserAgent] = "ZMB-Updater";
                    wc.Headers[HttpRequestHeader.Accept] = "application/vnd.github+json";
                    wc.Encoding = Encoding.UTF8;
                    string cj = wc.DownloadString("https://api.github.com/repos/" + repo + "/contents/src/Program.cs?ref=" + branch);
                    Dictionary<string, object> d = new JavaScriptSerializer().DeserializeObject(cj) as Dictionary<string, object>;
                    object c;
                    if (d != null && d.TryGetValue("content", out c) && c != null)
                        prog = Encoding.UTF8.GetString(Convert.FromBase64String(c.ToString().Replace("\n", "").Replace("\r", "")));
                }
            }
            catch { prog = null; }
            // 2) cadangan: raw.githubusercontent.com
            if (prog == null)
            {
                using (WebClient wc = new WebClient())
                {
                    wc.Headers[HttpRequestHeader.UserAgent] = "ZMB-Updater";
                    wc.Encoding = Encoding.UTF8;
                    try { prog = wc.DownloadString("https://raw.githubusercontent.com/" + repo + "/" + branch + "/src/Program.cs?t=" + DateTime.UtcNow.Ticks); }
                    catch (WebException ex)
                    {
                        HttpWebResponse r = ex.Response as HttpWebResponse;
                        if (r != null && (int)r.StatusCode == 404) throw new Exception("Repositori " + repo + Lang.T(" belum berisi ZMB (atau bersifat private)"));
                        throw new Exception(Lang.T("Gagal cek update: ") + ex.Message);
                    }
                }
            }
            Match m = Regex.Match(prog, "AssemblyVersion\\(\"([0-9.]+)\"\\)");
            if (!m.Success) throw new Exception(Lang.T("Versi tidak ditemukan di repositori"));
            OnlineRelease rel = new OnlineRelease();
            rel.Version = new Version(m.Groups[1].Value);
            rel.Tag = branch;
            rel.ZipUrl = "https://github.com/" + repo + "/archive/refs/heads/" + branch + ".zip";
            try
            {
                using (WebClient wc = new WebClient())
                {
                    wc.Headers[HttpRequestHeader.UserAgent] = "ZMB-Updater";
                    wc.Encoding = Encoding.UTF8;
                    string cj = wc.DownloadString("https://api.github.com/repos/" + repo + "/commits/" + branch);
                    Match cm = Regex.Match(cj, "\"message\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"");
                    if (cm.Success) rel.Notes = Regex.Unescape(cm.Groups[1].Value);
                }
            }
            catch { }
            return rel;
        }

        public static string DownloadRelease(OnlineRelease rel)
        {
            ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072;
            string dir = Path.Combine(Config.BaseDir, "update_download");
            Directory.CreateDirectory(dir);
            string file = Path.Combine(dir, "ZMB_" + rel.Version + ".zip");
            using (WebClient wc = new WebClient())
            {
                wc.Headers[HttpRequestHeader.UserAgent] = "ZMB-Updater";
                wc.DownloadFile(rel.ZipUrl, file + ".part");
            }
            if (File.Exists(file)) File.Delete(file);
            File.Move(file + ".part", file);
            return file;
        }

        static string FindRoot(string dir)
        {
            if (Directory.Exists(Path.Combine(dir, "src"))) return dir;
            foreach (string d in Directory.GetDirectories(dir))
                if (Directory.Exists(Path.Combine(d, "src"))) return d;
            return null;
        }

        // Ekstrak & compile. Mengembalikan path exe baru atau null (log berisi pesan compiler)
        public static string Build(string zip, out string log, out string srcRoot)
        {
            log = ""; srcRoot = null;
            string tmp = Path.Combine(Config.BaseDir, "update_tmp");
            try { if (Directory.Exists(tmp)) Directory.Delete(tmp, true); } catch { }
            Directory.CreateDirectory(tmp);
            using (FileStream fs = File.OpenRead(zip))
            using (ZipArchive z = new ZipArchive(fs, ZipArchiveMode.Read))
            {
                foreach (ZipArchiveEntry e in z.Entries)
                {
                    if (e.FullName.EndsWith("/") || e.FullName.Contains("..")) continue;
                    string dest = Path.Combine(tmp, e.FullName.Replace('/', '\\'));
                    Directory.CreateDirectory(Path.GetDirectoryName(dest));
                    using (Stream es = e.Open())
                    using (FileStream o = File.Create(dest)) es.CopyTo(o);
                }
            }
            srcRoot = FindRoot(tmp);
            if (srcRoot == null) { log = Lang.T("Folder src tidak ditemukan di dalam ZIP"); return null; }

            string fw = System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory();
            string csc = Path.Combine(fw, "csc.exe");
            if (!File.Exists(csc)) { log = "csc.exe tidak ditemukan di " + fw; return null; }
            string outExe = Path.Combine(Config.BaseDir, "ZMB_new.exe");
            try { if (File.Exists(outExe)) File.Delete(outExe); } catch { }

            StringBuilder args = new StringBuilder();
            args.Append("/nologo /target:winexe /optimize+ /platform:anycpu ");
            args.Append("/out:\"" + outExe + "\" ");
            string ico = Path.Combine(srcRoot, @"assets\zmb.ico"), man = Path.Combine(srcRoot, @"assets\app.manifest");
            if (File.Exists(ico)) args.Append("/win32icon:\"" + ico + "\" ");
            if (File.Exists(man)) args.Append("/win32manifest:\"" + man + "\" ");
            args.Append("/nowarn:168,219,414,649,618,252 ");
            string assetDir = Path.Combine(srcRoot, "assets");
            if (Directory.Exists(assetDir))
                foreach (string pat in new string[] { "*.png", "*.jpg", "*.bin" })
                    foreach (string af in Directory.GetFiles(assetDir, pat))
                        args.Append("/resource:\"" + af + "\",ZMB." + Path.GetFileName(af) + " ");
            args.Append("/lib:\"" + fw.TrimEnd('\\') + "\" /lib:\"" + Path.Combine(fw, "WPF") + "\" ");
            foreach (string r in new string[] { "System.dll", "System.Core.dll", "System.Drawing.dll", "System.Windows.Forms.dll", "System.Xml.dll",
                "System.Web.Extensions.dll", "System.IO.Compression.dll", "System.Xaml.dll", "WindowsBase.dll", "PresentationCore.dll", "PresentationFramework.dll" })
                args.Append("/r:" + r + " ");
            args.Append("\"" + Path.Combine(srcRoot, "src") + "\\*.cs\"");

            ProcessStartInfo psi = new ProcessStartInfo(csc, args.ToString());
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            psi.WorkingDirectory = srcRoot;
            using (Process p = Process.Start(psi))
            {
                string o1 = p.StandardOutput.ReadToEnd();
                string o2 = p.StandardError.ReadToEnd();
                p.WaitForExit(180000);
                log = (o1 + o2).Trim();
                if (p.ExitCode != 0 || !File.Exists(outExe)) return null;
            }
            return outExe;
        }

        // Salin file sumber baru, lalu ganti exe setelah ZMB tertutup dan jalankan ulang
        public static void InstallAndRestart(string newExe, string srcRoot)
        {
            string baseDir = Config.BaseDir.TrimEnd('\\');
            try
            {
                foreach (string sub in new string[] { "src", "assets" })
                {
                    string from = Path.Combine(srcRoot, sub);
                    if (!Directory.Exists(from)) continue;
                    foreach (string f in Directory.GetFiles(from, "*", SearchOption.AllDirectories))
                    {
                        string rel = f.Substring(from.Length).TrimStart('\\');
                        string dest = Path.Combine(Path.Combine(baseDir, sub), rel);
                        Directory.CreateDirectory(Path.GetDirectoryName(dest));
                        File.Copy(f, dest, true);
                    }
                }
                foreach (string f in new string[] { "PANDUAN.txt", "BUILD.bat" })
                    if (File.Exists(Path.Combine(srcRoot, f))) File.Copy(Path.Combine(srcRoot, f), Path.Combine(baseDir, f), true);
            }
            catch { }

            string exe = ExePath;
            int pid = Process.GetCurrentProcess().Id;
            string bat = Path.Combine(baseDir, "zmb_update.bat");
            StringBuilder b = new StringBuilder();
            b.AppendLine("@echo off");
            b.AppendLine("cd /d \"" + baseDir + "\"");
            b.AppendLine(":wait");
            b.AppendLine("tasklist /FI \"PID eq " + pid + "\" 2>nul | find \"" + pid + "\" >nul && (timeout /t 1 /nobreak >nul & goto wait)");
            b.AppendLine("move /y \"" + newExe + "\" \"" + exe + "\" >nul");
            b.AppendLine("start \"\" \"" + exe + "\"");
            b.AppendLine("rmdir /s /q \"" + Path.Combine(baseDir, "update_tmp") + "\" 2>nul");
            b.AppendLine("del \"%~f0\"");
            File.WriteAllText(bat, b.ToString(), Encoding.Default);
            ProcessStartInfo psi = new ProcessStartInfo("cmd.exe", "/c \"" + bat + "\"");
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.WorkingDirectory = baseDir;
            Process.Start(psi);
        }
    }
}
