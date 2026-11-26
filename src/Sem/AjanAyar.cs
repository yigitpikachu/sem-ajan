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
        public bool OtomatikKapanma = true; // yönetici bilgisayarında kapalı
        public bool OtoGuncelle = true;     // GitHub'dan doğrulanmış yeni sürümü kendisi kursun

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
            if (d.TryGetValue("otomatik_kapanma", out v)) a.OtomatikKapanma = v != "0";
            if (d.TryGetValue("oto_guncelle", out v)) a.OtoGuncelle = v != "0";
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
            sb.AppendLine("otomatik_kapanma=" + (OtomatikKapanma ? "1" : "0"));
            sb.AppendLine("oto_guncelle=" + (OtoGuncelle ? "1" : "0"));
            var tmp = Dosya + ".tmp";
            File.WriteAllText(tmp, sb.ToString(), Encoding.UTF8);
            if (File.Exists(Dosya)) File.Replace(tmp, Dosya, null); else File.Move(tmp, Dosya);
        }

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
