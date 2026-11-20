using System;
using System.Threading;

[assembly: System.Reflection.AssemblyTitle("ZMB")]
[assembly: System.Reflection.AssemblyProduct("ZMB")]
[assembly: System.Reflection.AssemblyVersion("2.23.2.0")]

namespace ZMB
{
    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            Lang.Init(); // bahasa dibaca dulu, sebelum teks menu dibuat
            bool created;
            using (Mutex m = new Mutex(true, "ZMB_SingleInstance_7F3A", out created))
            {
                // dibuka ulang (mis. ganti bahasa): tunggu instance lama selesai menutup
                if (!created && args != null && Array.IndexOf(args, "--restart") >= 0)
                {
                    try { created = m.WaitOne(15000); } catch (AbandonedMutexException) { created = true; }
                }
                if (!created) return; // sudah berjalan
                System.Windows.Forms.Application.EnableVisualStyles();
                AppDomain.CurrentDomain.UnhandledException += delegate (object s, UnhandledExceptionEventArgs e)
                {
                    ZmbWindow.Log("Unhandled: " + e.ExceptionObject);
                };
                System.Windows.Application app = new System.Windows.Application();
                app.ShutdownMode = System.Windows.ShutdownMode.OnMainWindowClose;
                app.DispatcherUnhandledException += delegate (object s, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
                {
                    ZmbWindow.Log("DispatcherException: " + e.Exception);
                    e.Handled = true;
                };
                app.Run(new ZmbWindow());
            }
        }
    }
}
