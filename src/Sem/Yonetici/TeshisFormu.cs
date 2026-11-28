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
        static int Tam(Mesaj m, string k) { int n; return int.TryParse(m[k], out n) ? n : 0; }
        static string Ghz(int mhz) => (mhz / 1000.0).ToString("0.0", Tr) + " GHz";
        static int Medyan(IEnumerable<int> l) { var a = l.OrderBy(x => x).ToList(); return a.Count == 0 ? -1 : a[a.Count / 2]; }
        static readonly string[] Windows = { "msmpeng", "tiworker", "trustedinstaller", "wuauserv", "searchindexer", "system", "svchost", "compattelrunner", "mrt", "msiexec" };

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
            int diskOlay = Tam(m, "olay_disk");
            if (diskOlay > 0) b.Add(new Bulgu { Baslik = "Disk hata kaydediyor", Olasilik = 2, Donanim = true, Kanit = "Windows son 7 günde " + diskOlay + " disk hatası kaydetti. Disk: " + m["disk_tur"] + ".", Oneri = "Önemli dosyaların yedeğini alın; disk değişmeye yakın olabilir. Disk taraması başlatılabilir.", Coz = "disk", CozMetin = "Diski tara" });
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
            Parallel.ForEach(adaylar, p => { try { var c = Baglanti.Gonder(p.Ip, new Mesaj("PERF_VERI"), 30000); if (c.Tamam && c["islemci"] == m["islemci"] && m["islemci"] != "") sonuclar.Add((p, c)); } catch { } });
            var en = sonuclar.Select(x => (x.p, x.m, l: PerfOlcum.Coz(x.m.Veri))).OrderByDescending(x => x.l.Count).FirstOrDefault();
            if (en.p != null) { ikizAd = en.p.Baslik; ikiz = en.m; ikizL = en.l; }
            var bulgular = Teshis.Incele(m, l, ikizAd, ikiz, ikizL);
            Ui(() => Ciz(m, l, bulgular, ikizAd));
        }

        void Ciz(Mesaj m, List<PerfOlcum> l, List<Bulgu> bulgular, string ikizAd)
        {
            var tr = new CultureInfo("tr-TR");
            DateTime acilis; var ac = Izleme.Zaman(m["acilis"], out acilis) ? (DateTime.Now - acilis).TotalDays < 1 ? "bugün açıldı" : (int)(DateTime.Now - acilis).TotalDays + " gündür açık" : "";
            ozet.Text = m["islemci"] + " · RAM " + m["ram_detay"] + " · " + m["disk_tur"] + (ac != "" ? " · " + ac : "") +
                        "\n" + l.Count + " ölçüm (son 2 gün)" + (ikizAd != "" ? " · " + ikizAd + " ile karşılaştırıldı" : "");
            grafik.Veri = l; grafik.Invalidate();
            liste.Temizle();
            if (l.Count < 6) liste.Metin("Kayıt yeni başladı (" + l.Count + " ölçüm). Kesin sonuç için bilgisayar birkaç saat açık kalsın, özellikle kasmaya başladıktan sonra tekrar bakın.", Renk.Turuncu, 9.5f, false, 40);
            var asil = bulgular.Where(x => x.Olasilik >= 0).ToList();
            if (asil.Count == 0) liste.Metin("Belirgin bir sorun görünmüyor.", Renk.YesilKoyu, 11f, true, 28);
            foreach (var b in bulgular) liste.EklePx(Kart(b));
            liste.Bitir();
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
                var c = new Buton(b.CozMetin, Ik.Tik, ButonTuru.Birincil) { Location = new Point(Tema.S(14), y) };
                c.Click += (s, e) => Coz(b, c);
                p.Controls.Add(c); y += c.Height + Tema.S(6);
            }
            p.Height = y + Tema.S(10);
            p.Paint += (s, e) => { using (var pen = new Pen(Renk.Cizgi)) e.Graphics.DrawRectangle(pen, 0, 0, p.Width - 1, p.Height - 1); using (var br = new SolidBrush(renk)) e.Graphics.FillRectangle(br, 0, 0, Tema.S(3), p.Height); };
            var kap = new Panel { Width = w, Height = p.Height + Tema.S(10), BackColor = Renk.Zemin, Anchor = p.Anchor };
            kap.Controls.Add(p); p.Location = Point.Empty;
            return Sar(kap);
        }

        // SatirListesi.Ekle yüksekliği sabit istediği için kartın kendi yüksekliğini ileten küçük sarmalayıcı
        Control Sar(Panel kap) { kap.Tag = kap.Height; return kap; }

        void Coz(Bulgu b, Buton c)
        {
            c.Enabled = false; c.Text = "Uygulanıyor…";
            Task.Run(() =>
            {
                string sonuc; bool ok;
                try { var r = Baglanti.Gonder(pc.Ip, new Mesaj("PERF_COZ") { ["islem"] = b.Coz, ["ad"] = b.CozAd }, 120000); ok = r.Tamam; sonuc = r["aciklama"]; }
                catch (Exception ex) { ok = false; sonuc = ex.Message; }
                kaydet?.Invoke(pc.Baslik + ": " + sonuc, ok ? 1 : 2);
                Ui(() => { c.Text = ok ? "✓ " + sonuc : "Olmadı: " + sonuc; c.BoyutAyarla(); c.Enabled = !ok; });
            });
        }
    }
}
