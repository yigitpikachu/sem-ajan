using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using Sem.Arayuz;
using Sem.Ortak;

namespace Sem.Yonetici
{
    public class Pc
    {
        public string Ad = "", Ip = "", Mac = "";
        public string GorunenAd = "", Grup = Gruplar.Varsayilan, Simge = "";
        public bool Acik;
        public string Hata = "";
        public Mesaj SonDurum;
        public DateTime SonGorulme = DateTime.MinValue;
        public string SonIslem = "", SonSaglik = "";

        // Ekran önizlemesi
        public Image Onizleme;
        public DateTime OnizlemeZaman = DateTime.MinValue;
        public string OnizlemeHata = "";

        /// <summary>Kartta görünen ad: verilen ad varsa o, yoksa bilgisayarın adı.</summary>
        public string Baslik => GorunenAd != "" ? GorunenAd : Ad;
        public string Ikon => Simge != "" ? Simge : Ik.Masaustu;
    }

    /// <summary>Oda grupları; her birinin kendi rengi var.</summary>
    static class Gruplar
    {
        public const string Varsayilan = "diger";
        public static readonly (string Kod, string Ad, Color Renk)[] Liste =
        {
            ("sinif", "Sınıflar", Renk.Mavi),
            ("terapi", "Terapi odaları", Renk.Yesil),
            ("ofis", "Ofis", Renk.Turuncu),
            ("diger", "Diğer", Renk.Yazi3),
        };
        public static int Sira(string kod) { int i = Array.FindIndex(Liste, g => g.Kod == kod); return i < 0 ? Liste.Length - 1 : i; }
        public static string Ad(string kod) => Liste[Sira(kod)].Ad;
        public static Color Renk_(string kod) => Liste[Sira(kod)].Renk;
    }

    /// <summary>Bilgisayar kartında seçilebilecek simgeler.</summary>
    static class Simgeler
    {
        public static readonly (string Ikon, string Ad)[] Liste =
        {
            (Ik.Masaustu, "Bilgisayar"), (Ik.Dizustu, "Dizüstü"), (Ik.Mezuniyet, "Sınıf"), (Ik.Ogretmen, "Öğretmen"),
            (Ik.Kalp, "Terapi"), (Ik.Yapboz, "Duyu"), (Ik.Kitap, "Kitap"), (Ik.Muzik, "Müzik"),
            (Ik.Firca, "Sanat"), (Ik.Oyun, "Oyun"), (Ik.Sohbet, "Dil ve konuşma"), (Ik.Bina, "Ofis"),
            (Ik.Kisi, "Kişi"), (Ik.Yildiz, "Yıldız"),
        };
    }

    /// <summary>Yönetim ekranının bu bilgisayardaki dosyaları (%LocalAppData%\Sem).</summary>
    static class Depo
    {
        public static string Klasor { get { var k = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Sem"); Directory.CreateDirectory(k); return k; } }
        static string ListeDosya => Path.Combine(Klasor, "bilgisayarlar.txt");
        static string EskiDosya => Path.Combine(Klasor, "eski_anahtarlar.txt");

        // Satır biçimi: ad, ip, mac, görünen ad, grup, simge (sekmeyle ayrılmış)
        public static List<Pc> ListeOku()
        {
            var l = new List<Pc>();
            if (!File.Exists(ListeDosya)) return l;
            foreach (var s in File.ReadAllLines(ListeDosya, Encoding.UTF8))
            {
                var p = s.Split('\t');
                Func<int, string> a = i => p.Length > i ? p[i].Trim() : "";
                if (a(0) == "") continue;
                l.Add(new Pc { Ad = a(0), Ip = a(1), Mac = a(2), GorunenAd = a(3), Grup = a(4) == "" ? Gruplar.Varsayilan : a(4), Simge = a(5) });
            }
            return l;
        }

        static string Temiz(string s) => (s ?? "").Replace("\t", " ").Replace("\r", " ").Replace("\n", " ").Trim();

        public static void ListeYaz(IEnumerable<Pc> l) =>
            File.WriteAllLines(ListeDosya, l.Select(p => string.Join("\t", new[] { p.Ad, p.Ip, p.Mac, p.GorunenAd, p.Grup, p.Simge }.Select(Temiz))), Encoding.UTF8);

        /// <summary>Şifre değiştirildiğinde o an kapalı olan bilgisayarlar açılınca yeni şifreye geçirilsin diye eski anahtarlar.</summary>
        public static List<byte[]> EskiAnahtarlar() =>
            File.Exists(EskiDosya) ? File.ReadAllLines(EskiDosya).Where(x => x.Trim() != "").Select(Convert.FromBase64String).ToList() : new List<byte[]>();

        public static void EskiAnahtarEkle(byte[] k)
        {
            var l = EskiAnahtarlar();
            if (!l.Any(x => Sifreleme.Esit(x, k))) l.Add(k);
            File.WriteAllLines(EskiDosya, l.Skip(Math.Max(0, l.Count - 5)).Select(Convert.ToBase64String));
        }
    }

    /// <summary>Yönetim ekranı ayarları: hangi özellikler görünsün, kart mı liste mi.</summary>
    class YonetimAyar
    {
        public bool Onizleme = true, Dosya = true, Yazici = true, Program = true, Duvar = true, Duyurular = true, Sabah = true;
        public bool Liste;

        static string Dosya_ => Path.Combine(Depo.Klasor, "ayarlar.txt");

        public static YonetimAyar Oku()
        {
            var a = new YonetimAyar();
            try
            {
                if (!File.Exists(Dosya_)) return a;
                var d = File.ReadAllLines(Dosya_).Select(x => x.Split('=')).Where(x => x.Length == 2).ToDictionary(x => x[0].Trim(), x => x[1].Trim() == "1");
                bool v;
                if (d.TryGetValue("onizleme", out v)) a.Onizleme = v;
                if (d.TryGetValue("dosya", out v)) a.Dosya = v;
                if (d.TryGetValue("yazici", out v)) a.Yazici = v;
                if (d.TryGetValue("program", out v)) a.Program = v;
                if (d.TryGetValue("duvar", out v)) a.Duvar = v;
                if (d.TryGetValue("duyurular", out v)) a.Duyurular = v;
                if (d.TryGetValue("sabah", out v)) a.Sabah = v;
                if (d.TryGetValue("liste", out v)) a.Liste = v;
            }
            catch { }
            return a;
        }

        public void Yaz()
        {
            Func<bool, string> b = x => x ? "1" : "0";
            try
            {
                File.WriteAllLines(Dosya_, new[] {
                    "onizleme=" + b(Onizleme), "dosya=" + b(Dosya), "yazici=" + b(Yazici), "program=" + b(Program),
                    "duvar=" + b(Duvar), "duyurular=" + b(Duyurular), "sabah=" + b(Sabah), "liste=" + b(Liste) });
            }
            catch { }
        }
    }

    /// <summary>Gönderilen bir duyuru ve kimlerin "Tamam"a bastığı.</summary>
    class DuyuruKaydi
    {
        public string Id = Guid.NewGuid().ToString("N").Substring(0, 12), Baslik = "", Metin = "";
        public DateTime Zaman = DateTime.Now;
        public List<string> Ulasan = new List<string>();
        public Dictionary<string, DateTime> Okuyan = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
    }

    static class DuyuruGecmisi
    {
        static string Dosya => Path.Combine(Depo.Klasor, "duyurular.txt");
        static readonly object kilit = new object();
        static List<DuyuruKaydi> liste;

        public static List<DuyuruKaydi> Liste { get { lock (kilit) { Yukle(); return liste.ToList(); } } }

        static void Yukle()
        {
            if (liste != null) return;
            liste = new List<DuyuruKaydi>();
            try
            {
                if (!File.Exists(Dosya)) return;
                foreach (var s in File.ReadAllLines(Dosya, Encoding.UTF8))
                {
                    var p = s.Split('\t').Select(Uri.UnescapeDataString).ToArray();
                    if (p.Length < 6) continue;
                    var k = new DuyuruKaydi { Id = p[0], Baslik = p[2], Metin = p[3] };
                    DateTime z; if (DateTime.TryParse(p[1], out z)) k.Zaman = z;
                    k.Ulasan = p[4].Split(',').Where(x => x != "").ToList();
                    foreach (var o in p[5].Split(';').Select(x => x.Split('=')).Where(x => x.Length == 2))
                        if (DateTime.TryParse(o[1], out z)) k.Okuyan[o[0]] = z;
                    liste.Add(k);
                }
            }
            catch { }
        }

        static void Kaydet()
        {
            try
            {
                File.WriteAllLines(Dosya, liste.Select(k => string.Join("\t", new[] {
                    k.Id, k.Zaman.ToString("s"), k.Baslik, k.Metin, string.Join(",", k.Ulasan),
                    string.Join(";", k.Okuyan.Select(o => o.Key + "=" + o.Value.ToString("s"))) }.Select(Uri.EscapeDataString))), Encoding.UTF8);
            }
            catch { }
        }

        public static void Ekle(DuyuruKaydi k) { lock (kilit) { Yukle(); liste.Insert(0, k); if (liste.Count > 200) liste.RemoveRange(200, liste.Count - 200); Kaydet(); } }

        public static void Ulasti(string id, string pc) { lock (kilit) { Yukle(); var k = liste.FirstOrDefault(x => x.Id == id); if (k != null && !k.Ulasan.Contains(pc)) { k.Ulasan.Add(pc); Kaydet(); } } }

        /// <summary>Bilgisayarın bildirdiği okunan duyuruları ("id|zaman;...") işler; yeni bir şey varsa true.</summary>
        public static bool OkunanlariIsle(string pc, string okunan)
        {
            if (string.IsNullOrEmpty(okunan)) return false;
            bool degisti = false;
            lock (kilit)
            {
                Yukle();
                foreach (var o in okunan.Split(';').Select(x => x.Split('|')).Where(x => x.Length == 2))
                {
                    var k = liste.FirstOrDefault(x => x.Id == o[0]);
                    DateTime z;
                    if (k == null || k.Okuyan.ContainsKey(pc) || !DateTime.TryParse(o[1], out z)) continue;
                    k.Okuyan[pc] = z; degisti = true;
                }
                if (degisti) Kaydet();
            }
            return degisti;
        }

        public static void Temizle() { lock (kilit) { liste = new List<DuyuruKaydi>(); Kaydet(); } }
    }

    static class Baglanti
    {
        public static byte[] Anahtar;

        /// <summary>Komut gönderir. Bilgisayar eski bir şifrede kaldıysa (şifre değişirken kapalıydı) önce şifresini günceller.</summary>
        public static Mesaj Gonder(string ip, Mesaj m, int zamanAsimiMs = 5000)
        {
            try { return Ag.Gonder(ip, Anahtar, m, zamanAsimiMs); }
            catch (CryptographicException)
            {
                foreach (var eski in Depo.EskiAnahtarlar())
                {
                    try
                    {
                        var s = new Mesaj("SIFRE") { Veri = Anahtar };
                        if (Ag.Gonder(ip, eski, s, zamanAsimiMs).Tamam) return Ag.Gonder(ip, Anahtar, m, zamanAsimiMs);
                    }
                    catch (CryptographicException) { }
                }
                throw new Exception("Şifre uyuşmuyor (bu bilgisayar farklı bir şifreyle kurulmuş)");
            }
        }

        /// <summary>Ağa "kim var?" diye sorar, cevap veren Sem bilgisayarlarını döndürür (IP → ad).</summary>
        public static Dictionary<string, string> Kesfet(int beklemeMs = 1200) => Kesif.Sor(beklemeMs).ToDictionary(k => k.Ip, k => k.Ad);

        /// <summary>Kapalı bilgisayarı ağdan açmayı dener (BIOS'ta Wake-on-LAN açık olmalı).</summary>
        public static void Uyandir(string mac) => Uyandirma.Gonder(mac);
    }
}
