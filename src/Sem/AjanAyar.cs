using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Sem.Ajan
{
    /// <summary>C:\ProgramData\Sem\ajan.ini içinde tutulan ayarlar.</summary>
    public class AjanAyar
    {
        public static readonly string Klasor = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Sem");
        public static string Dosya => Path.Combine(Klasor, "ajan.ini");
        public static string DuvarKagidi => Path.Combine(Klasor, "duvar.png");
        public static string OkunanDosya => Path.Combine(Klasor, "okunan.txt");
        public static readonly string KurulumKlasoru = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Sem");
        public static string KurulumExe => Path.Combine(KurulumKlasoru, "Sem.exe");

        // Gün adları: DayOfWeek sırasıyla (Pazar=0)
        public static readonly string[] GunKod = { "paz", "pzt", "sal", "car", "per", "cum", "cmt" };
        public static readonly string[] GunAd = { "Pazar", "Pazartesi", "Salı", "Çarşamba", "Perşembe", "Cuma", "Cumartesi" };

        public byte[] Anahtar;
        public string[] Kapanma = { "18:00", "18:00", "18:00", "18:00", "18:00", "18:00", "17:00" };
        public int UyariDk = 5;
        public int ErteleDk = 15;
        public int ErteleHak = 2;
        public string BugunKapatmaTarihi = ""; // yyyy-MM-dd: o gün otomatik kapanma yapılmaz
        public bool DuvarKoru = true;
        public string DuvarKip = "";        // "" = gönderilen resim, "kurumsal", "otomatik" (mevsim + özel günler)
        public bool DuvarOda = true;        // tasarımda bilgisayarın adı görünsün
        public bool DuvarKilit = true;      // kilit ekranına da uygula
        public bool OtomatikKapanma = true; // yönetici bilgisayarında kapalı
        public bool OtoGuncelle = true;     // GitHub'dan doğrulanmış yeni sürümü kendisi kursun

        // Sabah açılma: bu bilgisayar açıksa, belirtilen saatte listedeki bilgisayarlara açma sinyali gönderir.
        public string SabahSaat = "";       // "08:30"; boşsa kapalı
        public string SabahGunler = "2,3,4,5,6"; // DayOfWeek (0 = Pazar)
        public string SabahMacler = "";     // "AA-BB-..;CC-DD-.."
        public bool WolHazir;               // ağ kartının uyandırma ayarı açıldı mı

        // Paylaşım (1.4)
        public bool Yonetici;               // kurulumda "Yönetici bilgisayarı" seçildi mi
        public string GorunenAd = "", Grup = "", Simge = "", GrupAd = ""; // yönetim ekranında verilen ad (paylaşımda görünür)
        public bool Paylasim = true;        // bilgisayarlar arası ve telefon paylaşımı
        public bool Telefon = true;         // LocalSend ile telefonlardan dosya kabul et
        public bool EkranIzni = true;       // yönetici ekranı görebilir / uzaktan kontrol edebilir
        public string Parmakizi = "";       // LocalSend cihaz kimliği
        public string YaziciIzinli = "";    // "her zaman izin ver" denen telefonlar (parmak izi;...)
        public bool LocalSendKaldirildi;

        // Yazıcıyı sabitle (1.5.3)
        public bool YaziciKilit;            // yalnız Canon (+ PDF; yöneticide USB yazıcılar da) kalsın
        public string YaziciIp = "192.168.1.50";
        public bool YaziciIlke;             // yazıcı ekleme engelleri uygulandı mı (kaldırırken geri almak için)
        public string Isim => GorunenAd != "" ? GorunenAd : Environment.MachineName;

        public static AjanAyar Oku()
        {
            var a = new AjanAyar();
            if (!File.Exists(Dosya)) return a;
            var d = new Dictionary<string, string>();
            foreach (var s in File.ReadAllLines(Dosya, Encoding.UTF8))
            {
                int i = s.IndexOf('=');
                if (i > 0) d[s.Substring(0, i).Trim()] = s.Substring(i + 1).Trim();
            }
            string v;
            if (d.TryGetValue("anahtar", out v) && v != "") a.Anahtar = Convert.FromBase64String(v);
            for (int g = 0; g < 7; g++) if (d.TryGetValue("kapanma_" + GunKod[g], out v)) a.Kapanma[g] = v;
            if (d.TryGetValue("uyari_dk", out v)) int.TryParse(v, out a.UyariDk);
            if (d.TryGetValue("ertele_dk", out v)) int.TryParse(v, out a.ErteleDk);
            if (d.TryGetValue("ertele_hak", out v)) int.TryParse(v, out a.ErteleHak);
            if (d.TryGetValue("bugun_kapatma", out v)) a.BugunKapatmaTarihi = v;
            if (d.TryGetValue("duvar_koru", out v)) a.DuvarKoru = v != "0";
            if (d.TryGetValue("duvar_kip", out v)) a.DuvarKip = v;
            if (d.TryGetValue("duvar_oda", out v)) a.DuvarOda = v != "0";
            if (d.TryGetValue("duvar_kilit", out v)) a.DuvarKilit = v != "0";
            if (d.TryGetValue("otomatik_kapanma", out v)) a.OtomatikKapanma = v != "0";
            if (d.TryGetValue("oto_guncelle", out v)) a.OtoGuncelle = v != "0";
            if (d.TryGetValue("sabah_saat", out v)) a.SabahSaat = v;
            if (d.TryGetValue("sabah_gunler", out v)) a.SabahGunler = v;
            if (d.TryGetValue("sabah_macler", out v)) a.SabahMacler = v;
            if (d.TryGetValue("wol_hazir2", out v)) a.WolHazir = v == "1"; // 1.5.3: uyandırma izni de eklendi, eskiler bir kez yeniden hazırlanır
            if (d.TryGetValue("yonetici", out v)) a.Yonetici = v == "1"; else a.Yonetici = !a.OtomatikKapanma;
            if (d.TryGetValue("gorunen_ad", out v)) a.GorunenAd = v;
            if (d.TryGetValue("grup", out v)) a.Grup = v;
            if (d.TryGetValue("grup_ad", out v)) a.GrupAd = v;
            if (d.TryGetValue("simge", out v)) a.Simge = v;
            if (d.TryGetValue("paylasim", out v)) a.Paylasim = v != "0";
            if (d.TryGetValue("telefon", out v)) a.Telefon = v != "0";
            if (d.TryGetValue("ekran_izni", out v)) a.EkranIzni = v != "0";
            if (d.TryGetValue("parmakizi", out v)) a.Parmakizi = v;
            if (d.TryGetValue("yazici_izinli", out v)) a.YaziciIzinli = v;
            if (d.TryGetValue("localsend_kaldirildi", out v)) a.LocalSendKaldirildi = v == "1";
            if (d.TryGetValue("yazici_kilit", out v)) a.YaziciKilit = v == "1";
            if (d.TryGetValue("yazici_ip", out v) && v != "") a.YaziciIp = v;
            if (d.TryGetValue("yazici_ilke", out v)) a.YaziciIlke = v == "1";
            return a;
        }

        public void Yaz()
        {
            Directory.CreateDirectory(Klasor);
            var sb = new StringBuilder();
            sb.AppendLine("anahtar=" + (Anahtar == null ? "" : Convert.ToBase64String(Anahtar)));
            for (int g = 0; g < 7; g++) sb.AppendLine("kapanma_" + GunKod[g] + "=" + Kapanma[g]);
            sb.AppendLine("uyari_dk=" + UyariDk);
            sb.AppendLine("ertele_dk=" + ErteleDk);
            sb.AppendLine("ertele_hak=" + ErteleHak);
            sb.AppendLine("bugun_kapatma=" + BugunKapatmaTarihi);
            sb.AppendLine("duvar_koru=" + (DuvarKoru ? "1" : "0"));
            sb.AppendLine("duvar_kip=" + DuvarKip);
            sb.AppendLine("duvar_oda=" + (DuvarOda ? "1" : "0"));
            sb.AppendLine("duvar_kilit=" + (DuvarKilit ? "1" : "0"));
            sb.AppendLine("otomatik_kapanma=" + (OtomatikKapanma ? "1" : "0"));
            sb.AppendLine("oto_guncelle=" + (OtoGuncelle ? "1" : "0"));
            sb.AppendLine("sabah_saat=" + SabahSaat);
            sb.AppendLine("sabah_gunler=" + SabahGunler);
            sb.AppendLine("sabah_macler=" + SabahMacler);
            sb.AppendLine("wol_hazir2=" + (WolHazir ? "1" : "0"));
            sb.AppendLine("yonetici=" + (Yonetici ? "1" : "0"));
            sb.AppendLine("gorunen_ad=" + Tek(GorunenAd));
            sb.AppendLine("grup=" + Tek(Grup));
            sb.AppendLine("grup_ad=" + Tek(GrupAd));
            sb.AppendLine("simge=" + Tek(Simge));
            sb.AppendLine("paylasim=" + (Paylasim ? "1" : "0"));
            sb.AppendLine("telefon=" + (Telefon ? "1" : "0"));
            sb.AppendLine("ekran_izni=" + (EkranIzni ? "1" : "0"));
            sb.AppendLine("yazici_kilit=" + (YaziciKilit ? "1" : "0"));
            sb.AppendLine("yazici_ip=" + Tek(YaziciIp));
            sb.AppendLine("yazici_ilke=" + (YaziciIlke ? "1" : "0"));
            sb.AppendLine("parmakizi=" + Parmakizi);
            sb.AppendLine("yazici_izinli=" + YaziciIzinli);
            sb.AppendLine("localsend_kaldirildi=" + (LocalSendKaldirildi ? "1" : "0"));
            var tmp = Dosya + ".tmp";
            File.WriteAllText(tmp, sb.ToString(), Encoding.UTF8);
            if (File.Exists(Dosya)) File.Replace(tmp, Dosya, null); else File.Move(tmp, Dosya);
        }

        static string Tek(string s) => (s ?? "").Replace("\r", " ").Replace("\n", " ").Trim();

        /// <summary>"18:00" → bugünün o saati; boş ya da "yok" → null (o gün otomatik kapanma yok).</summary>
        public DateTime? BugunkuKapanma(DateTime simdi)
        {
            if (!OtomatikKapanma) return null;
            var s = Kapanma[(int)simdi.DayOfWeek];
            TimeSpan t;
            if (string.IsNullOrWhiteSpace(s) || !TimeSpan.TryParse(s, out t)) return null;
            return simdi.Date + t;
        }
    }
}
