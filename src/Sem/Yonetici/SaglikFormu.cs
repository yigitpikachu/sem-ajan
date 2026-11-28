// Merkez > Bilgisayar sağlığı: her bilgisayarın Teşhis özeti tek ekranda; seçilenlere toplu bakım.
// Arka planda 6 saatte bir açık bilgisayarlar taranır, yeni ciddi sorun Etkinlik'e düşer.
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Sem.Arayuz;
using Sem.Ortak;
using MK = Sem.Arayuz.MesajKutusu;

namespace Sem.Yonetici
{
    class SaglikDurumu
    {
        public DateTime Zaman; public List<Bulgu> Bulgular = new List<Bulgu>(); public List<IslemKaydi> Islemler = new List<IslemKaydi>(); public string Hata = "";
        public int Seviye => Hata != "" ? -1 : Bulgular.Where(b => b.Olasilik >= 0).Select(b => b.Olasilik).DefaultIfEmpty(-1).Max();
        public Bulgu EnOnemli => Bulgular.Where(b => b.Olasilik >= 0).OrderByDescending(b => b.Olasilik).FirstOrDefault();
    }

    static class SaglikTarama
    {
        public static readonly ConcurrentDictionary<string, SaglikDurumu> Son = new ConcurrentDictionary<string, SaglikDurumu>(StringComparer.OrdinalIgnoreCase);

        public static SaglikDurumu Tara(Pc p)
        {
            var d = new SaglikDurumu { Zaman = DateTime.Now };
            try
            {
                var m = Baglanti.Gonder(p.Ip, new Mesaj("PERF_VERI"), 45000);
                if (!m.Tamam) d.Hata = m["aciklama"].Contains("Bilinmeyen") ? "Sem güncellenmeli" : m["aciklama"];
                else d.Bulgular = Teshis.Incele(m, PerfOlcum.Coz(m.Veri), "", null, null);
                try { var r = Baglanti.Gonder(p.Ip, new Mesaj("ISLEMLER"), 15000); if (r.Tamam) d.Islemler = IslemKaydi.Coz(r.Veri); } catch { }
            }
            catch (Exception e) { d.Hata = "Ulaşılamadı: " + e.Message; }
            Son[p.Ad] = d;
            return d;
        }

        // Aynı sorun 7 günde bir bildirilir
        static string BildirimDosya => Path.Combine(Depo.Klasor, "saglik_bildirim.txt");
        static readonly object k = new object();

        /// <summary>Yeni ciddi bulguları döner (daha önce bildirilmemiş olanlar) ve kaydeder.</summary>
        public static List<string> YeniCiddi(Pc p, SaglikDurumu d)
        {
            var yeni = new List<string>();
            lock (k)
            {
                var eski = new Dictionary<string, DateTime>();
                try { foreach (var x in File.ReadAllLines(BildirimDosya)) { var a = x.Split('|'); DateTime z; if (a.Length == 2 && DateTime.TryParse(a[1], CultureInfo.InvariantCulture, DateTimeStyles.None, out z)) eski[a[0]] = z; } } catch { }
                foreach (var b in d.Bulgular.Where(x => x.Olasilik == 2))
                {
                    var anahtar = p.Ad + ":" + (b.Baslik.Contains("(") ? b.Baslik.Substring(0, b.Baslik.IndexOf('(')).Trim() : b.Baslik);
                    DateTime z;
                    if (eski.TryGetValue(anahtar, out z) && (DateTime.Now - z).TotalDays < 7) continue;
                    eski[anahtar] = DateTime.Now; yeni.Add(b.Baslik);
                }
                if (yeni.Count > 0)
                    try { Directory.CreateDirectory(Depo.Klasor); File.WriteAllLines(BildirimDosya, eski.Where(x => (DateTime.Now - x.Value).TotalDays < 30).Select(x => x.Key + "|" + x.Value.ToString("o", CultureInfo.InvariantCulture))); } catch { }
            }
            return yeni;
        }

        public static int SorunluSayisi(IEnumerable<Pc> pcler) => pcler.Count(p => Son.TryGetValue(p.Ad, out var d) && d.Seviye == 2);
    }

    class SaglikForm : ModernForm
    {
        readonly List<Pc> pcler; readonly Action<string, int> kaydet; readonly Action degisti;
        readonly SatirListesi liste = new SatirListesi();
        readonly HashSet<string> secili = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        readonly HashSet<string> taraniyor = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        readonly Label durum; readonly Panel arac;
        readonly System.Windows.Forms.Timer canli = new System.Windows.Forms.Timer { Interval = 5000 };

        public SaglikForm(List<Pc> pcler, Action<string, int> kaydet, Action degisti) : base("Bilgisayar sağlığı", 760, 580)
        {
            SabitBoyut = true; this.pcler = pcler.OrderBy(p => p.Baslik).ToList(); this.kaydet = kaydet; this.degisti = degisti;
            int g = Icerik.Width;
            arac = new Panel { BackColor = Renk.Zemin, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
            arac.SetBounds(0, 0, g, Tema.S(84));
            Icerik.Resize += (s, e) => AraclariDiz();
            liste.Kutu.SetBounds(0, Tema.S(92), g, Icerik.Height - Tema.S(92) - Tema.S(36));
            durum = Yazi.Etiket("", 9.5f, false, Renk.Yazi2, false); durum.SetBounds(0, Icerik.Height - Tema.S(30), g, Tema.S(28));
            liste.Kutu.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right; durum.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            Icerik.Controls.AddRange(new Control[] { arac, liste.Kutu, durum });
            AraclariKur();
            Shown += (s, e) => { Ciz(); Yenile(false); };
            // Süren işlem varken satırlar 5 sn'de bir güncellenir
            canli.Tick += (s, e) => CanliGuncelle(); canli.Start(); FormClosed += (s, e) => canli.Dispose();
        }

        void Ui(Action a) { try { if (!IsDisposed && IsHandleCreated) BeginInvoke(a); } catch { } }

        readonly List<Control> aracSirasi = new List<Control>();

        void AraclariKur()
        {
            Buton B(string metin, string ikon, ButonTuru tur, Action tik)
            {
                var b = new Buton(metin, ikon, tur);
                b.Click += (s, e) => tik();
                arac.Controls.Add(b); aracSirasi.Add(b); return b;
            }
            B("Hepsini yeniden tara", Ik.Esitle, ButonTuru.Birincil, () => Yenile(true));
            B("Tümünü seç", Ik.Tik, ButonTuru.Ikincil, () => { var acik = pcler.Where(p => p.Acik).Select(p => p.Ad).ToList(); if (acik.All(secili.Contains)) secili.Clear(); else foreach (var a in acik) secili.Add(a); foreach (var sk in satirlar.Values) sk.Daire.Invalidate(); });
            var l = Yazi.Etiket("Seçilenlere:", 9.5f, true, Renk.Yazi2, false); l.Size = new Size(Tema.S(92), Tema.S(24)); l.TextAlign = ContentAlignment.MiddleLeft;
            arac.Controls.Add(l); aracSirasi.Add(null); aracSirasi.Add(l);
            foreach (var (islem, metin, ikon) in new[] { ("checkup", "Check-up", Ik.Kalp), ("defender", "Virüs taraması", Ik.Kalkan), ("temp", "Geçici dosyalar", Ik.Sil), ("saat", "Saati eşitle", Ik.Takvim), ("sicaklik", "Sıcaklık ölçümü", Ik.Kalp) })
            {
                var isl = islem;
                B(metin, ikon, ButonTuru.Ikincil, () => Toplu(isl));
            }
            arac.Resize += (s, e) => AraclariDiz();
            AraclariDiz();
        }

        /// <summary>Düğmeler pencere genişliğine göre alt satıra kayar; liste aracın altından başlar.</summary>
        void AraclariDiz()
        {
            int x = 0, y = 0, satirH = 0, w = arac.Width;
            foreach (var c in aracSirasi)
            {
                if (c == null) { x = 0; y += satirH + Tema.S(10); satirH = 0; continue; }
                if (x > 0 && x + c.Width > w) { x = 0; y += satirH + Tema.S(8); satirH = 0; }
                satirH = Math.Max(satirH, c.Height);
                c.Location = new Point(x, y); x += c.Width + Tema.S(8);
            }
            // Etiket düğme satırının ortasında
            foreach (var c in aracSirasi.OfType<Label>()) c.Height = aracSirasi[0].Height;
            int h = y + satirH;
            if (arac.Height != h) arac.Height = h;
            int ust = h + Tema.S(10);
            if (liste.Kutu.Top != ust) liste.Kutu.SetBounds(0, ust, liste.Kutu.Width, Icerik.ClientSize.Height - ust - Tema.S(36));
        }

        void Toplu(string islem)
        {
            var hedef = pcler.Where(p => p.Acik && secili.Contains(p.Ad)).ToList();
            if (hedef.Count == 0) { MK.Bilgi(this, "Bilgisayar seçin", "Önce listeden bir ya da daha fazla açık bilgisayarı seçin (satırın solundaki daireye tıklayın)."); return; }
            string metin; TeshisForm.Onayli.TryGetValue(islem, out metin);
            if (islem == "sicaklik") metin = "Seçilen bilgisayarlara açık kaynaklı LibreHardwareMonitor'un imzalı sürücüsü (PawnIO) kurulur; işlemci sıcaklığı ve fan devri ölçülmeye başlar. Her bilgisayarda Geri al ile kaldırılabilir.";
            if (MK.Sor(this, IslemKaydi.Adi(islem, "") + " · " + hedef.Count + " bilgisayar", (metin ?? "İşlem seçilen bilgisayarlarda başlatılır.") + "\n\nİlerleme bu listede ve her bilgisayarın Teşhis'inde görünür.", "Başlat") != true) return;
            durum.Text = hedef.Count + " bilgisayarda başlatılıyor…";
            Task.Run(() =>
            {
                int ok = 0; var hatalar = new ConcurrentBag<string>();
                Parallel.ForEach(hedef, new ParallelOptions { MaxDegreeOfParallelism = 4 }, p =>
                {
                    try
                    {
                        var r = Baglanti.Gonder(p.Ip, new Mesaj("PERF_COZ") { ["islem"] = islem, ["ad"] = "" }, 120000);
                        if (r.Tamam || r["id"] != "") Interlocked.Increment(ref ok); else hatalar.Add(p.Baslik + ": " + r["aciklama"]);
                    }
                    catch (Exception e) { hatalar.Add(p.Baslik + ": " + (e.Message.Contains("Bilinmeyen") ? "Sem güncellenmeli" : e.Message)); }
                    IslemleriAl(p);
                });
                Ui(() =>
                {
                    durum.Text = ok + " bilgisayarda başladı" + (hatalar.Count > 0 ? " · olmayanlar: " + string.Join(", ", hatalar) : "") + ".";
                    Ciz();
                });
            });
        }

        void IslemleriAl(Pc p)
        {
            try
            {
                var r = Baglanti.Gonder(p.Ip, new Mesaj("ISLEMLER"), 15000);
                if (r.Tamam && SaglikTarama.Son.TryGetValue(p.Ad, out var d)) d.Islemler = IslemKaydi.Coz(r.Veri);
                else if (r.Tamam) SaglikTarama.Son[p.Ad] = new SaglikDurumu { Zaman = DateTime.MinValue, Islemler = IslemKaydi.Coz(r.Veri) };
            }
            catch { }
        }

        bool soruluyor;
        void CanliGuncelle()
        {
            if (soruluyor) return;
            var suren = pcler.Where(p => p.Acik && SaglikTarama.Son.TryGetValue(p.Ad, out var d) && d.Islemler.Any(i => i.Suruyor)).ToList();
            if (suren.Count == 0) return;
            soruluyor = true;
            Task.Run(() => { try { Parallel.ForEach(suren, IslemleriAl); } finally { soruluyor = false; Ui(Ciz); } });
        }

        void Yenile(bool hepsi)
        {
            var hedef = pcler.Where(p => p.Acik && (hepsi || !SaglikTarama.Son.TryGetValue(p.Ad, out var d) || (DateTime.Now - d.Zaman).TotalMinutes > 30)).ToList();
            if (hedef.Count == 0) { durum.Text = SonTaramaMetni(); return; }
            foreach (var p in hedef) taraniyor.Add(p.Ad);
            Ciz();
            durum.Text = hedef.Count + " bilgisayar taranıyor…";
            Task.Run(() =>
            {
                Parallel.ForEach(hedef, new ParallelOptions { MaxDegreeOfParallelism = 3 }, p =>
                {
                    var d = SaglikTarama.Tara(p);
                    foreach (var b in SaglikTarama.YeniCiddi(p, d)) kaydet?.Invoke(p.Baslik + ": " + b, 2);
                    Ui(() => { taraniyor.Remove(p.Ad); Guncelle(p); });
                });
                Ui(() => { durum.Text = SonTaramaMetni(); degisti?.Invoke(); });
            });
        }

        string SonTaramaMetni()
        {
            var z = SaglikTarama.Son.Values.Where(d => d.Zaman > DateTime.MinValue).Select(d => d.Zaman).DefaultIfEmpty(DateTime.MinValue).Min();
            return z == DateTime.MinValue ? "" : "Son tarama " + z.ToString("HH:mm") + ". Açık bilgisayarlar 6 saatte bir kendiliğinden taranır; ciddi sorun Etkinlik'e düşer.";
        }

        class SatirKontrol { public Panel Pn, Daire; public Label Ad, Rozet, Alt; public Buton Teshis; public Color Renk; }
        readonly Dictionary<string, SatirKontrol> satirlar = new Dictionary<string, SatirKontrol>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Satırlar bir kez kurulur; sonra yalnızca yazı ve renkleri güncellenir (yanıp sönmez).</summary>
        void Ciz()
        {
            if (satirlar.Count == 0)
            {
                liste.Temizle();
                foreach (var p in pcler) liste.EklePx(Satir(p));
                liste.Bitir();
            }
            foreach (var p in pcler) Guncelle(p);
        }

        Control Satir(Pc p)
        {
            int w = liste.Genislik;
            var kap = new Panel { Width = w, Height = Tema.S(72), BackColor = Renk.Zemin, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
            var pn = new Panel { Width = w, Height = Tema.S(64), BackColor = Renk.Yuzey, Anchor = kap.Anchor, Cursor = Cursors.Hand };
            kap.Controls.Add(pn);
            var sk = new SatirKontrol { Pn = pn, Renk = Renk.Yazi3 };
            var daire = new Panel { BackColor = Renk.Yuzey };
            daire.SetBounds(Tema.S(12), (pn.Height - Tema.S(20)) / 2, Tema.S(20), Tema.S(20));
            daire.Paint += (s, e) =>
            {
                bool sec = secili.Contains(p.Ad);
                Tema.Kaliteli(e.Graphics);
                using (var pen = new Pen(p.Acik ? (sec ? Renk.Mavi : Renk.CizgiKoyu) : Renk.Cizgi, Tema.S(2) / 1.5f)) e.Graphics.DrawEllipse(pen, 1, 1, daire.Width - 3, daire.Height - 3);
                if (sec) using (var br = new SolidBrush(Renk.Mavi)) e.Graphics.FillEllipse(br, Tema.S(5), Tema.S(5), daire.Width - Tema.S(10) - 1, daire.Height - Tema.S(10) - 1);
            };
            daire.Click += (s, e) => { if (!p.Acik) return; if (!secili.Remove(p.Ad)) secili.Add(p.Ad); daire.Invalidate(); };
            sk.Daire = daire;
            sk.Ad = Yazi.Etiket(p.Baslik, 10.5f, true, Renk.Yazi, false); sk.Ad.SetBounds(Tema.S(44), Tema.S(10), w - Tema.S(320), Tema.S(24)); sk.Ad.AutoEllipsis = true; sk.Ad.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            sk.Teshis = new Buton("Teşhis", Ik.Kalp, ButonTuru.Sade);
            sk.Teshis.Location = new Point(w - sk.Teshis.Width - Tema.S(12), (pn.Height - sk.Teshis.Height) / 2); sk.Teshis.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            // Durum yazısı Teşhis düğmesiyle aynı hizada, dikeyde ortalı
            sk.Rozet = Yazi.Etiket("", 9.5f, true, Renk.Yazi3, false);
            sk.Rozet.SetBounds(sk.Teshis.Left - Tema.S(126), (pn.Height - Tema.S(24)) / 2, Tema.S(118), Tema.S(24)); sk.Rozet.TextAlign = ContentAlignment.MiddleRight; sk.Rozet.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            sk.Alt = Yazi.Etiket("", 9.25f, false, Renk.Yazi2, false); sk.Alt.SetBounds(Tema.S(44), Tema.S(36), sk.Rozet.Left - Tema.S(52), Tema.S(20)); sk.Alt.AutoEllipsis = true; sk.Alt.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            Action ac = () =>
            {
                if (!p.Acik) return;
                using (var f = new TeshisForm(p, pcler, kaydet)) f.ShowDialog(Sahip);
                Task.Run(() => { SaglikTarama.Tara(p); Ui(() => { Guncelle(p); degisti?.Invoke(); }); });
            };
            sk.Teshis.Click += (s, e) => ac();
            foreach (var c in new Control[] { pn, sk.Ad, sk.Alt, sk.Rozet }) c.Click += (s, e) => ac();
            pn.Controls.AddRange(new Control[] { daire, sk.Ad, sk.Rozet, sk.Alt, sk.Teshis });
            pn.Paint += (s, e) => { using (var pen = new Pen(Renk.Cizgi)) e.Graphics.DrawRectangle(pen, 0, 0, pn.Width - 1, pn.Height - 1); using (var br = new SolidBrush(sk.Renk)) e.Graphics.FillRectangle(br, 0, 0, Tema.S(3), pn.Height); };
            satirlar[p.Ad] = sk;
            return kap;
        }

        void Guncelle(Pc p)
        {
            if (!satirlar.TryGetValue(p.Ad, out var sk)) return;
            SaglikTarama.Son.TryGetValue(p.Ad, out var d);
            var tr = new CultureInfo("tr-TR");
            bool tar = taraniyor.Contains(p.Ad);
            int sv = d == null || d.Zaman == DateTime.MinValue ? -2 : d.Seviye;
            Color renk; string rozet;
            if (!p.Acik) { renk = Renk.Yazi3; rozet = "Kapalı"; }
            else if (tar) { renk = Renk.Mavi; rozet = "Taranıyor…"; }
            else if (d != null && d.Hata != "") { renk = Renk.Yazi3; rozet = "Okunamadı"; }
            else if (sv == 2) { renk = Renk.PembeKoyu; rozet = "Sorun var"; }
            else if (sv == 1) { renk = Renk.Turuncu; rozet = "Dikkat"; }
            else if (sv >= -1 && d != null) { renk = Renk.YesilKoyu; rozet = "İyi"; }
            else { renk = Renk.Yazi3; rozet = "Taranmadı"; }
            string alt;
            var sur = d?.Islemler.FirstOrDefault(i => i.Suruyor);
            if (sur != null) alt = "Sürüyor: " + IslemKaydi.Adi(sur.Islem, sur.Ad) + (sur.Ilerleme >= 0 ? " %" + sur.Ilerleme : "") + (sur.Asama != "" ? " · " + sur.Asama : "") + " · " + IslemKaydi.Sure(DateTime.Now - sur.Bas);
            else if (d == null || d.Zaman == DateTime.MinValue)
            {
                alt = p.Acik ? (tar ? "Bilgiler alınıyor…" : "Henüz taranmadı") : "Bilgisayar kapalı";
                var son0 = d?.Islemler.FirstOrDefault();
                if (son0 != null) alt += " · son işlem: " + IslemKaydi.Adi(son0.Islem, son0.Ad) + (son0.Durum == "t" ? " ✓" : " (sorunlu: " + son0.Sonuc + ")");
            }
            else if (d.Hata != "") alt = d.Hata;
            else
            {
                var en = d.EnOnemli; int adet = d.Bulgular.Count(b => b.Olasilik >= 1);
                alt = en == null ? "Belirgin bir sorun görünmüyor" : en.Baslik + (adet > 1 ? " · ve " + (adet - 1) + " konu daha" : "");
                alt += " · " + d.Zaman.ToString("d MMM HH:mm", tr) + (p.Acik ? "" : " (kapanmadan önce)");
                var son = d.Islemler.FirstOrDefault(i => !i.Suruyor && (DateTime.Now - i.Bit).TotalHours < 24);
                if (son != null) alt += " · son işlem: " + IslemKaydi.Adi(son.Islem, son.Ad) + (son.Durum == "t" ? " ✓" : " (sorunlu)");
            }
            // Yalnızca değişen yazı/renk güncellenir
            if (sk.Rozet.Text != rozet) sk.Rozet.Text = rozet;
            if (sk.Rozet.ForeColor != renk) sk.Rozet.ForeColor = renk;
            if (sk.Alt.Text != alt) sk.Alt.Text = alt;
            var altRenk = sur != null ? Renk.Mavi : Renk.Yazi2;
            if (sk.Alt.ForeColor != altRenk) sk.Alt.ForeColor = altRenk;
            var adRenk = p.Acik ? Renk.Yazi : Renk.Yazi3;
            if (sk.Ad.ForeColor != adRenk) sk.Ad.ForeColor = adRenk;
            sk.Teshis.Enabled = p.Acik; sk.Pn.Cursor = sk.Daire.Cursor = p.Acik ? Cursors.Hand : Cursors.Default;
            if (sk.Renk != renk) { sk.Renk = renk; sk.Pn.Invalidate(new Rectangle(0, 0, Tema.S(4), sk.Pn.Height)); }
        }
    }
}
