using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Sem.Ajan;
using Sem.Arayuz;
using Sem.Ortak;
using MK = Sem.Arayuz.MesajKutusu;

namespace Sem.Yonetici
{
    public class AnaForm : Form
    {
        readonly List<Pc> pcler = Depo.ListeOku();
        readonly object pcKilit = new object();
        readonly Dictionary<string, PcKart> kartlar = new Dictionary<string, PcKart>(StringComparer.OrdinalIgnoreCase);
        readonly System.Windows.Forms.Timer yenile;
        int yenileniyor;

        // Görünüm
        readonly Panel sayfaPc, sayfaKayit;
        readonly MenuOgesi menuPc, menuKayit, menuGuncelle;
        readonly Izgara izgara;
        readonly Label altBaslik, bosMesaj, secimYazi;
        readonly OzetKart ozAcik, ozKapali, ozKapanma;
        readonly KartPanel islemCubugu;
        readonly List<Buton> islemDugmeleri = new List<Buton>();
        readonly KayitListesi kayit;
        readonly KartPanel tost; readonly Label tostYazi; readonly System.Windows.Forms.Timer tostZaman;
        readonly Buton tumunuSec;

        public AnaForm()
        {
            Text = "Sem Ajan";
            StartPosition = FormStartPosition.CenterScreen; ClientSize = Tema.S(1240, 800); MinimumSize = Tema.S(980, 640);
            BackColor = Renk.Zemin; Font = Tema.Yazi(10f); DoubleBuffered = true;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            // ------------------------------------------------ Yan menü
            var yan = new Panel { Dock = DockStyle.Left, Width = Tema.S(248), BackColor = Renk.YanMenu, Padding = Tema.Pd(16, 20, 16, 20) };
            yan.Paint += (s, e) => { using (var p = new Pen(Renk.Cizgi)) e.Graphics.DrawLine(p, yan.Width - 1, 0, yan.Width - 1, yan.Height); };
            var logo = new PictureBox { Image = Tema.Logo(), SizeMode = PictureBoxSizeMode.Zoom, Size = Tema.S(48, 48), Location = Tema.P(16, 20), BackColor = Renk.YanMenu };
            var ad = Yazi.Etiket("Sem Ajan", 15f, true); ad.Location = Tema.P(72, 22); ad.BackColor = Renk.YanMenu;
            var ad2 = Yazi.Etiket("Bilgisayar Yönetimi", 9f, false, Renk.Yazi2); ad2.Location = Tema.P(73, 48); ad2.BackColor = Renk.YanMenu;
            yan.Controls.AddRange(new Control[] { logo, ad, ad2 });

            int y = Tema.S(100);
            menuPc = new MenuOgesi("Bilgisayarlar", Ik.Masaustu) { Location = new Point(Tema.S(12), y), Width = yan.Width - Tema.S(24), Aktif = true };
            menuKayit = new MenuOgesi("Etkinlik", Ik.Gecmis) { Location = new Point(Tema.S(12), y + Tema.S(46)), Width = yan.Width - Tema.S(24) };
            var ust2 = Yazi.UstBaslik("Yönetim", Renk.Yazi3); ust2.Location = new Point(Tema.S(24), y + Tema.S(112)); ust2.BackColor = Renk.YanMenu;
            var menuEkle = new MenuOgesi("Bilgisayar ekle", Ik.Ekle) { Location = new Point(Tema.S(12), y + Tema.S(136)), Width = yan.Width - Tema.S(24) };
            var menuSifre = new MenuOgesi("Şifre değiştir", Ik.Anahtar) { Location = new Point(Tema.S(12), y + Tema.S(182)), Width = yan.Width - Tema.S(24) };
            menuGuncelle = new MenuOgesi("Güncellemeler", Ik.Esitle) { Location = new Point(Tema.S(12), y + Tema.S(228)), Width = yan.Width - Tema.S(24) };
            yan.Controls.AddRange(new Control[] { menuPc, menuKayit, ust2, menuEkle, menuSifre, menuGuncelle });

            var slogan = Yazi.Etiket("Her adım bir başarı,\nher çocuk bir dünya.", 9f, false, Renk.Yazi2, false);
            slogan.SetBounds(Tema.S(24), 0, yan.Width - Tema.S(40), Tema.S(44)); slogan.BackColor = Renk.YanMenu;
            var surum = Yazi.Etiket("Sürüm " + Sabitler.Surum, 8f, false, Renk.Yazi3); surum.BackColor = Renk.YanMenu;
            yan.Controls.Add(slogan); yan.Controls.Add(surum);
            yan.Resize += (s, e) => { slogan.Top = yan.Height - Tema.S(86); surum.Location = new Point(Tema.S(24), yan.Height - Tema.S(36)); };

            // ------------------------------------------------ Bilgisayarlar sayfası
            sayfaPc = new Panel { Dock = DockStyle.Fill, BackColor = Renk.Zemin, Padding = Tema.Pd(36, 28, 20, 0) };
            var bas = new Panel { Dock = DockStyle.Top, Height = Tema.S(92), BackColor = Renk.Zemin };
            var eyebrow = Yazi.UstBaslik("Sem Özel Eğitim ve Rehabilitasyon Merkezi"); eyebrow.Location = new Point(0, 0);
            var h1 = Yazi.Etiket("Bilgisayarlar", 22f, true); h1.Location = new Point(-Tema.S(3), Tema.S(18));
            altBaslik = Yazi.Etiket("Bilgisayarlar aranıyor…", 10f, false, Renk.Yazi2); altBaslik.Location = new Point(0, Tema.S(62));
            tumunuSec = new Buton("Tümünü seç", Ik.HepsiniSec, ButonTuru.Ikincil) { Anchor = AnchorStyles.Top | AnchorStyles.Right };
            var yenileBtn = new Buton("", Ik.Yenile, ButonTuru.Ikincil) { Size = Tema.S(40, 40), Anchor = AnchorStyles.Top | AnchorStyles.Right };
            bas.Controls.AddRange(new Control[] { eyebrow, h1, altBaslik, tumunuSec, yenileBtn });
            bas.Resize += (s, e) =>
            {
                yenileBtn.Location = new Point(bas.Width - yenileBtn.Width - Tema.S(16), Tema.S(22));
                tumunuSec.Location = new Point(yenileBtn.Left - tumunuSec.Width - Tema.S(8), Tema.S(22));
            };
            tumunuSec.Click += (s, e) => TumunuSec();
            yenileBtn.Click += (s, e) => Yenile();

            var ozet = new FlowLayoutPanel { Dock = DockStyle.Top, Height = Tema.S(108), BackColor = Renk.Zemin, Padding = Tema.Pd(0, 8, 0, 16), WrapContents = false };
            ozAcik = new OzetKart("Açık bilgisayar", Ik.Masaustu, Renk.Yesil);
            ozKapali = new OzetKart("Kapalı bilgisayar", Ik.Kopuk, Renk.Yazi3);
            ozKapanma = new OzetKart("Bugünkü kapanma", Ik.Guc, Renk.Turuncu);
            ozet.Controls.AddRange(new Control[] { ozAcik, ozKapali, ozKapanma });

            izgara = new Izgara { Dock = DockStyle.Fill, Padding = Tema.Pd(0, 4, 0, 120) };
            izgara.AllowDrop = true;
            izgara.DragEnter += (s, e) => e.Effect = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
            izgara.DragDrop += (s, e) => DosyaGonder((string[])e.Data.GetData(DataFormats.FileDrop));
            bosMesaj = Yazi.Etiket("Henüz bilgisayar bulunamadı.\nDiğer bilgisayarlara Sem kurulu ve açık olmalı. Birkaç saniye içinde burada görünürler.", 11f, false, Renk.Yazi2, false);
            bosMesaj.TextAlign = ContentAlignment.MiddleCenter; bosMesaj.Size = Tema.S(620, 80); bosMesaj.Visible = false;

            // Alt işlem çubuğu (macOS dock benzeri, yüzen)
            islemCubugu = new KartPanel { Height = Tema.S(92), Yaricap = 20, BackColor = Renk.Zemin };
            secimYazi = Yazi.Etiket("Bilgisayar seçin", 10.5f, true); secimYazi.BackColor = Renk.Yuzey;
            secimYazi.Location = new Point(Tema.S(22), Tema.S(20));
            var secimAlt = Yazi.Etiket("Kartlara tıklayarak seçin", 9f, false, Renk.Yazi2); secimAlt.BackColor = Renk.Yuzey; secimAlt.Location = new Point(Tema.S(22), Tema.S(46));
            islemCubugu.Controls.Add(secimYazi); islemCubugu.Controls.Add(secimAlt);
            int bx = Tema.S(200);
            foreach (var (metin, ikon, is_, renk) in new (string, string, Action, Color)[] {
                ("Duyuru", Ik.Megafon, DuyuruGonder, Renk.Mavi),
                ("Dosya gönder", Ik.Yukle, () => DosyaGonder(null), Renk.Mavi),
                ("Dosya al", Ik.Indir, DosyaAl, Renk.Mavi),
                ("Kilitle", Ik.Kilit, Kilitle, Renk.Yazi2),
                ("Yeniden başlat", Ik.Yenile, () => Kapat(true), Renk.Turuncu),
                ("Kapat", Ik.Guc, () => Kapat(false), Renk.Pembe),
                ("Diğer", Ik.Daha, null, Renk.Yazi2) })
            {
                var b = new Buton(metin, ikon, ButonTuru.Sade) { Dikey = true, ForeColorOzel = renk, Font = Tema.Yazi(8.5f) };
                b.BoyutAyarla(); b.Width = Math.Max(b.Width, Tema.S(84)); b.Location = new Point(bx, Tema.S(14)); b.BackColor = Color.Transparent;
                if (is_ != null) b.Click += (s, e) => Guvenli(is_);
                else b.Click += (s, e) => DigerMenu().Show(b, new Point(0, -Tema.S(8)), ToolStripDropDownDirection.AboveRight);
                islemCubugu.Controls.Add(b); islemDugmeleri.Add(b);
                bx += b.Width + Tema.S(2);
            }
            islemCubugu.Width = bx + Tema.S(16);

            // Küçük bildirim (tost)
            tost = new KartPanel { Size = Tema.S(380, 64), Yaricap = 14, Visible = false, BackColor = Renk.Zemin, Dolgu = Renk.Yazi };
            tostYazi = Yazi.Etiket("", 9.75f, false, Color.White, false); tostYazi.BackColor = Renk.Yazi; tostYazi.SetBounds(Tema.S(18), Tema.S(10), tost.Width - Tema.S(36), tost.Height - Tema.S(24));
            tostYazi.TextAlign = ContentAlignment.MiddleLeft;
            tost.Controls.Add(tostYazi);
            tostZaman = new System.Windows.Forms.Timer { Interval = 5000 };
            tostZaman.Tick += (s, e) => { tost.Visible = false; tostZaman.Stop(); };
            tost.Click += (s, e) => { SayfaGoster(false); }; tostYazi.Click += (s, e) => SayfaGoster(false);

            sayfaPc.Controls.Add(izgara); sayfaPc.Controls.Add(ozet); sayfaPc.Controls.Add(bas);
            izgara.Controls.Add(bosMesaj);
            sayfaPc.Controls.Add(islemCubugu); islemCubugu.BringToFront();
            sayfaPc.Controls.Add(tost); tost.BringToFront();
            sayfaPc.Resize += (s, e) => Yerlestir();

            // ------------------------------------------------ Etkinlik sayfası
            sayfaKayit = new Panel { Dock = DockStyle.Fill, BackColor = Renk.Zemin, Padding = Tema.Pd(36, 28, 36, 28), Visible = false };
            var kbas = new Panel { Dock = DockStyle.Top, Height = Tema.S(100), BackColor = Renk.Zemin };
            var ke = Yazi.UstBaslik("Yapılan işlemler"); ke.Location = new Point(0, 0);
            var kh = Yazi.Etiket("Etkinlik", 22f, true); kh.Location = new Point(-Tema.S(3), Tema.S(18));
            var ka = Yazi.Etiket("Bu oturumda gönderilen komutlar ve sonuçları", 10f, false, Renk.Yazi2); ka.Location = new Point(0, Tema.S(62));
            kbas.Controls.AddRange(new Control[] { ke, kh, ka });
            var kkart = new KartPanel { Dock = DockStyle.Fill, Padding = Tema.Pd(4, 10, 4, 14), BackColor = Renk.Zemin };
            kayit = new KayitListesi { Dock = DockStyle.Fill };
            kkart.Controls.Add(kayit);
            sayfaKayit.Controls.Add(kkart); sayfaKayit.Controls.Add(kbas);

            Controls.Add(sayfaPc); Controls.Add(sayfaKayit); Controls.Add(yan);

            menuPc.Click += (s, e) => SayfaGoster(true);
            menuKayit.Click += (s, e) => SayfaGoster(false);
            menuEkle.Click += (s, e) => Guvenli(BilgisayarEkle);
            menuSifre.Click += (s, e) => Guvenli(SifreDegistir);
            menuGuncelle.Click += (s, e) => Guvenli(Guncellemeler);

            yenile = new System.Windows.Forms.Timer { Interval = 10000 };
            yenile.Tick += (s, e) => Yenile();
            Load += (s, e) =>
            {
                Tema.BaslikRengi(Handle, Renk.YanMenu);
                if (!Giris()) { Close(); return; }
                ListeyiCiz(); SecimGuncelle(); Yenile(); yenile.Start();
                Task.Run(() =>
                {
                    try { var s2 = Guncelleme.SonSurum(); if (s2.YeniMi) BeginInvoke((Action)(() => { menuGuncelle.Rozet = "Yeni"; menuGuncelle.Invalidate(); Tost("Yeni sürüm hazır: " + s2.Numara + ". Yan menüden \"Güncellemeler\"e tıklayın."); })); }
                    catch { }
                });
            };
        }

        void SayfaGoster(bool pc)
        {
            sayfaPc.Visible = pc; sayfaKayit.Visible = !pc;
            menuPc.Aktif = pc; menuKayit.Aktif = !pc;
            if (!pc) { menuKayit.Rozet = ""; menuKayit.Invalidate(); }
        }

        void Yerlestir()
        {
            int sol = sayfaPc.Padding.Left, gen = sayfaPc.ClientSize.Width - sol - sayfaPc.Padding.Right;
            islemCubugu.Location = new Point(sol + Math.Max(0, (gen - islemCubugu.Width) / 2), sayfaPc.ClientSize.Height - islemCubugu.Height - Tema.S(20));
            tost.Location = new Point(sayfaPc.ClientSize.Width - tost.Width - Tema.S(28), islemCubugu.Top - tost.Height - Tema.S(12));
            bosMesaj.Margin = new Padding(Math.Max(0, (izgara.ClientSize.Width - bosMesaj.Width) / 2), Tema.S(60), 0, 0);
        }

        void Guvenli(Action a) { try { a(); } catch (Exception ex) { Kaydet("Hata: " + ex.Message, 2); Tost("Hata: " + ex.Message); } }

        ContextMenuStrip DigerMenu()
        {
            var m = new ContextMenuStrip { Renderer = new MenuCizici(), ShowImageMargin = true, Font = Tema.Yazi(10f), Padding = Tema.Pd(4, 6, 4, 6) };
            m.Items.Add(MenuCizici.Oge("Bugün kapanmasın", Ik.Duraklat, (s, e) => Guvenli(BugunKapatma)));
            m.Items.Add(MenuCizici.Oge("Kapanma saatleri", Ik.Takvim, (s, e) => Guvenli(KapanmaSaatleri)));
            m.Items.Add(MenuCizici.Oge("Duvar kâğıdı", Ik.Resim, (s, e) => Guvenli(DuvarKagidi)));
            m.Items.Add(MenuCizici.Oge("Uyandır (aç)", Ik.Gunes, (s, e) => Guvenli(Uyandir)));
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add(MenuCizici.Oge("Yazıcı kur", Ik.Yazici, (s, e) => Guvenli(YaziciKur)));
            m.Items.Add(MenuCizici.Oge("Yazıcıyı onar", Ik.Anahtar_, (s, e) => Guvenli(YaziciOnar)));
            m.Items.Add(MenuCizici.Oge("Program kur", Ik.ProgramEkle, (s, e) => Guvenli(ProgramKur)));
            m.Items.Add(MenuCizici.Oge("Sem Ajan'ı güncelle (dosyadan)", Ik.Esitle, (s, e) => Guvenli(SemGuncelle)));
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add(MenuCizici.Oge("Listeden çıkar", Ik.Sil, (s, e) => Guvenli(ListedenCikar), true));
            return m;
        }

        // ------------------------------------------------------------------ Giriş
        bool Giris()
        {
            var ayar = AjanAyar.Oku();
            if (ayar.Anahtar == null)
            {
                MK.Bilgi(null, "Sem Ajan kurulu değil", "Bu bilgisayarda Sem Ajan kurulu değil. Önce Sem.exe'yi çalıştırıp kurulum yapın.", true);
                return false;
            }
            using (var f = new GirisFormu(s => Sifreleme.Esit(Sifreleme.AnahtarUret(s), ayar.Anahtar)))
            {
                if (f.ShowDialog() != DialogResult.OK) return false;
                Baglanti.Anahtar = Sifreleme.AnahtarUret(f.Sifre);
                return true;
            }
        }

        // ------------------------------------------------------------------ Liste ve yenileme
        void Yenile()
        {
            if (Interlocked.Exchange(ref yenileniyor, 1) == 1) return;
            Task.Run(() =>
            {
                try
                {
                    Dictionary<string, string> bulunan;
                    try { bulunan = Baglanti.Kesfet(); } catch { bulunan = new Dictionary<string, string>(); }
                    lock (pcKilit)
                    {
                        foreach (var kv in bulunan)
                        {
                            var p = pcler.FirstOrDefault(x => x.Ad.Equals(kv.Value, StringComparison.OrdinalIgnoreCase));
                            if (p == null) { p = new Pc { Ad = kv.Value }; pcler.Add(p); }
                            p.Ip = kv.Key;
                        }
                        if (!pcler.Any(x => x.Ad.Equals(Environment.MachineName, StringComparison.OrdinalIgnoreCase)))
                            pcler.Add(new Pc { Ad = Environment.MachineName, Ip = "127.0.0.1" });
                    }
                    List<Pc> kopya; lock (pcKilit) kopya = pcler.ToList();
                    Parallel.ForEach(kopya, new ParallelOptions { MaxDegreeOfParallelism = 16 }, p =>
                    {
                        try
                        {
                            var d = Baglanti.Gonder(p.Ip, new Mesaj("DURUM"), 3000);
                            p.SonDurum = d; p.Acik = true; p.Hata = ""; p.SonGorulme = DateTime.Now;
                            if (d["mac"] != "") p.Mac = d["mac"];
                        }
                        catch (Exception e)
                        {
                            p.Acik = false;
                            p.Hata = e.Message.StartsWith("Şifre") ? e.Message : "";
                        }
                    });
                    lock (pcKilit) Depo.ListeYaz(pcler);
                }
                finally
                {
                    Interlocked.Exchange(ref yenileniyor, 0);
                    try { BeginInvoke((Action)ListeyiCiz); } catch { }
                }
            });
        }

        void ListeyiCiz()
        {
            List<Pc> kopya; lock (pcKilit) kopya = pcler.OrderBy(p => p.Ad).ToList();
            izgara.SuspendLayout();
            foreach (var p in kopya)
            {
                PcKart k;
                if (!kartlar.TryGetValue(p.Ad, out k))
                {
                    k = new PcKart(p);
                    k.SecimDegisti += (s, e) => SecimGuncelle();
                    k.ContextMenuStrip = DigerMenu();
                    kartlar[p.Ad] = k; izgara.Controls.Add(k);
                }
                k.Pc = p; k.Invalidate();
            }
            foreach (var ad in kartlar.Keys.ToList())
                if (!kopya.Any(p => p.Ad.Equals(ad, StringComparison.OrdinalIgnoreCase))) { izgara.Controls.Remove(kartlar[ad]); kartlar[ad].Dispose(); kartlar.Remove(ad); }
            // Sıralama: ada göre
            int i = 1;
            foreach (var p in kopya) izgara.Controls.SetChildIndex(kartlar[p.Ad], i++);
            bosMesaj.Visible = kopya.Count == 0;
            izgara.ResumeLayout();

            int acik = kopya.Count(p => p.Acik);
            ozAcik.Deger = acik.ToString(); ozKapali.Deger = (kopya.Count - acik).ToString();
            var saatler = kopya.Where(p => p.Acik && p.SonDurum != null && p.SonDurum["otomatik"] != "0").Select(p => p.SonDurum["kapanma"]).Where(s => s.Length >= 5 && char.IsDigit(s[0])).Select(s => s.Substring(0, 5)).ToList();
            ozKapanma.Deger = saatler.Count == 0 ? "—" : saatler.GroupBy(s => s).OrderByDescending(gr => gr.Count()).First().Key;
            ozKapanma.Etiket = saatler.Distinct().Count() > 1 ? "Bugünkü kapanma (çoğunluk)" : "Bugünkü kapanma";
            ozAcik.Invalidate(); ozKapali.Invalidate(); ozKapanma.Invalidate();
            altBaslik.Text = kopya.Count == 0 ? "Ağdaki Sem bilgisayarları aranıyor…" : $"{kopya.Count} bilgisayar  ·  son kontrol {DateTime.Now:HH:mm:ss}";
            Yerlestir();
        }

        void SecimGuncelle()
        {
            var n = kartlar.Values.Count(k => k.Secili);
            secimYazi.Text = n == 0 ? "Bilgisayar seçin" : n + " bilgisayar seçili";
            secimYazi.ForeColor = n == 0 ? Renk.Yazi2 : Renk.Yazi;
            foreach (var b in islemDugmeleri) b.Enabled = n > 0;
            tumunuSec.Text = kartlar.Count > 0 && kartlar.Values.All(k => k.Secili) ? "Seçimi kaldır" : "Tümünü seç";
            tumunuSec.BoyutAyarla(); Yerlestir();
            var bas = tumunuSec.Parent; if (bas != null) { var yb = bas.Controls.OfType<Buton>().First(b => b != tumunuSec); tumunuSec.Left = yb.Left - tumunuSec.Width - Tema.S(8); }
        }

        void Tost(string s)
        {
            if (InvokeRequired) { BeginInvoke((Action)(() => Tost(s))); return; }
            tostYazi.Text = s; tost.Visible = true; tost.BringToFront(); tostZaman.Stop(); tostZaman.Start();
        }

        void Kaydet(string s, int tur = 0)
        {
            if (InvokeRequired) { BeginInvoke((Action)(() => Kaydet(s, tur))); return; }
            kayit.Items.Insert(0, new KayitListesi.Oge { Zaman = DateTime.Now, Metin = s, Tur = tur });
            if (!sayfaKayit.Visible && tur == 2) { menuKayit.Rozet = "!"; menuKayit.Invalidate(); }
        }

        List<Pc> Hedefler(bool sadeceAcik = true, bool tekBilgisayar = false)
        {
            var l = kartlar.Values.Where(k => k.Secili).Select(k => k.Pc).ToList();
            if (l.Count == 0) { MK.Bilgi(this, "Bilgisayar seçin", "Önce işlem yapmak istediğiniz bilgisayarların kartına tıklayın.", true); return null; }
            if (tekBilgisayar && l.Count != 1) { MK.Bilgi(this, "Tek bilgisayar seçin", "Bu işlem için yalnızca bir bilgisayar seçin.", true); return null; }
            if (sadeceAcik)
            {
                var kapali = l.Where(p => !p.Acik).ToList();
                if (kapali.Count > 0) Kaydet("Kapalı olduğu için atlandı: " + string.Join(", ", kapali.Select(p => p.Ad)), 2);
                l = l.Where(p => p.Acik).ToList();
                if (l.Count == 0) { MK.Bilgi(this, "Bilgisayarlar kapalı", "Seçtiğiniz bilgisayarların hepsi kapalı.", true); return null; }
            }
            return l;
        }

        /// <summary>Komutu seçili bilgisayarlara aynı anda gönderir; sonucu tost olarak ve Etkinlik sayfasında gösterir.</summary>
        void Uygula(string islem, List<Pc> hedef, Func<Pc, Mesaj> uret, int zamanAsimiMs = 8000)
        {
            if (hedef == null || hedef.Count == 0) return;
            Kaydet(islem + "  →  " + string.Join(", ", hedef.Select(p => p.Ad)));
            Tost(islem + ": " + hedef.Count + " bilgisayara gönderiliyor…");
            Task.Run(() =>
            {
                int ok = 0; var hatalar = new List<string>();
                Parallel.ForEach(hedef, p =>
                {
                    try
                    {
                        var c = Baglanti.Gonder(p.Ip, uret(p), zamanAsimiMs);
                        Kaydet(p.Ad + ": " + c["aciklama"], c.Tamam ? 1 : 2);
                        if (c.Tamam) Interlocked.Increment(ref ok); else lock (hatalar) hatalar.Add(p.Ad);
                    }
                    catch (Exception e) { Kaydet(p.Ad + ": " + e.Message, 2); lock (hatalar) hatalar.Add(p.Ad); }
                });
                Tost(hatalar.Count == 0 ? $"✓  {islem}: {ok} bilgisayarda tamamlandı" : $"{islem}: {ok} tamam, {hatalar.Count} hata ({string.Join(", ", hatalar)}). Ayrıntı için tıklayın.");
                Thread.Sleep(1500);
                try { BeginInvoke((Action)Yenile); } catch { }
            });
        }

        // ------------------------------------------------------------------ İşlemler
        void TumunuSec()
        {
            bool hepsi = kartlar.Values.All(k => k.Secili);
            foreach (var k in kartlar.Values) k.Secili = !hepsi;
        }

        void DuyuruGonder()
        {
            var h = Hedefler(); if (h == null) return;
            using (var f = new DuyuruYazForm(h.Count))
            {
                if (f.ShowDialog(this) != DialogResult.OK) return;
                string b = f.Baslik, m = f.Metin;
                Uygula("Duyuru", h, p => { var x = new Mesaj("DUYURU"); x["baslik"] = b; x["metin"] = m; return x; });
            }
        }

        void DosyaGonder(string[] dosyalar)
        {
            var h = Hedefler(); if (h == null) return;
            if (dosyalar == null)
            {
                using (var o = new OpenFileDialog { Multiselect = true, Title = "Gönderilecek dosyaları seçin" })
                    if (o.ShowDialog(this) != DialogResult.OK) return; else dosyalar = o.FileNames;
            }
            dosyalar = dosyalar.Where(File.Exists).ToArray();
            if (dosyalar.Length == 0) { Tost("Klasör gönderilemez, lütfen dosya seçin."); return; }
            if (MK.Sor(this, "Dosya gönderilsin mi?", dosyalar.Length + " dosya, " + h.Count + " bilgisayarın masaüstündeki \"Sem Gelen Dosyalar\" klasörüne gönderilecek.\n\n" + string.Join("\n", dosyalar.Take(5).Select(Path.GetFileName)) + (dosyalar.Length > 5 ? "\n…" : ""), "Gönder") != true) return;
            foreach (var f in dosyalar)
            {
                if (new FileInfo(f).Length > Sabitler.MaxCerceve - 1024 * 1024) { Kaydet(Path.GetFileName(f) + ": çok büyük (en fazla 400 MB)", 2); continue; }
                var veri = File.ReadAllBytes(f);
                var ad = Path.GetFileName(f);
                Uygula("Dosya: " + ad, h, p => { var m = new Mesaj("DOSYA_GONDER") { Veri = veri }; m["ad"] = ad; return m; }, 120000);
            }
        }

        void DosyaAl()
        {
            var h = Hedefler(true, true); if (h == null) return;
            using (var f = new DosyaTarayici(h[0])) f.ShowDialog(this);
        }

        void Kapat(bool yeniden)
        {
            var h = Hedefler(); if (h == null) return;
            string fiil = yeniden ? "yeniden başlatılsın" : "kapatılsın";
            int s = MK.Secim(this, yeniden ? "Yeniden başlat" : "Kapat",
                h.Count + " bilgisayar " + fiil + ": " + string.Join(", ", h.Select(p => p.Ad)),
                ("1 dakika uyarı göster", "Ekranda geri sayım çıkar, kişi işini kaydedebilir", Ik.Zamanlayici),
                ("Hemen", "Açık dosyalar kaydedilmeden kapanır", yeniden ? Ik.Yenile : Ik.Guc));
            if (s < 0) return;
            Uygula(yeniden ? "Yeniden başlat" : "Kapat", h, p => { var m = new Mesaj("KAPAT"); m["tip"] = yeniden ? "yeniden" : "kapat"; m["gecikme"] = s == 0 ? "60" : "0"; return m; });
        }

        void Kilitle()
        {
            var h = Hedefler(); if (h == null) return;
            Uygula("Kilitle", h, p => new Mesaj("KILITLE"));
        }

        void Uyandir()
        {
            var h = Hedefler(false); if (h == null) return;
            foreach (var p in h)
            {
                try { Baglanti.Uyandir(p.Mac); Kaydet(p.Ad + ": açma sinyali gönderildi", 1); }
                catch (Exception e) { Kaydet(p.Ad + ": " + e.Message, 2); }
            }
            Tost("Açma sinyali gönderildi. Bilgisayarın ayarlarında Wake-on-LAN açıksa 1-2 dakikada açılır.");
        }

        void KapanmaSaatleri()
        {
            var h = Hedefler(); if (h == null) return;
            using (var f = new KapanmaForm(h[0].SonDurum ?? new Mesaj(), h.Count))
            {
                if (f.ShowDialog(this) != DialogResult.OK) return;
                var m = f.Sonuc;
                Uygula("Kapanma saatleri", h, p => m);
            }
        }

        void BugunKapatma()
        {
            var h = Hedefler(); if (h == null) return;
            int s = MK.Secim(this, "Bugünkü kapanma", "Seçili " + h.Count + " bilgisayar bugün ne yapsın?",
                ("Bugün kapanmasın", "Yarın normal saatine döner", Ik.Duraklat),
                ("Normal saatinde kapansın", "Daha önce verilen iptali geri alır", Ik.Guc));
            if (s < 0) return;
            Uygula(s == 0 ? "Bugün kapanmasın" : "Normal saatinde kapansın", h, p => { var m = new Mesaj("BUGUN_KAPATMA"); m["deger"] = s == 0 ? "1" : "0"; return m; });
            if (s == 0) Uygula("Bekleyen kapatmayı iptal", h, p => new Mesaj("KAPAT_IPTAL"));
        }

        void DuvarKagidi()
        {
            var h = Hedefler(); if (h == null) return;
            int s = MK.Secim(this, "Duvar kâğıdı", "Seçili bilgisayarlara hangi duvar kâğıdı uygulansın? Biri değiştirirse program geri getirir.",
                ("Sem logolu duvar kâğıdı", "Kurumun hazır duvar kâğıdı", Ik.Ev),
                ("Bilgisayardan resim seç…", "JPG, PNG ya da BMP", Ik.Resim));
            byte[] veri;
            if (s == 0)
                using (var r = typeof(AnaForm).Assembly.GetManifestResourceStream("duvar.png"))
                using (var ms = new MemoryStream()) { r.CopyTo(ms); veri = ms.ToArray(); }
            else if (s == 1)
                using (var o = new OpenFileDialog { Filter = "Resimler|*.jpg;*.jpeg;*.png;*.bmp", Title = "Duvar kâğıdı seçin" })
                {
                    if (o.ShowDialog(this) != DialogResult.OK) return;
                    veri = File.ReadAllBytes(o.FileName);
                }
            else return;
            Uygula("Duvar kâğıdı", h, p => { var m = new Mesaj("DUVAR") { Veri = veri }; m["koru"] = "1"; return m; }, 30000);
        }

        void YaziciKur()
        {
            var h = Hedefler(); if (h == null) return;
            using (var f = new YaziciForm())
            {
                if (f.ShowDialog(this) != DialogResult.OK) return;
                var tmp = Path.Combine(Path.GetTempPath(), "sem_yazici_" + Guid.NewGuid().ToString("N") + ".zip");
                ZipFile.CreateFromDirectory(f.Klasor, tmp, CompressionLevel.Optimal, false);
                var veri = File.ReadAllBytes(tmp);
                File.Delete(tmp);
                string ip = f.Ip, ad = f.Ad, surucu = f.Surucu;
                Uygula("Yazıcı kur", h, p => { var m = new Mesaj("YAZICI_KUR") { Veri = veri }; m["ip"] = ip; m["ad"] = ad; m["surucu"] = surucu; return m; }, 180000);
            }
        }

        void YaziciOnar()
        {
            var h = Hedefler(); if (h == null) return;
            Uygula("Yazıcıyı onar", h, p => new Mesaj("YAZICI_ONAR"));
        }

        void ProgramKur()
        {
            var h = Hedefler(); if (h == null) return;
            string dosya;
            using (var o = new OpenFileDialog { Filter = "Kurulum dosyaları|*.exe;*.msi", Title = "Kurulacak programı seçin" })
            {
                if (o.ShowDialog(this) != DialogResult.OK) return;
                dosya = o.FileName;
            }
            bool msi = dosya.EndsWith(".msi", StringComparison.OrdinalIgnoreCase);
            var arg = Diyalog.Metin(this, "Program kur",
                msi ? "MSI dosyası sessiz kurulacak. Ek parametre gerekmiyorsa boş bırakın."
                    : "Sessiz kurulum parametresi programın sitesinde yazar. Çoğu için /S, /silent ya da /quiet olur.",
                msi ? "" : "/S", "Kur");
            if (arg == null) return;
            var veri = File.ReadAllBytes(dosya);
            var ad = Path.GetFileName(dosya);
            Uygula("Program kur: " + ad, h, p => { var m = new Mesaj("PROGRAM_KUR") { Veri = veri }; m["ad"] = ad; m["arguman"] = arg; return m; }, 180000);
        }

        void Guncellemeler()
        {
            List<Pc> acik; lock (pcKilit) acik = pcler.Where(p => p.Acik).ToList();
            bool otoAcik = acik.Select(p => p.SonDurum?["oto_guncelle"]).FirstOrDefault(x => !string.IsNullOrEmpty(x)) != "0";
            using (var f = new GuncellemeForm(otoAcik))
            {
                if (f.ShowDialog(this) != DialogResult.OK) return;
                if (f.OtoGuncelle != null && f.OtoGuncelle.Value != otoAcik)
                {
                    var v = f.OtoGuncelle.Value ? "1" : "0";
                    Uygula(f.OtoGuncelle.Value ? "Otomatik güncelleme açıldı" : "Otomatik güncelleme kapatıldı", acik, p => { var m = new Mesaj("AYAR"); m["oto_guncelle"] = v; return m; });
                }
                if (f.DosyadanSec) { SemGuncelle(); return; }
                if (f.Exe == null) return;
                var exe = f.Exe;
                // Bu bilgisayar en sona: kendi ajanı da güncellenir; yönetim ekranı açık kalır, sonra yeniden açılması önerilir.
                Uygula("Sem Ajan " + f.YeniSurum + " sürümüne güncelleniyor", acik, p => new Mesaj("GUNCELLE") { Veri = exe }, 60000);
                menuGuncelle.Rozet = ""; menuGuncelle.Invalidate();
                Kaydet("Güncelleme bitince Sem Ajan'ı kapatıp yeniden açın, yeni görünüm ve özellikler gelsin.", 0);
            }
        }

        void SemGuncelle()
        {
            List<Pc> h;
            if (kartlar.Values.Any(k => k.Secili)) h = Hedefler();
            else { lock (pcKilit) h = pcler.Where(p => p.Acik).ToList(); }
            if (h == null || h.Count == 0) { Tost("Açık bilgisayar yok."); return; }
            string dosya;
            using (var o = new OpenFileDialog { Filter = "Sem programı|Sem.exe;*.exe", Title = "Yeni Sem.exe dosyasını seçin" })
            {
                if (o.ShowDialog(this) != DialogResult.OK) return;
                dosya = o.FileName;
            }
            var vi = FileVersionInfo.GetVersionInfo(dosya);
            if (vi.ProductName != "Sem") { MK.Bilgi(this, "Geçersiz dosya", "Bu dosya Sem programı değil.", true); return; }
            if (MK.Sor(this, "Sem güncellensin mi?", h.Count + " bilgisayardaki Sem, " + vi.ProductVersion + " sürümüne güncellenecek. Ayarlar korunur.", "Güncelle") != true) return;
            var veri = File.ReadAllBytes(dosya);
            Uygula("Sem güncelle (" + vi.ProductVersion + ")", h, p => new Mesaj("GUNCELLE") { Veri = veri }, 60000);
        }

        void BilgisayarEkle()
        {
            var ip = Diyalog.Metin(this, "Bilgisayar ekle", "Liste kendiliğinden dolar. Bir bilgisayar görünmüyorsa IP adresini yazın (ör. 192.168.1.25).", "", "Ekle");
            if (string.IsNullOrWhiteSpace(ip)) return;
            ip = ip.Trim();
            Task.Run(() =>
            {
                try
                {
                    var d = Baglanti.Gonder(ip, new Mesaj("DURUM"), 4000);
                    lock (pcKilit)
                    {
                        var p = pcler.FirstOrDefault(x => x.Ad == d["ad"]);
                        if (p == null) pcler.Add(p = new Pc { Ad = d["ad"] });
                        p.Ip = ip; p.Mac = d["mac"]; p.SonDurum = d; p.Acik = true; p.SonGorulme = DateTime.Now;
                        Depo.ListeYaz(pcler);
                    }
                    Kaydet(d["ad"] + " eklendi", 1); Tost("✓  " + d["ad"] + " eklendi");
                    BeginInvoke((Action)ListeyiCiz);
                }
                catch (Exception e) { Kaydet(ip + ": eklenemedi (" + e.Message + ")", 2); Tost(ip + " eklenemedi. Sem kurulu ve bilgisayar açık mı?"); }
            });
        }

        void ListedenCikar()
        {
            var h = Hedefler(false); if (h == null) return;
            if (MK.Sor(this, "Listeden çıkarılsın mı?", string.Join(", ", h.Select(p => p.Ad)) + "\n\nSem açık olduğu sürece tekrar görünür. Tamamen kaldırmak için o bilgisayarda Sem.exe'yi çalıştırıp \"Kaldır\" deyin.", "Çıkar", true) != true) return;
            lock (pcKilit) { pcler.RemoveAll(p => h.Contains(p)); Depo.ListeYaz(pcler); }
            ListeyiCiz(); SecimGuncelle();
        }

        void SifreDegistir()
        {
            using (var f = new SifreDegistirForm(Baglanti.Anahtar))
            {
                if (f.ShowDialog(this) != DialogResult.OK) return;
                List<Pc> hepsi; lock (pcKilit) hepsi = pcler.ToList();
                var yeni = Sifreleme.AnahtarUret(f.YeniSifre);
                var eskiAnahtar = Baglanti.Anahtar;
                Kaydet("Şifre değiştiriliyor");
                Task.Run(() =>
                {
                    bool buPc = false;
                    Parallel.ForEach(hepsi.Where(p => p.Acik), p =>
                    {
                        try
                        {
                            var c = Baglanti.Gonder(p.Ip, new Mesaj("SIFRE") { Veri = yeni });
                            Kaydet(p.Ad + ": " + (c.Tamam ? "şifre güncellendi" : c["aciklama"]), c.Tamam ? 1 : 2);
                            if (c.Tamam && p.Ad.Equals(Environment.MachineName, StringComparison.OrdinalIgnoreCase)) buPc = true;
                        }
                        catch (Exception e) { Kaydet(p.Ad + ": " + e.Message, 2); }
                    });
                    Depo.EskiAnahtarEkle(eskiAnahtar);
                    Baglanti.Anahtar = yeni;
                    Tost(buPc ? "✓  Yeni şifre geçerli. Kapalı bilgisayarlar açılınca otomatik güncellenir."
                              : "Dikkat: bu bilgisayarın şifresi değiştirilemedi. Ayrıntı Etkinlik sayfasında.");
                });
            }
        }
    }
}
