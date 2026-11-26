using System;
using System.Diagnostics;
using System.Linq;
using System.Security.Principal;
using System.ServiceProcess;
using System.Threading;
using System.Windows.Forms;
using Sem.Ajan;

namespace Sem
{
    static class Program
    {
        [STAThread]
        static int Main(string[] args)
        {
            string mod = args.FirstOrDefault()?.ToLowerInvariant() ?? "";

            if (mod == "/servis") { ServiceBase.Run(new AjanServis()); return 0; }
            if (mod == "/guncelle") return Kurulum.KendiniGuncelle();

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            if (mod == "/ekran")
            {
                bool yeni;
                using (var m = new Mutex(true, @"Local\SemEkran", out yeni))
                {
                    if (!yeni) return 0;
                    Application.Run(new Ekran());
                }
                return 0;
            }

            if (mod == "/yonetim")
            {
                bool tepsi = args.Skip(1).Any(a => a.Equals("/tepsi", StringComparison.OrdinalIgnoreCase));
                bool ilk;
                using (var m = new Mutex(true, @"Local\SemYonetim", out ilk))
                using (var goster = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\SemYonetimGoster"))
                {
                    // Zaten açıksa (ör. saatin yanında bekliyorsa) o pencereyi öne getir.
                    if (!ilk) { if (!tepsi) goster.Set(); return 0; }
                    Application.Run(new Yonetici.AnaForm(tepsi, goster));
                }
                return 0;
            }

            // Çift tıklanınca: kurulum / güncelleme / kaldırma penceresi (yönetici yetkisi gerekir)
            if (!YoneticiMi())
            {
                try { Process.Start(new ProcessStartInfo(Application.ExecutablePath) { Verb = "runas", UseShellExecute = true }); }
                catch { MessageBox.Show("Kurulum için yönetici izni gerekiyor.", "Sem Ajan", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
                return 0;
            }
            Application.Run(new KurulumForm());
            return 0;
        }

        public static bool YoneticiMi()
        {
            using (var id = WindowsIdentity.GetCurrent())
                return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
        }
    }
}
