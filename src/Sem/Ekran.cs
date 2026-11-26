using System;
using System.Drawing;
using System.IO;
using System.IO.Pipes;
using System.Media;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;
using Sem.Ortak;
using Sem.Arayuz;

namespace Sem.Ajan
{
    /// <summary>
    /// Oturum açmış kullanıcının masaüstünde görünmeden çalışan kısım: duyuru ve kapanma uyarısı pencereleri,
    /// duvar kâğıdı, ekran kilitleme. Komutları hizmetten yerel bir kanal (named pipe) üzerinden alır.
    /// </summary>
    public class Ekran : ApplicationContext
    {
        readonly Form gizli;
        StreamWriter yaz;
        readonly object yazKilit = new object();
        UyariForm uyari;
        string duvarYolu; bool duvarKoru;
        readonly System.Windows.Forms.Timer duvarBekci;

        public Ekran()
        {
            gizli = new Form { ShowInTaskbar = false, WindowState = FormWindowState.Minimized, Opacity = 0 };
            var _ = gizli.Handle; // pencere kolu oluşsun ki başka iş parçacığından BeginInvoke yapılabilsin
            duvarBekci = new System.Windows.Forms.Timer { Interval = 60000 };
            duvarBekci.Tick += (s, e) => DuvarKontrol(false);
            duvarBekci.Start();
            new Thread(Dongu) { IsBackground = true }.Start();
        }

        void Dongu()
        {
            while (true)
            {
                try
                {
                    using (var p = new NamedPipeClientStream(".", Sabitler.PipeAdi, PipeDirection.InOut, PipeOptions.Asynchronous))
                    {
                        p.Connect(5000);
                        var oku = new StreamReader(p, new UTF8Encoding(false));
                        lock (yazKilit) yaz = new StreamWriter(p, new UTF8Encoding(false)) { AutoFlush = true };
                        Gonder("MERHABA", Sabitler.Surum);
                        string satir;
                        while ((satir = oku.ReadLine()) != null)
                        {
                            var parca = EkranHatti.Coz(satir);
                            gizli.BeginInvoke((Action)(() => { try { Isle(parca); } catch { } }));
                        }
                    }
                }
                catch { }
                lock (yazKilit) yaz = null;
                Thread.Sleep(3000);
            }
        }

        public void Gonder(params string[] p)
        {
            lock (yazKilit) { try { yaz?.WriteLine(EkranHatti.Paketle(p)); } catch { } }
        }

        void Isle(string[] p)
        {
            switch (p[0])
            {
                case "DUYURU":
                    new DuyuruForm(p[1], p.Length > 2 ? p[2] : "").Show();
                    break;
                case "UYARI":
                    uyari?.Close();
                    uyari = new UyariForm(this, int.Parse(p[1]), int.Parse(p[2]), int.Parse(p[3]), p[4]);
                    uyari.FormClosed += (s, e) => { if (s == uyari) uyari = null; };
                    uyari.Show();
                    break;
                case "UYARI_KAPAT":
                    uyari?.Close();
                    break;
                case "IPTAL_SONUC":
                    uyari?.IptalSonucu(p[1] == "1");
                    break;
                case "DUVAR":
                    duvarYolu = p[1]; duvarKoru = p.Length > 2 && p[2] == "1";
                    DuvarKontrol(true);
                    break;
                case "DOSYA":
                    new BildirimForm("Yeni dosya geldi", p[1] + "\nMasaüstünde 'Sem Gelen Dosyalar' klasöründe.", Ik.Klasor, Renk.Turuncu).Show();
                    break;
                case "CIK":
                    Environment.Exit(0);
                    break;
                case "KILITLE":
                    Yerel.LockWorkStation();
                    break;
            }
        }

        void DuvarKontrol(bool zorla)
        {
            if (string.IsNullOrEmpty(duvarYolu) || !File.Exists(duvarYolu)) return;
            if (!zorla)
            {
                if (!duvarKoru) return;
                var sb = new StringBuilder(520);
                Yerel.SystemParametersInfo(Yerel.SPI_GETDESKWALLPAPER, (uint)sb.Capacity, sb, 0);
                if (string.Equals(sb.ToString(), duvarYolu, StringComparison.OrdinalIgnoreCase)) return;
            }
            try
            {
                using (var k = Registry.CurrentUser.CreateSubKey(@"Control Panel\Desktop"))
                {
                    k.SetValue("WallpaperStyle", "10"); // Doldur
                    k.SetValue("TileWallpaper", "0");
                }
            }
            catch { }
            Yerel.SystemParametersInfo(Yerel.SPI_SETDESKWALLPAPER, 0, duvarYolu, Yerel.SPIF_UPDATEINIFILE | Yerel.SPIF_SENDCHANGE);
        }

        public static Image Logo() => Sem.Arayuz.Tema.Logo();
    }
}
