// Teşhis ve hızlandır: bilgisayarın son 2 günlük performans kaydını ve Windows hata günlüklerini inceleyip
// yavaşlığın muhtemel nedenlerini düz Türkçe yazar; yazılımdan kaynaklananlar tek tıkla çözülür.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Sem.Arayuz;
using Sem.Ortak;
using MK = Sem.Arayuz.MesajKutusu;

namespace Sem.Yonetici
{
    class PerfOlcum
    {
        public DateTime Zaman; public int Cpu, Hiz, Mhz, Sicaklik, Ram, RamMb, Disk; public string UstCpu = "", UstRam = ""; public int UstCpuY, UstRamMb;

        public static List<PerfOlcum> Coz(byte[] veri)
        {
            var l = new List<PerfOlcum>();
            foreach (var p in Izleme.Satirlar(veri))
            {
                if (p.Length < 10) continue;
                DateTime z; if (!Izleme.Zaman(p[0], out z)) continue;
                int I(string s) { int n; return int.TryParse(s, out n) ? n : -1; }
                var o = new PerfOlcum { Zaman = z, Cpu = I(p[1]), Hiz = I(p[2]), Mhz = I(p[3]), Sicaklik = I(p[4]), Ram = I(p[5]), RamMb = I(p[6]), Disk = I(p[7]) };
                int i = p[8].LastIndexOf(':'); if (i > 0) { o.UstCpu = p[8].Substring(0, i); o.UstCpuY = I(p[8].Substring(i + 1)); }
                i = p[9].LastIndexOf(':'); if (i > 0) { o.UstRam = p[9].Substring(0, i); o.UstRamMb = I(p[9].Substring(i + 1)); }
                l.Add(o);
            }
            return l.Where(x => x.Zaman >= DateTime.Now.AddDays(-2)).OrderBy(x => x.Zaman).ToList();
        }
    }

    /// <summary>Çöz ile başlatılmış bir işlemin kaydı (ajanın islemler.txt'si).</summary>
    class IslemKaydi
    {
        public string Id, Islem, Ad, Durum, Asama, Sonuc, Ek; public int Ilerleme = -1; public DateTime Bas, Bit;
        public bool Suruyor => Durum == "s";
        static bool Z(string s, out DateTime d) => DateTime.TryParseExact(s, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out d);
        public static List<IslemKaydi> Coz(byte[] veri)
        {
            var l = new List<IslemKaydi>();
            foreach (var p in Izleme.Satirlar(veri))
            {
                if (p.Length < 10) continue;
                var k = new IslemKaydi { Id = p[0], Islem = p[3], Ad = p[4], Durum = p[5], Asama = p[7], Sonuc = p[8], Ek = p[9].Trim() };
                if (!Z(p[1], out k.Bas)) continue; Z(p[2], out k.Bit);
                int n; if (int.TryParse(p[6], out n)) k.Ilerleme = n;
                l.Add(k);
            }
            return l.OrderByDescending(x => x.Bas).ToList();
        }
        public static string Adi(string islem, string ad)
        {
            switch (islem)
            {
                case "guc": return "Güç ayarı: Yüksek performans";
                case "hizlibaslat": return "Hızlı başlatmayı kapat";
                case "program": return "Programı kapat: " + ad;
                case "temp": return "Geçici dosya temizliği";
                case "disk": return "Disk taraması";
                case "diskonar": return "Açılışta disk onarımı";
                case "sfc": return "Sistem dosyası onarımı";
                case "dism": return "Windows onarımı (DISM)";
                case "defender": return "Virüs tanımı güncelleme ve tarama";
                case "wuonar": return "Windows Update onarımı";
                case "aghafif": return "Ağ onarımı (hafif)";
                case "agtam": return "Ağ ayarlarını sıfırlama";
                case "saat": return "Saat eşitleme";
                case "aygit": return "Aygıtları yeniden tarama";
                case "bellek": return "Bellek testi (sonraki açılış)";
                case "sysmain": return "SysMain'i kapat";
                case "acilis": return "Açılıştan çıkar: " + ad;
                case "checkup": return "Tam check-up";
                case "tamvirus": return "Tam virüs taraması";
                case "sicaklik": return "Sıcaklık ölçümünü aç";
                case "geri": return "Geri al";
                default: return islem;
            }
        }
        public static string Sure(TimeSpan t) => t.TotalMinutes < 1 ? (int)t.TotalSeconds + " sn" : t.TotalHours < 1 ? (int)t.TotalMinutes + " dk" : (int)t.TotalHours + " sa " + t.Minutes + " dk";
    }

    class Bulgu
    {
        public string Baslik, Kanit, Oneri, Coz = "", CozAd = "", CozMetin = "Çöz";
        public int Olasilik; // 2 yüksek, 1 orta, 0 düşük
        public bool Donanim;
    }

    static class Teshis
    {
        static readonly CultureInfo Tr = new CultureInfo("tr-TR");
        static double Ciftle(Mesaj m, string k) => Izleme.Ciftle(m[k]);
        internal static int Tam(Mesaj m, string k) { int n; return int.TryParse(m[k], out n) ? n : 0; }
        static string Ghz(int mhz) => (mhz / 1000.0).ToString("0.0", Tr) + " GHz";
        static int Medyan(IEnumerable<int> l) { var a = l.OrderBy(x => x).ToList(); return a.Count == 0 ? -1 : a[a.Count / 2]; }
        static readonly string[] Windows = { "msmpeng", "tiworker", "trustedinstaller", "wuauserv", "searchindexer", "system", "svchost", "compattelrunner", "mrt", "msiexec" };

        static string OlayAdi(string kod)
        {
            switch (kod)
            {
                case "7": return "bozuk kesim";
                case "51": return "yazma hatası";
                case "153": return "okuma/yazma yeniden denendi";
                case "ntfs55": return "dosya sistemi bozuk";
                case "129": return "disk yanıt vermedi";
                default: return "olay " + kod;
            }
        }

        static void DiskKurallari(Mesaj m, List<Bulgu> b)
        {
            int hata = Tam(m, "olay_disk"), sifir = Tam(m, "olay_disk_sifir"), saat = Tam(m, "disk_saat"), duz = Tam(m, "disk_duzeltilemeyen");
            bool smart = m["smart_uyari"] == "1", saglikKotu = m["disk_tur"].Contains("Uyarı") || m["disk_tur"].Contains("Arızal");
            var tr = new CultureInfo("tr-TR");
            var olaylar = m["olay_disk_detay"].Split(';').Where(x => x.Contains(",")).Select(x =>
            {
                var p = x.Split(','); DateTime z; var zs = DateTime.TryParseExact(p[0], "yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out z) ? z.ToString("d MMM HH:mm", tr) : p[0];
                return zs + " " + OlayAdi(p[1]);
            }).ToList();
            var sayac = new List<string>();
            if (saat > 0) sayac.Add(saat.ToString("N0", tr) + " saat çalışmış (~" + Math.Round(saat / 2500.0, 1).ToString(tr) + " yıl mesai)");
            if (Tam(m, "disk_isi") > 0) sayac.Add("sıcaklık " + m["disk_isi"] + "°");
            if (m["disk_duzeltilemeyen"] != "") sayac.Add("düzeltilemeyen hata " + duz);
            if (Tam(m, "disk_asinma") > 0) sayac.Add("aşınma %" + m["disk_asinma"]);
            string sayacMetin = sayac.Count > 0 ? " Disk bilgisi: " + string.Join(", ", sayac) + "." : "";
            string olayMetin = olaylar.Count > 0 ? " Son kayıtlar: " + string.Join(" · ", olaylar) + "." : "";

            // Kesin arıza belirtisi: SMART, Windows sağlık durumu, düzeltilemeyen hata ya da çok sayıda gerçek disk hatası
            if (smart || saglikKotu || duz > 0 || hata >= 3)
            {
                var neden = new List<string>();
                if (smart) neden.Add("disk kendi arıza uyarısını (SMART) veriyor");
                if (saglikKotu) neden.Add("Windows disk sağlığını \"" + (m["disk_tur"].Contains("Arızal") ? "arızalı" : "uyarı") + "\" gösteriyor");
                if (duz > 0) neden.Add(duz + " düzeltilemeyen okuma/yazma hatası var");
                if (hata >= 3) neden.Add("son 7 günde sistem diskinde " + hata + " hata kaydı var");
                b.Add(new Bulgu { Baslik = "Disk arızalanıyor olabilir", Olasilik = 2, Donanim = true, Kanit = char.ToUpper(neden[0][0]) + string.Join("; ", neden).Substring(1) + "." + olayMetin + sayacMetin,
                    Oneri = "Önemli dosyaların yedeğini hemen alın; disk değişmeli. Tarama sorunlu kesimleri gösterir.", Coz = "disk", CozMetin = "Diski tara" });
            }
            else if (hata > 0)
                b.Add(new Bulgu { Baslik = "Disk " + hata + " kez hata kaydetti", Olasilik = 0, Kanit = "Son 7 günde sistem diskinde " + hata + " hata kaydı var; disk sağlığı ve SMART normal." + olayMetin + sayacMetin,
                    Oneri = "Tek tük kayıt çoğunlukla elektrik kesintisi ya da ani kapanmadan olur. Tekrarlarsa tarama yapın.", Coz = "disk", CozMetin = "Diski tara" });
            if (sifir >= 5)
                b.Add(new Bulgu { Baslik = "Disk zaman zaman yanıt vermiyor", Olasilik = 1, Donanim = true, Kanit = "Son 7 günde disk denetleyicisi " + sifir + " kez yanıt alamayıp diski sıfırladı; bu anlarda bilgisayar birkaç saniye donar." + sayacMetin,
                    Oneri = "SATA kablosunu ve güç kablosunu kontrol edin, mümkünse değiştirin. Disk eskiyse yenilemek gerekir." });
        }

        static List<string[]> Parca(string s) => s.Split(';').Where(x => x.Trim() != "").Select(x => x.Split(':')).ToList();

        /// <summary>Çökme, Windows, güvenlik, ağ, açılış, servis ve bellek kontrolleri.</summary>
        static void Kontroller(Mesaj m, List<Bulgu> b)
        {
            // ---- Çökmeler
            int mavi = Tam(m, "mavi_ekran");
            if (mavi > 0)
            {
                var kod = m["mavi_ekran_kod"]; var anlam = MaviEkran(kod);
                b.Add(new Bulgu { Baslik = "Mavi ekran (" + mavi + " kez, son 30 gün)", Olasilik = 2, Donanim = anlam.donanim,
                    Kanit = "Windows çöküp yeniden başlamış. Son hata kodu " + (kod == "" ? "okunamadı" : kod) + (anlam.metin != "" ? ": " + anlam.metin : "") + ".",
                    Oneri = anlam.donanim ? "Kod donanımı gösteriyor; bellek testi yapın, sürerse servise gösterin." : "Önce sistem dosyası onarımı yapın; sürerse son kurulan program ya da sürücü kaldırılmalı.",
                    Coz = anlam.donanim ? "bellek" : "sfc", CozMetin = anlam.donanim ? "Bellek testini ayarla" : "Sistem dosyalarını onar" });
            }
            int bek = Tam(m, "beklenmedik");
            if (bek >= 2 && Tam(m, "olay_guc41") == 0)
                b.Add(new Bulgu { Baslik = "Düzgün kapanmadı (" + bek + " kez)", Olasilik = 0, Kanit = "Son 7 günde Windows " + bek + " kez kapatılmadan kapanmış.", Oneri = "Fişten çekme ya da elektrik kesintisi ise sorun değil; kendiliğinden oluyorsa güç kaynağına bakılmalı." });
            var cokme = Parca(m["program_cokme"]).Where(x => x.Length >= 3).ToList();
            int topC = cokme.Sum(x => Tam2(x[1]) + Tam2(x[2]));
            if (topC >= 3)
                b.Add(new Bulgu { Baslik = "Programlar çöküyor ya da donuyor", Olasilik = topC >= 8 ? 2 : 1,
                    Kanit = "Son 7 günde: " + string.Join(", ", cokme.Select(x => ProgramAdi(x[0]) + " " + (Tam2(x[1]) > 0 ? Tam2(x[1]) + " kez çöktü" : "") + (Tam2(x[1]) > 0 && Tam2(x[2]) > 0 ? ", " : "") + (Tam2(x[2]) > 0 ? Tam2(x[2]) + " kez yanıt vermedi" : ""))) + ".",
                    Oneri = "Tek program ise onarın ya da yeniden kurun (Program ekle/kaldır). Birçok program birden çöküyorsa sistem dosyası onarımı ve bellek testi yapın.", Coz = "sfc", CozMetin = "Sistem dosyalarını onar" });
            int ekran = Tam(m, "ekran_surucu");
            if (ekran > 0)
                b.Add(new Bulgu { Baslik = "Ekran kartı sürücüsü " + ekran + " kez çöktü", Olasilik = ekran >= 3 ? 2 : 1,
                    Kanit = "Son 7 günde ekran sürücüsü yanıt vermeyip yeniden başlatıldı; bu anlarda ekran kararır ya da donar.",
                    Oneri = "Ekran kartı (işlemcideki) sürücüsünü üreticinin sitesinden güncelleyin; RAM tek kanalsa ve ısı yüksekse bu da tetikler." });

            // ---- Windows
            if (m["yeniden_bekliyor"] == "1")
                b.Add(new Bulgu { Baslik = "Yeniden başlatma bekliyor", Olasilik = 1, Kanit = "Bir Windows güncellemesi yarım kalmış, yeniden başlatılmayı bekliyor. Bu sırada sistem yavaşlayabilir.", Oneri = "Bilgisayarı yeniden başlatın (kapatıp açmak hızlı başlatma açıksa yetmez)." });
            DateTime sg;
            if (DateTime.TryParseExact(m["son_guncelleme"], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out sg) && (DateTime.Now - sg).TotalDays > 45 || Tam(m, "wu_hata") >= 3)
                b.Add(new Bulgu { Baslik = "Windows güncellenemiyor", Olasilik = 1,
                    Kanit = (m["son_guncelleme"] != "" ? "Son güncelleme " + sg.ToString("d MMMM yyyy", Tr) + ". " : "") + (Tam(m, "wu_hata") > 0 ? "Son 14 günde " + m["wu_hata"] + " güncelleme kurulamadı." : ""),
                    Oneri = "Windows Update sıfırlanır (önbellek yenilenir, önce geri yükleme noktası alınır).", Coz = "wuonar", CozMetin = "Windows Update'i onar" });
            var aygit = Parca(m["aygit_sorun"]);
            if (aygit.Count > 0)
                b.Add(new Bulgu { Baslik = aygit.Count + " aygıtta sürücü sorunu", Olasilik = 1, Kanit = string.Join(", ", aygit.Select(x => x[0] + (x.Length > 1 ? " (" + AygitKod(x[1]) + ")" : ""))) + ".",
                    Oneri = "Aygıtlar yeniden taranır; düzelmezse sürücüsü üreticinin sitesinden kurulmalı.", Coz = "aygit", CozMetin = "Aygıtları yeniden tara" });
            DateTime su;
            if (DateTime.TryParseExact(m["saat_utc"], "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out su))
            {
                var fark = Math.Abs((DateTime.UtcNow - su).TotalSeconds);
                if (fark > 120) b.Add(new Bulgu { Baslik = "Saat " + (fark >= 3600 ? (int)(fark / 3600) + " saat" : (int)(fark / 60) + " dakika") + " kaymış", Olasilik = 1, Kanit = "Bu bilgisayarın saati bu bilgisayarla (" + Environment.MachineName + ") uyuşmuyor. Yanlış saat güvenli sitelerde ve e-devlette hata verir.", Oneri = "Saat internetten eşitlenir.", Coz = "saat", CozMetin = "Saati eşitle" });
            }
            if (m["sayfa_dosyasi"] == "0")
                b.Add(new Bulgu { Baslik = "Sanal bellek kapalı", Olasilik = 1, Kanit = "Sayfa dosyası yok; RAM dolunca programlar çöker.", Oneri = "Sistem > Gelişmiş > Performans > Sanal bellek: \"Otomatik yönet\" seçilmeli." });
            if (m["isletim"].Contains("Windows 10"))
                b.Add(new Bulgu { Baslik = "Windows 10 desteği bitti", Olasilik = -1, Kanit = m["isletim"] + ". Microsoft Ekim 2025'ten beri güvenlik güncellemesi vermiyor.", Oneri = "Donanım uygunsa Windows 11'e geçilmeli; değilse antivirüsün güncel kalması daha önemli." });

            // ---- Güvenlik
            var av = Parca(m["antivirus"]).Where(x => x.Length >= 2).ToList();
            var baskaAv = av.Where(x => x[1] == "1" && x[0].IndexOf("Defender", StringComparison.OrdinalIgnoreCase) < 0).ToList();
            if (baskaAv.Count >= 2)
                b.Add(new Bulgu { Baslik = "İki antivirüs birlikte çalışıyor", Olasilik = 2, Kanit = string.Join(" ve ", baskaAv.Select(x => x[0])) + " aynı anda açık; birbirini tarayıp bilgisayarı ciddi yavaşlatır.", Oneri = "Birini Program ekle/kaldır'dan kaldırın." });
            if (m.Alan.ContainsKey("def_etkin") && baskaAv.Count == 0)
            {
                int imza = Tam(m, "def_imza_gun"), tarama = Tam(m, "def_tarama_gun");
                if (m["def_etkin"] != "1" || m["def_anlik"] != "1")
                    b.Add(new Bulgu { Baslik = "Virüs koruması kapalı", Olasilik = 2, Kanit = "Windows Defender " + (m["def_etkin"] != "1" ? "kapalı" : "anlık koruması kapalı") + " ve başka antivirüs yok.", Oneri = "Windows Güvenliği'nden gerçek zamanlı korumayı açın; ardından tarama yapılır.", Coz = "defender", CozMetin = "Güncelle ve tara" });
                else if (imza > 7 || tarama > 30)
                    b.Add(new Bulgu { Baslik = "Virüs koruması güncel değil", Olasilik = 0, Kanit = (imza > 7 ? "Tanımlar " + imza + " günlük. " : "") + (tarama > 30 && tarama < 10000 ? "Son tarama " + tarama + " gün önce." : tarama >= 10000 ? "Hiç tarama yapılmamış." : ""), Oneri = "Tanımlar güncellenir ve hızlı tarama yapılır.", Coz = "defender", CozMetin = "Güncelle ve tara" });
            }
            int tehdit = Tam(m, "tehdit");
            if (tehdit > 0)
                b.Add(new Bulgu { Baslik = "Virüs bulundu (" + tehdit + " kez)", Olasilik = 2, Kanit = "Son 7 günde Defender tehdit yakaladı" + (m["tehdit_ad"] != "" ? ": " + m["tehdit_ad"] : "") + ".", Oneri = "Defender genelde karantinaya alır. Tam tarama bütün diskleri gezer (1-3 saat); hızlı tarama için Bakım araçlarındaki Virüs taraması. USB belleklere dikkat.", Coz = "tamvirus", CozMetin = "Tam tara" });

            // ---- Ağ
            if (m["ag_ad"] != "")
            {
                int hiz = Tam(m, "ag_hiz"), kayip = Tam(m, "ag_kayip"), ms = Tam(m, "ag_ms"), dns = Tam(m, "dns_ms"), sinyal = Tam(m, "wifi_sinyal");
                bool kablosuz = m["ag_kablosuz"] == "1";
                if (!kablosuz && hiz > 0 && hiz < 1000)
                    b.Add(new Bulgu { Baslik = "Ağ kablosu " + hiz + " Mbps'de çalışıyor", Olasilik = hiz <= 10 ? 2 : 1, Donanim = true, Kanit = "Kart ve switch 1000 Mbps destekliyorsa bu, kablonun ya da fişin bozuk olduğunu gösterir (8 telden 4'ü çalışıyor). Dosya paylaşımı ve ekran izleme yavaşlar.", Oneri = "Ağ kablosunu değiştirin ya da duvardaki prizi kontrol edin." });
                if (kayip >= 10 || ms >= 50)
                    b.Add(new Bulgu { Baslik = "Modeme bağlantı sorunlu", Olasilik = kayip >= 30 ? 2 : 1, Donanim = !kablosuz, Kanit = "Modeme (" + m["ag_gecit"] + ") 10 ping: kayıp %" + kayip + (ms >= 0 ? ", ortalama " + ms + " ms" : "") + ". Normalde %0 ve 1-2 ms olmalı." + (kablosuz && sinyal > 0 ? " Wi-Fi sinyali %" + sinyal + "." : ""),
                        Oneri = kablosuz ? "Erişim noktasına yakın olun ya da kablo kullanın." : "Kablo ve switch bağlantısını kontrol edin. Hafif ağ onarımı denenebilir.", Coz = "aghafif", CozMetin = "Ağı onar (hafif)" });
                else if (kablosuz && sinyal > 0 && sinyal < 50)
                    b.Add(new Bulgu { Baslik = "Wi-Fi sinyali zayıf (%" + sinyal + ")", Olasilik = 1, Donanim = true, Kanit = "Zayıf sinyal internetin kesilip yavaşlamasına yol açar.", Oneri = "Erişim noktasına yakın olun ya da kablo kullanın." });
                if (dns < 0 || dns > 1500)
                    b.Add(new Bulgu { Baslik = dns < 0 ? "İsim çözümleme (DNS) çalışmıyor" : "İsim çözümleme (DNS) yavaş", Olasilik = dns < 0 ? 2 : 1, Kanit = dns < 0 ? "Siteler açılmaz ama ağ bağlı görünür." : "Bir site adı " + dns + " ms'de çözüldü; siteler geç açılır.", Oneri = "DNS önbelleği temizlenir ve IP yenilenir; düzelmezse tam ağ sıfırlama.", Coz = "aghafif", CozMetin = "Ağı onar (hafif)" });
                int cakisma = Tam(m, "ip_cakisma");
                if (cakisma > 0)
                    b.Add(new Bulgu { Baslik = "IP adresi çakışması", Olasilik = 2, Kanit = "Son 7 günde başka bir cihaz bu bilgisayarla aynı IP'yi kullandı (" + cakisma + " kez); bağlantı kopar.", Oneri = "IP yenilenir. Elle IP verilmiş bir cihaz varsa modemin dağıttığı aralığın (100-200) dışına alınmalı.", Coz = "aghafif", CozMetin = "IP'yi yenile" });
            }

            // ---- Açılış
            int acilisAdet = Tam(m, "acilis_adet"), acilisSn = Tam(m, "acilis_sn");
            if (acilisAdet >= 8 || acilisSn >= 120)
                b.Add(new Bulgu { Baslik = "Açılış kalabalık" + (acilisSn > 0 ? " (" + acilisSn + " sn)" : ""), Olasilik = acilisSn >= 180 || acilisAdet >= 12 ? 1 : 0,
                    Kanit = acilisAdet + " program Windows'la birlikte başlıyor" + (acilisSn > 0 ? ", açılış ortalama " + acilisSn + " saniye sürüyor" : "") + ". Aşağıdaki Açılış programları bölümünden kapatılabilir.",
                    Oneri = "Gereksizleri açılıştan çıkarın; program kaldırılmaz, istenirse geri alınır." });

            // ---- Klasik disk ve arka plan servisleri
            if (m["disk_tur"].Contains("HDD") && (m["sysmain"] == "2" || m["sysmain"] == "3"))
                b.Add(new Bulgu { Baslik = "SysMain klasik diski meşgul ediyor", Olasilik = 1, Kanit = "SysMain (Superfetch) programları önceden belleğe yüklemek için diski sürekli okur; HDD'de açılıştan sonra uzun süre %100 disk ve donmaya yol açar.", Oneri = "SysMain kapatılır; istenirse geri alınır.", Coz = "sysmain", CozMetin = "SysMain'i kapat" });

            // ---- Sıcaklık ve fanlar (LibreHardwareMonitor)
            if (m["sicaklik_surucu"] == "0")
                b.Add(new Bulgu { Baslik = "İşlemci sıcaklığı ölçülmüyor", Olasilik = -1, Kanit = "Windows bu bilgisayarın işlemci sıcaklığını ve fan devrini vermiyor; ısınma ancak dolaylı anlaşılıyor.", Oneri = "Açık kaynaklı LibreHardwareMonitor'un imzalı sürücüsü (PawnIO) kurulur; sonra sıcaklık 5 dakikada bir kaydedilir. Geri al ile kaldırılır.", Coz = "sicaklik", CozMetin = "Sıcaklık ölçümünü aç" });
            int cpuT = Tam(m, "sn_cpu_sicaklik");
            var fanlar = Parca(m["sn_fanlar"]).Where(x => x.Length >= 2).Select(x => (ad: x[0], rpm: Tam2(x[1]))).ToList();
            if (m["sn_hata"] != "" && m["sicaklik_surucu"] == "1")
                b.Add(new Bulgu { Baslik = "Sıcaklık okunamadı", Olasilik = -1, Kanit = "Sürücü kurulu ama okuma hata verdi: " + m["sn_hata"], Oneri = "Bilgisayarı yeniden başlatıp tekrar bakın; düzelmezse Geri al ile kaldırın." });
            if (fanlar.Count > 0 && fanlar.All(f => f.rpm == 0))
                b.Add(new Bulgu { Baslik = "Fan dönmüyor görünüyor", Olasilik = cpuT >= 75 ? 2 : 1, Donanim = true, Kanit = "Anakart " + fanlar.Count + " fan başlığı bildiriyor, hepsi 0 devir" + (cpuT > 0 ? "; işlemci " + cpuT + "°" : "") + ". (Bazı anakartlar fan devrini hiç bildirmez.)", Oneri = "Kasayı açıp işlemci fanının döndüğüne bakın; tozluysa temizleyin, dönmüyorsa değiştirin." });
            else if (cpuT >= 80)
                b.Add(new Bulgu { Baslik = "İşlemci şu an " + cpuT + "°", Olasilik = cpuT >= 90 ? 2 : 1, Donanim = true, Kanit = "Ryzen işlemciler 95°'de kendini yavaşlatır; boşta 40-55°, yük altında 60-80° normaldir." + (fanlar.Count > 0 ? " Fanlar: " + string.Join(", ", fanlar.Select(f => f.ad + " " + f.rpm + " rpm")) + "." : ""), Oneri = "Fan ve soğutucunun tozunu alın, termal macunu yenileyin." });

            // ---- Bellek testi
            var bt = m["bellek_test"].Split(',');
            if (bt.Length == 2 && bt[1] == "hata")
                b.Add(new Bulgu { Baslik = "Bellek testi hata buldu", Olasilik = 2, Donanim = true, Kanit = bt[0] + " tarihli Windows bellek testi RAM'de hata buldu.", Oneri = "RAM tarağı değişmeli. Birden fazla tarak varsa tek tek takıp test ederek bozuğu bulun." });
            else if (m["bellek_bekliyor"] == "1")
                b.Add(new Bulgu { Baslik = "Bellek testi sonraki açılışta", Olasilik = -1, Kanit = "Bilgisayar yeniden başlatılınca 15-20 dakikalık bellek testi yapılacak.", Oneri = "Sonuç burada görünür." });
        }

        static int Tam2(string s) { int n; return int.TryParse(s, out n) ? n : 0; }

        static string ProgramAdi(string exe)
        {
            var e = exe.ToLowerInvariant().Replace(".exe", "");
            switch (e)
            {
                case "winword": return "Word"; case "excel": return "Excel"; case "powerpnt": return "PowerPoint"; case "outlook": return "Outlook";
                case "chrome": return "Chrome"; case "msedge": return "Edge"; case "firefox": return "Firefox"; case "explorer": return "Dosya Gezgini";
                case "acrord32": case "acrobat": return "Adobe Reader"; case "wps": case "et": case "wpp": return "WPS Office";
                default: return exe;
            }
        }

        static string AygitKod(string k)
        {
            switch (k) { case "28": return "sürücü yok"; case "10": return "başlatılamıyor"; case "43": return "aygıt hata bildirdi"; case "31": return "sürücü yüklenemedi"; case "1": return "yapılandırılmamış"; default: return "kod " + k; }
        }

        static (string metin, bool donanim) MaviEkran(string kod)
        {
            var k = kod.ToLowerInvariant().Replace("0x", "").TrimStart('0');
            switch (k)
            {
                case "1a": return ("bellek yönetimi (çoğunlukla RAM arızası)", true);
                case "50": return ("geçersiz bellek erişimi (RAM ya da sürücü)", true);
                case "124": return ("donanım hatası (işlemci, ısı ya da güç)", true);
                case "9f": return ("sürücü güç durumu hatası (uykuda takılma)", false);
                case "a": case "d1": return ("sürücü hatası", false);
                case "7e": case "1e": case "3b": return ("sistem ya da sürücü hatası", false);
                case "ef": return ("kritik Windows işlemi kapandı (bozuk sistem dosyası ya da disk)", false);
                case "7a": case "77": return ("disk okunamadı", true);
                case "133": return ("işlem zaman aşımı (çoğunlukla sürücü ya da disk)", false);
                case "116": case "117": return ("ekran sürücüsü yanıt vermedi", false);
                default: return ("", false);
            }
        }

        public static List<Bulgu> Incele(Mesaj m, List<PerfOlcum> l, string ikizAd, Mesaj ikiz, List<PerfOlcum> ikizL)
        {
            var b = new List<Bulgu>();
            int maxMhz = Tam(m, "max_mhz");
            // ---- İşlemci hızı (yük varken)
            var yuk = l.Where(x => x.Cpu >= 25 && x.Hiz >= 0).ToList();
            var yavas = yuk.Where(x => x.Hiz < 55).ToList();
            int kisma = Tam(m, "olay_kisma"), sicOlay = Tam(m, "olay_sicaklik");
            int maxSic = l.Count > 0 ? l.Max(x => x.Sicaklik) : -1;
            string ikizMetin = "";
            if (ikiz != null && ikizL != null)
            {
                var iy = ikizL.Where(x => x.Cpu >= 25 && x.Hiz >= 0).ToList();
                if (iy.Count >= 3) ikizMetin = " Aynı işlemcili " + ikizAd + " yük altında " + (Tam(ikiz, "max_mhz") > 0 ? Ghz(Tam(ikiz, "max_mhz") * Medyan(iy.Select(x => x.Hiz)) / 100) : "%" + Medyan(iy.Select(x => x.Hiz))) + " hızda çalışıyor.";
            }
            if (yuk.Count >= 3 && yavas.Count >= Math.Max(2, yuk.Count * 0.3) || kisma > 0)
            {
                int med = Medyan(yavas.Count > 0 ? yavas.Select(x => x.Hiz) : yuk.Select(x => x.Hiz));
                string hizMetin = maxMhz > 0 && med >= 0 ? Ghz(maxMhz * med / 100) + " (olması gereken " + Ghz(maxMhz) + ")" : "%" + med;
                var ilk = yavas.FirstOrDefault();
                bool isi = maxSic >= 85 || sicOlay > 0 || kisma > 0;
                var kanit = new StringBuilder();
                if (yavas.Count > 0) kanit.Append("İş yaparken " + yuk.Count + " ölçümün " + yavas.Count + "'inde işlemci " + hizMetin + " hızda kalmış.");
                if (ilk != null) kanit.Append(" İlk kez " + ilk.Zaman.ToString("d MMM HH:mm", Tr) + "'de.");
                if (maxSic > 0) kanit.Append(" En yüksek sıcaklık " + maxSic + "°.");
                if (kisma > 0) kanit.Append(" Windows son 7 günde " + kisma + " kez \"işlemci hızı donanım tarafından sınırlandı\" kaydı tuttu.");
                kanit.Append(ikizMetin);
                if (isi)
                    b.Add(new Bulgu
                    {
                        Baslik = "İşlemci ısınıp kendini yavaşlatıyor", Olasilik = 2, Donanim = true, Kanit = kanit.ToString().Trim(),
                        Oneri = "İşlemci fanının ve soğutucunun tozunu alın, termal macunu yenileyin, kasanın önü ve arkası açık dursun. Bu bir donanım işi; yazılımla tam çözülmez.",
                    });
                else
                    b.Add(new Bulgu
                    {
                        Baslik = "İşlemci düşük hızda takılı kalıyor", Olasilik = 2, Kanit = kanit.ToString().Trim(),
                        Oneri = "Güç ayarı Yüksek performans yapılır, uyku kapatılır. Sürerse BIOS'taki güç tasarrufu ayarlarına bakmak gerekir.",
                        Coz = "guc", CozMetin = "Yüksek performans yap",
                    });
            }
            // ---- Güç ayarı
            var guc = m["guc"];
            bool yuksek = guc.Contains("8c5e7fda") || guc.IndexOf("performans", StringComparison.OrdinalIgnoreCase) >= 0 || guc.IndexOf("performance", StringComparison.OrdinalIgnoreCase) >= 0;
            if (guc != "" && !yuksek && !b.Any(x => x.Coz == "guc"))
            {
                var ad = guc.Contains("(") ? guc.Substring(guc.LastIndexOf('(') + 1).TrimEnd(')', ' ') : guc;
                b.Add(new Bulgu { Baslik = "Güç ayarı \"" + ad + "\"", Olasilik = 1, Kanit = "Bilgisayar prizde olduğu halde işlemci tasarruf için yavaşlayabilir.", Oneri = "Yüksek performans yapılır ve uyku kapatılır.", Coz = "guc", CozMetin = "Yüksek performans yap" });
            }
            // ---- Hızlı başlatma ve açık kalma
            DateTime acilis; bool acilisVar = Izleme.Zaman(m["acilis"], out acilis);
            double gun = acilisVar ? (DateTime.Now - acilis).TotalDays : 0;
            if (m["hizli_baslatma"] == "1")
                b.Add(new Bulgu
                {
                    Baslik = "Hızlı başlatma açık", Olasilik = gun >= 2 ? 1 : 0,
                    Kanit = "Kapatıp açınca Windows tam sıfırlanmıyor" + (acilisVar ? "; bu bilgisayar gerçekte " + (gun < 1 ? "bugün" : (int)gun + " gündür") + " yeniden başlamadı." : ".") + " Elektriği çekince düzelmesi bununla da ilgili olabilir.",
                    Oneri = "Hızlı başlatma kapatılır; her kapanışta sistem tam sıfırlanır (açılış birkaç saniye uzar).", Coz = "hizlibaslat", CozMetin = "Hızlı başlatmayı kapat",
                });
            else if (gun >= 7)
                b.Add(new Bulgu { Baslik = (int)gun + " gündür yeniden başlamadı", Olasilik = 1, Kanit = "Uzun süre açık kalan Windows'ta bellek ve sürücü sorunları birikir.", Oneri = "Bilgisayarı yeniden başlatın." });
            // ---- RAM
            int tarak = Tam(m, "ram_tarak"); double ramGb = Ciftle(m, "ram_gb"), gorunen = Ciftle(m, "ram_gorunen_gb");
            if (tarak == 1)
                b.Add(new Bulgu
                {
                    Baslik = "RAM tek tarak (tek kanal)", Olasilik = 1, Donanim = true,
                    Kanit = "Takılı: " + m["ram_detay"] + ". " + (m["islemci"].Contains("Radeon") || m["islemci"].Contains("Vega") || m["islemci"].Contains("G ") ? "Bu işlemcide ekran kartı da RAM'i kullandığı için tek kanal hem bilgisayarı hem görüntüyü belirgin yavaşlatır." : "Tek kanal bellek hızını yarıya düşürür."),
                    Oneri = "Aynı hızda ikinci bir RAM tarağı takın (ör. 2 × 8 GB).",
                });
            if (ramGb > 0 && ramGb < 7.5)
                b.Add(new Bulgu { Baslik = "RAM az: " + ramGb.ToString("0.#", Tr) + " GB", Olasilik = 1, Donanim = true, Kanit = "Windows 10/11 ve tarayıcı için 8 GB altı yetersiz kalır.", Oneri = "RAM'i en az 8 GB, tercihen 16 GB yapın." });
            if (ramGb > 0 && gorunen > 0 && ramGb - gorunen >= 0.9)
                b.Add(new Bulgu { Baslik = (ramGb - gorunen).ToString("0.#", Tr) + " GB RAM ekran kartına ayrılmış", Olasilik = 0, Kanit = ramGb.ToString("0.#", Tr) + " GB'ın " + gorunen.ToString("0.#", Tr) + " GB'ı Windows'a kalıyor.", Oneri = "BIOS'ta 'UMA Frame Buffer' 512 MB yapılabilir (ofis işinde yeterli)." });
            var dolu = l.Where(x => x.Ram >= 90).ToList();
            if (l.Count >= 6 && dolu.Count >= l.Count * 0.2)
            {
                var ust = dolu.GroupBy(x => x.UstRam).OrderByDescending(g => g.Count()).First();
                bool win = Windows.Contains(ust.Key.ToLowerInvariant());
                b.Add(new Bulgu
                {
                    Baslik = "RAM sık sık doluyor", Olasilik = 2, Kanit = l.Count + " ölçümün " + dolu.Count + "'inde RAM %90'ın üstünde. En çok kullanan: " + ust.Key + " (" + Izleme.Sayi(ust.Max(x => x.UstRamMb)) + " MB).",
                    Oneri = win ? "Windows'un kendi işlemi; yeniden başlatmak geçici çözer." : ust.Key + " kapatılır ve açılışta kendiliğinden başlamaz.", Coz = win ? "" : "program", CozAd = ust.Key, CozMetin = ust.Key + "'i kapat",
                });
            }
            // ---- Zamanla şişen program
            foreach (var g in l.Where(x => x.UstRam != "").GroupBy(x => x.UstRam))
            {
                var s = g.OrderBy(x => x.Zaman).ToList();
                if (s.Count < 4) continue;
                int ilk = s.Take(2).Min(x => x.UstRamMb), en = s.Max(x => x.UstRamMb);
                if (en - ilk >= 800 && en >= ilk * 2 && !b.Any(x => x.CozAd == g.Key) && !Windows.Contains(g.Key.ToLowerInvariant()))
                    b.Add(new Bulgu { Baslik = g.Key + " zamanla şişiyor", Olasilik = 1, Kanit = "Bellek kullanımı " + Izleme.Sayi(ilk) + " MB'tan " + Izleme.Sayi(en) + " MB'a çıkmış (bellek sızıntısı).", Oneri = g.Key + " kapatılır; sık oluyorsa güncellenmeli ya da kaldırılmalı.", Coz = "program", CozAd = g.Key, CozMetin = g.Key + "'i kapat" });
            }
            // ---- Sürekli işlemci yiyen program
            if (l.Count >= 6)
                foreach (var g in l.Where(x => x.UstCpuY >= 40 && x.UstCpu != "").GroupBy(x => x.UstCpu).Where(g => g.Count() >= l.Count * 0.3))
                {
                    var ad = g.Key; bool win = Windows.Contains(ad.ToLowerInvariant());
                    b.Add(new Bulgu
                    {
                        Baslik = ad + " sürekli işlemci kullanıyor", Olasilik = win ? 1 : 2, Kanit = l.Count + " ölçümün " + g.Count() + "'inde en çok işlemciyi o kullanmış (ortalama %" + (int)g.Average(x => x.UstCpuY) + ").",
                        Oneri = win ? (ad.ToLowerInvariant() == "msmpeng" ? "Windows Defender taraması; genelde kendiliğinden biter." : "Windows güncellemesi ya da arama dizini; bitmesini bekleyin, sürerse yeniden başlatın.") : ad + " kapatılır ve açılışta kendiliğinden başlamaz.",
                        Coz = win ? "" : "program", CozAd = ad, CozMetin = ad + "'i kapat",
                    });
                }
            // ---- Disk
            DiskKurallari(m, b);
            var mesgul = l.Where(x => x.Disk >= 80).ToList();
            if (l.Count >= 6 && mesgul.Count >= l.Count * 0.25)
            {
                bool hdd = m["disk_tur"].Contains("HDD");
                b.Add(new Bulgu { Baslik = "Disk sürekli meşgul", Olasilik = hdd ? 2 : 1, Donanim = hdd, Kanit = l.Count + " ölçümün " + mesgul.Count + "'inde disk %80'in üstünde dolu çalışıyor. Disk: " + m["disk_tur"] + ".", Oneri = hdd ? "Klasik disk (HDD) en büyük yavaşlık nedenidir; SSD'ye geçmek en büyük farkı yapar." : "Geçici dosyalar temizlenir." , Coz = hdd ? "" : "temp", CozMetin = "Geçici dosyaları temizle" });
            }
            double bos = Ciftle(m, "disk_bos_gb"); int bosY = Tam(m, "disk_yuzde");
            if (m["disk_bos_gb"] != "" && (bos < 15 || bosY < 10))
                b.Add(new Bulgu { Baslik = "C: diskinde yer az (" + bos.ToString("0.#", Tr) + " GB)", Olasilik = 1, Kanit = "Windows'un rahat çalışması için en az 15-20 GB boş yer gerekir.", Oneri = "Geçici dosyalar temizlenir; masaüstü düzeni ve gereksiz programları kaldırmak da yer açar.", Coz = "temp", CozMetin = "Geçici dosyaları temizle" });
            // ---- Donanım hataları
            int whea = Tam(m, "olay_whea"), g41 = Tam(m, "olay_guc41");
            if (whea > 0) b.Add(new Bulgu { Baslik = "Windows donanım hatası kaydetti", Olasilik = 2, Donanim = true, Kanit = "Son 7 günde " + whea + " donanım hatası (WHEA). Genelde RAM, işlemci ya da anakarttan gelir.", Oneri = "RAM'leri çıkarıp takın; sürerse teknik servise gösterin." });
            if (g41 > 0) b.Add(new Bulgu { Baslik = "Beklenmedik kapanma (" + g41 + " kez)", Olasilik = g41 >= 3 ? 1 : 0, Donanim = true, Kanit = "Son 7 günde bilgisayar " + g41 + " kez düzgün kapanmadan kapanmış (elektrik kesintisi, fişten çekme ya da güç kaynağı).", Oneri = "Elektrik çekmeden kapanıyorsa güç kaynağı kontrol edilmeli." });
            Kontroller(m, b);
            // ---- Genel bakım
            if (!b.Any(x => x.Coz == "temp"))
                b.Add(new Bulgu { Baslik = "Genel bakım", Olasilik = -1, Kanit = "Windows'un ve programların biriktirdiği geçici dosyalar.", Oneri = "2 günden eski geçici dosyalar silinir.", Coz = "temp", CozMetin = "Geçici dosyaları temizle" });
            return b.OrderByDescending(x => x.Olasilik).ToList();
        }
    }

    /// <summary>Son 2 günün çizgi grafiği: işlemci hızı, işlemci yükü, RAM.</summary>
    class PerfGrafik : Control
    {
        public List<PerfOlcum> Veri = new List<PerfOlcum>();
        public PerfGrafik() { SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true); BackColor = Renk.Yuzey; }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; Tema.Kaliteli(g);
            g.Clear(Renk.Yuzey);
            using (var pen = new Pen(Renk.Cizgi)) g.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
            var seriler = new (string ad, Color renk, Func<PerfOlcum, int> d)[] { ("İşlemci hızı", Renk.Mavi, x => x.Hiz), ("İşlemci yükü", Renk.Yazi3, x => x.Cpu), ("RAM", Renk.Pembe, x => x.Ram) };
            float lx = Tema.S(12);
            using (var f = Tema.Yazi(8.5f))
                foreach (var s in seriler)
                {
                    using (var b = new SolidBrush(s.renk)) g.FillEllipse(b, lx, Tema.S(10), Tema.S(8), Tema.S(8));
                    using (var b = new SolidBrush(Renk.Yazi2)) g.DrawString(s.ad, f, b, lx + Tema.S(11), Tema.S(5));
                    lx += g.MeasureString(s.ad, f).Width + Tema.S(26);
                }
            var r = new RectangleF(Tema.S(12), Tema.S(28), Width - Tema.S(24), Height - Tema.S(46));
            using (var pen = new Pen(Renk.Cizgi) { DashStyle = DashStyle.Dot }) foreach (var y in new[] { 0, 50, 100 }) g.DrawLine(pen, r.Left, r.Bottom - r.Height * y / 100f, r.Right, r.Bottom - r.Height * y / 100f);
            if (Veri.Count < 2)
            {
                Tema.YaziCiz(g, "Ölçümler birikiyor: her 5 dakikada bir eklenir.", Tema.Yazi(9f), Renk.Yazi3, r);
                return;
            }
            DateTime t0 = Veri.First().Zaman, t1 = Veri.Last().Zaman; double toplam = Math.Max(1, (t1 - t0).TotalMinutes);
            foreach (var s in seriler)
            {
                var noktalar = new List<PointF>();
                using (var pen = new Pen(s.renk, Tema.S(1.6f)))
                {
                    PerfOlcum onceki = null;
                    foreach (var o in Veri)
                    {
                        int v = s.d(o); if (v < 0) continue;
                        // 20 dakikadan uzun boşluk (bilgisayar kapalı) çizgiyi böler
                        if (onceki != null && (o.Zaman - onceki.Zaman).TotalMinutes > 20 && noktalar.Count > 0) { if (noktalar.Count > 1) g.DrawLines(pen, noktalar.ToArray()); noktalar.Clear(); }
                        noktalar.Add(new PointF(r.Left + (float)((o.Zaman - t0).TotalMinutes / toplam) * r.Width, r.Bottom - r.Height * Math.Min(100, v) / 100f));
                        onceki = o;
                    }
                    if (noktalar.Count > 1) g.DrawLines(pen, noktalar.ToArray());
                }
            }
            using (var f = Tema.Yazi(8f)) using (var b = new SolidBrush(Renk.Yazi3))
            {
                g.DrawString(t0.ToString("d MMM HH:mm", new CultureInfo("tr-TR")), f, b, r.Left, r.Bottom + Tema.S(2));
                var son = t1.ToString("d MMM HH:mm", new CultureInfo("tr-TR")); g.DrawString(son, f, b, r.Right - g.MeasureString(son, f).Width, r.Bottom + Tema.S(2));
            }
        }
    }

    class TeshisForm : ModernForm
    {
        readonly Pc pc; readonly List<Pc> digerleri;
        readonly SatirListesi liste = new SatirListesi();
        readonly PerfGrafik grafik = new PerfGrafik();
        readonly Label ozet, durum;
        readonly Action<string, int> kaydet;
        Mesaj sonM; List<PerfOlcum> sonL; List<Bulgu> sonB; string sonIkiz = "";
        List<IslemKaydi> islemler = new List<IslemKaydi>();
        readonly Timer canli = new Timer { Interval = 3000 };
        readonly Dictionary<string, (Label durum, Label detay, Panel cubuk)> canliKart = new Dictionary<string, (Label, Label, Panel)>();
        bool soruluyor;

        public TeshisForm(Pc pc, List<Pc> digerleri, Action<string, int> kaydet) : base(pc.Baslik + " · teşhis ve hızlandır", 780, 700)
        {
            SabitBoyut = true; this.pc = pc; this.digerleri = digerleri; this.kaydet = kaydet;
            int g = Icerik.Width;
            ozet = Yazi.Etiket("Bilgiler alınıyor…", 9.5f, false, Renk.Yazi2, false); ozet.SetBounds(0, 0, g, Tema.S(40));
            grafik.SetBounds(0, Tema.S(44), g, Tema.S(150));
            liste.Kutu.SetBounds(0, Tema.S(206), g, Icerik.Height - Tema.S(206) - Tema.S(36));
            durum = Yazi.Etiket("", 9.5f, false, Renk.Yazi2, false); durum.SetBounds(0, Icerik.Height - Tema.S(30), g, Tema.S(28));
            ozet.Anchor = grafik.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            liste.Kutu.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right; durum.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            Icerik.Controls.AddRange(new Control[] { ozet, grafik, liste.Kutu, durum });
            Shown += (s, e) => Task.Run(Yukle);
            // Süren işlem varken 3 sn'de bir durum sorulur; pencere kapanınca işlem bilgisayarda sürer
            canli.Tick += (s, e) => { if (!soruluyor && islemler.Any(x => x.Suruyor)) { soruluyor = true; Task.Run(() => { try { IslemleriAl(); } finally { soruluyor = false; } }); } };
            canli.Start(); FormClosed += (s, e) => canli.Dispose();
        }

        void Ui(Action a) { try { if (!IsDisposed) BeginInvoke(a); } catch { } }

        void Yukle()
        {
            Mesaj m;
            try { m = Baglanti.Gonder(pc.Ip, new Mesaj("PERF_VERI"), 30000); }
            catch (Exception ex) { Ui(() => ozet.Text = "Bilgisayara ulaşılamadı: " + ex.Message); return; }
            if (!m.Tamam) { Ui(() => ozet.Text = m["aciklama"].Contains("Bilinmeyen") ? "Bu bilgisayardaki Sem güncellenmeli." : m["aciklama"]); return; }
            var l = PerfOlcum.Coz(m.Veri);
            // Aynı işlemcili başka bir açık bilgisayarla karşılaştır
            string ikizAd = ""; Mesaj ikiz = null; List<PerfOlcum> ikizL = null;
            var adaylar = digerleri.Where(p => p.Acik && p.Ad != pc.Ad).ToList();
            var sonuclar = new System.Collections.Concurrent.ConcurrentBag<(Pc p, Mesaj m)>();
            Parallel.ForEach(adaylar, p => { try { var c = Baglanti.Gonder(p.Ip, new Mesaj("PERF_VERI") { ["hafif"] = "1" }, 30000); if (c.Tamam && c["islemci"] == m["islemci"] && m["islemci"] != "") sonuclar.Add((p, c)); } catch { } });
            var en = sonuclar.Select(x => (x.p, x.m, l: PerfOlcum.Coz(x.m.Veri))).OrderByDescending(x => x.l.Count).FirstOrDefault();
            if (en.p != null) { ikizAd = en.p.Baslik; ikiz = en.m; ikizL = en.l; }
            var bulgular = Teshis.Incele(m, l, ikizAd, ikiz, ikizL);
            List<IslemKaydi> il = null;
            try { var r = Baglanti.Gonder(pc.Ip, new Mesaj("ISLEMLER"), 15000); if (r.Tamam) il = IslemKaydi.Coz(r.Veri); } catch { }
            Ui(() => { if (il != null) islemler = il; Ciz(m, l, bulgular, ikizAd); });
        }

        void IslemleriAl()
        {
            List<IslemKaydi> il;
            try { var r = Baglanti.Gonder(pc.Ip, new Mesaj("ISLEMLER"), 15000); if (!r.Tamam) return; il = IslemKaydi.Coz(r.Veri); } catch { return; }
            Ui(() =>
            {
                // Durumu değişen (bitti/yeni başladı) varsa liste yeniden çizilir; yoksa yalnızca ilerleme güncellenir
                bool yapi = il.Count != islemler.Count || il.Any(x => !islemler.Any(y => y.Id == x.Id && y.Durum == x.Durum && y.Sonuc == x.Sonuc));
                islemler = il;
                if (yapi || canliKart.Count == 0) { if (sonM != null) Ciz(sonM, sonL, sonB, sonIkiz); return; }
                foreach (var x in il.Where(k => k.Suruyor && canliKart.ContainsKey(k.Id))) CanliYaz(x, canliKart[x.Id]);
            });
        }

        void Ciz(Mesaj m, List<PerfOlcum> l, List<Bulgu> bulgular, string ikizAd)
        {
            sonM = m; sonL = l; sonB = bulgular; sonIkiz = ikizAd;
            int kaydir = -liste.Govde.AutoScrollPosition.Y;
            var tr = new CultureInfo("tr-TR");
            DateTime acilis; var ac = Izleme.Zaman(m["acilis"], out acilis) ? (DateTime.Now - acilis).TotalDays < 1 ? "bugün açıldı" : (int)(DateTime.Now - acilis).TotalDays + " gündür açık" : "";
            ozet.Text = m["islemci"] + " · RAM " + m["ram_detay"] + " · " + m["disk_tur"] + (Teshis.Tam(m, "disk_saat") > 0 ? " (" + Teshis.Tam(m, "disk_saat").ToString("N0", tr) + " saat)" : "") + (ac != "" ? " · " + ac : "") +
                        (Teshis.Tam(m, "sn_cpu_sicaklik") > 0 ? " · işlemci " + m["sn_cpu_sicaklik"] + "°" : "") +
                        (m["sn_fanlar"] != "" ? " · fan " + string.Join("/", m["sn_fanlar"].Split(';').Where(x => x.Contains(":")).Select(x => x.Split(':')[1])) + " rpm" : "") +
                        "\n" + l.Count + " ölçüm (son 2 gün)" + (ikizAd != "" ? " · " + ikizAd + " ile karşılaştırıldı" : "");
            grafik.Veri = l; grafik.Invalidate();
            liste.Temizle(); canliKart.Clear();
            // Yapılan işlemler (son 7 gün): sürenler canlı, bitenler sonucuyla
            var gorunen = islemler.Where(x => x.Suruyor || (DateTime.Now - x.Bas).TotalDays < 7).Take(6).ToList();
            if (gorunen.Count > 0)
            {
                liste.Baslik("Yapılan işlemler");
                foreach (var x in gorunen) liste.EklePx(IslemKart(x));
                liste.Baslik("Olası nedenler");
            }
            if (l.Count < 6) liste.Metin("Kayıt yeni başladı (" + l.Count + " ölçüm). Kesin sonuç için bilgisayar birkaç saat açık kalsın, özellikle kasmaya başladıktan sonra tekrar bakın.", Renk.Turuncu, 9.5f, false, 40);
            var asil = bulgular.Where(x => x.Olasilik >= 0).ToList();
            if (asil.Count == 0) liste.Metin("Belirgin bir sorun görünmüyor.", Renk.YesilKoyu, 11f, true, 28);
            foreach (var b in bulgular) liste.EklePx(Kart(b));
            // Açılış programları
            var acilisL = m["acilis_liste"].Split(';').Where(x => x.Trim() != "").ToList();
            if (acilisL.Count > 0)
            {
                liste.Baslik("Açılışta başlayan programlar (" + acilisL.Count + ")");
                foreach (var a in acilisL) liste.EklePx(AcilisSatiri(a));
            }
            liste.Baslik("Bakım araçları");
            liste.EklePx(BakimAraclari());
            liste.Bitir();
            if (kaydir > 0) liste.Govde.AutoScrollPosition = new Point(0, kaydir);
            durum.Text = "Isı ve RAM gibi donanım nedenleri yazılımla çözülemez; yapılacak iş kartta yazar.";
        }

        Control Kart(Bulgu b)
        {
            int w = liste.Genislik;
            var renk = b.Olasilik == 2 ? Renk.PembeKoyu : b.Olasilik == 1 ? Renk.Turuncu : Renk.Yazi2;
            var olas = b.Olasilik == 2 ? "Yüksek ihtimal" : b.Olasilik == 1 ? "Orta" : b.Olasilik == 0 ? "Düşük" : "Bakım";
            var p = new Panel { Width = w, BackColor = Renk.Yuzey, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
            var t = Yazi.Etiket(b.Baslik, 10.5f, true, renk, false); t.SetBounds(Tema.S(14), Tema.S(10), w - Tema.S(200), Tema.S(24)); t.AutoEllipsis = true;
            var o = Yazi.Etiket(olas + (b.Donanim ? " · donanım" : ""), 8.5f, true, renk, false); o.SetBounds(w - Tema.S(180), Tema.S(13), Tema.S(166), Tema.S(20)); o.TextAlign = ContentAlignment.TopRight; o.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            var k = Yazi.Etiket(b.Kanit, 9.25f, false, Renk.Yazi, false);
            var n = Yazi.Etiket("Ne yapmalı: " + b.Oneri, 9.25f, false, Renk.Yazi2, false);
            int metinW = w - Tema.S(28);
            int kh, nh;
            using (var g = CreateGraphics()) { kh = (int)Math.Ceiling(g.MeasureString(b.Kanit, k.Font, metinW).Height) + Tema.S(2); nh = (int)Math.Ceiling(g.MeasureString(n.Text, n.Font, metinW).Height) + Tema.S(2); }
            k.SetBounds(Tema.S(14), Tema.S(36), metinW, kh); n.SetBounds(Tema.S(14), Tema.S(40) + kh, metinW, nh);
            k.Anchor = n.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            int y = Tema.S(46) + kh + nh;
            p.Controls.AddRange(new Control[] { t, o, k, n });
            if (b.Coz != "")
            {
                var son = islemler.FirstOrDefault(x => x.Islem == b.Coz && (b.Coz != "program" || x.Ad == b.CozAd));
                bool suruyor = son != null && son.Suruyor;
                var c = new Buton(suruyor ? "Sürüyor…" : son != null ? "Tekrar: " + b.CozMetin : b.CozMetin, Ik.Tik, son != null ? ButonTuru.Ikincil : ButonTuru.Birincil) { Location = new Point(Tema.S(14), y), Enabled = !suruyor };
                c.Click += (s, e) => { if (Onayla(b.Coz)) Coz(b.Coz, b.CozAd, c); };
                p.Controls.Add(c);
                if (son != null && !suruyor)
                {
                    var sn = Yazi.Etiket("Son: " + son.Bit.ToString("d MMM HH:mm", new CultureInfo("tr-TR")) + " · " + son.Sonuc, 8.75f, false, son.Durum == "t" ? Renk.YesilKoyu : Renk.PembeKoyu, false);
                    sn.SetBounds(c.Right + Tema.S(12), y + (c.Height - Tema.S(20)) / 2, w - c.Right - Tema.S(26), Tema.S(20)); sn.AutoEllipsis = true; sn.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
                    p.Controls.Add(sn);
                }
                y += c.Height + Tema.S(6);
            }
            p.Height = y + Tema.S(10);
            p.Paint += (s, e) => { using (var pen = new Pen(Renk.Cizgi)) e.Graphics.DrawRectangle(pen, 0, 0, p.Width - 1, p.Height - 1); using (var br = new SolidBrush(renk)) e.Graphics.FillRectangle(br, 0, 0, Tema.S(3), p.Height); };
            var kap = new Panel { Width = w, Height = p.Height + Tema.S(10), BackColor = Renk.Zemin, Anchor = p.Anchor };
            kap.Controls.Add(p); p.Location = Point.Empty;
            return Sar(kap);
        }

        // SatirListesi.Ekle yüksekliği sabit istediği için kartın kendi yüksekliğini ileten küçük sarmalayıcı
        Control Sar(Panel kap) { kap.Tag = kap.Height; return kap; }

        void Coz(string islem, string ad, Buton c)
        {
            c.Enabled = false; c.Text = "Başlatılıyor…"; c.BoyutAyarla();
            Task.Run(() =>
            {
                string hata = null;
                try { var r = Baglanti.Gonder(pc.Ip, new Mesaj("PERF_COZ") { ["islem"] = islem, ["ad"] = ad }, 120000); if (r["id"] == "" && !r.Tamam) hata = r["aciklama"]; }
                catch (Exception ex) { hata = ex.Message; }
                if (hata != null) { Ui(() => { c.Text = "Olmadı: " + hata; c.BoyutAyarla(); c.Enabled = true; }); return; }
                // Sonuç "Yapılan işlemler"de görünür; bittiğinde Etkinlik'e de düşer
                IslemleriAl();
            });
        }

        // Uzun süren ya da yeniden başlatma gerektirenler için önce onay istenir
        internal static readonly Dictionary<string, string> Onayli = new Dictionary<string, string>
        {
            ["checkup"] = "Virüs taraması, disk taraması ve sistem dosyası denetimi sırayla yapılır. Klasik diskte 1 saati bulabilir; bu sırada bilgisayar kullanılabilir ama yavaşlar. Önce geri yükleme noktası alınır.",
            ["dism"] = "Windows bileşenleri internetten onarılır; 10-30 dakika sürer. Önce geri yükleme noktası alınır.",
            ["sfc"] = "Sistem dosyaları denetlenip bozuklar Windows'un orijinal kopyasıyla değiştirilir; 10-20 dakika sürer. Önce geri yükleme noktası alınır.",
            ["wuonar"] = "Güncelleme servisleri durdurulup önbellek yenilenir; o sırada iniyorsa güncelleme baştan iner. Önce geri yükleme noktası alınır.",
            ["agtam"] = "Ağ ayarları fabrika haline döner; bilgisayar yeniden başlatılana kadar geçerli olmaz. Elle girilmiş IP varsa yapılmaz. Önce geri yükleme noktası alınır.",
            ["bellek"] = "Bilgisayar bir sonraki yeniden başlatmada 15-20 dakika bellek testi yapar; o sırada kullanılamaz. Şimdi yeniden başlatılmaz. Geri alınabilir.",
            ["tamvirus"] = "Bütün diskler taranır; 1-3 saat sürebilir ve bu sırada bilgisayar biraz yavaşlar. Bulunan tehditler Windows Defender tarafından karantinaya alınır, başka dosyaya dokunulmaz.",
            ["aghafif"] = "DNS önbelleği temizlenir ve IP yenilenir; bağlantı birkaç saniye kopabilir.",
        };

        bool Onayla(string islem)
        {
            string metin; if (!Onayli.TryGetValue(islem, out metin)) return true;
            return MK.Sor(this, IslemKaydi.Adi(islem, ""), metin, "Başlat") == true;
        }

        Control BakimAraclari()
        {
            int w = liste.Genislik;
            var p = new Panel { Width = w, BackColor = Renk.Zemin, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
            var araclar = new (string islem, string metin, string ikon, bool ana)[]
            {
                ("checkup", "Tam check-up", Ik.Kalp, true), ("defender", "Virüs taraması", Ik.Kalkan, false), ("tamvirus", "Tam virüs taraması", Ik.Kalkan, false), ("disk", "Disk tara", Ik.Anahtar_, false),
                ("sfc", "Sistem dosyalarını onar", Ik.Anahtar_, false), ("dism", "Windows onarımı", Ik.Anahtar_, false), ("wuonar", "Windows Update onar", Ik.Esitle, false),
                ("temp", "Geçici dosyalar", Ik.Sil, false), ("aghafif", "Ağı onar (hafif)", Ik.Kopuk, false), ("agtam", "Ağı sıfırla (tam)", Ik.Kopuk, false),
                ("saat", "Saati eşitle", Ik.Takvim, false), ("aygit", "Aygıtları tara", Ik.Masaustu, false), ("bellek", "Bellek testi", Ik.Kalp, false),
            };
            int x = 0, y = 0, satirH = 0;
            foreach (var a in araclar)
            {
                var sur = islemler.FirstOrDefault(k => k.Islem == a.islem);
                bool suruyor = sur != null && sur.Suruyor;
                var c = new Buton(suruyor ? a.metin + " · sürüyor" : a.metin, a.ikon, a.ana ? ButonTuru.Birincil : ButonTuru.Ikincil) { Enabled = !suruyor };
                var isl = a.islem;
                c.Click += (s, e) => { if (Onayla(isl)) Coz(isl, "", c); };
                if (x > 0 && x + c.Width > w) { x = 0; y += satirH + Tema.S(8); satirH = 0; }
                c.Location = new Point(x, y); p.Controls.Add(c);
                x += c.Width + Tema.S(8); satirH = Math.Max(satirH, c.Height);
            }
            var not = Yazi.Etiket("Hiçbiri kendiliğinden çalışmaz. Ayar değiştirenler \"Geri al\" ile eski haline döner; sistem dosyalarına dokunanlardan önce geri yükleme noktası alınır; kişisel dosyalar silinmez.", 8.75f, false, Renk.Yazi3, false);
            y += satirH + Tema.S(8);
            not.SetBounds(0, y, w, Tema.S(36)); not.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            p.Controls.Add(not);
            p.Height = y + Tema.S(44);
            return p;
        }

        Control AcilisSatiri(string ad)
        {
            int w = liste.Genislik;
            var p = new Panel { Width = w, Height = Tema.S(36), BackColor = Renk.Zemin, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
            var son = islemler.FirstOrDefault(k => k.Islem == "acilis" && k.Ad == ad && k.Durum == "t" && k.Ek != "gerialindi");
            var t = Yazi.Etiket(ad, 9.75f, false, son != null ? Renk.Yazi3 : Renk.Yazi, false); t.SetBounds(0, Tema.S(8), w - Tema.S(200), Tema.S(22)); t.AutoEllipsis = true; t.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            p.Controls.Add(t);
            var c = new Buton(son != null ? "Çıkarıldı" : "Açılıştan çıkar", Ik.Sil, ButonTuru.Sade) { Enabled = son == null };
            c.Location = new Point(w - c.Width, (p.Height - c.Height) / 2); c.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            c.Click += (s, e) => Coz("acilis", ad, c);
            p.Controls.Add(c);
            p.Paint += (s, e) => { using (var pen = new Pen(Renk.Cizgi)) e.Graphics.DrawLine(pen, 0, p.Height - 1, p.Width, p.Height - 1); };
            return p;
        }

        Control IslemKart(IslemKaydi x)
        {
            int w = liste.Genislik;
            var tr = new CultureInfo("tr-TR");
            var renk = x.Suruyor ? Renk.Mavi : x.Durum == "t" ? Renk.YesilKoyu : x.Durum == "y" ? Renk.Turuncu : Renk.PembeKoyu;
            var p = new Panel { Width = w, BackColor = Renk.Yuzey, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
            var baslik = x.Islem == "geri" ? "Geri al: " + IslemKaydi.Adi(islemler.FirstOrDefault(k => k.Id == x.Ad)?.Islem ?? "", islemler.FirstOrDefault(k => k.Id == x.Ad)?.Ad ?? "").TrimEnd(' ', ':') : IslemKaydi.Adi(x.Islem, x.Ad);
            var t = Yazi.Etiket(baslik, 10f, true, Renk.Yazi, false); t.SetBounds(Tema.S(14), Tema.S(9), w - Tema.S(220), Tema.S(22)); t.AutoEllipsis = true;
            var d = Yazi.Etiket("", 9f, true, renk, false); d.SetBounds(w - Tema.S(200), Tema.S(11), Tema.S(186), Tema.S(20)); d.TextAlign = ContentAlignment.TopRight; d.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            var a = Yazi.Etiket("", 9f, false, Renk.Yazi2, false); a.SetBounds(Tema.S(14), Tema.S(32), w - Tema.S(28), Tema.S(20)); a.AutoEllipsis = true; a.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            p.Controls.AddRange(new Control[] { t, d, a });
            int y = Tema.S(56);
            Panel cubuk = null;
            if (x.Suruyor)
            {
                cubuk = new Panel { BackColor = Renk.Cizgi, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right, Tag = x.Ilerleme };
                cubuk.SetBounds(Tema.S(14), y, w - Tema.S(28), Tema.S(6));
                var cb = cubuk;
                cubuk.Paint += (s, e) => { int yz = cb.Tag is int n ? n : -1; if (yz < 0) return; using (var br = new SolidBrush(Renk.Mavi)) e.Graphics.FillRectangle(br, 0, 0, cb.Width * Math.Min(100, yz) / 100, cb.Height); };
                p.Controls.Add(cubuk); y += Tema.S(14);
                if (x.Sonuc != "")
                {
                    var ks = "Biten aşamalar: " + x.Sonuc;
                    var sn = Yazi.Etiket(ks, 9.25f, false, Renk.Yazi, false);
                    int sh; using (var g = CreateGraphics()) sh = (int)Math.Ceiling(g.MeasureString(ks, sn.Font, w - Tema.S(28)).Height) + Tema.S(2);
                    sn.SetBounds(Tema.S(14), y - Tema.S(2), w - Tema.S(28), sh); sn.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
                    p.Controls.Add(sn); y += sh + Tema.S(4);
                }
                canliKart[x.Id] = (d, a, cubuk);
                CanliYaz(x, (d, a, cubuk));
            }
            else
            {
                d.Text = x.Durum == "t" ? "✓ Tamamlandı" : x.Durum == "y" ? "Yarıda kaldı" : "Sorun var";
                a.Text = x.Bas.ToString("d MMM HH:mm", tr) + (x.Bit > x.Bas ? " · " + IslemKaydi.Sure(x.Bit - x.Bas) + " sürdü" : "");
                var sn = Yazi.Etiket(x.Sonuc, 9.25f, false, x.Durum == "t" ? Renk.Yazi : renk, false);
                int sh; using (var g = CreateGraphics()) sh = (int)Math.Ceiling(g.MeasureString(x.Sonuc, sn.Font, w - Tema.S(28)).Height) + Tema.S(2);
                sn.SetBounds(Tema.S(14), y - Tema.S(2), w - Tema.S(28), sh); sn.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
                p.Controls.Add(sn); y += sh + Tema.S(4);
                // Sonraki adım: tarama hata bulduysa açılışta onarım
                var sonraki = x.Ek == "diskonar" ? ("diskonar", "Açılışta onar") : x.Ek == "dism" ? ("dism", "Windows onarımını başlat") : x.Ek == "sfc" && x.Islem == "dism" ? ("sfc", "Sistem dosyalarını onar") : x.Ek == "tamvirus" ? ("tamvirus", "Tam virüs taraması yap") : (null, null);
                if (sonraki.Item1 != null && !islemler.Any(k => k.Islem == sonraki.Item1 && k.Bas > x.Bas))
                {
                    var c = new Buton(sonraki.Item2, Ik.Anahtar_, ButonTuru.Birincil) { Location = new Point(Tema.S(14), y) };
                    var isl = sonraki.Item1;
                    c.Click += (s, e) => { if (Onayla(isl)) Coz(isl, "", c); };
                    p.Controls.Add(c); y += c.Height + Tema.S(6);
                }
                if (DetayliIslem(x.Islem))
                {
                    // Sonraki adım düğmesi varsa onun yanına, yoksa kendi satırına
                    var once = p.Controls.OfType<Buton>().LastOrDefault();
                    var c = new Buton("Ayrıntılar", Ik.Belge, ButonTuru.Ikincil);
                    if (once != null) c.Location = new Point(once.Right + Tema.S(8), once.Top);
                    else { c.Location = new Point(Tema.S(14), y); y += c.Height + Tema.S(6); }
                    c.Click += (s, e) => new IslemDetayForm(pc, x, baslik).ShowDialog(Sahip);
                    p.Controls.Add(c);
                }
                if (x.Ek == "geri" && x.Durum == "t")
                {
                    var c = new Buton("Geri al", Ik.Esitle, ButonTuru.Ikincil) { Location = new Point(Tema.S(14), y) };
                    c.Click += (s, e) => { if (MK.Sor(this, "Geri al", IslemKaydi.Adi(x.Islem, x.Ad) + " işlemi geri alınsın mı? Ayar eski haline döner.", "Geri al") == true) Coz("geri", x.Id, c); };
                    p.Controls.Add(c); y += c.Height + Tema.S(6);
                }
                else if (x.Ek == "gerialindi")
                {
                    var g = Yazi.Etiket("Geri alındı", 9f, true, Renk.Yazi3, false); g.SetBounds(Tema.S(14), y, Tema.S(200), Tema.S(20));
                    p.Controls.Add(g); y += Tema.S(24);
                }
            }
            p.Height = y + Tema.S(6);
            p.Paint += (s, e) => { using (var pen = new Pen(Renk.Cizgi)) e.Graphics.DrawRectangle(pen, 0, 0, p.Width - 1, p.Height - 1); using (var br = new SolidBrush(renk)) e.Graphics.FillRectangle(br, 0, 0, Tema.S(3), p.Height); };
            var kap = new Panel { Width = w, Height = p.Height + Tema.S(8), BackColor = Renk.Zemin, Anchor = p.Anchor };
            kap.Controls.Add(p); p.Location = Point.Empty;
            return kap;
        }

        static bool DetayliIslem(string islem) => new[] { "checkup", "sfc", "dism", "defender", "tamvirus", "disk" }.Contains(islem);

        void CanliYaz(IslemKaydi x, (Label durum, Label detay, Panel cubuk) k)
        {
            k.durum.Text = "Sürüyor" + (x.Ilerleme >= 0 ? " · %" + x.Ilerleme : "");
            k.detay.Text = x.Bas.ToString("HH:mm") + "'de başladı · " + IslemKaydi.Sure(DateTime.Now - x.Bas) + " geçti" + (x.Asama != "" ? " · " + x.Asama : "");
            k.cubuk.Tag = x.Ilerleme; k.cubuk.Invalidate();
        }
    }
}
