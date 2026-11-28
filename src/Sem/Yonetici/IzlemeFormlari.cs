// Baskı ve toner, İnternet ve ağ, Sorun bildirimleri pencereleri (yönetim paneli).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Sem.Arayuz;
using Sem.Ortak;
using MK = Sem.Arayuz.MesajKutusu;

namespace Sem.Yonetici
{
    static class Izleme
    {
        static readonly CultureInfo Tr = new CultureInfo("tr-TR");
        public static string Sayi(long n) => n.ToString("N0", Tr);
        public static string Ondalik(double d) => d.ToString("0.#", Tr);
        public static bool Zaman(string s, out DateTime d) => DateTime.TryParseExact(s, "yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out d);
        public static double Ciftle(string s) { double d; return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out d) ? d : 0; }
        public static string AyAdi(string ay) { DateTime d; return DateTime.TryParseExact(ay + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out d) ? d.ToString("MMMM yyyy", Tr) : ay; }
        public static string Sure(TimeSpan t) => t.TotalMinutes < 60 ? Math.Max(1, (int)Math.Round(t.TotalMinutes)) + " dk" : (int)t.TotalHours + " sa " + t.Minutes + " dk";

        /// <summary>Birden çok satırı bir satır listesine böler.</summary>
        public static List<string[]> Satirlar(byte[] veri) =>
            Encoding.UTF8.GetString(veri ?? new byte[0]).Split('\n').Where(x => x.Trim() != "").Select(x => x.Split('|')).ToList();

        /// <summary>Yazıcı ve internet işlerini yapan bilgisayar (Müdür): önce bu bilgisayar.</summary>
        public static Pc YoneticiBul(IEnumerable<Pc> pcler) =>
            pcler.Where(p => p.Acik && p.SonDurum != null && p.SonDurum["yonetici"] == "1")
                 .OrderBy(p => p.Ad.Equals(Environment.MachineName, StringComparison.OrdinalIgnoreCase) ? 0 : 1).FirstOrDefault();

        // ------------------------------------------------------------ Sorun bildirimleri (panelde saklanır)
        public static string SorunKlasor => Path.Combine(Depo.Klasor, "sorun");
        static readonly HashSet<string> alinan = new HashSet<string>();

        /// <summary>DURUM'daki bekleyen bildirimleri alır, kaydeder ve bilgisayardan siler. Yeni alınanların özetini döndürür.</summary>
        public static List<string> SorunlariAl(Pc p, string bekleyen)
        {
            var yeni = new List<string>();
            foreach (var id in (bekleyen ?? "").Split(';').Where(x => x != ""))
            {
                lock (alinan) if (!alinan.Add(p.Ad + "_" + id)) continue;
                try
                {
                    var c = Baglanti.Gonder(p.Ip, new Mesaj("SORUN_AL") { ["id"] = id }, 15000);
                    if (!c.Tamam) continue;
                    Directory.CreateDirectory(SorunKlasor);
                    var ad = p.Ad + "_" + id;
                    var sb = new StringBuilder();
                    sb.AppendLine("pc=" + p.Baslik); sb.AppendLine("zaman=" + c["zaman"]); sb.AppendLine("kullanici=" + c["kullanici"]);
                    sb.AppendLine("yazici=" + c["yazici"]); sb.AppendLine("saglik=" + c["saglik"]);
                    sb.AppendLine("not=" + Convert.ToBase64String(Encoding.UTF8.GetBytes(c["not"])));
                    File.WriteAllText(Path.Combine(SorunKlasor, ad + ".txt"), sb.ToString());
                    if (c.Veri.Length > 0) File.WriteAllBytes(Path.Combine(SorunKlasor, ad + ".jpg"), c.Veri);
                    try { Baglanti.Gonder(p.Ip, new Mesaj("SORUN_SIL") { ["id"] = id }, 5000); } catch { }
                    var not = c["not"].Trim();
                    yeni.Add(not == "" ? "Ekran görüntüsü gönderdi" : not.Length > 80 ? not.Substring(0, 80) + "…" : not);
                }
                catch { lock (alinan) alinan.Remove(p.Ad + "_" + id); }
            }
            return yeni;
        }

        public class Sorun { public string Dosya, Pc, Zaman, Kullanici, Not, Yazici, Saglik, Resim; }

        public static List<Sorun> Sorunlar()
        {
            var l = new List<Sorun>();
            try
            {
                if (!Directory.Exists(SorunKlasor)) return l;
                foreach (var f in Directory.GetFiles(SorunKlasor, "*.txt"))
                {
                    var d = new Dictionary<string, string>();
                    foreach (var s in File.ReadAllLines(f)) { int i = s.IndexOf('='); if (i > 0) d[s.Substring(0, i)] = s.Substring(i + 1); }
                    string v(string k) => d.TryGetValue(k, out var x) ? x : "";
                    string not; try { not = Encoding.UTF8.GetString(Convert.FromBase64String(v("not"))); } catch { not = ""; }
                    var j = Path.ChangeExtension(f, ".jpg");
                    l.Add(new Sorun { Dosya = f, Pc = v("pc"), Zaman = v("zaman"), Kullanici = v("kullanici"), Not = not, Yazici = v("yazici"), Saglik = v("saglik"), Resim = File.Exists(j) ? j : "" });
                }
            }
            catch { }
            return l.OrderByDescending(x => x.Zaman).ToList();
        }

        // ------------------------------------------------------------ Baskı defteri önbelleği (kapalı bilgisayarlar da görünsün)
        static string BaskiDosya(string pc) => Path.Combine(Depo.Klasor, "baski", string.Concat(pc.Split(Path.GetInvalidFileNameChars())) + ".txt");

        public static List<string> BaskiBirlestir(string pc, IEnumerable<string> yeni)
        {
            var f = BaskiDosya(pc);
            var set = new SortedSet<string>(StringComparer.Ordinal);
            try { if (File.Exists(f)) foreach (var s in File.ReadAllLines(f)) if (s.Contains("|")) set.Add(s); } catch { }
            foreach (var s in yeni) set.Add(s);
            var sinir = DateTime.Today.AddDays(-400).ToString("yyyy-MM-dd");
            var l = set.Where(s => string.CompareOrdinal(s, sinir) >= 0).ToList();
            try { Directory.CreateDirectory(Path.GetDirectoryName(f)); File.WriteAllLines(f, l); } catch { }
            return l;
        }

        public static List<string> BaskiOnbellek()
        {
            var k = Path.Combine(Depo.Klasor, "baski");
            try { return Directory.Exists(k) ? Directory.GetFiles(k, "*.txt").Select(Path.GetFileNameWithoutExtension).ToList() : new List<string>(); }
            catch { return new List<string>(); }
        }

        public static List<string> BaskiOku(string pc) { try { var f = BaskiDosya(pc); return File.Exists(f) ? File.ReadAllLines(f).ToList() : new List<string>(); } catch { return new List<string>(); } }

        // ------------------------------------------------------------ Aylık internet raporu (yazdırılabilir sayfa)
        public static string RaporYaz(string ay, List<string[]> s)
        {
            string H(string x) => WebUtility.HtmlEncode(x);
            var kes = s.Where(x => x[0] == "K" && x.Length >= 4).ToList();
            var hiz = s.Where(x => x[0] == "H" && x.Length >= 5).ToList();
            var gun = s.Where(x => x[0] == "G" && x.Length >= 4).ToList();
            long olcum = gun.Sum(x => long.Parse(x[2])), ok = gun.Sum(x => long.Parse(x[3]));
            var toplam = TimeSpan.Zero;
            var kesSatir = new StringBuilder();
            foreach (var k in kes)
            {
                DateTime a, b; if (!Zaman(k[1], out a) || !Zaman(k[2], out b)) continue;
                toplam += b - a;
                kesSatir.Append("<tr><td>" + a.ToString("dd.MM.yyyy ddd", Tr) + "</td><td>" + a.ToString("HH:mm") + "</td><td>" + b.ToString("HH:mm") + "</td><td>" + Sure(b - a) + "</td><td>" + (k[3] == "ag" ? "Yerel ağ / modem" : "İnternet (servis sağlayıcı)") + "</td></tr>");
            }
            var hizSatir = new StringBuilder();
            foreach (var h in hiz)
            {
                DateTime a; Zaman(h[1], out a);
                hizSatir.Append("<tr><td>" + a.ToString("dd.MM.yyyy ddd HH:mm", Tr) + "</td><td>" + Ondalik(Ciftle(h[2])) + "</td><td>" + Ondalik(Ciftle(h[3])) + "</td><td>" + h[4] + "</td></tr>");
            }
            double ortI = hiz.Count > 0 ? hiz.Average(x => Ciftle(x[2])) : 0, ortY = hiz.Count > 0 ? hiz.Average(x => Ciftle(x[3])) : 0;
            double minI = hiz.Count > 0 ? hiz.Min(x => Ciftle(x[2])) : 0;
            var html = new StringBuilder();
            html.Append("<!doctype html><html lang=tr><head><meta charset=utf-8><title>İnternet raporu " + H(AyAdi(ay)) + "</title><style>" +
                "body{font-family:'Segoe UI',Arial,sans-serif;color:#1e242c;margin:32px auto;max-width:860px;padding:0 16px}h1{font-size:22px;margin:0}" +
                ".alt{color:#686e78;margin:4px 0 24px}.kutu{display:flex;gap:12px;flex-wrap:wrap;margin-bottom:24px}.k{border:1px solid #e7e6e2;border-radius:8px;padding:12px 16px;min-width:150px}" +
                ".k b{display:block;font-size:20px}.k span{color:#686e78;font-size:13px}h2{font-size:15px;margin:28px 0 8px}table{border-collapse:collapse;width:100%;font-size:13px}" +
                "td,th{border-bottom:1px solid #e7e6e2;padding:6px 8px;text-align:left}th{color:#686e78;font-weight:600}.not{color:#686e78;font-size:12px;margin-top:28px}" +
                "button{float:right;padding:8px 14px;border:1px solid #d4d3cd;background:#fff;border-radius:6px;cursor:pointer}@media print{button{display:none}}</style></head><body>");
            html.Append("<button onclick='print()'>Yazdır / PDF</button><h1>İnternet bağlantı raporu</h1><p class=alt>" + H(AyAdi(ay)) + " · Sem Özel Eğitim ve Rehabilitasyon Merkezi</p>");
            html.Append("<div class=kutu><div class=k><b>%" + (olcum > 0 ? (ok * 100.0 / olcum).ToString("0.0", Tr) : "-") + "</b><span>erişilebilirlik</span></div>" +
                "<div class=k><b>" + kes.Count + "</b><span>kesinti</span></div><div class=k><b>" + (kes.Count > 0 ? Sure(toplam) : "0 dk") + "</b><span>toplam kesinti</span></div>" +
                "<div class=k><b>" + (hiz.Count > 0 ? Ondalik(ortI) + " / " + Ondalik(ortY) : "-") + "</b><span>ortalama indirme / yükleme (Mbit/sn)</span></div>" +
                "<div class=k><b>" + (hiz.Count > 0 ? Ondalik(minI) : "-") + "</b><span>en düşük indirme (Mbit/sn)</span></div></div>");
            html.Append("<h2>Kesintiler</h2>" + (kes.Count == 0 ? "<p>Bu ay kesinti kaydedilmedi.</p>" : "<table><tr><th>Tarih</th><th>Başlangıç</th><th>Bitiş</th><th>Süre</th><th>Tür</th></tr>" + kesSatir + "</table>"));
            html.Append("<h2>Günlük hız testleri</h2>" + (hiz.Count == 0 ? "<p>Hız testi kaydı yok.</p>" : "<table><tr><th>Zaman</th><th>İndirme (Mbit/sn)</th><th>Yükleme (Mbit/sn)</th><th>Gecikme (ms)</th></tr>" + hizSatir + "</table>"));
            html.Append("<p class=not>Ölçüm: yönetim bilgisayarı açıkken her dakika 1.1.1.1, 8.8.8.8 ve 208.67.222.222 adreslerine ulaşılabilirlik denetlenir; en az iki dakika süren erişimsizlik kesinti sayılır. " +
                "Hız testi her iş günü saat 09:00'dan sonra bir kez Cloudflare hız sunucusuyla yapılır. Toplam " + Sayi(olcum) + " dakika ölçüldü.</p></body></html>");
            var k2 = Path.Combine(Depo.Klasor, "rapor"); Directory.CreateDirectory(k2);
            var yol = Path.Combine(k2, "internet-" + ay + ".html");
            File.WriteAllText(yol, html.ToString(), new UTF8Encoding(true));
            return yol;
        }
    }

    /// <summary>Kaydırılabilir, satır satır doldurulan basit liste.</summary>
    class SatirListesi
    {
        public readonly Panel Govde; public readonly Panel Kutu;
        int y;
        public SatirListesi() { Govde = new Panel { AutoScroll = true, BackColor = Renk.Zemin }; Kutu = InceKaydirma.Sar(Govde); }
        public int Genislik => Kutu.Width - Tema.S(20);
        public void Temizle() { Govde.SuspendLayout(); foreach (Control c in Govde.Controls.Cast<Control>().ToList()) { Govde.Controls.Remove(c); c.Dispose(); } y = 0; Govde.AutoScrollPosition = Point.Empty; }
        public void Bitir() => Govde.ResumeLayout();
        public Label Baslik(string metin) { if (y > 0) y += Tema.S(10); var l = Yazi.UstBaslik(metin, Renk.Yazi3); l.Location = new Point(0, y); Govde.Controls.Add(l); y += Tema.S(24); return l; }
        public Label Metin(string metin, Color? renk = null, float boyut = 9.75f, bool kalin = false, int yuk = 22)
        {
            var l = Yazi.Etiket(metin, boyut, kalin, renk ?? Renk.Yazi, false); l.SetBounds(0, y, Genislik, Tema.S(yuk)); l.AutoEllipsis = true; l.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            Govde.Controls.Add(l); y += Tema.S(yuk) + Tema.S(2); return l;
        }
        /// <summary>Solda metin, sağda soluk ek bilgi.</summary>
        public Label Ikili(string sol, string sag, Color? solRenk = null, Action tik = null)
        {
            int sw = Tema.S(170);
            var a = Yazi.Etiket(sol, 9.75f, false, solRenk ?? Renk.Yazi, false); a.SetBounds(0, y, Genislik - sw - Tema.S(8), Tema.S(22)); a.AutoEllipsis = true; a.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            var b = Yazi.Etiket(sag, 9f, false, Renk.Yazi3, false); b.SetBounds(Genislik - sw, y + Tema.S(1), sw, Tema.S(22)); b.TextAlign = ContentAlignment.TopRight; b.AutoEllipsis = true; b.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            if (tik != null) foreach (var c in new[] { a, b }) { c.Cursor = Cursors.Hand; c.Click += (s, e) => tik(); c.MouseEnter += (s, e) => a.ForeColor = Renk.Mavi; c.MouseLeave += (s, e) => a.ForeColor = solRenk ?? Renk.Yazi; }
            Govde.Controls.Add(a); Govde.Controls.Add(b); y += Tema.S(26); return a;
        }
        public void Bosluk(int px) => y += Tema.S(px);
        public void Ekle(Control c, int yuk) { c.Location = new Point(0, y); Govde.Controls.Add(c); y += Tema.S(yuk); }
    }

    // ================================================================== Baskı ve toner
    class BaskiForm : ModernForm
    {
        readonly List<Pc> pcler; readonly Pc yonetici;
        readonly SatirListesi liste = new SatirListesi();
        readonly Label tonerBas, toner1, toner2, durum;
        readonly Buton degisti;

        public BaskiForm(List<Pc> pcler, Pc yonetici) : base("Baskı ve toner", 720, 600)
        {
            SabitBoyut = true; this.pcler = pcler; this.yonetici = yonetici;
            int g = Icerik.Width;
            var kart = new KartPanel { Location = new Point(0, 0), Size = new Size(g, Tema.S(112)), Dolgu = Renk.Yuzey };
            tonerBas = Yazi.Etiket("Yazıcı", 11f, true, Renk.Yazi, false); tonerBas.SetBounds(Tema.S(16), Tema.S(12), g - Tema.S(200), Tema.S(24)); tonerBas.AutoEllipsis = true;
            toner1 = Yazi.Etiket("Yazıcı bilgisi alınıyor…", 9.5f, false, Renk.Yazi2, false); toner1.SetBounds(Tema.S(16), Tema.S(40), g - Tema.S(32), Tema.S(22)); toner1.AutoEllipsis = true;
            toner2 = Yazi.Etiket("", 9.5f, false, Renk.Yazi2, false); toner2.SetBounds(Tema.S(16), Tema.S(64), g - Tema.S(32), Tema.S(40));
            degisti = new Buton("Toner değişti", Ik.Yenile, ButonTuru.Ikincil) { BasiliTut = true, Visible = false };
            degisti.Location = new Point(g - degisti.Width - Tema.S(14), Tema.S(10));
            degisti.KisaBasildi += (s, e) => toner2.Text = "Yanlışlıkla olmasın diye düğmeyi dolana kadar basılı tutun.";
            degisti.Onaylandi += (s, e) => TonerDegisti();
            kart.Controls.AddRange(new Control[] { tonerBas, toner1, toner2, degisti });
            kart.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right; tonerBas.Anchor = toner1.Anchor = toner2.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right; degisti.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            liste.Kutu.SetBounds(0, Tema.S(126), g, Icerik.Height - Tema.S(126) - Tema.S(40));
            durum = Yazi.Etiket("Baskı kayıtları alınıyor…", 9.5f, false, Renk.Yazi2, false); durum.SetBounds(0, Icerik.Height - Tema.S(34), g, Tema.S(30));
            liste.Kutu.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right; durum.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            Icerik.Controls.AddRange(new Control[] { kart, liste.Kutu, durum });
            Shown += (s, e) => { Task.Run(Toner); Task.Run(Baskilar); };
        }

        void Ui(Action a) { try { if (!IsDisposed) BeginInvoke(a); } catch { } }

        void Toner()
        {
            if (yonetici == null) { Ui(() => { toner1.Text = "Müdür bilgisayarı kapalı; yazıcı sayacı ondan okunur."; }); return; }
            Mesaj c;
            try { c = Baglanti.Gonder(yonetici.Ip, new Mesaj("TONER_DURUM"), 15000); }
            catch (Exception ex) { Ui(() => toner1.Text = "Okunamadı: " + ex.Message); return; }
            Ui(() =>
            {
                if (!c.Tamam) { toner1.Text = c["aciklama"].Contains("Bilinmeyen") ? "Müdür bilgisayarındaki Sem güncellenmeli." : c["aciklama"]; return; }
                degisti.Visible = true;
                tonerBas.Text = c["model"] != "" ? c["model"].Split('/')[0].Trim() : "Yazıcı";
                long sayac; long.TryParse(c["sayac"], out sayac);
                double gunluk = Izleme.Ciftle(c["gunluk"]);
                var t1 = c["ulasti"] == "1" ? "Sayaç: " + Izleme.Sayi(sayac) + " sayfa" : "Yazıcıya şu an ulaşılamıyor" + (sayac > 0 ? " · son sayaç " + Izleme.Sayi(sayac) : "");
                if (gunluk > 0) t1 += " · günde ortalama " + Izleme.Ondalik(gunluk) + " sayfa";
                if (c["ekran"] != "") t1 += " · ekranda: " + c["ekran"];
                toner1.Text = t1;
                int yuzde; int.TryParse(c["yuzde"], out yuzde);
                string t2;
                if (c["degisim"] == "")
                    t2 = (yuzde >= 0 ? "Toner %" + yuzde + ". " : "Yazıcı toner seviyesini bildirmiyor (orijinal olmayan kartuş). ") +
                         "Toner değiştiğinde \"Toner değişti\"yi basılı tutun; Sem o andan itibaren sayfaları sayıp bitişi tahmin eder.";
                else
                {
                    DateTime d; Izleme.Zaman(c["degisim"], out d);
                    long basilan, kalan; long.TryParse(c["basilan"], out basilan); long.TryParse(c["kalan_sayfa"], out kalan);
                    t2 = "Son değişim " + d.ToString("d MMMM", new CultureInfo("tr-TR")) + ": o günden beri " + Izleme.Sayi(basilan) + " sayfa basıldı. " +
                         "Tahminen " + Izleme.Sayi(kalan) + " sayfa" + (c["kalan_gun"] != "" ? " (yaklaşık " + c["kalan_gun"] + " gün)" : "") + " kaldı" +
                         (c["verim_ogrenildi"] == "1" ? "." : "; tahmin bir kartuş " + Izleme.Sayi(long.Parse(c["verim"])) + " sayfa sayılarak yapılıyor, ilk değişimden sonra öğrenilir.");
                }
                if (c["uyari"] != "") { t2 = "⚠ " + c["uyari"] + ". " + t2; toner2.ForeColor = Renk.PembeKoyu; } else toner2.ForeColor = Renk.Yazi2;
                toner2.Text = t2;
            });
        }

        void TonerDegisti()
        {
            if (yonetici == null) return;
            degisti.Enabled = false;
            Task.Run(() =>
            {
                string sonuc;
                try { var c = Baglanti.Gonder(yonetici.Ip, new Mesaj("TONER_DEGISTI"), 15000); sonuc = c.Tamam ? "" : c["aciklama"]; }
                catch (Exception ex) { sonuc = ex.Message; }
                Ui(() => { degisti.Enabled = true; if (sonuc != "") toner2.Text = "Kaydedilemedi: " + sonuc; });
                if (sonuc == "") Toner();
            });
        }

        void Baskilar()
        {
            var veriler = new System.Collections.Concurrent.ConcurrentDictionary<string, List<string>>();
            int hata = 0;
            Parallel.ForEach(pcler, p =>
            {
                try
                {
                    var c = Baglanti.Gonder(p.Ip, new Mesaj("BASKI_DEFTERI") { ["gun"] = "62" }, 30000);
                    if (!c.Tamam) { Interlocked.Increment(ref hata); return; }
                    var yeni = Encoding.UTF8.GetString(c.Veri).Split('\n').Where(x => x.Contains("|"));
                    veriler[p.Baslik] = Izleme.BaskiBirlestir(p.Baslik, yeni);
                }
                catch { Interlocked.Increment(ref hata); }
            });
            foreach (var ad in Izleme.BaskiOnbellek()) if (!veriler.ContainsKey(ad)) veriler[ad] = Izleme.BaskiOku(ad);
            var hepsi = veriler.SelectMany(kv => kv.Value.Select(s => (pc: kv.Key, p: s.Split('|')))).Where(x => x.p.Length >= 5).ToList();
            Ui(() => Ciz(hepsi, hata));
        }

        void Ciz(List<(string pc, string[] p)> hepsi, int hata)
        {
            liste.Temizle();
            var tr = new CultureInfo("tr-TR");
            string buAy = DateTime.Today.ToString("yyyy-MM"), gecen = DateTime.Today.AddMonths(-1).ToString("yyyy-MM");
            int S(string[] p) { int n; return int.TryParse(p[4], out n) ? n : 0; }
            var ay = hepsi.Where(x => x.p[0].StartsWith(buAy)).ToList();
            var gAy = hepsi.Where(x => x.p[0].StartsWith(gecen)).ToList();
            liste.Baslik("Bu ay · " + Izleme.Sayi(ay.Sum(x => S(x.p))) + " sayfa" + (gAy.Count > 0 ? "  (geçen ay " + Izleme.Sayi(gAy.Sum(x => S(x.p))) + ")" : ""));
            if (ay.Count == 0) liste.Metin("Bu ay henüz baskı kaydı yok. Kayıt, Sem güncellendikten sonra başlar.", Renk.Yazi2);
            foreach (var grup in ay.GroupBy(x => x.pc).OrderByDescending(g => g.Sum(x => S(x.p))))
                liste.Ikili(grup.Key, Izleme.Sayi(grup.Sum(x => S(x.p))) + " sayfa · " + grup.Count() + " belge");
            var son = hepsi.OrderByDescending(x => x.p[0]).Take(200).ToList();
            if (son.Count > 0)
            {
                liste.Baslik("Son baskılar");
                foreach (var x in son)
                {
                    DateTime d; Izleme.Zaman(x.p[0], out d);
                    var belge = x.p[2] == "" ? "(adsız belge)" : x.p[2];
                    liste.Ikili(belge, x.pc + " · " + d.ToString("d MMM HH:mm", tr) + " · " + S(x.p) + " sf");
                }
            }
            liste.Bitir();
            durum.Text = hata > 0 ? hata + " bilgisayardan okunamadı (kapalı olanların eski kayıtları gösteriliyor)." : "Her bilgisayarın Windows yazdırma kaydından okunur.";
        }
    }

    // ================================================================== İnternet ve ağ
    class AgForm : ModernForm
    {
        readonly Pc yonetici; readonly List<Pc> pcler;
        readonly SatirListesi liste = new SatirListesi();
        readonly HapSecici sekme;
        readonly Buton rapor, hepsiTanidik;
        readonly Label durum;
        List<string[]> karne, cihazlar;

        public AgForm(Pc yonetici, List<Pc> pcler) : base("İnternet ve ağ", 720, 600)
        {
            SabitBoyut = true; this.yonetici = yonetici; this.pcler = pcler;
            int g = Icerik.Width;
            sekme = new HapSecici(("İnternet karnesi", Color.Empty), ("Ağdaki cihazlar", Color.Empty)) { Location = new Point(0, 0), Width = g - Tema.S(200) };
            rapor = new Buton("Aylık rapor", Ik.Belge, ButonTuru.Ikincil); rapor.Location = new Point(g - rapor.Width, 0);
            hepsiTanidik = new Buton("Hepsi tanıdık", Ik.TikDaire, ButonTuru.Ikincil) { Visible = false }; hepsiTanidik.Location = new Point(g - hepsiTanidik.Width, 0);
            liste.Kutu.SetBounds(0, Tema.S(50), g, Icerik.Height - Tema.S(50) - Tema.S(40));
            durum = Yazi.Etiket("", 9.5f, false, Renk.Yazi2, false); durum.SetBounds(0, Icerik.Height - Tema.S(34), g, Tema.S(30));
            liste.Kutu.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right; durum.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right; rapor.Anchor = hepsiTanidik.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            Icerik.Controls.AddRange(new Control[] { sekme, rapor, hepsiTanidik, liste.Kutu, durum });
            sekme.Degisti += (s, e) => Ciz();
            rapor.Click += (s, e) => RaporAc();
            hepsiTanidik.Click += (s, e) => Degistir("tamam", "hepsi", "");
            Shown += (s, e) => { if (yonetici == null) { durum.Text = "Müdür bilgisayarı kapalı; bu bilgiler orada tutulur."; return; } durum.Text = "Yükleniyor…"; Task.Run(Yukle); };
        }

        void Ui(Action a) { try { if (!IsDisposed) BeginInvoke(a); } catch { } }

        void Yukle()
        {
            try
            {
                var k = Baglanti.Gonder(yonetici.Ip, new Mesaj("INTERNET_KARNE") { ["ay"] = "" }, 15000);
                var r = Baglanti.Gonder(yonetici.Ip, new Mesaj("RADAR_LISTE"), 15000);
                if (!k.Tamam || !r.Tamam) { Ui(() => durum.Text = (k.Tamam ? r : k)["aciklama"].Contains("Bilinmeyen") ? "Müdür bilgisayarındaki Sem güncellenmeli." : (k.Tamam ? r : k)["aciklama"]); return; }
                var kl = Izleme.Satirlar(k.Veri); var rl = Izleme.Satirlar(r.Veri);
                Ui(() => { karne = kl; cihazlar = rl; durum.Text = ""; Ciz(); });
            }
            catch (Exception ex) { Ui(() => durum.Text = "Okunamadı: " + ex.Message); }
        }

        void Ciz()
        {
            bool ag = sekme.Secili == 1;
            rapor.Visible = !ag; hepsiTanidik.Visible = ag && cihazlar != null && cihazlar.Any(x => x.Length >= 7 && x[6] == "y");
            if (karne == null) return;
            liste.Temizle();
            if (ag) CihazlariCiz(); else KarneyiCiz();
            liste.Bitir();
        }

        void KarneyiCiz()
        {
            var tr = new CultureInfo("tr-TR");
            var simdi = karne.FirstOrDefault(x => x[0] == "S");
            DateTime ks;
            if (simdi != null && Izleme.Zaman(simdi[1], out ks)) liste.Metin("İnternet şu an kesik (" + ks.ToString("HH:mm") + "'den beri)", Renk.PembeKoyu, 11f, true, 26);
            else liste.Metin("İnternet şu an bağlı", Renk.YesilKoyu, 11f, true, 26);
            var ay = DateTime.Today.ToString("yyyy-MM");
            var gun = karne.Where(x => x[0] == "G" && x.Length >= 4 && x[1].StartsWith(ay)).ToList();
            long olcum = gun.Sum(x => long.Parse(x[2])), ok = gun.Sum(x => long.Parse(x[3]));
            var kes = karne.Where(x => x[0] == "K" && x.Length >= 4 && x[1].StartsWith(ay)).ToList();
            var top = TimeSpan.Zero; foreach (var k in kes) { DateTime a, b; if (Izleme.Zaman(k[1], out a) && Izleme.Zaman(k[2], out b)) top += b - a; }
            liste.Metin("Bu ay: " + (olcum > 0 ? "%" + (ok * 100.0 / olcum).ToString("0.0", tr) + " erişilebilir · " : "") + kes.Count + " kesinti" + (kes.Count > 0 ? ", toplam " + Izleme.Sure(top) : ""), Renk.Yazi2);
            var hiz = karne.Where(x => x[0] == "H" && x.Length >= 5).OrderByDescending(x => x[1]).ToList();
            liste.Baslik("Hız testleri (her gün 09:00'dan sonra)");
            if (hiz.Count == 0) liste.Metin("Henüz hız testi yapılmadı. İlk test Müdür bilgisayarı açıkken 09:00'dan sonra yapılır.", Renk.Yazi2);
            foreach (var h in hiz.Take(14))
            {
                DateTime d; Izleme.Zaman(h[1], out d);
                liste.Ikili("↓ " + Izleme.Ondalik(Izleme.Ciftle(h[2])) + "  ·  ↑ " + Izleme.Ondalik(Izleme.Ciftle(h[3])) + " Mbit/sn  ·  " + h[4] + " ms", d.ToString("d MMMM ddd HH:mm", tr));
            }
            var tumKes = karne.Where(x => x[0] == "K" && x.Length >= 4).OrderByDescending(x => x[1]).ToList();
            liste.Baslik("Kesintiler");
            if (tumKes.Count == 0) liste.Metin("Kesinti kaydı yok.", Renk.Yazi2);
            foreach (var k in tumKes.Take(40))
            {
                DateTime a, b; Izleme.Zaman(k[1], out a); Izleme.Zaman(k[2], out b);
                liste.Ikili(Izleme.Sure(b - a) + (k[3] == "ag" ? " · yerel ağ / modem" : " · internet"), a.ToString("d MMMM ddd HH:mm", tr), k[3] == "ag" ? Renk.Turuncu : Renk.PembeKoyu);
            }
            durum.Text = "Yalnızca Müdür bilgisayarı ölçer: dakikada bir kısa ping, günde bir hız testi.";
        }

        void CihazlariCiz()
        {
            var tr = new CultureInfo("tr-TR");
            var l = cihazlar.Where(x => x.Length >= 7).OrderBy(x => x[6] == "y" ? 0 : 1).ThenByDescending(x => x[5]).ToList();
            int yeni = l.Count(x => x[6] == "y");
            liste.Baslik(l.Count + " cihaz" + (yeni > 0 ? " · " + yeni + " yeni" : ""));
            foreach (var c in l)
            {
                var mac = c[0]; var ip = c[1];
                var sem = pcler.FirstOrDefault(p => p.Ip == ip || (p.Mac != "" && p.Mac.Equals(mac, StringComparison.OrdinalIgnoreCase)));
                var ad = c[2] != "" ? c[2] : sem != null ? sem.Baslik + " (Sem)" : c[3] != "" ? c[3] : "Adsız cihaz";
                DateTime son; Izleme.Zaman(c[5], out son);
                var sonM = (DateTime.Now - son).TotalMinutes < 15 ? "şu an ağda" : son.ToString("d MMM HH:mm", tr);
                liste.Ikili((c[6] == "y" ? "● Yeni · " : "") + ad + "   " + ip, sonM, c[6] == "y" ? Renk.PembeKoyu : (DateTime.Now - son).TotalMinutes < 15 ? Renk.Yazi : Renk.Yazi3, () => Adlandir(mac, c[2], ip));
            }
            durum.Text = "İsim vermek için bir cihaza tıklayın. Ağa yeni bir cihaz katılınca panel haber verir.";
        }

        void Adlandir(string mac, string eski, string ip)
        {
            var ad = Diyalog.Metin(Sahip, "Cihaza ad ver", ip + " · " + mac + "\nÖrn: Modem, Canon yazıcı, Osman'ın telefonu", eski, "Kaydet");
            if (ad == null) return;
            Degistir("ad", mac, ad.Trim());
        }

        void Degistir(string islem, string mac, string ad)
        {
            Task.Run(() =>
            {
                try { Baglanti.Gonder(yonetici.Ip, new Mesaj("RADAR_DEGISTIR") { ["islem"] = islem, ["mac"] = mac, ["ad"] = ad }, 10000); } catch { }
                Yukle();
            });
        }

        void RaporAc()
        {
            var secim = MK.Secim(Sahip, "Aylık rapor", "Hangi ayın raporu açılsın? Tarayıcıda açılır, oradan yazdırabilir ya da PDF kaydedebilirsiniz.",
                ("Bu ay", Izleme.AyAdi(DateTime.Today.ToString("yyyy-MM")), Ik.Takvim), ("Geçen ay", Izleme.AyAdi(DateTime.Today.AddMonths(-1).ToString("yyyy-MM")), Ik.Takvim));
            if (secim < 0) return;
            var ay = DateTime.Today.AddMonths(-secim).ToString("yyyy-MM");
            Task.Run(() =>
            {
                try
                {
                    var k = Baglanti.Gonder(yonetici.Ip, new Mesaj("INTERNET_KARNE") { ["ay"] = ay }, 15000);
                    if (!k.Tamam) { Ui(() => durum.Text = k["aciklama"]); return; }
                    var yol = Izleme.RaporYaz(ay, Izleme.Satirlar(k.Veri));
                    Process.Start(new ProcessStartInfo(yol) { UseShellExecute = true });
                }
                catch (Exception ex) { Ui(() => durum.Text = "Rapor açılamadı: " + ex.Message); }
            });
        }
    }

    // ================================================================== Sorun bildirimleri
    class SorunlarForm : ModernForm
    {
        readonly SatirListesi liste = new SatirListesi();

        public Action Degisti;
        public SorunlarForm() : base("Sorun bildirimleri", 640, 560)
        {
            SabitBoyut = true;
            liste.Kutu.SetBounds(0, 0, Icerik.Width, Icerik.Height - Tema.S(40));
            var d = Yazi.Etiket("Öğretmenler saatin yanındaki Sem simgesinden \"Sorun bildir\" der; buraya düşer.", 9.5f, false, Renk.Yazi2, false);
            d.SetBounds(0, Icerik.Height - Tema.S(34), Icerik.Width, Tema.S(30));
            liste.Kutu.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right; d.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            Icerik.Controls.AddRange(new Control[] { liste.Kutu, d });
            Shown += (s, e) => Ciz();
        }

        void Ciz()
        {
            liste.Temizle();
            var l = Izleme.Sorunlar();
            var tr = new CultureInfo("tr-TR");
            if (l.Count == 0) liste.Metin("Bekleyen sorun bildirimi yok.", Renk.Yazi2);
            foreach (var x in l)
            {
                DateTime z; Izleme.Zaman(x.Zaman, out z);
                var not = x.Not.Trim() == "" ? "(not yok, ekran görüntüsü)" : x.Not.Replace("\r", " ").Replace("\n", " ");
                liste.Ikili(x.Pc + ": " + not, z.ToString("d MMM HH:mm", tr), null, () => { using (var f = new SorunDetayForm(x)) if (f.ShowDialog(Sahip) == DialogResult.OK) { Ciz(); Degisti?.Invoke(); } });
            }
            liste.Bitir();
        }
    }

    class SorunDetayForm : ModernForm
    {
        public SorunDetayForm(Izleme.Sorun x) : base(x.Pc + " · sorun bildirimi", 720, 620)
        {
            SabitBoyut = true;
            int g = Icerik.Width; int y = 0;
            DateTime z; Izleme.Zaman(x.Zaman, out z);
            var ust = Yazi.Etiket(z.ToString("d MMMM yyyy dddd, HH:mm", new CultureInfo("tr-TR")) + (x.Kullanici != "" ? " · " + x.Kullanici : ""), 9.5f, false, Renk.Yazi2, false);
            ust.SetBounds(0, y, g, Tema.S(22)); y += Tema.S(26);
            var not = Yazi.Etiket(x.Not.Trim() == "" ? "(not yazılmamış)" : x.Not, 11f, true, Renk.Yazi, false); not.SetBounds(0, y, g, Tema.S(48)); y += Tema.S(52);
            var yz = Yazi.Etiket("Yazıcı: " + (x.Yazici == "" ? "-" : x.Yazici) + (x.Saglik != "" ? "\nBilgisayar: " + x.Saglik : ""), 9f, false, Renk.Yazi2, false); yz.SetBounds(0, y, g, Tema.S(52)); y += Tema.S(58);
            Icerik.Controls.AddRange(new Control[] { ust, not, yz });
            if (x.Resim != "")
            {
                Image im = null; try { using (var fs = File.OpenRead(x.Resim)) im = Image.FromStream(new MemoryStream(ReadAll(fs))); } catch { }
                var pb = new PictureBox { Image = im, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Renk.Yuzey, Cursor = Cursors.Hand };
                pb.SetBounds(0, y, g, Icerik.Height - y - Tema.S(52));
                pb.Click += (s, e) => { try { Process.Start(new ProcessStartInfo(x.Resim) { UseShellExecute = true }); } catch { } };
                Icerik.Controls.Add(pb);
                FormClosed += (s, e) => im?.Dispose();
            }
            var cozuldu = Diyalog.AltDugmeler(this, "Çözüldü, kaldır", ButonTuru.Basari, Ik.Tik);
            cozuldu.Click += (s, e) =>
            {
                try { File.Delete(x.Dosya); if (x.Resim != "") File.Delete(x.Resim); } catch { }
                DialogResult = DialogResult.OK; Close();
            };
        }

        static byte[] ReadAll(Stream s) { var ms = new MemoryStream(); s.CopyTo(ms); return ms.ToArray(); }
    }
}
