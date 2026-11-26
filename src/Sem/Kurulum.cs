using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.ServiceProcess;
using System.Threading;
using System.Windows.Forms;
using Sem.Ajan;
using Sem.Ortak;
using Sem.Arayuz;
using Mesaj = Sem.Arayuz.MesajKutusu;

namespace Sem
{
    static class Kurulum
    {
        public static bool KuruluMu { get { try { return ServiceController.GetServices().Any(s => s.ServiceName == AjanServis.HizmetAdi); } catch { return false; } } }

        public static string KuruluSurum
        {
            get { try { return File.Exists(AjanAyar.KurulumExe) ? FileVersionInfo.GetVersionInfo(AjanAyar.KurulumExe).ProductVersion : ""; } catch { return ""; } }
        }

        static void Komut(string exe, string arg)
        {
            using (var p = Process.Start(new ProcessStartInfo(exe, arg) { UseShellExecute = false, CreateNoWindow = true }))
                p.WaitForExit(60000);
        }

        static void HizmetiDurdur()
        {
            try
            {
                using (var sc = new ServiceController(AjanServis.HizmetAdi))
                {
                    if (sc.Status != ServiceControllerStatus.Stopped)
                    {
                        sc.Stop();
                        sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(30));
                    }
                }
            }
            catch { }
        }

        static void DigerSemleriKapat()
        {
            int ben = Process.GetCurrentProcess().Id;
            foreach (var p in Process.GetProcessesByName("Sem").Concat(Process.GetProcessesByName("SemAjan")))
                if (p.Id != ben) { try { p.Kill(); p.WaitForExit(5000); } catch { } }
        }

        /// <summary>Kendini Program Files\Sem\Sem.exe olarak kopyalar. Çalışan eski exe kilitliyse önce adını değiştirir
        /// (Windows çalışan bir exe'nin adının değiştirilmesine izin verir), böylece açık yönetim ekranı kapatılmaz.</summary>
        public static void ExeKopyala()
        {
            var kaynak = Application.ExecutablePath;
            var hedef = AjanAyar.KurulumExe;
            if (string.Equals(Path.GetFullPath(kaynak), Path.GetFullPath(hedef), StringComparison.OrdinalIgnoreCase)) return;
            Directory.CreateDirectory(AjanAyar.KurulumKlasoru);
            for (int i = 0; ; i++)
            {
                try
                {
                    if (File.Exists(hedef))
                    {
                        var eski = hedef + ".eski";
                        try { if (File.Exists(eski)) File.Delete(eski); } catch { eski = hedef + "." + DateTime.Now.Ticks + ".eski"; }
                        File.Move(hedef, eski);
                    }
                    File.Copy(kaynak, hedef, true);
                    return;
                }
                catch when (i < 20) { Thread.Sleep(500); }
            }
        }

        public static void EskileriTemizle()
        {
            try { foreach (var f in Directory.GetFiles(AjanAyar.KurulumKlasoru, "*.eski")) try { File.Delete(f); } catch { } } catch { }
        }

        static void HizmetiBaslat()
        {
            using (var sc = new ServiceController(AjanServis.HizmetAdi))
            {
                sc.Refresh();
                if (sc.Status != ServiceControllerStatus.Running) { sc.Start(); sc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(30)); }
            }
        }

        public static void Kur(string sifre, bool yoneticiBilgisayari)
        {
            bool vardi = KuruluMu;
            if (vardi) HizmetiDurdur();
            DigerSemleriKapat();
            ExeKopyala();

            var ayar = AjanAyar.Oku();
            ayar.Anahtar = Sifreleme.AnahtarUret(sifre);
            ayar.OtomatikKapanma = !yoneticiBilgisayari;
            ayar.Yaz();

            if (!File.Exists(AjanAyar.DuvarKagidi))
                using (var s = typeof(Kurulum).Assembly.GetManifestResourceStream("duvar.png"))
                using (var f = File.Create(AjanAyar.DuvarKagidi)) s.CopyTo(f);

            var exe = AjanAyar.KurulumExe;
            if (!vardi)
                Komut("sc.exe", "create " + AjanServis.HizmetAdi + " binPath= \"\\\"" + exe + "\\\" /servis\" start= auto DisplayName= \"Sem Ajan\"");
            else
                Komut("sc.exe", "config " + AjanServis.HizmetAdi + " binPath= \"\\\"" + exe + "\\\" /servis\" start= auto");
            Komut("sc.exe", "description " + AjanServis.HizmetAdi + " \"Sem Ozel Egitim bilgisayar yonetimi\"");
            Komut("sc.exe", "failure " + AjanServis.HizmetAdi + " reset= 86400 actions= restart/5000/restart/5000/restart/5000");

            Komut("netsh.exe", "advfirewall firewall delete rule name=\"Sem Ajan\"");
            Komut("netsh.exe", "advfirewall firewall add rule name=\"Sem Ajan\" dir=in action=allow program=\"" + exe + "\" enable=yes profile=any");

            // v1.0'dan kalan eski adlı kısayolları temizle.
            foreach (var eski in new[] { Environment.SpecialFolder.CommonPrograms, Environment.SpecialFolder.CommonDesktopDirectory })
                try { File.Delete(Path.Combine(Environment.GetFolderPath(eski), "Sem Yönetim.lnk")); } catch { }
            KisayolOlustur(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), "Sem Ajan.lnk"), exe, "/yonetim");
            var masaustu = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory), "Sem Ajan.lnk");
            if (yoneticiBilgisayari) KisayolOlustur(masaustu, exe, "/yonetim");
            else if (File.Exists(masaustu)) File.Delete(masaustu);

            HizmetiBaslat();
        }

        public static void Kaldir()
        {
            HizmetiDurdur();
            Komut("sc.exe", "delete " + AjanServis.HizmetAdi);
            DigerSemleriKapat();
            Komut("netsh.exe", "advfirewall firewall delete rule name=\"Sem Ajan\"");
            foreach (var k in new[] { Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), "Sem Ajan.lnk"),
                                      Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory), "Sem Ajan.lnk") })
                try { File.Delete(k); } catch { }
            try { Directory.Delete(AjanAyar.Klasor, true); } catch { }
            // Çalışan exe kendini silemez; kısa bir bekleyişten sonra cmd silsin.
            Process.Start(new ProcessStartInfo("cmd.exe", "/c ping 127.0.0.1 -n 3 >nul & rmdir /s /q \"" + AjanAyar.KurulumKlasoru + "\"") { CreateNoWindow = true, UseShellExecute = false });
        }

        static void KisayolOlustur(string lnk, string hedef, string arg)
        {
            try
            {
                var t = Type.GetTypeFromProgID("WScript.Shell");
                dynamic sh = Activator.CreateInstance(t);
                dynamic k = sh.CreateShortcut(lnk);
                k.TargetPath = hedef; k.Arguments = arg; k.IconLocation = hedef + ",0"; k.Description = "Sem Ajan bilgisayar yönetimi";
                k.Save();
            }
            catch { }
        }

        /// <summary>Yöneticiden gelen yeni sürüm: hizmet tarafından ProgramData\Sem\guncelle\Sem.exe /guncelle olarak başlatılır.</summary>
        public static int KendiniGuncelle()
        {
            try
            {
                Thread.Sleep(2000);
                HizmetiDurdur();
                ExeKopyala();
                Gunluk.Yaz("Güncellendi: " + Sabitler.Surum);
                HizmetiBaslat();
                return 0;
            }
            catch (Exception e)
            {
                Gunluk.Yaz("Güncelleme hatası: " + e.Message);
                try { HizmetiBaslat(); } catch { }
                return 1;
            }
        }
    }

    class KurulumForm : ModernForm
    {
        readonly Giris sifre1, sifre2;
        readonly Anahtar yonetici;
        readonly Buton kur, kaldir;
        readonly Label durum;

        public KurulumForm() : base(null, 460, 600)
        {
            bool kurulu = Kurulum.KuruluMu;
            var ayar = AjanAyar.Oku();
            bool anahtarVar = kurulu && ayar.Anahtar != null;
            Text = "Sem Ajan Kurulum"; ShowInTaskbar = true; StartPosition = FormStartPosition.CenterScreen;
            int g = Icerik.Width;

            var logo = new PictureBox { Image = Tema.Logo(), SizeMode = PictureBoxSizeMode.Zoom, Size = Tema.S(96, 96), BackColor = Renk.Zemin };
            logo.Location = new Point((g - logo.Width) / 2, 0);
            var bas = Yazi.Etiket("Sem Ajan", 16f, true, Renk.Yazi, false);
            bas.TextAlign = ContentAlignment.MiddleCenter; bas.SetBounds(0, Tema.S(104), g, Tema.S(34));
            var slogan = Yazi.Etiket("Her adım bir başarı, her çocuk bir dünya.", 9.5f, false, Renk.Yazi2, false);
            slogan.TextAlign = ContentAlignment.MiddleCenter; slogan.SetBounds(0, Tema.S(138), g, Tema.S(22));
            durum = Yazi.Etiket(kurulu ? "Kurulu  ·  sürüm " + Kurulum.KuruluSurum + "  →  " + Sabitler.Surum : "Bu bilgisayarda henüz kurulu değil", 9f, true, kurulu ? Renk.YesilKoyu : Renk.Yazi2, false);
            durum.TextAlign = ContentAlignment.MiddleCenter; durum.SetBounds(0, Tema.S(170), g, Tema.S(26));
            durum.Paint += (s, e) => { };
            Icerik.Controls.AddRange(new Control[] { logo, bas, slogan, durum });

            int y = Tema.S(214);
            var l1 = Yazi.Etiket(anahtarVar ? "Yönetici şifresi" : "Yönetici şifresi  (tüm bilgisayarlarda aynı olmalı)", 9.5f, true, Renk.Yazi2);
            l1.Location = new Point(0, y);
            sifre1 = new Giris(true) { Location = new Point(0, y + Tema.S(24)), Width = g, Ipucu = "Şifre" };
            Icerik.Controls.Add(l1); Icerik.Controls.Add(sifre1);
            y += Tema.S(80);
            sifre2 = new Giris(true) { Location = new Point(0, y), Width = g, Ipucu = "Şifre tekrar", Visible = !anahtarVar };
            Icerik.Controls.Add(sifre2);
            if (!anahtarVar) y += Tema.S(60);

            yonetici = new Anahtar("Yönetici bilgisayarı", "Otomatik kapanmaz ve masaüstüne \"Sem Ajan\" kısayolu konur.") { Location = new Point(0, y), Width = g, Acik = kurulu && !ayar.OtomatikKapanma };
            Icerik.Controls.Add(yonetici);

            kur = new Buton(kurulu ? "Güncelle" : "Kur", kurulu ? Ik.Esitle : Ik.Indir, ButonTuru.Birincil) { Width = g, Height = Tema.S(48), Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom };
            kur.Location = new Point(0, Icerik.Height - Tema.S(kurulu ? 100 : 52));
            kur.Click += (s, e) => KurTikla(anahtarVar, ayar);
            Icerik.Controls.Add(kur);
            kaldir = new Buton("Bu bilgisayardan kaldır", Ik.Sil, ButonTuru.Sade) { Visible = kurulu, Anchor = AnchorStyles.Bottom, ForeColorOzel = Renk.Pembe };
            kaldir.BoyutAyarla(); kaldir.Location = new Point((g - kaldir.Width) / 2, Icerik.Height - Tema.S(44));
            kaldir.Click += (s, e) => KaldirTikla(ayar);
            Icerik.Controls.Add(kaldir);
            AcceptButton = null;
            sifre1.Kutu.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter && !sifre2.Visible) { e.SuppressKeyPress = true; KurTikla(anahtarVar, ayar); } };
            sifre2.Kutu.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; KurTikla(anahtarVar, ayar); } };
            Shown += (s, e) => sifre1.Focus();
        }

        void KurTikla(bool anahtarVar, AjanAyar ayar)
        {
            if (sifre1.Text.Length < 4) { Uyar("Şifre en az 4 karakter olmalı."); return; }
            if (anahtarVar)
            {
                if (!Sifreleme.Esit(Sifreleme.AnahtarUret(sifre1.Text), ayar.Anahtar)) { Uyar("Şifre yanlış. Bu bilgisayara daha önce kurulurken kullanılan yönetici şifresini girin."); return; }
            }
            else if (sifre1.Text != sifre2.Text) { Uyar("İki şifre aynı değil."); return; }

            Calis(() => Kurulum.Kur(sifre1.Text, yonetici.Acik),
                  "Kurulum tamamlandı",
                  "Sem Ajan bu bilgisayarda arka planda çalışıyor. Bu pencereyi kapatıp USB belleği çıkarabilirsiniz." +
                  (yonetici.Acik ? "\n\nYönetim ekranını masaüstündeki \"Sem Ajan\" kısayolundan açabilirsiniz." : ""));
        }

        void KaldirTikla(AjanAyar ayar)
        {
            if (ayar.Anahtar != null && !Sifreleme.Esit(Sifreleme.AnahtarUret(sifre1.Text), ayar.Anahtar)) { Uyar("Kaldırmak için yönetici şifresini yukarıya yazın."); return; }
            if (Mesaj.Sor(this, "Sem Ajan kaldırılsın mı?", "Bu bilgisayar artık yönetim ekranında görünmeyecek ve otomatik kapanmayacak.", "Kaldır", true) != true) return;
            Calis(Kurulum.Kaldir, "Kaldırıldı", "Sem Ajan bu bilgisayardan kaldırıldı.");
        }

        void Calis(Action is_, string baslik, string basariMetni)
        {
            kur.Enabled = kaldir.Enabled = false; UseWaitCursor = true; durum.Text = "Lütfen bekleyin…"; durum.ForeColor = Renk.Mavi;
            var t = new Thread(() =>
            {
                string hata = null;
                try { is_(); } catch (Exception e) { hata = e.Message; }
                BeginInvoke((Action)(() =>
                {
                    UseWaitCursor = false;
                    if (hata == null) { Mesaj.Bilgi(this, baslik, basariMetni); Close(); }
                    else { kur.Enabled = kaldir.Enabled = true; durum.Text = "Hata oluştu"; durum.ForeColor = Renk.Pembe; Uyar("İşlem tamamlanamadı:\n" + hata); }
                }));
            }) { IsBackground = true };
            t.Start();
        }

        void Uyar(string s) => Mesaj.Bilgi(this, "Dikkat", s, true);
    }
}
