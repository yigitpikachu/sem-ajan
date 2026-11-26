using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.IO.Compression;
using System.IO.Pipes;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.ServiceProcess;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Sem.Ortak;

namespace Sem.Ajan
{
    /// <summary>
    /// SYSTEM hesabıyla çalışan Windows hizmeti. Ağdan gelen komutları karşılar, kapanma saatini takip eder
    /// ve ekrandaki kullanıcı tarafını (Sem.exe /ekran) ayakta tutar.
    /// </summary>
    public class AjanServis : ServiceBase
    {
        public const string HizmetAdi = "SemAjan";

        readonly object kilit = new object();
        AjanAyar ayar;
        volatile bool calisiyor;
        TcpListener tcp;
        UdpClient udp;
        Timer zamanlayici;
        readonly DateTime baslangic = DateTime.Now;

        // Kapanma durumu
        DateTime gun = DateTime.MinValue;
        int erteleSayisi, toplamErteleDk;
        bool uyariGosterildi;
        DateTime? elleHedef; bool elleYenidenBaslat;
        bool kapaniyor;

        // Ekran (kullanıcı tarafı) bağlantısı
        StreamWriter ekranYaz;
        readonly object ekranKilit = new object();
        DateTime sonEkranBaslatma = DateTime.MinValue;

        string sonIslem = "";

        // Ekran görüntüsü istekleri: ekran tarafı cevabı gönderene kadar bekler
        class BekleyenResim { public readonly ManualResetEventSlim Olay = new ManualResetEventSlim(); public string Veri = "", Hata = ""; }
        readonly Dictionary<string, BekleyenResim> bekleyenResim = new Dictionary<string, BekleyenResim>();

        // Okunan duyurular: "id|yyyy-MM-ddTHH:mm:ss"
        readonly List<string> okunan = new List<string>();
        DateTime sonSabahSinyal = DateTime.MinValue;

        public AjanServis() { ServiceName = HizmetAdi; CanStop = true; CanShutdown = true; }

        protected override void OnStart(string[] args) { Baslat(); }
        protected override void OnStop() { Durdur(); }
        protected override void OnShutdown() { Durdur(); }

        public void Baslat()
        {
            ayar = AjanAyar.Oku();
            Kurulum.EskileriTemizle();
            try { if (File.Exists(AjanAyar.OkunanDosya)) okunan.AddRange(File.ReadAllLines(AjanAyar.OkunanDosya).Where(x => x.Contains("|"))); } catch { }
            calisiyor = true;
            new Thread(TcpDongu) { IsBackground = true }.Start();
            new Thread(KesifDongu) { IsBackground = true }.Start();
            new Thread(EkranDongu) { IsBackground = true }.Start();
            zamanlayici = new Timer(_ => Tik(), null, 3000, 5000);
            Gunluk.Yaz("Hizmet başladı, sürüm " + Sabitler.Surum);
        }

        public void Durdur()
        {
            calisiyor = false;
            try { zamanlayici?.Dispose(); } catch { }
            try { tcp?.Stop(); } catch { }
            try { udp?.Close(); } catch { }
        }

        // ---------------------------------------------------------------- Zamanlayıcı
        void Tik()
        {
            try { KapanmaKontrol(); } catch (Exception e) { Gunluk.Yaz("Tik: " + e.Message); }
            try { EkranBekci(); } catch (Exception e) { Gunluk.Yaz("Ekran: " + e.Message); }
            try { GuncellemeKontrol(); } catch (Exception e) { Gunluk.Yaz("Güncelleme: " + e.Message); }
            try { SabahKontrol(); } catch (Exception e) { Gunluk.Yaz("Sabah: " + e.Message); }
        }

        // ---------------------------------------------------------------- Sabah açılma
        bool SabahPlaniVar(out TimeSpan saat)
        {
            saat = TimeSpan.Zero;
            return !string.IsNullOrEmpty(ayar.SabahSaat) && ayar.SabahMacler != "" && TimeSpan.TryParse(ayar.SabahSaat, out saat);
        }

        /// <summary>Planlanan saatte listedeki bilgisayarlara üç dakika boyunca aralıklı açma sinyali gönderir.</summary>
        void SabahKontrol()
        {
            TimeSpan saat;
            if (!SabahPlaniVar(out saat)) return;
            // Otomatik kapanmayan (yönetici) bilgisayar gece uykuya geçmesin ki sabah sinyali gönderebilsin.
            if (!ayar.OtomatikKapanma) Yerel.SetThreadExecutionState(Yerel.ES_SYSTEM_REQUIRED);
            var simdi = DateTime.Now;
            if (!ayar.SabahGunler.Split(',').Contains(((int)simdi.DayOfWeek).ToString())) return;
            var bas = simdi.Date + saat;
            if (simdi < bas || simdi > bas.AddMinutes(3) || (simdi - sonSabahSinyal).TotalSeconds < 30) return;
            if (sonSabahSinyal.Date != simdi.Date) Gunluk.Yaz("Sabah açılma sinyali gönderiliyor");
            sonSabahSinyal = simdi;
            foreach (var mac in ayar.SabahMacler.Split(';').Where(x => x.Trim() != ""))
                try { Uyandirma.Gonder(mac.Trim()); } catch { }
        }

        /// <summary>Ağ kartının "sihirli paketle uyanma" ayarını açar ve hızlı başlatmayı kapatır (Wake-on-LAN için).</summary>
        void WolHazirla()
        {
            Arkaplan("Ağdan açılma hazırlığı", () =>
            {
                var ps = "$ErrorActionPreference='SilentlyContinue'\r\n" +
                         "Get-NetAdapter -Physical | ForEach-Object {\r\n" +
                         "  Set-NetAdapterPowerManagement -Name $_.Name -WakeOnMagicPacket Enabled -NoRestart\r\n" +
                         "  foreach ($k in '*WakeOnMagicPacket','EnablePME','S5WakeOnLan','WakeOnLink') { Set-NetAdapterAdvancedProperty -Name $_.Name -RegistryKeyword $k -RegistryValue 1 -NoRestart }\r\n" +
                         "}\r\n" +
                         "Set-ItemProperty -Path 'HKLM:\\SYSTEM\\CurrentControlSet\\Control\\Session Manager\\Power' -Name HiberbootEnabled -Value 0\r\n";
                var klasor = Path.Combine(AjanAyar.Klasor, "wol");
                Directory.CreateDirectory(klasor);
                var dosya = Path.Combine(klasor, "wol.ps1");
                File.WriteAllText(dosya, ps, new UTF8Encoding(true));
                Calistir("powershell.exe", "-NoProfile -ExecutionPolicy Bypass -File \"" + dosya + "\"", true);
                try { Directory.Delete(klasor, true); } catch { }
                lock (kilit) { ayar.WolHazir = true; ayar.Yaz(); }
                return "tamamlandı (BIOS'ta Wake-on-LAN da açık olmalı)";
            });
        }

        // ---------------------------------------------------------------- Online güncelleme
        DateTime sonrakiGuncellemeKontrolu = DateTime.Now.AddMinutes(10 + new Random().Next(30));
        int guncellemeSuruyor;

        void GuncellemeKontrol()
        {
            if (!ayar.OtoGuncelle || DateTime.Now < sonrakiGuncellemeKontrolu) return;
            if (Interlocked.Exchange(ref guncellemeSuruyor, 1) == 1) return;
            sonrakiGuncellemeKontrolu = DateTime.Now.AddHours(6);
            new Thread(() =>
            {
                try
                {
                    var s = Guncelleme.SonSurum();
                    if (!s.YeniMi) return;
                    var exe = Guncelleme.Indir(s); // özet ve sürüm doğrulanır
                    Gunluk.Yaz("Yeni sürüm bulundu: " + s.Etiket);
                    GuncellemeBaslat(exe);
                }
                catch (Exception e) { Gunluk.Yaz("Güncelleme denetimi: " + e.Message); }
                finally { Interlocked.Exchange(ref guncellemeSuruyor, 0); }
            }) { IsBackground = true }.Start();
        }

        static void GuncellemeBaslat(byte[] exe)
        {
            var klasor = Path.Combine(AjanAyar.Klasor, "guncelle");
            Directory.CreateDirectory(klasor);
            var yeni = Path.Combine(klasor, "Sem.exe");
            File.WriteAllBytes(yeni, exe);
            Process.Start(new ProcessStartInfo(yeni, "/guncelle") { UseShellExecute = false, CreateNoWindow = true });
        }

        DateTime? OtomatikHedef(DateTime simdi)
        {
            if (ayar.BugunKapatmaTarihi == simdi.ToString("yyyy-MM-dd")) return null;
            var baz = ayar.BugunkuKapanma(simdi);
            if (baz == null) return null;
            var hedef = baz.Value.AddMinutes(toplamErteleDk);
            // Bilgisayar kapanma saatinden sonra açıldıysa hemen kapatmayalım, önce uyarı süresi kadar zaman tanıyalım.
            var enErken = baslangic.AddMinutes(ayar.UyariDk + 1);
            return hedef < enErken ? enErken : hedef;
        }

        void KapanmaKontrol()
        {
            lock (kilit)
            {
                if (kapaniyor) return;
                var simdi = DateTime.Now;
                if (gun != simdi.Date) { gun = simdi.Date; erteleSayisi = 0; toplamErteleDk = 0; uyariGosterildi = false; }

                DateTime? hedef = elleHedef ?? OtomatikHedef(simdi);
                if (hedef == null) return;
                bool elle = elleHedef != null;
                var kalan = hedef.Value - simdi;

                if (kalan <= TimeSpan.Zero)
                {
                    kapaniyor = true;
                    Gunluk.Yaz(elle ? "Yöneticiden kapatma" : "Otomatik kapanma");
                    Calistir("shutdown.exe", (elle && elleYenidenBaslat ? "/r" : "/s") + " /f /t 0", false);
                    return;
                }
                if (!uyariGosterildi && kalan <= TimeSpan.FromMinutes(elle ? 60 : ayar.UyariDk))
                {
                    int hak = elle ? 0 : Math.Max(0, ayar.ErteleHak - erteleSayisi);
                    string neden = elle ? (elleYenidenBaslat ? "Yönetici bu bilgisayarı yeniden başlatıyor." : "Yönetici bu bilgisayarı kapatıyor.")
                                        : "Mesai bitti, bilgisayar otomatik olarak kapanacak.";
                    if (EkranaGonder("UYARI", ((int)kalan.TotalSeconds).ToString(), hak.ToString(), ayar.ErteleDk.ToString(), neden))
                        uyariGosterildi = true;
                }
            }
        }

        string KapanmaMetni()
        {
            lock (kilit)
            {
                var simdi = DateTime.Now;
                if (elleHedef != null) return (elleYenidenBaslat ? "Yeniden başlatma " : "Kapatma ") + elleHedef.Value.ToString("HH:mm");
                if (ayar.BugunKapatmaTarihi == simdi.ToString("yyyy-MM-dd")) return "Bugün kapanmayacak";
                var h = OtomatikHedef(simdi);
                if (h == null) return ayar.OtomatikKapanma ? "Bugün kapanma yok" : "Kapanmaz";
                return h.Value.ToString("HH:mm") + (erteleSayisi > 0 ? " (" + erteleSayisi + " kez ertelendi)" : "");
            }
        }

        // ---------------------------------------------------------------- Ekran (kullanıcı oturumu) tarafı
        bool EkranBagli { get { lock (ekranKilit) return ekranYaz != null; } }

        void EkranBekci()
        {
            if (EkranBagli || (DateTime.Now - sonEkranBaslatma).TotalSeconds < 30) return;
            sonEkranBaslatma = DateTime.Now;
            var exe = Process.GetCurrentProcess().MainModule.FileName;
            Yerel.KullanicidaBaslat(exe, "/ekran");
        }

        void EkranDongu()
        {
            while (calisiyor)
            {
                try
                {
                    var g = new PipeSecurity();
                    g.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null), PipeAccessRights.ReadWrite, AccessControlType.Allow));
                    g.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), PipeAccessRights.FullControl, AccessControlType.Allow));
                    using (var p = new NamedPipeServerStream(Sabitler.PipeAdi, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 4096, 4096, g))
                    {
                        p.WaitForConnection();
                        var oku = new StreamReader(p, new UTF8Encoding(false));
                        var yaz = new StreamWriter(p, new UTF8Encoding(false)) { AutoFlush = true };
                        lock (ekranKilit) ekranYaz = yaz;
                        lock (kilit) uyariGosterildi = false; // yeni açılan ekrana bekleyen uyarıyı tekrar göster
                        EkranaGonder("DUVAR", AjanAyar.DuvarKagidi, ayar.DuvarKoru ? "1" : "0");
                        string satir;
                        while ((satir = oku.ReadLine()) != null) EkrandanGelen(EkranHatti.Coz(satir));
                    }
                }
                catch (Exception e) { Gunluk.Yaz("Pipe: " + e.Message); Thread.Sleep(1000); }
                finally { lock (ekranKilit) ekranYaz = null; }
            }
        }

        bool EkranaGonder(params string[] parcalar)
        {
            lock (ekranKilit)
            {
                if (ekranYaz == null) return false;
                try { ekranYaz.WriteLine(EkranHatti.Paketle(parcalar)); return true; }
                catch { ekranYaz = null; return false; }
            }
        }

        void EkrandanGelen(string[] p)
        {
            if (p.Length == 0) return;
            switch (p[0])
            {
                case "MERHABA":
                    // Güncellemeden sonra eski sürümde kalan ekran tarafı kapansın, bekçi yenisini açar.
                    if (p.Length < 2 || p[1] != Sabitler.Surum) { sonEkranBaslatma = DateTime.MinValue; EkranaGonder("CIK"); }
                    break;
                case "RESIM":
                {
                    BekleyenResim b;
                    lock (bekleyenResim) bekleyenResim.TryGetValue(p.Length > 1 ? p[1] : "", out b);
                    if (b != null) { b.Veri = p.Length > 2 ? p[2] : ""; b.Hata = p.Length > 3 ? p[3] : ""; b.Olay.Set(); }
                    break;
                }
                case "OKUNDU":
                    if (p.Length < 2 || p[1] == "") break;
                    lock (okunan)
                    {
                        okunan.Add(p[1] + "|" + DateTime.Now.ToString("s"));
                        if (okunan.Count > 100) okunan.RemoveRange(0, okunan.Count - 100);
                        try { Directory.CreateDirectory(AjanAyar.Klasor); File.WriteAllLines(AjanAyar.OkunanDosya, okunan); } catch { }
                    }
                    break;
                case "ERTELE":
                    lock (kilit)
                    {
                        if (elleHedef == null && erteleSayisi < ayar.ErteleHak)
                        {
                            erteleSayisi++; toplamErteleDk += ayar.ErteleDk; uyariGosterildi = false;
                            Gunluk.Yaz("Kapanma ertelendi (" + erteleSayisi + ")");
                        }
                    }
                    break;
                case "IPTAL":
                    bool dogru = p.Length > 1 && ayar.Anahtar != null && Sifreleme.Esit(Sifreleme.AnahtarUret(p[1]), ayar.Anahtar);
                    if (dogru)
                    {
                        lock (kilit)
                        {
                            ayar.BugunKapatmaTarihi = DateTime.Now.ToString("yyyy-MM-dd"); ayar.Yaz();
                            elleHedef = null; uyariGosterildi = false;
                        }
                        Gunluk.Yaz("Kapanma yönetici şifresiyle bugün için iptal edildi");
                    }
                    EkranaGonder("IPTAL_SONUC", dogru ? "1" : "0");
                    break;
            }
        }

        // ---------------------------------------------------------------- Ağ keşfi (UDP)
        void KesifDongu()
        {
            while (calisiyor)
            {
                try
                {
                    udp = new UdpClient();
                    udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                    udp.Client.Bind(new IPEndPoint(IPAddress.Any, Sabitler.KesifPort));
                    while (calisiyor)
                    {
                        var uzak = new IPEndPoint(IPAddress.Any, 0);
                        var b = udp.Receive(ref uzak);
                        if (Encoding.UTF8.GetString(b) != Sabitler.KesifSoru) continue;
                        var cevap = Encoding.UTF8.GetBytes(Sabitler.KesifCevap + "|" + Environment.MachineName + "|" + Sabitler.Surum);
                        udp.Send(cevap, cevap.Length, uzak);
                    }
                }
                catch (Exception e) { if (calisiyor) { Gunluk.Yaz("UDP: " + e.Message); Thread.Sleep(3000); } }
                finally { try { udp?.Close(); } catch { } }
            }
        }

        // ---------------------------------------------------------------- Komutlar (TCP)
        void TcpDongu()
        {
            while (calisiyor)
            {
                try
                {
                    tcp = new TcpListener(IPAddress.Any, Sabitler.TcpPort);
                    tcp.Start();
                    while (calisiyor)
                    {
                        var c = tcp.AcceptTcpClient();
                        new Thread(() => Karsila(c)) { IsBackground = true }.Start();
                    }
                }
                catch (Exception e) { if (calisiyor) { Gunluk.Yaz("TCP: " + e.Message); Thread.Sleep(3000); } }
            }
        }

        void Karsila(TcpClient c)
        {
            using (c)
            {
                try
                {
                    c.ReceiveTimeout = 120000; c.SendTimeout = 120000;
                    var s = c.GetStream();
                    var nonce = new byte[16];
                    using (var r = RandomNumberGenerator.Create()) r.GetBytes(nonce);
                    s.Write(nonce, 0, 16);
                    var paket = Ag.CerceveOku(s);
                    Mesaj istek;
                    try
                    {
                        if (ayar.Anahtar == null) throw new CryptographicException();
                        istek = Mesaj.Coz(Sifreleme.Ac(ayar.Anahtar, nonce, "Y", paket));
                    }
                    catch (CryptographicException)
                    {
                        Ag.CerceveYaz(s, new byte[] { 0 }); // yönetici "şifre uyuşmuyor" anlasın
                        return;
                    }
                    Mesaj cevap;
                    try { cevap = Isle(istek, c); }
                    catch (Exception e) { cevap = Mesaj.Hata(e.Message); }
                    Ag.CerceveYaz(s, Sifreleme.Kilitle(ayar.Anahtar, nonce, "A", cevap.Paketle()));
                }
                catch (Exception e) { Gunluk.Yaz("Bağlantı: " + e.Message); }
            }
        }

        Mesaj Isle(Mesaj m, TcpClient c)
        {
            switch (m.Komut)
            {
                case "DURUM": return Durum(c);

                case "DUYURU":
                    return EkranaGonder("DUYURU", m["metin"], m["baslik"], m["id"]) ? Mesaj.Basari("Duyuru ekrana geldi") : Mesaj.Hata("Ekranda açık oturum yok");

                case "EKRAN_RESMI": return EkranResmi(m["genislik"]);

                case "SABAH_PLANI":
                    lock (kilit)
                    {
                        ayar.SabahSaat = m["saat"]; ayar.SabahGunler = m["gunler"]; ayar.SabahMacler = m["macler"];
                        ayar.Yaz();
                    }
                    if (ayar.SabahSaat != "" && !ayar.WolHazir) WolHazirla();
                    return Mesaj.Basari(ayar.SabahSaat == "" ? "Sabah açılma kapatıldı" : "Sabah açılma " + ayar.SabahSaat + " olarak kaydedildi");

                case "KILITLE":
                    return EkranaGonder("KILITLE") ? Mesaj.Basari("Kilitlendi") : Mesaj.Hata("Ekranda açık oturum yok");

                case "KAPAT":
                {
                    int gecikme; int.TryParse(m["gecikme"], out gecikme);
                    bool yeniden = m["tip"] == "yeniden";
                    if (gecikme <= 0)
                    {
                        lock (kilit) kapaniyor = true;
                        new Thread(() => { Thread.Sleep(1500); Calistir("shutdown.exe", (yeniden ? "/r" : "/s") + " /f /t 0", false); }) { IsBackground = true }.Start();
                        return Mesaj.Basari(yeniden ? "Yeniden başlatılıyor" : "Kapatılıyor");
                    }
                    lock (kilit) { elleHedef = DateTime.Now.AddSeconds(gecikme); elleYenidenBaslat = yeniden; uyariGosterildi = false; }
                    KapanmaKontrol();
                    return Mesaj.Basari((yeniden ? "Yeniden başlatma " : "Kapatma ") + gecikme + " sn sonra, uyarı gösterildi");
                }

                case "KAPAT_IPTAL":
                    lock (kilit) { elleHedef = null; uyariGosterildi = false; }
                    EkranaGonder("UYARI_KAPAT");
                    return Mesaj.Basari("Bekleyen kapatma iptal edildi");

                case "BUGUN_KAPATMA":
                    lock (kilit)
                    {
                        ayar.BugunKapatmaTarihi = m["deger"] == "1" ? DateTime.Now.ToString("yyyy-MM-dd") : "";
                        ayar.Yaz(); uyariGosterildi = false;
                    }
                    EkranaGonder("UYARI_KAPAT");
                    return Mesaj.Basari(m["deger"] == "1" ? "Bugün otomatik kapanmayacak" : "Bugün normal saatinde kapanacak");

                case "AYAR":
                    lock (kilit)
                    {
                        for (int g = 0; g < 7; g++) if (m.Alan.ContainsKey("kapanma_" + AjanAyar.GunKod[g])) ayar.Kapanma[g] = m["kapanma_" + AjanAyar.GunKod[g]];
                        int v;
                        if (int.TryParse(m["uyari_dk"], out v) && v >= 1 && v <= 30) ayar.UyariDk = v;
                        if (int.TryParse(m["ertele_dk"], out v) && v >= 1 && v <= 120) ayar.ErteleDk = v;
                        if (int.TryParse(m["ertele_hak"], out v) && v >= 0 && v <= 10) ayar.ErteleHak = v;
                        if (m.Alan.ContainsKey("otomatik")) ayar.OtomatikKapanma = m["otomatik"] == "1";
                        if (m.Alan.ContainsKey("oto_guncelle")) ayar.OtoGuncelle = m["oto_guncelle"] == "1";
                        ayar.Yaz(); uyariGosterildi = false;
                    }
                    EkranaGonder("UYARI_KAPAT");
                    return Mesaj.Basari("Kapanma saatleri kaydedildi");

                case "DUVAR":
                {
                    Directory.CreateDirectory(AjanAyar.Klasor);
                    using (var ms = new MemoryStream(m.Veri))
                    using (var img = Image.FromStream(ms))
                        img.Save(AjanAyar.DuvarKagidi, ImageFormat.Png);
                    if (m.Alan.ContainsKey("koru")) { ayar.DuvarKoru = m["koru"] == "1"; lock (kilit) ayar.Yaz(); }
                    EkranaGonder("DUVAR", AjanAyar.DuvarKagidi, ayar.DuvarKoru ? "1" : "0");
                    return Mesaj.Basari("Duvar kâğıdı değişti");
                }

                case "DOSYA_GONDER":
                {
                    var klasor = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory), "Sem Gelen Dosyalar");
                    Directory.CreateDirectory(klasor);
                    var ad = Path.GetFileName(m["ad"]);
                    if (string.IsNullOrWhiteSpace(ad)) return Mesaj.Hata("Dosya adı yok");
                    var hedef = Path.Combine(klasor, ad);
                    for (int i = 2; File.Exists(hedef); i++) hedef = Path.Combine(klasor, Path.GetFileNameWithoutExtension(ad) + " (" + i + ")" + Path.GetExtension(ad));
                    File.WriteAllBytes(hedef, m.Veri);
                    EkranaGonder("DOSYA", Path.GetFileName(hedef));
                    return Mesaj.Basari("Dosya masaüstündeki 'Sem Gelen Dosyalar' klasörüne kondu");
                }

                case "DOSYA_LISTE": return DosyaListe(m["yol"]);

                case "DOSYA_AL":
                {
                    var f = new FileInfo(m["yol"]);
                    if (!f.Exists) return Mesaj.Hata("Dosya bulunamadı");
                    if (f.Length > Sabitler.MaxCerceve - 1024 * 1024) return Mesaj.Hata("Dosya çok büyük (en fazla 400 MB)");
                    var r = Mesaj.Basari(); r["ad"] = f.Name;
                    using (var fs = new FileStream(f.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                        r.Veri = Ag.TamOku(fs, (int)f.Length);
                    return r;
                }

                case "PROGRAM_KUR":
                {
                    var klasor = Path.Combine(AjanAyar.Klasor, "kurulum");
                    Directory.CreateDirectory(klasor);
                    var dosya = Path.Combine(klasor, Path.GetFileName(m["ad"]));
                    File.WriteAllBytes(dosya, m.Veri);
                    var arg = m["arguman"];
                    Arkaplan("Program kurulumu: " + Path.GetFileName(dosya), () =>
                    {
                        int kod = dosya.EndsWith(".msi", StringComparison.OrdinalIgnoreCase)
                            ? Calistir("msiexec.exe", "/i \"" + dosya + "\" /qn /norestart " + arg, true)
                            : Calistir(dosya, arg, true);
                        try { File.Delete(dosya); } catch { }
                        return kod == 0 || kod == 3010 ? "tamamlandı" : "bitti, çıkış kodu " + kod;
                    });
                    return Mesaj.Basari("Kurulum başladı, sonucu Etkinlik sayfasında görünecek");
                }

                case "YAZICI_KUR":
                {
                    var klasor = Path.Combine(AjanAyar.Klasor, "yazici");
                    try { if (Directory.Exists(klasor)) Directory.Delete(klasor, true); } catch { }
                    Directory.CreateDirectory(klasor);
                    using (var ms = new MemoryStream(m.Veri))
                    using (var z = new ZipArchive(ms))
                        z.ExtractToDirectory(klasor);
                    string ip = m["ip"], ad = m["ad"], surucu = m["surucu"];
                    Arkaplan("Yazıcı kurulumu", () => YaziciKur(klasor, ip, ad, surucu));
                    return Mesaj.Basari("Yazıcı kurulumu başladı, sonucu Etkinlik sayfasında görünecek");
                }

                case "YAZICI_PAKET": return YaziciPaketi();

                case "YAZICI_ONAR":
                    Arkaplan("Yazıcı onarımı", () =>
                    {
                        Calistir("net.exe", "stop spooler /y", true);
                        int silinen = 0;
                        try
                        {
                            foreach (var f in Directory.GetFiles(Path.Combine(Environment.SystemDirectory, "spool", "PRINTERS")))
                                try { File.Delete(f); silinen++; } catch { }
                        }
                        catch { }
                        Calistir("net.exe", "start spooler", true);
                        return "tamamlandı, " + (silinen / 2) + " takılı iş temizlendi";
                    });
                    return Mesaj.Basari("Yazıcı onarımı başladı");

                case "SIFRE":
                    if (m.Veri.Length != 32) return Mesaj.Hata("Geçersiz anahtar");
                    lock (kilit) { ayar.Anahtar = m.Veri; ayar.Yaz(); }
                    return Mesaj.Basari("Şifre güncellendi");

                case "GUNCELLE":
                    GuncellemeBaslat(m.Veri);
                    return Mesaj.Basari("Güncelleme başladı, birkaç saniye içinde yeni sürümle açılacak");

                default: return Mesaj.Hata("Bilinmeyen komut: " + m.Komut);
            }
        }

        Mesaj Durum(TcpClient c)
        {
            var r = Mesaj.Basari();
            var yerelIp = ((IPEndPoint)c.Client.LocalEndPoint).Address;
            if (yerelIp.IsIPv4MappedToIPv6) yerelIp = yerelIp.MapToIPv4();
            r["ad"] = Environment.MachineName;
            r["ip"] = yerelIp.ToString();
            r["mac"] = MacBul(yerelIp);
            r["surum"] = Sabitler.Surum;
            try { r["acik_sure"] = (Yerel.GetTickCount64() / 1000).ToString(); } catch { r["acik_sure"] = ((uint)Environment.TickCount / 1000).ToString(); }
            r["kapanma"] = KapanmaMetni();
            r["ekran"] = EkranBagli ? "1" : "0";
            r["son"] = sonIslem;
            try { r["yazicilar"] = string.Join(";", System.Drawing.Printing.PrinterSettings.InstalledPrinters.Cast<string>()); } catch { }
            for (int g = 0; g < 7; g++) r["kapanma_" + AjanAyar.GunKod[g]] = ayar.Kapanma[g];
            r["uyari_dk"] = ayar.UyariDk.ToString(); r["ertele_dk"] = ayar.ErteleDk.ToString(); r["ertele_hak"] = ayar.ErteleHak.ToString();
            r["otomatik"] = ayar.OtomatikKapanma ? "1" : "0";
            r["oto_guncelle"] = ayar.OtoGuncelle ? "1" : "0";
            r["sabah_saat"] = ayar.SabahSaat; r["sabah_gunler"] = ayar.SabahGunler;
            lock (okunan) r["okunan"] = string.Join(";", okunan.Skip(Math.Max(0, okunan.Count - 50)));
            return r;
        }

        /// <summary>Ekran tarafından ekranın küçük bir JPEG görüntüsünü ister.</summary>
        Mesaj EkranResmi(string genislik)
        {
            var id = Guid.NewGuid().ToString("N");
            var b = new BekleyenResim();
            lock (bekleyenResim) bekleyenResim[id] = b;
            try
            {
                if (!EkranaGonder("RESIM", id, genislik)) return Mesaj.Hata("Ekranda açık oturum yok");
                if (!b.Olay.Wait(6000)) return Mesaj.Hata("Ekran cevap vermedi");
                if (b.Veri == "") return Mesaj.Hata(b.Hata != "" ? b.Hata : "Ekran görüntüsü alınamadı");
                var r = Mesaj.Basari(); r.Veri = Convert.FromBase64String(b.Veri);
                return r;
            }
            finally { lock (bekleyenResim) bekleyenResim.Remove(id); b.Olay.Dispose(); }
        }

        static string MacBul(IPAddress ip)
        {
            try
            {
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                    if (ni.GetIPProperties().UnicastAddresses.Any(u => u.Address.Equals(ip)))
                        return string.Join("-", ni.GetPhysicalAddress().GetAddressBytes().Select(b => b.ToString("X2")));
            }
            catch { }
            return "";
        }

        static Mesaj DosyaListe(string yol)
        {
            var r = Mesaj.Basari();
            var sb = new StringBuilder();
            if (string.IsNullOrEmpty(yol))
            {
                sb.Append("D|" + Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory) + "\n");
                var kullanicilar = Path.Combine(Path.GetPathRoot(Environment.SystemDirectory), "Users");
                if (Directory.Exists(kullanicilar))
                    foreach (var k in Directory.GetDirectories(kullanicilar))
                    {
                        var ad = Path.GetFileName(k);
                        if (ad == "Public" || ad == "Default" || ad == "Default User" || ad == "All Users") continue;
                        foreach (var alt in new[] { "Desktop", "Documents", "Downloads", Path.Combine("OneDrive", "Desktop"), Path.Combine("OneDrive", "Masaüstü") })
                            if (Directory.Exists(Path.Combine(k, alt))) sb.Append("D|" + Path.Combine(k, alt) + "\n");
                    }
                foreach (var d in DriveInfo.GetDrives())
                    if (d.IsReady && (d.DriveType == DriveType.Fixed || d.DriveType == DriveType.Removable)) sb.Append("D|" + d.RootDirectory.FullName + "\n");
            }
            else
            {
                var di = new DirectoryInfo(yol);
                if (!di.Exists) return Mesaj.Hata("Klasör bulunamadı");
                foreach (var d in di.GetDirectories().OrderBy(x => x.Name))
                    if ((d.Attributes & (FileAttributes.Hidden | FileAttributes.System)) == 0) sb.Append("D|" + d.FullName + "\n");
                foreach (var f in di.GetFiles().OrderBy(x => x.Name))
                    if ((f.Attributes & (FileAttributes.Hidden | FileAttributes.System)) == 0) sb.Append("F|" + f.FullName + "|" + f.Length + "|" + f.LastWriteTime.ToString("dd.MM.yyyy HH:mm") + "\n");
            }
            r["liste"] = sb.ToString();
            return r;
        }

        // ---------------------------------------------------------------- Yazıcı
        static string YaziciKur(string klasor, string ip, string ad, string surucu)
        {
            var inf = Directory.GetFiles(klasor, "*.inf", SearchOption.AllDirectories);
            if (inf.Length == 0) return "HATA: pakette .inf dosyası yok";
            var pnp = Path.Combine(Environment.SystemDirectory, "pnputil.exe");
            Calistir(pnp, "/add-driver \"" + Path.Combine(klasor, "*.inf") + "\" /subdirs /install", true);

            if (string.IsNullOrWhiteSpace(surucu)) surucu = SurucuAdiBul(inf);
            if (string.IsNullOrWhiteSpace(surucu)) return "HATA: sürücü adı bulunamadı, yöneticide sürücü adını elle yazın";
            if (string.IsNullOrWhiteSpace(ad)) ad = surucu;
            var port = "IP_" + ip;
            Func<string, string> t = x => x.Replace("'", "''");
            var ps = "$ErrorActionPreference='Stop'\r\n" +
                     $"Add-PrinterDriver -Name '{t(surucu)}'\r\n" +
                     $"if (-not (Get-PrinterPort -Name '{t(port)}' -ErrorAction SilentlyContinue)) {{ Add-PrinterPort -Name '{t(port)}' -PrinterHostAddress '{t(ip)}' }}\r\n" +
                     $"$p = Get-Printer -Name '{t(ad)}' -ErrorAction SilentlyContinue\r\n" +
                     $"if ($p) {{ Set-Printer -Name '{t(ad)}' -DriverName '{t(surucu)}' -PortName '{t(port)}' }} else {{ Add-Printer -Name '{t(ad)}' -DriverName '{t(surucu)}' -PortName '{t(port)}' }}\r\n";
            var dosya = Path.Combine(klasor, "kur.ps1");
            File.WriteAllText(dosya, ps, new UTF8Encoding(true));
            string cikti;
            int kod = Calistir("powershell.exe", "-NoProfile -ExecutionPolicy Bypass -File \"" + dosya + "\"", true, out cikti);
            return kod == 0 ? "tamamlandı: " + ad + " (" + surucu + ")" : "HATA: " + Kisalt(cikti, 300);
        }

        /// <summary>
        /// Bu bilgisayarda kurulu Canon yazıcının sürücü paketini (DriverStore klasörü) zip olarak döndürür;
        /// yazıcının adı, sürücü adı ve IP adresi de eklenir. Yönetim ekranı bunu diğer bilgisayarlara kurar.
        /// </summary>
        static Mesaj YaziciPaketi()
        {
            var klasor = Path.Combine(AjanAyar.Klasor, "yazici_paket");
            try { if (Directory.Exists(klasor)) Directory.Delete(klasor, true); } catch { }
            Directory.CreateDirectory(klasor);
            var surucuKlasor = Path.Combine(klasor, "surucu");
            var bilgi = Path.Combine(klasor, "bilgi.txt");
            Func<string, string> t = x => x.Replace("'", "''");
            var ps = "$ErrorActionPreference='Stop'\r\n" +
                     "$y = Get-Printer | Where-Object { $_.Name -like '*1643*' -or $_.DriverName -like '*1643*' } | Select-Object -First 1\r\n" +
                     "if (-not $y) { $y = Get-Printer | Where-Object { $_.DriverName -like 'Canon*' } | Select-Object -First 1 }\r\n" +
                     "if (-not $y) { throw 'Bu bilgisayarda kurulu Canon yazıcı bulunamadı' }\r\n" +
                     "$d = Get-PrinterDriver -Name $y.DriverName\r\n" +
                     "$kaynak = Split-Path -Parent $d.InfPath\r\n" +
                     $"Copy-Item -Path $kaynak -Destination '{t(surucuKlasor)}' -Recurse\r\n" +
                     "$ip = ''\r\n" +
                     "try { $ip = (Get-PrinterPort -Name $y.PortName).PrinterHostAddress } catch { }\r\n" +
                     "if (-not $ip -and $y.PortName -match '(\\d{1,3}\\.){3}\\d{1,3}') { $ip = $Matches[0] }\r\n" +
                     $"Set-Content -Path '{t(bilgi)}' -Encoding UTF8 -Value @(('ad=' + $y.Name), ('surucu=' + $y.DriverName), ('ip=' + $ip))\r\n";
            var dosya = Path.Combine(klasor, "paket.ps1");
            File.WriteAllText(dosya, ps, new UTF8Encoding(true));
            string cikti;
            int kod = Calistir("powershell.exe", "-NoProfile -ExecutionPolicy Bypass -File \"" + dosya + "\"", true, out cikti);
            if (kod != 0 || !File.Exists(bilgi)) return Mesaj.Hata(Kisalt(cikti, 300));
            var r = Mesaj.Basari();
            foreach (var satir in File.ReadAllLines(bilgi, Encoding.UTF8))
            {
                int i = satir.IndexOf('='); if (i > 0) r[satir.Substring(0, i).Trim().TrimStart('\uFEFF')] = satir.Substring(i + 1).Trim();
            }
            var zip = Path.Combine(klasor, "surucu.zip");
            ZipFile.CreateFromDirectory(surucuKlasor, zip, CompressionLevel.Optimal, false);
            r.Veri = File.ReadAllBytes(zip);
            try { Directory.Delete(klasor, true); } catch { }
            return r;
        }

        /// <summary>INF dosyalarındaki model adlarından en uygununu seçer (önce 1643, sonra Generic Plus UFR II).</summary>
        static string SurucuAdiBul(string[] infler)
        {
            var adlar = new List<string>();
            var re = new Regex("^\\s*\"([^\"]+)\"\\s*=", RegexOptions.Multiline);
            foreach (var f in infler)
            {
                string metin;
                try { metin = File.ReadAllText(f); } catch { continue; }
                foreach (Match mm in re.Matches(metin)) adlar.Add(mm.Groups[1].Value);
            }
            return adlar.FirstOrDefault(a => a.Contains("1643"))
                ?? adlar.FirstOrDefault(a => a.IndexOf("Generic Plus UFR II", StringComparison.OrdinalIgnoreCase) >= 0)
                ?? adlar.FirstOrDefault(a => a.IndexOf("Canon", StringComparison.OrdinalIgnoreCase) >= 0)
                ?? adlar.FirstOrDefault();
        }

        // ---------------------------------------------------------------- Yardımcılar
        void Arkaplan(string ad, Func<string> is_)
        {
            sonIslem = DateTime.Now.ToString("HH:mm") + " " + ad + ": sürüyor";
            new Thread(() =>
            {
                string sonuc;
                try { sonuc = is_(); } catch (Exception e) { sonuc = "HATA: " + e.Message; }
                sonIslem = DateTime.Now.ToString("HH:mm") + " " + ad + ": " + sonuc;
                Gunluk.Yaz(sonIslem);
            }) { IsBackground = true }.Start();
        }

        static int Calistir(string exe, string arg, bool bekle) { string _; return Calistir(exe, arg, bekle, out _); }

        static int Calistir(string exe, string arg, bool bekle, out string cikti)
        {
            cikti = "";
            var psi = new ProcessStartInfo(exe, arg) { UseShellExecute = false, CreateNoWindow = true };
            if (bekle) { psi.RedirectStandardOutput = true; psi.RedirectStandardError = true; }
            using (var p = Process.Start(psi))
            {
                if (!bekle) return 0;
                var hata = p.StandardError.ReadToEndAsync();
                var o = p.StandardOutput.ReadToEnd();
                if (!p.WaitForExit(30 * 60 * 1000)) { try { p.Kill(); } catch { } return -1; }
                cikti = (o + "\n" + hata.Result).Trim();
                return p.ExitCode;
            }
        }

        static string Kisalt(string s, int n) => s == null ? "" : (s.Length <= n ? s : s.Substring(0, n) + "…");
    }

    /// <summary>Hizmet ile ekran tarafı arasındaki satır biçimi: base64 parçalar, sekmeyle ayrılmış.</summary>
    public static class EkranHatti
    {
        public static string Paketle(string[] p) => string.Join("\t", p.Select(x => Convert.ToBase64String(Encoding.UTF8.GetBytes(x ?? ""))));
        public static string[] Coz(string s) => s.Split('\t').Select(x => { try { return Encoding.UTF8.GetString(Convert.FromBase64String(x)); } catch { return ""; } }).ToArray();
    }

    public static class Gunluk
    {
        static readonly object k = new object();
        public static void Yaz(string s)
        {
            try
            {
                lock (k)
                {
                    Directory.CreateDirectory(AjanAyar.Klasor);
                    var f = Path.Combine(AjanAyar.Klasor, "gunluk.txt");
                    if (File.Exists(f) && new FileInfo(f).Length > 1024 * 1024) File.Delete(f);
                    File.AppendAllText(f, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + s + Environment.NewLine);
                }
            }
            catch { }
        }
    }
}
