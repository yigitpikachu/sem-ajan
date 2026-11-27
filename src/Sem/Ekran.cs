using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
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
        string duvarKip = "", duvarOda = "", duvarTon = ""; DateTime duvarGunu; bool duvarKoyuCizildi;
        readonly System.Windows.Forms.Timer duvarBekci;
        NotifyIcon tepsi;
        string kokKlasor = "";

        public Ekran()
        {
            gizli = new Form { ShowInTaskbar = false, WindowState = FormWindowState.Minimized, Opacity = 0 };
            var _ = gizli.Handle; // pencere kolu oluşsun ki başka iş parçacığından BeginInvoke yapılabilsin
            duvarBekci = new System.Windows.Forms.Timer { Interval = 60000 };
            duvarBekci.Tick += (s, e) => { if (duvarKip != "" && (duvarGunu != DateTime.Today || (duvarTon == "otomatik" && Duvar.WindowsKoyu() != duvarKoyuCizildi))) DuvarCiz(); DuvarKontrol(false); };
            duvarBekci.Start();
            new Thread(Dongu) { IsBackground = true }.Start();
            try { GonderMenusu(); } catch { }
        }

        /// <summary>Dosyaya sağ tık > Gönder > "Sem Paylaş".</summary>
        static void GonderMenusu()
        {
            var k = Environment.GetFolderPath(Environment.SpecialFolder.SendTo);
            if (string.IsNullOrEmpty(k)) return;
            var lnk = Path.Combine(k, "Sem Paylaş.lnk");
            if (!File.Exists(lnk)) Kurulum.KisayolOlustur(lnk, Application.ExecutablePath, "/paylas", "Dosyaları bilgisayarlara ve telefonlara gönder");
        }

        static void PaylasAc() { try { System.Diagnostics.Process.Start(Application.ExecutablePath, "/paylas"); } catch { } }

        void KlasorAc(string k)
        {
            try { if (Directory.Exists(k)) System.Diagnostics.Process.Start("explorer.exe", "\"" + k + "\""); } catch { }
        }

        void TepsiAyarla(bool acik)
        {
            if (!acik) { if (tepsi != null) { tepsi.Visible = false; tepsi.Dispose(); tepsi = null; } return; }
            if (tepsi != null) return;
            tepsi = new NotifyIcon { Text = "Sem Paylaş: dosya gönder", Visible = true };
            try { tepsi.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            var m = new ContextMenuStrip();
            m.Items.Add("Dosya gönder…", null, (s, e) => PaylasAc());
            m.Items.Add("Sem Dosyalar klasörünü aç", null, (s, e) => KlasorAc(kokKlasor));
            tepsi.ContextMenuStrip = m;
            tepsi.MouseClick += (s, e) => { if (e.Button == MouseButtons.Left) PaylasAc(); };
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
                            if (parca[0] == "RESIM") { ThreadPool.QueueUserWorkItem(_ => ResimGonder(parca)); continue; }
                            if (parca[0] == "GIRDI") { if (parca.Length > 1) Girdi.Uygula(parca[1]); continue; }
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
                {
                    var id = p.Length > 3 ? p[3] : "";
                    new DuyuruForm(p[1], p.Length > 2 ? p[2] : "", () => { if (id != "") Gonder("OKUNDU", id); }).Show();
                    break;
                }
                case "UYARI":
                    uyari?.Close();
                    uyari = new UyariForm(this, int.Parse(p[1]), int.Parse(p[2]), int.Parse(p[3]), p[4]);
                    uyari.FormClosed += (s, e) => { if (s == uyari) uyari = null; };
                    uyari.Show();
                    break;
                case "UZAK":
                    UzakBandi.Goster(p.Length > 1 && p[1] == "1");
                    break;
                case "UYARI_KAPAT":
                    uyari?.Close();
                    break;
                case "VARSAYILAN_YAZICI":
                    // Yazıcıyı sabitle: Canon bu kullanıcının varsayılan yazıcısı olsun
                    try { if (p.Length > 1 && p[1] != "" && VarsayilanYazici() != p[1]) SetDefaultPrinter(p[1]); } catch { }
                    break;
                case "IPTAL_SONUC":
                    uyari?.IptalSonucu(p[1] == "1");
                    break;
                case "DUVAR":
                    duvarKip = "";
                    duvarYolu = p[1]; duvarKoru = p.Length > 2 && p[2] == "1";
                    DuvarKontrol(true);
                    break;
                case "DUVAR_CIZ":
                    duvarKip = p[1]; duvarOda = p.Length > 2 ? p[2] : ""; duvarKoru = p.Length > 3 && p[3] == "1"; duvarTon = p.Length > 4 ? p[4] : "";
                    DuvarCiz();
                    break;
                case "DOSYA":
                    new BildirimForm("Yeni dosya geldi", p[1], Ik.Klasor, Renk.Mavi).Show();
                    break;
                case "PAYLASIM":
                    kokKlasor = p.Length > 2 ? p[2] : "";
                    TepsiAyarla(p[1] == "1");
                    break;
                case "SOR":
                {
                    var id = p[1]; int sn; int.TryParse(p.Length > 5 ? p[5] : "90", out sn);
                    new SoruKarti(p[2], p[3], p[4], sn, c => Gonder("CEVAP", id, c.ToString())).Show();
                    break;
                }
                case "METIN":
                    new MetinKarti(p[1], p[2]).Show();
                    break;
                case "GELDI":
                {
                    string kimden = p[1], adet = p[2], klasor = p[3], ad = p.Length > 4 ? p[4] : "";
                    new BildirimForm(kimden + " dosya gönderdi", (ad != "" ? ad : adet + " dosya") + "\nAçmak için tıklayın", Ik.KlasorAc, Renk.Mavi, () => KlasorAc(klasor)).Show();
                    break;
                }
                case "YAZDIR":
                {
                    string yol = p[1], kimden = p.Length > 2 ? p[2] : "";
                    ThreadPool.QueueUserWorkItem(_ =>
                    {
                        var hata = Yazdirici.Yazdir(yol);
                        gizli.BeginInvoke((Action)(() =>
                        {
                            if (hata == null) new BildirimForm("Yazdırılıyor", kimden + ": " + Path.GetFileName(yol), Ik.YaziciIk, Renk.Mavi).Show();
                            else new BildirimForm("Yazdırılamadı", Path.GetFileName(yol) + "\n" + hata, Ik.Uyari, Renk.Pembe, () => KlasorAc(Path.GetDirectoryName(yol))).Show();
                        }));
                    });
                    break;
                }
                case "CIK":
                    TepsiAyarla(false);
                    Environment.Exit(0);
                    break;
                case "KILITLE":
                    Yerel.LockWorkStation();
                    break;
            }
        }

        /// <summary>Ekranın küçültülmüş JPEG görüntüsünü hizmete gönderir (yönetim ekranındaki önizleme için).</summary>
        readonly FarkKodlayici fark = new FarkKodlayici();

        [System.Runtime.InteropServices.DllImport("winspool.drv", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
        static extern bool SetDefaultPrinter(string ad);
        [System.Runtime.InteropServices.DllImport("winspool.drv", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
        static extern bool GetDefaultPrinter(StringBuilder ad, ref int boyut);
        static string VarsayilanYazici() { int n = 512; var sb = new StringBuilder(n); return GetDefaultPrinter(sb, ref n) ? sb.ToString() : ""; }

        void ResimGonder(string[] p)
        {
            string id = p.Length > 1 ? p[1] : "";
            try
            {
                int w; if (p.Length < 3 || !int.TryParse(p[2], out w)) w = 480;
                // Uzaktan kontrol: yalnızca değişen bölgeler gönderilir (6. alan = yönetim ekranındaki son kare sırası).
                int temel;
                if (p.Length > 5 && int.TryParse(p[5], out temel))
                {
                    long k = 60; if (p.Length > 3) long.TryParse(p[3], out k);
                    Gonder("RESIM", id, Convert.ToBase64String(fark.EkranPaketi(w, k, temel, p.Length > 4 && p[4] == "1")), "");
                    return;
                }
                var b = Screen.PrimaryScreen.Bounds;
                w = Math.Max(160, Math.Min(Math.Min(1920, b.Width), w));
                int h = Math.Max(1, (int)Math.Round(b.Height * (double)w / b.Width));
                using (var ham = new Bitmap(b.Width, b.Height))
                using (var kucuk = new Bitmap(w, h))
                using (var ms = new MemoryStream())
                {
                    using (var g = Graphics.FromImage(ham)) { g.CopyFromScreen(b.Location, Point.Empty, b.Size); if (p.Length > 4 && p[4] == "1") Girdi.ImlecCiz(g, b); }
                    using (var g = Graphics.FromImage(kucuk))
                    {
                        g.InterpolationMode = InterpolationMode.HighQualityBilinear; g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                        g.DrawImage(ham, new Rectangle(0, 0, w, h));
                    }
                    var jpeg = ImageCodecInfo.GetImageEncoders().First(c => c.FormatID == ImageFormat.Jpeg.Guid);
                    using (var ep = new EncoderParameters(1))
                    {
                        long kalite = 72; if (id.Length > 0 && p.Length > 3) long.TryParse(p[3], out kalite);
                        ep.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, Math.Max(30L, Math.Min(90L, kalite)));
                        kucuk.Save(ms, jpeg, ep);
                    }
                    Gonder("RESIM", id, Convert.ToBase64String(ms.ToArray()), "");
                }
            }
            catch { Gonder("RESIM", id, "", "Ekran kilitli ya da görüntü alınamadı"); }
        }

        /// <summary>Tasarımı bu ekranın çözünürlüğünde çizer ve uygular (otomatik kipte her gün yenilenir).</summary>
        void DuvarCiz()
        {
            try
            {
                duvarGunu = DateTime.Today;
                var k = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Sem");
                Directory.CreateDirectory(k);
                bool koyuWin = Duvar.WindowsKoyu();
                var t = Duvar.Sec(duvarKip, DateTime.Now, duvarTon, koyuWin);
                duvarKoyuCizildi = koyuWin;
                var yol = Path.Combine(k, "duvar_" + duvarKip + (t.Koyu ? "_koyu_" : "_") + DateTime.Now.ToString("yyyyMMdd") + ".png");
                var b = Screen.PrimaryScreen.Bounds;
                using (var logo = Tema.Logo())
                using (var bmp = Duvar.Ciz(t, Math.Max(800, b.Width), Math.Max(600, b.Height), duvarOda, logo))
                    bmp.Save(yol, ImageFormat.Png);
                foreach (var f in Directory.GetFiles(k, "duvar_*.png")) if (!f.Equals(yol, StringComparison.OrdinalIgnoreCase)) try { File.Delete(f); } catch { }
                duvarYolu = yol;
                DuvarKontrol(true);
            }
            catch { }
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
