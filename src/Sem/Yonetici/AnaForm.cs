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
        readonly Dictionary<string, GrupBaslik> basliklar = new Dictionary<string, GrupBaslik>();
        readonly System.Windows.Forms.Timer yenile, onizlemeZaman;
        int yenileniyor, onizleniyor;
        YonetimAyar ayarlar = YonetimAyar.Oku();

        // Saatin yanında çalışma
        readonly bool tepsiModu;
        readonly NotifyIcon tepsi;
        bool gorunurIzin, oturumAcik, cikiliyor, tepsiBilgisiVerildi, basladi;

        // Görünüm
        readonly Panel yan, sayfaPc, sayfaDuyuru, sayfaKayit;
        readonly MenuOgesi menuPc, menuDuyuru, menuKayit, menuEkle, menuSifre, menuGuncelle, menuAyar;
        readonly Label yonetimBaslik;
        readonly Izgara izgara;
        readonly Panel altBosluk = new Panel { Height = Tema.S(110), Width = Tema.S(10), Margin = Padding.Empty, BackColor = Renk.Zemin };
        readonly Label altBaslik, bosMesaj, secimYazi, secimAlt, duyuruBos;
        readonly OzetKart ozAcik, ozKapali, ozKapanma;
        readonly KartPanel islemCubugu;
        readonly List<Buton> islemDugmeleri = new List<Buton>();
        readonly KayitListesi kayit;
        readonly DuyuruListesi duyurular;
        readonly KartPanel tost; readonly Label tostYazi; readonly System.Windows.Forms.Timer tostZaman, tostAnim;
        readonly Buton tumunuSec, yenileBtn, kartGorunum, listeGorunum;

        public AnaForm() : this(false, null) { }

        readonly string acJetonu;

        public AnaForm(bool tepsiModu, EventWaitHandle gosterSinyali, string acJetonu = "")
        {
            this.acJetonu = acJetonu ?? "";
            this.tepsiModu = tepsiModu; gorunurIzin = !tepsiModu;
            Text = "Sem Ajan";
            StartPosition = FormStartPosition.CenterScreen; ClientSize = Tema.S(1240, 800); MinimumSize = Tema.S(860, 600);
            BackColor = Renk.Zemin; Font = Tema.Yazi(10f); DoubleBuffered = true;
            // Kenarlıksız, yuvarlak köşeli pencere: kendi küçült/büyüt/kapat düğmeleri, kenardan boyutlandırma
            FormBorderStyle = FormBorderStyle.None; Padding = new Padding(Kenar);
            // Kenara yakın fare olayları (iç denetimlerin üstünde olsa bile) pencereyi boyutlandırır
            Application.AddMessageFilter(new KenarFiltresi(this, () => Tema.S(7)));
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            // ------------------------------------------------ Yan menü
            yan = new Panel { Dock = DockStyle.Left, Width = Tema.S(200), BackColor = Renk.YanMenu };
            var logo = new PictureBox { Image = Tema.Logo(), SizeMode = PictureBoxSizeMode.Zoom, Size = Tema.S(38, 38), Location = Tema.P(16, 22), BackColor = Renk.YanMenu };
            var ad = Yazi.Etiket("Sem Ajan", 13f, true); ad.Location = Tema.P(60, 20); ad.BackColor = Renk.YanMenu;
            var ad2 = Yazi.Etiket("Bilgisayar Yönetimi", 8.75f, false, Renk.Yazi2); ad2.Location = Tema.P(61, 42); ad2.BackColor = Renk.YanMenu;
            yan.Controls.AddRange(new Control[] { logo, ad, ad2 });

            Func<string, string, MenuOgesi> menu = (m, ik) => { var o = new MenuOgesi(m, ik) { Width = yan.Width - Tema.S(20), Left = Tema.S(10), Height = Tema.S(36) }; yan.Controls.Add(o); return o; };
            menuPc = menu("Bilgisayarlar", Ik.Masaustu); menuPc.Aktif = true;
            menuDuyuru = menu("Duyurular", Ik.Megafon);
            menuKayit = menu("Etkinlik", Ik.Gecmis);
            yonetimBaslik = Yazi.UstBaslik("Yönetim", Renk.Yazi3); yonetimBaslik.BackColor = Renk.YanMenu; yan.Controls.Add(yonetimBaslik);
            menuEkle = menu("Bilgisayar ekle", Ik.Ekle);
            menuSifre = menu("Şifre değiştir", Ik.Anahtar);
            menuGuncelle = menu("Güncellemeler", Ik.Esitle);
            menuAyar = menu("Ayarlar", Ik.Ayar);

            var slogan = Yazi.Etiket("Her adım bir başarı,\nher çocuk bir dünya.", 9f, false, Renk.Yazi2, false);
            slogan.SetBounds(Tema.S(20), 0, yan.Width - Tema.S(32), Tema.S(44)); slogan.BackColor = Renk.YanMenu;
            var surum = Yazi.Etiket("Sürüm " + Sabitler.Surum, 8f, false, Renk.Yazi3); surum.BackColor = Renk.YanMenu;
            yan.Controls.Add(slogan); yan.Controls.Add(surum);
            yan.Resize += (s, e) => { slogan.Top = yan.Height - Tema.S(86); surum.Location = new Point(Tema.S(20), yan.Height - Tema.S(36)); };

            // ------------------------------------------------ Bilgisayarlar sayfası
            sayfaPc = new Panel { Dock = DockStyle.Fill, BackColor = Renk.Zemin, Padding = Tema.Pd(28, 24, 16, 0) };
            var bas = new Panel { Dock = DockStyle.Top, Height = Tema.S(92), BackColor = Renk.Zemin };
            var eyebrow = Yazi.UstBaslik("Sem Özel Eğitim ve Rehabilitasyon Merkezi"); eyebrow.Location = new Point(0, 0);
            var h1 = Yazi.Etiket("Bilgisayarlar", 22f, true); h1.Location = new Point(-Tema.S(3), Tema.S(18));
            altBaslik = Yazi.Etiket("Bilgisayarlar aranıyor…", 10f, false, Renk.Yazi2); altBaslik.Location = new Point(0, Tema.S(62));
            tumunuSec = new Buton("Tümünü seç", Ik.HepsiniSec, ButonTuru.Ikincil) { Anchor = AnchorStyles.Top | AnchorStyles.Right };
            yenileBtn = new Buton("", Ik.Yenile, ButonTuru.Ikincil) { Size = Tema.S(40, 40), Anchor = AnchorStyles.Top | AnchorStyles.Right };
            kartGorunum = new Buton("", Ik.Kareler, ButonTuru.Sade) { Size = Tema.S(40, 40), Anchor = AnchorStyles.Top | AnchorStyles.Right };
            listeGorunum = new Buton("", Ik.Liste, ButonTuru.Sade) { Size = Tema.S(40, 40), Anchor = AnchorStyles.Top | AnchorStyles.Right };
            var ipucu = new ToolTip();
            ipucu.SetToolTip(kartGorunum, "Kart görünümü"); ipucu.SetToolTip(listeGorunum, "Liste görünümü"); ipucu.SetToolTip(yenileBtn, "Yenile");
            bas.Controls.AddRange(new Control[] { eyebrow, h1, altBaslik, tumunuSec, yenileBtn, kartGorunum, listeGorunum });
            bas.Resize += (s, e) => UstDugmeleriDiz();
            tumunuSec.Click += (s, e) => TumunuSec();
            yenileBtn.Click += (s, e) => { Yenile(); OnizlemeYenile(); };
            kartGorunum.Click += (s, e) => GorunumDegistir(false);
            listeGorunum.Click += (s, e) => GorunumDegistir(true);

            var ozet = new FlowLayoutPanel { Dock = DockStyle.Top, Height = Tema.S(108), BackColor = Renk.Zemin, Padding = Tema.Pd(0, 8, 0, 16), WrapContents = false };
            ozAcik = new OzetKart("Açık bilgisayar", Ik.Masaustu, Renk.Yesil);
            ozKapali = new OzetKart("Kapalı bilgisayar", Ik.Kopuk, Renk.Yazi3);
            ozKapanma = new OzetKart("Bugünkü kapanma", Ik.Guc, Renk.Turuncu);
            ozet.Controls.AddRange(new Control[] { ozAcik, ozKapali, ozKapanma });

            izgara = new Izgara { Dock = DockStyle.Fill, Padding = Tema.Pd(0, 4, 0, 0) };
            BirakmaHedefi(izgara);
            izgara.Resize += (s, e) => GenislikleriAyarla();
            bosMesaj = Yazi.Etiket("Henüz bilgisayar bulunamadı.\nDiğer bilgisayarlarda Sem Ajan kurulu ve açık olmalı. Birkaç saniye içinde burada görünürler.", 11f, false, Renk.Yazi2, false);
            bosMesaj.TextAlign = ContentAlignment.MiddleCenter; bosMesaj.Size = Tema.S(620, 80); bosMesaj.Visible = false;

            // Alt işlem çubuğu (yüzen)
            islemCubugu = new KartPanel { Height = Tema.S(88), Yaricap = 20, BackColor = Renk.Zemin, Cizgisiz = true, Kirp = true };
            secimYazi = Yazi.Etiket("Bilgisayar seçin", 10.5f, true); secimYazi.BackColor = Renk.Yuzey;
            secimYazi.Location = new Point(Tema.S(22), Tema.S(20));
            secimAlt = Yazi.Etiket("Kartlara tıklayarak seçin", 9f, false, Renk.Yazi2); secimAlt.BackColor = Renk.Yuzey; secimAlt.Location = new Point(Tema.S(22), Tema.S(44));
            islemCubugu.Controls.Add(secimYazi); islemCubugu.Controls.Add(secimAlt);

            // Küçük bildirim (tost)
            tost = new KartPanel { Size = Tema.S(380, 64), Yaricap = 14, Visible = false, BackColor = Renk.Zemin, Dolgu = Renk.Yazi, Cizgisiz = true, Kirp = true };
            tostYazi = Yazi.Etiket("", 9.75f, false, Color.White, false); tostYazi.BackColor = Renk.Yazi; tostYazi.SetBounds(Tema.S(18), Tema.S(8), tost.Width - Tema.S(36), tost.Height - Tema.S(22));
            tostYazi.TextAlign = ContentAlignment.MiddleLeft;
            tost.Controls.Add(tostYazi);
            tostZaman = new System.Windows.Forms.Timer { Interval = 5000 };
            tostZaman.Tick += (s, e) => { tost.Visible = false; tostZaman.Stop(); };
            tostAnim = new System.Windows.Forms.Timer { Interval = 15 };
            tostAnim.Tick += (s, e) =>
            {
                int hedef = TostHedefY();
                tost.Top += (int)Math.Floor((hedef - tost.Top) * 0.35);
                if (Math.Abs(tost.Top - hedef) <= 1) { tost.Top = hedef; tostAnim.Stop(); }
            };
            tost.Click += (s, e) => SayfaGoster(2); tostYazi.Click += (s, e) => SayfaGoster(2);

            sayfaPc.Controls.Add(InceKaydirma.Sar(izgara)); sayfaPc.Controls.Add(ozet); sayfaPc.Controls.Add(bas);
            izgara.Controls.Add(bosMesaj);
            izgara.Controls.Add(altBosluk); // alttaki işlem çubuğu son sıradaki kartları örtmesin
            sayfaPc.Controls.Add(islemCubugu); islemCubugu.BringToFront();
            sayfaPc.Controls.Add(tost); tost.BringToFront();
            sayfaPc.Resize += (s, e) => Yerlestir();

            // ------------------------------------------------ Duyurular sayfası
            sayfaDuyuru = SayfaOlustur("Gönderilen duyurular", "Duyurular", "Duyuruyu kimin okuduğu, \"Tamam\"a basıldığında burada görünür", out var dbas);
            var yeniDuyuru = new Buton("Yeni duyuru", Ik.Megafon, ButonTuru.Birincil) { Anchor = AnchorStyles.Top | AnchorStyles.Right };
            dbas.Controls.Add(yeniDuyuru);
            dbas.Resize += (s, e) => yeniDuyuru.Location = new Point(dbas.Width - yeniDuyuru.Width, Tema.S(22));
            yeniDuyuru.Click += (s, e) => Guvenli(() => { var h = AcikHedefler(); if (h.Count == 0) Tost("Açık bilgisayar yok."); else DuyuruGonder(h); });
            var dkart = new KartPanel { Dock = DockStyle.Fill, Padding = Tema.Pd(4, 8, 4, 12), BackColor = Renk.Zemin, Cizgisiz = true };
            duyurular = new DuyuruListesi { Dock = DockStyle.Fill };
            duyurular.MouseUp += (s, e) => { int i = duyurular.IndexFromPoint(e.Location); if (i >= 0) Guvenli(() => DuyuruDetay((DuyuruKaydi)duyurular.Items[i])); };
            duyuruBos = Yazi.Etiket("Henüz duyuru gönderilmedi.", 10.5f, false, Renk.Yazi3); duyuruBos.BackColor = Renk.Yuzey; duyuruBos.Location = Tema.P(24, 24);
            dkart.Controls.Add(duyuruBos);
            dkart.Controls.Add(InceKaydirma.Sar(duyurular));
            sayfaDuyuru.Controls.Add(dkart); dkart.BringToFront();

            // ------------------------------------------------ Etkinlik sayfası
            sayfaKayit = SayfaOlustur("Yapılan işlemler", "Etkinlik", "Bu oturumda gönderilen komutlar ve sonuçları", out var _);
            var kkart = new KartPanel { Dock = DockStyle.Fill, Padding = Tema.Pd(4, 10, 4, 14), BackColor = Renk.Zemin, Cizgisiz = true };
            kayit = new KayitListesi { Dock = DockStyle.Fill };
            kkart.Controls.Add(InceKaydirma.Sar(kayit));
            sayfaKayit.Controls.Add(kkart); kkart.BringToFront();

            Controls.Add(sayfaPc); Controls.Add(sayfaDuyuru); Controls.Add(sayfaKayit); Controls.Add(yan);
            pencereDugmeleri = new PencereDugmeleri(this) { Anchor = AnchorStyles.Top | AnchorStyles.Right };
            Controls.Add(pencereDugmeleri); pencereDugmeleri.BringToFront();
            // Boş yerden tutup taşıma, çift tıklayınca büyütme (başlık çubuğu yerine)
            foreach (var c in new Control[] { yan, bas, sayfaPc, sayfaDuyuru, sayfaKayit, dbas, logo, ad, ad2 }) TasimaEkle(c);
            foreach (Control c in sayfaKayit.Controls) if (c is Panel && c.Dock == DockStyle.Top) TasimaEkle(c);

            menuPc.Click += (s, e) => SayfaGoster(0);
            menuDuyuru.Click += (s, e) => SayfaGoster(1);
            menuKayit.Click += (s, e) => SayfaGoster(2);
            menuEkle.Click += (s, e) => Guvenli(BilgisayarEkle);
            menuSifre.Click += (s, e) => Guvenli(SifreDegistir);
            menuGuncelle.Click += (s, e) => Guvenli(Guncellemeler);
            menuAyar.Click += (s, e) => Guvenli(AyarlariAc);

            OzellikleriUygula();

            // ------------------------------------------------ Saatin yanındaki simge
            tepsi = new NotifyIcon { Icon = Icon, Text = "Sem Ajan", Visible = true };
            tepsi.MouseUp += (s, e) =>
            {
                if (e.Button == MouseButtons.Left) Guvenli(TepsiPaneli);
                else if (e.Button == MouseButtons.Right)
                    new AcilirMenu().Ekle("Paneli aç", Ik.Ac, () => Guvenli(PaneliAc))
                        .Ekle("Dosya gönder…", Ik.Paylas, () => { try { Process.Start(AjanAyar.KurulumExe, "/paylas"); } catch { } })
                        .Ekle("Sem Dosyalar klasörü", Ik.Klasor, () => { try { Process.Start("explorer.exe", "\"" + Sem.Ajan.Klasorler.Kok + "\""); } catch { } })
                        .Ayrac().Ekle("Sem Ajan'dan çık", Ik.Kapat, Cik).ImlecteGoster(this);
            };
            tepsi.BalloonTipClicked += (s, e) => Guvenli(PaneliAc);

            yenile = new System.Windows.Forms.Timer { Interval = 10000 };
            yenile.Tick += (s, e) => { Yenile(); SurumDenetle(); };
            onizlemeZaman = new System.Windows.Forms.Timer { Interval = 20000 };
            onizlemeZaman.Tick += (s, e) => OnizlemeYenile();

            Load += (s, e) =>
            {
                KoseleriAyarla();
                if (tepsiModu) return;
                if (!Giris()) { Cik(); return; }
                Basla();
            };
            if (tepsiModu) { CreateHandle(); BeginInvoke((Action)TepsideBasla); }

            // Güncellemeden sonra yeni sürümle açılan ekran tarafı "yeni sürüm var" sinyali verir: zamanlayıcıyı beklemeden geç.
            new Thread(() =>
            {
                try
                {
                    using (var ys = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\SemYeniSurum"))
                        while (ys.WaitOne())
                        {
                            PanelKaydi("sinyal geldi");
                            // Sinyal, dosya yerine konmadan biraz önce gelebilir: 2 dakika boyunca 3 saniyede bir bakılır
                            for (int i = 0; i < 40 && !IsDisposed; i++)
                            {
                                BeginInvoke((Action)(() => { sonSurumDenetimi = DateTime.MinValue; SurumDenetle(); }));
                                Thread.Sleep(3000);
                                if (yeniSurumGoruldu != null) break;
                            }
                        }
                }
                catch (Exception ex) { PanelKaydi("sinyal dinlenemedi: " + ex.Message); }
            }) { IsBackground = true }.Start();

            if (gosterSinyali != null)
                new Thread(() =>
                {
                    try { while (gosterSinyali.WaitOne()) BeginInvoke((Action)(() => Guvenli(PaneliAc))); } catch { }
                }) { IsBackground = true }.Start();
        }

        Panel SayfaOlustur(string ust, string baslik, string alt, out Panel bas)
        {
            var sayfa = new Panel { Dock = DockStyle.Fill, BackColor = Renk.Zemin, Padding = Tema.Pd(36, 28, 36, 28), Visible = false };
            bas = new Panel { Dock = DockStyle.Top, Height = Tema.S(100), BackColor = Renk.Zemin };
            var e1 = Yazi.UstBaslik(ust); e1.Location = new Point(0, 0);
            var e2 = Yazi.Etiket(baslik, 22f, true); e2.Location = new Point(-Tema.S(3), Tema.S(18));
            var e3 = Yazi.Etiket(alt, 10f, false, Renk.Yazi2); e3.Location = new Point(0, Tema.S(62));
            bas.Controls.AddRange(new Control[] { e1, e2, e3 });
            sayfa.Controls.Add(bas);
            return sayfa;
        }

        // ------------------------------------------------------------------ Açılış, saatin yanında çalışma
        protected override void SetVisibleCore(bool value)
        {
            if (!gorunurIzin) { value = false; if (!IsHandleCreated) CreateHandle(); }
            base.SetVisibleCore(value);
        }

        void TepsideBasla()
        {
            var ayar = AjanAyar.Oku();
            if (ayar.Anahtar == null) { Cik(); return; }
            Baglanti.Anahtar = ayar.Anahtar; // durum için; işlem yapmadan önce şifre sorulur
            Basla();
            // Güncelleme sonrası yeniden açılış: pencere açıktıysa şifre sormadan geri gelsin
            if (acJetonu != "" && JetonGecerli(acJetonu)) { oturumAcik = true; PaneliAc(); Tost("Sem Ajan " + Sabitler.Surum + " sürümüne geçildi."); }
        }

        static string JetonDosyasi => Path.Combine(Depo.Klasor, "yeniden.jeton");

        static bool JetonGecerli(string jeton)
        {
            try
            {
                if (!File.Exists(JetonDosyasi)) return false;
                bool taze = (DateTime.Now - File.GetLastWriteTime(JetonDosyasi)).TotalSeconds < 90;
                bool esit = File.ReadAllText(JetonDosyasi).Trim() == jeton;
                File.Delete(JetonDosyasi); // tek kullanımlık
                return taze && esit;
            }
            catch { return false; }
        }

        DateTime? yenidenBaslat;

        /// <summary>Yeni sürüme geçer: kendini yeni Sem.exe ile yeniden açar; pencere açıksa ve oturum açıksa şifre sorulmadan geri gelir.</summary>
        void YeniSurumeGec(bool pencereAcik)
        {
            var arg = "/yonetim /tepsi /yeniden";
            if (pencereAcik && oturumAcik)
            {
                var jeton = Guid.NewGuid().ToString("N");
                try { File.WriteAllText(JetonDosyasi, jeton); arg += " /ac:" + jeton; } catch { }
            }
            else if (pencereAcik) arg = "/yonetim /yeniden";
            Process.Start(new ProcessStartInfo(AjanAyar.KurulumExe, arg) { UseShellExecute = false });
            Cik();
        }

        void Basla()
        {
            if (basladi) return;
            basladi = true;
            PanelKaydi("panel başladı: " + Environment.CommandLine);
            ListeyiCiz(); SecimGuncelle(); Yenile(); yenile.Start(); onizlemeZaman.Start();
            Task.Run(() =>
            {
                try { var s2 = Guncelleme.SonSurum(); if (s2.YeniMi) BeginInvoke((Action)(() => { menuGuncelle.Rozet = "Yeni"; menuGuncelle.Invalidate(); Tost("Yeni sürüm hazır: " + s2.Numara + ". Yan menüden \"Güncellemeler\"e tıklayın."); })); }
                catch { }
            });
        }

        /// <summary>Şifre bu oturumda bir kez sorulur.</summary>
        bool Yetki()
        {
            if (oturumAcik) return true;
            return Giris();
        }

        void PaneliAc()
        {
            if (!Yetki()) return;
            gorunurIzin = true;
            if (!Visible) Show();
            if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            Activate(); BringToFront();
            Basla();
            ListeyiCiz(); OnizlemeYenile();
        }

        void Cik()
        {
            cikiliyor = true;
            try { tepsi.Visible = false; tepsi.Dispose(); } catch { }
            Application.Exit();
        }

        // ------------------------------------------------------------------ Kenarlıksız pencere
        int Kenar => Tema.S(5);
        int KoseYaricap => Tema.S(7);
        PencereDugmeleri pencereDugmeleri;
        bool dwmYuvarlak;
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool ReleaseCapture();
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h, int m, IntPtr w, IntPtr l);

        void TasimaEkle(Control c)
        {
            c.MouseDown += (s, e) =>
            {
                if (e.Button != MouseButtons.Left || Environment.OSVersion.Platform != PlatformID.Win32NT) return;
                if (e.Clicks == 2) { PencereDugmeleri.BuyutKucult(this); return; }
                ReleaseCapture(); SendMessage(Handle, 0xA1, (IntPtr)2, IntPtr.Zero); // WM_NCLBUTTONDOWN, HTCAPTION
            };
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.Style |= 0x00020000 | 0x00010000 | 0x00080000; // WS_MINIMIZEBOX | WS_MAXIMIZEBOX | WS_SYSMENU: görev çubuğundan küçültme
                cp.ClassStyle |= 0x20000; // CS_DROPSHADOW
                return cp;
            }
        }

        void KoseleriAyarla()
        {
            if (!IsHandleCreated) return;
            if (!dwmYuvarlak) dwmYuvarlak = Tema.KoseleriYuvarla(Handle); // Windows 11: sistemin yumuşak köşesi
            bool tam = WindowState == FormWindowState.Maximized;
            var pd = new Padding(tam ? 0 : Kenar);
            if (Padding != pd) Padding = pd;
            if (pencereDugmeleri != null) { pencereDugmeleri.Location = new Point(ClientSize.Width - Padding.Right - pencereDugmeleri.Width, Padding.Top); pencereDugmeleri.Invalidate(); }
            if (dwmYuvarlak) return;
            // Windows 10: sahte yuvarlatma yok, pencere düz köşeli
            if (Region != null) { var eski = Region; Region = null; eski.Dispose(); }
        }

        protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); MaximizedBounds = Screen.FromHandle(Handle).WorkingArea; KoseleriAyarla(); }
        protected override void OnResize(EventArgs e) { base.OnResize(e); KoseleriAyarla(); Invalidate(); }
        protected override void OnLocationChanged(EventArgs e) { base.OnLocationChanged(e); if (IsHandleCreated) MaximizedBounds = Screen.FromControl(this).WorkingArea; }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (Padding.Left == 0) return;
            var g = e.Graphics;
            // Kenar payı: yan menünün yanı yan menü renginde, gerisi zemin renginde, kesintisiz görünsün
            using (var b = new SolidBrush(Renk.YanMenu)) g.FillRectangle(b, 0, 0, yan.Right, Height);
            Tema.Kaliteli(g);
            if (dwmYuvarlak) return; // Windows 11 kenarı kendi çizer
            // Windows 10: düz, ince kenar çizgisi
            Tema.PencereKenari(g, Width, Height, false, 0);
        }

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            if (m.Msg == 0x84) // WM_NCHITTEST: kenardan boyutlandırma
            {
                long lp = m.LParam.ToInt64();
                var p = new Point((short)(lp & 0xFFFF), (short)((lp >> 16) & 0xFFFF));
                m.Result = (IntPtr)Tema.KenarBul(this, p, m.Result.ToInt32(), Kenar);
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!cikiliyor && e.CloseReason == CloseReason.UserClosing)
            {
                // Kapat düğmesi pencereyi gizler; Sem Ajan saatin yanında çalışmaya devam eder.
                e.Cancel = true; Hide();
                if (!tepsiBilgisiVerildi) { tepsiBilgisiVerildi = true; tepsi.ShowBalloonTip(5000, "Sem Ajan çalışmaya devam ediyor", "Saatin yanındaki simgeden hızlıca duyuru gönderebilir, paneli açabilirsiniz.", ToolTipIcon.None); }
                return;
            }
            try { tepsi.Visible = false; } catch { }
            base.OnFormClosing(e);
        }

        void TepsiPaneli()
        {
            List<Pc> kopya; lock (pcKilit) kopya = pcler.ToList();
            int acik = kopya.Count(p => p.Acik);
            var f = new TepsiPanel(acik, kopya.Count, ozKapanma.Deger,
                () => Guvenli(() => { if (!Yetki()) return; var h = AcikHedefler(); if (h.Count == 0) Tost("Açık bilgisayar yok."); else DuyuruGonder(h); }),
                () => Guvenli(() => { if (!Yetki()) return; BugunKapanmasin(AcikHedefler(true)); }),
                () => Guvenli(() => { if (!Yetki()) return; var h = AcikHedefler(); if (h.Count == 0) Tost("Kilitlenecek açık bilgisayar yok."); else Uygula("Hepsini kilitle", h, p => new Mesaj("KILITLE")); }),
                () => Guvenli(PaneliAc));
            f.Show();
        }

        /// <summary>Açık olan bilgisayarlar; istenirse bu bilgisayar da dahil.</summary>
        List<Pc> AcikHedefler(bool buDahil = false)
        {
            lock (pcKilit) return pcler.Where(p => p.Acik && (buDahil || !p.Ad.Equals(Environment.MachineName, StringComparison.OrdinalIgnoreCase))).ToList();
        }

        DateTime sonSurumDenetimi = DateTime.MinValue;
        DateTime? yeniSurumGoruldu;
        string engelKaydi = "";
        bool gecisHatasiKaydedildi;
        KartPanel surumKarti;

        /// <summary>Bilgisayar güncellenince (Program Files'taki Sem.exe yenilenince) panel kendini yeni sürümle açar.
        /// Geçemezse "Yeni sürüm hazır" kartı çıkar, tek tıkla yeniden açılır.</summary>
        void SurumDenetle()
        {
            if ((DateTime.Now - sonSurumDenetimi).TotalSeconds < 9) return;
            sonSurumDenetimi = DateTime.Now;
            string yeni = "";
            try
            {
                if (!File.Exists(AjanAyar.KurulumExe)) { PanelKaydi("exe yok: " + AjanAyar.KurulumExe); return; }
                var v = FileVersionInfo.GetVersionInfo(AjanAyar.KurulumExe);
                PanelKaydi("denetim: " + v.ProductName + " " + v.FileMajorPart + "." + v.FileMinorPart + "." + v.FileBuildPart + ", bu " + Sabitler.Surum + ", görünür " + Visible);
                if (v.ProductName != "Sem Ajan" || new Version(v.FileMajorPart, v.FileMinorPart, v.FileBuildPart) <= Version.Parse(Sabitler.Surum)) return;
                yeni = v.FileMajorPart + "." + v.FileMinorPart + "." + v.FileBuildPart;
                if (!Visible) { PanelKaydi("gizli, yeni sürümle açılıyor"); YeniSurumeGec(false); return; }
                if (yeniSurumGoruldu == null) { yeniSurumGoruldu = DateTime.Now; Kaydet("Yeni sürüm kuruldu: " + yeni + "."); PanelKaydi("yeni sürüm kartı"); }
                // Açık panelde hemen "Yeniden aç" kartı: tek tıkla yeni sürüme geçilir
                SurumKartiGoster(yeni);
                return;
            }
            catch (Exception ex)
            {
                PanelKaydi("hata: " + ex);
                if (!gecisHatasiKaydedildi) { gecisHatasiKaydedildi = true; Kaydet("Yeni sürüme geçilemedi: " + ex.Message, 2); }
            }
        }

        string sonPanelKaydi = "";

        /// <summary>Sürüm geçişi teşhisi için %LocalAppData%\Sem\panel.log (aynı satır tekrar yazılmaz, 200 KB'ı geçince sıfırlanır).</summary>
        void PanelKaydi(string s)
        {
            try
            {
                lock (JetonKilit)
                {
                    if (s == sonPanelKaydi) return;
                    sonPanelKaydi = s;
                    var f = Path.Combine(Depo.Klasor, "panel.log");
                    if (File.Exists(f) && new FileInfo(f).Length > 200 * 1024) File.Delete(f);
                    File.AppendAllText(f, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  [" + Sabitler.Surum + " pid " + Process.GetCurrentProcess().Id + "] " + s + Environment.NewLine, System.Text.Encoding.UTF8);
                }
            }
            catch { }
        }
        static readonly object JetonKilit = new object();

        void SurumKartiGoster(string yeni)
        {
            if (surumKarti != null) { surumKarti.Visible = true; surumKarti.BringToFront(); Yerlestir(); return; }
            surumKarti = new KartPanel { Size = Tema.S(380, 112), Yaricap = 14, BackColor = Renk.Zemin, Dolgu = Renk.Yazi, Cizgisiz = true, Kirp = true };
            var b1 = Yazi.Etiket("Yeni sürüm hazır" + (yeni != "" ? "  ·  " + yeni : ""), 10.5f, true, Color.White, false);
            b1.BackColor = Renk.Yazi; b1.SetBounds(Tema.S(18), Tema.S(12), surumKarti.Width - Tema.S(36), Tema.S(24));
            var b2 = Yazi.Etiket("Panel yeniden açılınca yeni sürüm kullanılır.", 9f, false, Color.FromArgb(200, 255, 255, 255), false);
            b2.BackColor = Renk.Yazi; b2.SetBounds(Tema.S(18), Tema.S(36), surumKarti.Width - Tema.S(36), Tema.S(20));
            var ac = new Buton("Yeniden aç", Ik.Yenile, ButonTuru.Birincil) { Height = Tema.S(36) };
            ac.Location = new Point(Tema.S(18), Tema.S(62));
            ac.Click += (s, e) =>
            {
                try { YeniSurumeGec(true); }
                catch (Exception ex) { Kaydet("Yeniden açılamadı: " + ex.Message, 2); Tost("Yeniden açılamadı. Saatin yanındaki simgeden çıkıp Sem Ajan'ı yeniden açın."); }
            };
            surumKarti.Controls.AddRange(new Control[] { b1, b2, ac });
            sayfaPc.Controls.Add(surumKarti); surumKarti.BringToFront();
            Yerlestir();
        }

        // ------------------------------------------------------------------ Görünüm
        void SayfaGoster(int i)
        {
            if (i == 1 && !ayarlar.Duyurular) i = 0;
            sayfaPc.Visible = i == 0; sayfaDuyuru.Visible = i == 1; sayfaKayit.Visible = i == 2;
            menuPc.Aktif = i == 0; menuDuyuru.Aktif = i == 1; menuKayit.Aktif = i == 2;
            if (i == 1) DuyurulariCiz();
            if (i == 2) { menuKayit.Rozet = ""; menuKayit.Invalidate(); }
        }

        /// <summary>Ayarlarda kapatılan özellikleri menülerden kaldırır, yan menüyü ve işlem çubuğunu yeniden dizer.</summary>
        void OzellikleriUygula()
        {
            int y = Tema.S(84);
            foreach (var m in new[] { menuPc, menuDuyuru, menuKayit })
            {
                bool gorunsun = m != menuDuyuru || ayarlar.Duyurular;
                m.Visible = gorunsun;
                if (gorunsun) { m.Top = y; y += Tema.S(40); }
            }
            y += Tema.S(18);
            yonetimBaslik.Location = new Point(Tema.S(20), y); y += Tema.S(22);
            foreach (var m in new[] { menuEkle, menuSifre, menuGuncelle, menuAyar }) { m.Top = y; y += Tema.S(40); }
            if (!ayarlar.Duyurular && sayfaDuyuru.Visible) SayfaGoster(0);

            foreach (var b in islemDugmeleri) { islemCubugu.Controls.Remove(b); b.Dispose(); }
            islemDugmeleri.Clear();
            var liste = new List<(string, string, Action, Color, bool)>
            {
                ("Duyuru", Ik.Megafon, () => { var h = Hedefler(); if (h != null) DuyuruGonder(h); }, Renk.Yazi2, false),
            };
            if (ayarlar.Dosya)
            {
                liste.Add(("Dosya gönder", Ik.Yukle, () => DosyaGonder(null), Renk.Yazi2, false));
                liste.Add(("Dosyalar", Ik.Klasor, DosyaAl, Renk.Yazi2, false));
            }
            liste.Add(("Kilitle", Ik.Kilit, Kilitle, Renk.Yazi2, false));
            liste.Add(("Yeniden başlat", Ik.Yenile, () => Kapat(true, 60), Renk.Turuncu, true));
            liste.Add(("Kapat", Ik.Guc, () => Kapat(false, 60), Renk.Pembe, true));
            liste.Add(("Diğer", Ik.Daha, null, Renk.Yazi2, false));
            foreach (var (metin, ikon, is_, renk, tut) in liste)
            {
                var b = new Buton(metin, ikon, ButonTuru.Sade) { Dikey = true, ForeColorOzel = renk, Font = Tema.Yazi(8.25f), BasiliTut = tut, Tag = metin };
                b.BackColor = Color.Transparent;
                islemIpucu.SetToolTip(b, metin);
                if (tut)
                {
                    b.Onaylandi += (s, e) => Guvenli(is_);
                    b.KisaBasildi += (s, e) => Tost("Yanlışlıkla olmasın diye: \"" + metin + "\" düğmesini dolana kadar basılı tutun.");
                }
                else if (is_ != null) b.Click += (s, e) => Guvenli(is_);
                else b.Click += (s, e) => DigerMenu().Goster(b, true);
                islemCubugu.Controls.Add(b); islemDugmeleri.Add(b);
            }

            kartGorunum.Tur = ayarlar.Liste ? ButonTuru.Sade : ButonTuru.Ikincil;
            listeGorunum.Tur = ayarlar.Liste ? ButonTuru.Ikincil : ButonTuru.Sade;
            kartGorunum.Invalidate(); listeGorunum.Invalidate();
            foreach (var k in kartlar.Values) { k.ListeModu = ayarlar.Liste; k.OnizlemeGoster = ayarlar.Onizleme; }
            SecimGuncelle();
            if (basladi) ListeyiCiz();
            Yerlestir();
        }

        void GorunumDegistir(bool liste)
        {
            if (ayarlar.Liste == liste) return;
            ayarlar.Liste = liste; ayarlar.Yaz();
            OzellikleriUygula();
            if (!liste) OnizlemeYenile();
        }

        void UstDugmeleriDiz()
        {
            var bas = yenileBtn.Parent; if (bas == null) return;
            yenileBtn.Location = new Point(bas.Width - yenileBtn.Width - Tema.S(16), Tema.S(22));
            tumunuSec.Location = new Point(yenileBtn.Left - tumunuSec.Width - Tema.S(8), Tema.S(22));
            listeGorunum.Location = new Point(tumunuSec.Left - listeGorunum.Width - Tema.S(14), Tema.S(22));
            kartGorunum.Location = new Point(listeGorunum.Left - kartGorunum.Width - Tema.S(2), Tema.S(22));
        }

        int TostHedefY() => (surumKarti != null && surumKarti.Visible ? surumKarti.Top : islemCubugu.Top) - tost.Height - Tema.S(12);

        readonly ToolTip islemIpucu = new ToolTip();

        /// <summary>
        /// Alt işlem çubuğu sığmazsa önce soldaki "x bilgisayar seçili" yazısı gizlenir, yine sığmazsa düğmeler yalnız simge olur.
        /// Böylece pencere daraltılınca "Kapat" ve "Diğer" hiçbir zaman dışarıda kalmaz.
        /// </summary>
        void IslemCubuguSigdir(int gen)
        {
            if (islemDugmeleri.Count == 0) return;
            for (int kip = 1; kip <= 3; kip++)
            {
                int bx = kip == 1 ? Tema.S(172) : Tema.S(10); // solda "x bilgisayar seçili" yazısına yer
                foreach (var b in islemDugmeleri)
                {
                    if (kip == 3)
                    {
                        b.Text = ""; b.Dikey = false; b.Size = Tema.S(46, 46); b.Top = (islemCubugu.Height - b.Height) / 2;
                    }
                    else
                    {
                        b.Text = (string)b.Tag; b.Dikey = true; b.BoyutAyarla(); b.Width = Math.Max(b.Width, Tema.S(62)); b.Top = Tema.S(13);
                    }
                    b.Left = bx; bx += b.Width + (kip == 3 ? Tema.S(4) : 0);
                }
                int w = bx + Tema.S(10);
                if (w <= gen || kip == 3)
                {
                    islemCubugu.Width = w;
                    secimYazi.Visible = secimAlt.Visible = kip == 1;
                    foreach (var b in islemDugmeleri) b.Invalidate();
                    break;
                }
            }
        }

        void Yerlestir()
        {
            int sol = sayfaPc.Padding.Left, gen = sayfaPc.ClientSize.Width - sol - sayfaPc.Padding.Right;
            IslemCubuguSigdir(gen);
            islemCubugu.Location = new Point(sol + Math.Max(0, (gen - islemCubugu.Width) / 2), sayfaPc.ClientSize.Height - islemCubugu.Height - Tema.S(20));
            tost.Left = sayfaPc.ClientSize.Width - tost.Width - Tema.S(28);
            if (surumKarti != null) surumKarti.Location = new Point(sayfaPc.ClientSize.Width - surumKarti.Width - Tema.S(28), islemCubugu.Top - surumKarti.Height - Tema.S(12));
            if (!tostAnim.Enabled) tost.Top = TostHedefY();
            bosMesaj.Margin = new Padding(Math.Max(0, (izgara.ClientSize.Width - bosMesaj.Width) / 2), Tema.S(60), 0, 0);
            UstDugmeleriDiz();
        }

        /// <summary>Grup başlıkları ve liste satırları ızgaranın tam genişliğinde dursun.</summary>
        void GenislikleriAyarla()
        {
            // Dikey kaydırma çubuğu çıksa da yatay taşma olmasın diye onun genişliği kadar pay bırakılır.
            int w = izgara.Width - izgara.Padding.Horizontal - SystemInformation.VerticalScrollBarWidth - Tema.S(4);
            if (w <= 0) return;
            izgara.SuspendLayout();
            foreach (var b in basliklar.Values) b.Width = w;
            if (ayarlar.Liste) foreach (var k in kartlar.Values) k.Width = w;
            izgara.ResumeLayout();
        }

        void Guvenli(Action a) { try { a(); } catch (Exception ex) { Kaydet("Hata: " + ex.Message, 2); Tost("Hata: " + ex.Message); } }

        void MasaustuDuzeni()
        {
            var h = Hedefler(); if (h == null || h.Count == 0) return;
            using (var f = new MasaustuForm(h)) f.ShowDialog(this);
        }

        AcilirMenu DigerMenu()
        {
            var secili = kartlar.Values.Where(k => k.Secili).Select(k => k.Pc).ToList();
            var m = new AcilirMenu();
            if (secili.Count == 1)
            {
                if (ayarlar.Onizleme) m.Ekle("Ekranı göster", Ik.Kamera, () => Guvenli(() => EkranGoster(secili[0])))
                                       .Ekle("Uzaktan kontrol", Ik.Pencere, () => Guvenli(() => UzakKontrol(secili[0])));
                m.Ekle("Bilgiler", Ik.Bilgi, () => Guvenli(() => Bilgiler(secili[0])));
                m.Ekle("Adı ve grubu düzenle", Ik.Duzenle, () => Guvenli(() => Duzenle(secili[0]))).Ayrac();
            }
            else if (secili.Count > 1) m.Ekle("Grubunu değiştir", Ik.Etiket, () => Guvenli(GrupDegistir)).Ayrac();
            m.Ekle("Grupları düzenle", Ik.Etiket, () => Guvenli(GruplariDuzenle)).Ayrac();
            m.Ekle("Bugün kapanmasın", Ik.Duraklat, () => Guvenli(BugunKapatma))
             .Ekle("Açılış ve kapanış", Ik.Takvim, () => Guvenli(AcilisKapanis));
            if (ayarlar.Duvar) m.Ekle("Duvar kâğıdı", Ik.Resim, () => Guvenli(DuvarKagidi));
            m.Ekle("Uyandır (aç)", Ik.Gunes, () => Guvenli(Uyandir)).Ayrac();
            if (ayarlar.Yazici) m.Ekle("Yazıcı kur", Ik.Yazici, () => Guvenli(YaziciKur)).Ekle("Yazıcıyı onar", Ik.Anahtar_, () => Guvenli(YaziciOnar)).Ekle("Yazıcıyı sabitle", Ik.Kalkan, () => Guvenli(YaziciSabitle));
            if (ayarlar.Program) m.Ekle("Program ekle/kaldır", Ik.ProgramEkle, () => Guvenli(ProgramKur));
            m.Ekle("Masaüstü düzeni", Ik.Firca, () => Guvenli(MasaustuDuzeni));
            if (ayarlar.Yazici) m.Ekle("Baskı ve toner", Ik.YaziciIk, () => Guvenli(() => { List<Pc> h; lock (pcKilit) h = pcler.ToList(); using (var f = new BaskiForm(AcikHedefler(true), Izleme.YoneticiBul(h))) f.ShowDialog(this); }));
            m.Ekle("İnternet ve ağ", Ik.Kopuk, () => Guvenli(() => { List<Pc> h; lock (pcKilit) h = pcler.ToList(); using (var f = new AgForm(Izleme.YoneticiBul(h), h)) f.ShowDialog(this); }));
            m.Ekle("Sorun bildirimleri", Ik.Sohbet, () => Guvenli(SorunlariAc));
            m.Ekle("Lisanslar", Ik.Kalkan, () => Guvenli(() => { var h = AcikHedefler(true); if (h.Count == 0) Tost("Açık bilgisayar yok."); else using (var f = new LisansForm(h)) f.ShowDialog(this); }));
            m.Ekle("Dosyadan güncelle", Ik.Esitle, () => Guvenli(SemGuncelle)).Ayrac()
             .Ekle("Bekleyen kapatmayı iptal et", Ik.KapatDaire, () => Guvenli(KapatmayiIptal))
             .Ekle("Hemen kapat", Ik.Guc, () => Guvenli(HemenKapat), true)
             .Ekle("Listeden çıkar", Ik.Sil, () => Guvenli(ListedenCikar), true);
            return m;
        }

        // ------------------------------------------------------------------ İzleme uyarıları
        string sonInternet = null, tonerUyariGunu = "";
        readonly HashSet<string> bildirilenCihaz = new HashSet<string>();
        bool radarIlk = true;

        void SorunlariAc() { using (var f = new SorunlarForm()) f.ShowDialog(this); }

        void SorunGeldi(string pc, string not)
        {
            Kaydet(pc + " sorun bildirdi: " + not, 2);
            try
            {
                BeginInvoke((Action)(() =>
                {
                    try { new Sem.Ajan.BildirimForm(pc + " sorun bildirdi", not, Ik.Sohbet, Renk.Pembe, () => Guvenli(SorunlariAc)).Show(); } catch { }
                    if (!Visible) try { tepsi.ShowBalloonTip(6000, pc + " sorun bildirdi", not, ToolTipIcon.Warning); } catch { }
                }));
            }
            catch { }
        }

        /// <summary>Müdür bilgisayarının DURUM'undaki internet, ağ radarı ve toner bilgileri.</summary>
        void IzlemeUyarilari(Mesaj d)
        {
            var net = d["internet"];
            if (sonInternet != null && net != sonInternet)
            {
                if (net.StartsWith("0")) Kaydet("İnternet kesildi", 2);
                else if (sonInternet.StartsWith("0")) Kaydet("İnternet geri geldi", 1);
            }
            sonInternet = net;
            var yeni = d["radar_yeni"].Split(',').Where(x => x != "").ToList();
            int eklenen = 0;
            lock (bildirilenCihaz) foreach (var m in yeni) if (bildirilenCihaz.Add(m) && !radarIlk) eklenen++;
            if (radarIlk) { radarIlk = false; if (yeni.Count > 0) Kaydet("Ağda tanınmayan " + yeni.Count + " cihaz var (Diğer > İnternet ve ağ)", 0); }
            else if (eklenen > 0) { Kaydet("Ağa yeni bir cihaz bağlandı (Diğer > İnternet ve ağ)", 2); Tost("Ağa yeni bir cihaz bağlandı. Diğer > İnternet ve ağ'dan bakabilirsiniz."); }
            var tu = d["toner_uyari"]; var bugun = DateTime.Today.ToString("yyyy-MM-dd");
            if (tu != "" && tonerUyariGunu != bugun) { tonerUyariGunu = bugun; Kaydet(tu + " (Diğer > Baskı ve toner)", 2); Tost(tu + ". Yedek toner hazırlayın."); }
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
                oturumAcik = true;
                return true;
            }
        }

        // ------------------------------------------------------------------ Liste ve yenileme
        void Yenile()
        {
            if (Interlocked.Exchange(ref yenileniyor, 1) == 1) return;
            Task.Run(() =>
            {
                bool duyuruDegisti = false;
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
                    var acKap = ayarlar; var plan = AcKapPlani(acKap, kopya);
                    Parallel.ForEach(kopya, new ParallelOptions { MaxDegreeOfParallelism = 16 }, p =>
                    {
                        try
                        {
                            var d = Baglanti.Gonder(p.Ip, new Mesaj("DURUM"), 3000);
                            bool ilk = p.SonDurum == null;
                            p.SonDurum = d; p.Acik = true; p.Hata = ""; p.SonGorulme = DateTime.Now;
                            if (d["mac"] != "") p.Mac = d["mac"];
                            // Arka planda süren işlerin (yazıcı, program kurulumu) sonucu Etkinlik'e düşsün
                            if (d["son"] != p.SonIslem)
                            {
                                if (!ilk && d["son"] != "" && !d["son"].EndsWith("sürüyor")) Kaydet(p.Baslik + ": " + d["son"], d["son"].Contains("HATA") ? 2 : 1);
                                p.SonIslem = d["son"];
                            }
                            if (DuyuruGecmisi.OkunanlariIsle(p.Ad, d["okunan"])) duyuruDegisti = true;
                            // Verilen ad, grup ve simge bilgisayara da yazılsın (paylaşımda ve duvar kâğıdında görünür).
                            if (d.Alan.ContainsKey("gorunen") && (d["gorunen"] != p.GorunenAd || d["grup"] != p.Grup || d["simge"] != p.Simge || (d.Alan.ContainsKey("grup_ad") && d["grup_ad"] != Gruplar.Ad(p.Grup))))
                            {
                                var km = new Mesaj("KIMLIK"); km["gorunen"] = p.GorunenAd; km["grup"] = p.Grup; km["grup_ad"] = Gruplar.Ad(p.Grup); km["simge"] = p.Simge;
                                try { Baglanti.Gonder(p.Ip, km, 3000); } catch { }
                            }
                            // Ekran görme anahtarı (Ayarlar) tüm bilgisayarlara uygulanır; kapalıyken bilgisayar da reddeder.
                            if (d.Alan.ContainsKey("ekran_izni") && (d["ekran_izni"] == "1") != ayarlar.Onizleme)
                            {
                                var em = new Mesaj("AYAR"); em["ekran_izni"] = ayarlar.Onizleme ? "1" : "0";
                                try { Baglanti.Gonder(p.Ip, em, 3000); } catch { }
                            }
                            // Mesaja yanıt ve klasik sağ tık menüsü (Ayarlar) tüm bilgisayarlara
                            if ((d.Alan.ContainsKey("mesaj_yanit") && (d["mesaj_yanit"] == "1") != ayarlar.MesajYanit) || (d.Alan.ContainsKey("klasik_menu") && (d["klasik_menu"] == "1") != ayarlar.KlasikMenu))
                            {
                                var ym = new Mesaj("AYAR"); ym["mesaj_yanit"] = ayarlar.MesajYanit ? "1" : "0"; ym["klasik_menu"] = ayarlar.KlasikMenu ? "1" : "0";
                                try { Baglanti.Gonder(p.Ip, ym, 3000); } catch { }
                            }
                            // Açılış ve kapanış ayarları (tek ekrandan) her bilgisayara; kapalı olanlara açılınca
                            if (plan != null) AcKapEsitle(acKap, plan, p, d);
                            // Öğretmenden gelen sorun bildirimi
                            if (d["sorun"] != "") foreach (var not in Izleme.SorunlariAl(p, d["sorun"])) SorunGeldi(p.Baslik, not);
                            if (d["yonetici"] == "1" && d.Alan.ContainsKey("internet")) IzlemeUyarilari(d);
                            // Yeni sağlık uyarısı Etkinlik'e düşsün
                            var sg = d["saglik"];
                            if (sg != p.SonSaglik)
                            {
                                foreach (var u in sg.Split(';').Where(x => x != "" && !(";" + p.SonSaglik + ";").Contains(";" + x + ";")))
                                    Kaydet(p.Baslik + ": " + u.Substring(u.IndexOf('|') + 1), 2);
                                p.SonSaglik = sg;
                            }
                        }
                        catch (Exception e)
                        {
                            p.Acik = false;
                            p.Hata = e.Message.StartsWith("Şifre") ? e.Message : "";
                        }
                    });
                    lock (pcKilit) Depo.ListeYaz(pcler);
                    SabahPlaniTazele(kopya);
                }
                finally
                {
                    Interlocked.Exchange(ref yenileniyor, 0);
                    try
                    {
                        BeginInvoke((Action)(() =>
                        {
                            ListeyiCiz();
                            if (duyuruDegisti && sayfaDuyuru.Visible) DuyurulariCiz();
                            bool bekleyen; lock (pcKilit) bekleyen = pcler.Any(p => p.Acik && p.Onizleme == null && p.OnizlemeHata == "");
                            if (bekleyen) OnizlemeYenile();
                        }));
                    }
                    catch { }
                }
            });
        }

        /// <summary>Açık bilgisayarların ekranından küçük görüntü alır (kartlardaki canlı önizleme).</summary>
        void OnizlemeYenile()
        {
            if (!ayarlar.Onizleme || ayarlar.Liste || !Visible || WindowState == FormWindowState.Minimized || !sayfaPc.Visible) return;
            if (Interlocked.Exchange(ref onizleniyor, 1) == 1) return;
            List<Pc> acik; lock (pcKilit) acik = pcler.Where(p => p.Acik).ToList();
            Task.Run(() =>
            {
                try
                {
                    Parallel.ForEach(acik, new ParallelOptions { MaxDegreeOfParallelism = 8 }, p =>
                    {
                        Image img = null; string hata = "";
                        try
                        {
                            var m = new Mesaj("EKRAN_RESMI"); m["genislik"] = Tema.S(480).ToString();
                            var c = Baglanti.Gonder(p.Ip, m, 9000);
                            if (c.Tamam) using (var ms = new MemoryStream(c.Veri)) img = new Bitmap(Image.FromStream(ms));
                            else hata = c["aciklama"].StartsWith("Bilinmeyen") ? "Önizleme için güncelleme gerekli" : c["aciklama"];
                        }
                        catch (Exception e) { hata = e.Message; }
                        try
                        {
                            BeginInvoke((Action)(() =>
                            {
                                if (img != null) { var eski = p.Onizleme; p.Onizleme = img; p.OnizlemeZaman = DateTime.Now; eski?.Dispose(); p.OnizlemeHata = ""; }
                                else p.OnizlemeHata = hata;
                                PcKart k; if (kartlar.TryGetValue(p.Ad, out k)) k.Invalidate();
                            }));
                        }
                        catch { img?.Dispose(); }
                    });
                }
                finally { Interlocked.Exchange(ref onizleniyor, 0); }
            });
        }

        void ListeyiCiz()
        {
            List<Pc> kopya;
            lock (pcKilit) kopya = pcler.OrderBy(p => Gruplar.Sira(p.Grup)).ThenByDescending(p => p.Acik).ThenBy(p => p.Baslik, StringComparer.CurrentCultureIgnoreCase).ToList();
            bool gruplu = kopya.Any(p => p.Grup != Gruplar.Varsayilan);
            izgara.SuspendLayout();
            foreach (var p in kopya)
            {
                PcKart k;
                if (!kartlar.TryGetValue(p.Ad, out k))
                {
                    k = new PcKart(p, ayarlar.Liste, ayarlar.Onizleme);
                    k.SecimDegisti += (s, e) => SecimGuncelle();
                    k.BuyutIstendi += (s, e) => Guvenli(() => EkranGoster(((PcKart)s).Pc));
                    k.DuzenleIstendi += (s, e) => Guvenli(() => Duzenle(((PcKart)s).Pc));
                    BirakmaHedefi(k);
                    k.MouseUp += (s, e) => { if (e.Button == MouseButtons.Right) { var kk = (PcKart)s; if (!kk.Secili) kk.Secili = true; DigerMenu().ImlecteGoster(this); } };
                    kartlar[p.Ad] = k; izgara.Controls.Add(k);
                }
                k.Pc = p; k.Invalidate();
            }
            foreach (var ad in kartlar.Keys.ToList())
                if (!kopya.Any(p => p.Ad.Equals(ad, StringComparison.OrdinalIgnoreCase))) { izgara.Controls.Remove(kartlar[ad]); kartlar[ad].Dispose(); kartlar.Remove(ad); }

            // Sıralama: gruplar, her grupta önce açık olanlar, sonra ada göre
            var kullanilan = new HashSet<string>();
            int i = 1;
            foreach (var grup in kopya.GroupBy(p => Gruplar.Liste[Gruplar.Sira(p.Grup)].Kod))
            {
                var uyeler = grup.ToList();
                if (gruplu)
                {
                    GrupBaslik b;
                    if (!basliklar.TryGetValue(grup.Key, out b))
                    {
                        b = new GrupBaslik(grup.Key);
                        b.SecTiklandi += (s, e) => GrupSec(((GrupBaslik)s).Kod);
                        b.AdDegisti += (bb, ad) => Guvenli(() => GrupAdiDegistir(bb.Kod, ad));
                        BirakmaHedefi(b);
                        basliklar[grup.Key] = b; izgara.Controls.Add(b);
                    }
                    int acikSayi = uyeler.Count(p => p.Acik);
                    b.Bilgi = "·  " + uyeler.Count + " bilgisayar" + (acikSayi > 0 ? ", " + acikSayi + " açık" : "");
                    b.Invalidate();
                    izgara.Controls.SetChildIndex(b, i++); izgara.SetFlowBreak(b, true);
                    kullanilan.Add(grup.Key);
                }
                foreach (var p in uyeler) { var k = kartlar[p.Ad]; izgara.Controls.SetChildIndex(k, i++); izgara.SetFlowBreak(k, false); }
                izgara.SetFlowBreak(kartlar[uyeler[uyeler.Count - 1].Ad], true); // sonraki grup / boşluk yeni satırda
            }
            foreach (var kod in basliklar.Keys.ToList())
                if (!kullanilan.Contains(kod)) { izgara.Controls.Remove(basliklar[kod]); basliklar[kod].Dispose(); basliklar.Remove(kod); }
            izgara.Controls.SetChildIndex(altBosluk, i);
            bosMesaj.Visible = kopya.Count == 0;
            GenislikleriAyarla();
            izgara.ResumeLayout();
            GrupSecimleriniGuncelle();

            int acik = kopya.Count(p => p.Acik);
            ozAcik.Deger = acik.ToString(); ozKapali.Deger = (kopya.Count - acik).ToString();
            var saatler = kopya.Where(p => p.Acik && p.SonDurum != null && p.SonDurum["otomatik"] != "0").Select(p => p.SonDurum["kapanma"]).Where(s => s.Length >= 5 && char.IsDigit(s[0])).Select(s => s.Substring(0, 5)).ToList();
            ozKapanma.Deger = saatler.Count == 0 ? "—" : saatler.GroupBy(s => s).OrderByDescending(gr => gr.Count()).First().Key;
            ozKapanma.Etiket = saatler.Distinct().Count() > 1 ? "Çoğunun kapanma saati" : "Bugünkü kapanma";
            ozAcik.Invalidate(); ozKapali.Invalidate(); ozKapanma.Invalidate();
            altBaslik.Text = kopya.Count == 0 ? "Ağdaki Sem bilgisayarları aranıyor…" : $"{kopya.Count} bilgisayar  ·  son kontrol {DateTime.Now:HH:mm:ss}";
            tepsi.Text = "Sem Ajan  ·  " + acik + " / " + kopya.Count + " açık";
            Yerlestir();
        }

        void GrupSec(string kod)
        {
            var uyeler = kartlar.Values.Where(k => Gruplar.Liste[Gruplar.Sira(k.Pc.Grup)].Kod == kod).ToList();
            bool hepsi = uyeler.All(k => k.Secili);
            foreach (var k in uyeler) k.Secili = !hepsi;
        }

        void GrupSecimleriniGuncelle()
        {
            foreach (var b in basliklar.Values)
            {
                var uyeler = kartlar.Values.Where(k => Gruplar.Liste[Gruplar.Sira(k.Pc.Grup)].Kod == b.Kod).ToList();
                bool t = uyeler.Count > 0 && uyeler.All(k => k.Secili);
                if (t != b.TumuSecili) { b.TumuSecili = t; b.Invalidate(); }
            }
        }

        void SecimGuncelle()
        {
            var n = kartlar.Values.Count(k => k.Secili);
            secimYazi.Text = n == 0 ? "Bilgisayar seçin" : n + " bilgisayar seçili";
            secimYazi.ForeColor = n == 0 ? Renk.Yazi2 : Renk.Yazi;
            secimAlt.Text = n == 0 ? (ayarlar.Liste ? "Satırlara tıklayarak seçin" : "Kartlara tıklayarak seçin") : "Sağ tık: diğer işlemler";
            foreach (var b in islemDugmeleri) b.Enabled = n > 0;
            tumunuSec.Text = kartlar.Count > 0 && kartlar.Values.All(k => k.Secili) ? "Seçimi kaldır" : "Tümünü seç";
            tumunuSec.BoyutAyarla();
            GrupSecimleriniGuncelle();
            Yerlestir();
        }

        void Tost(string s)
        {
            if (InvokeRequired) { BeginInvoke((Action)(() => Tost(s))); return; }
            if (!Visible) { try { tepsi.ShowBalloonTip(4000, "Sem Ajan", s, ToolTipIcon.None); } catch { } return; }
            bool yeni = !tost.Visible;
            tostYazi.Text = s; tost.Visible = true; tost.BringToFront(); tostZaman.Stop(); tostZaman.Start();
            if (yeni) { tost.Top = TostHedefY() + Tema.S(18); tostAnim.Start(); }
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
                if (kapali.Count > 0) Kaydet("Kapalı olduğu için atlandı: " + string.Join(", ", kapali.Select(p => p.Baslik)), 2);
                l = l.Where(p => p.Acik).ToList();
                if (l.Count == 0) { MK.Bilgi(this, "Bilgisayarlar kapalı", "Seçtiğiniz bilgisayarların hepsi kapalı.", true); return null; }
            }
            return l;
        }

        /// <summary>Komutu bilgisayarlara aynı anda gönderir; sonucu tost olarak ve Etkinlik sayfasında gösterir.</summary>
        void Uygula(string islem, List<Pc> hedef, Func<Pc, Mesaj> uret, int zamanAsimiMs = 8000, Action<Pc> basarili = null)
        {
            if (hedef == null || hedef.Count == 0) return;
            Kaydet(islem + "  →  " + string.Join(", ", hedef.Select(p => p.Baslik)));
            Tost(islem + ": " + hedef.Count + " bilgisayara gönderiliyor…");
            Task.Run(() =>
            {
                int ok = 0; var hatalar = new List<string>();
                Parallel.ForEach(hedef, p =>
                {
                    try
                    {
                        var c = Baglanti.Gonder(p.Ip, uret(p), zamanAsimiMs);
                        Kaydet(p.Baslik + ": " + c["aciklama"], c.Tamam ? 1 : 2);
                        if (c.Tamam) { Interlocked.Increment(ref ok); basarili?.Invoke(p); } else lock (hatalar) hatalar.Add(p.Baslik);
                    }
                    catch (Exception e) { Kaydet(p.Baslik + ": " + e.Message, 2); lock (hatalar) hatalar.Add(p.Baslik); }
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

        void DuyuruGonder(List<Pc> h)
        {
            using (var f = new DuyuruYazForm(h.Count))
            {
                if (!Visible) { f.StartPosition = FormStartPosition.CenterScreen; f.TopMost = true; f.ShowInTaskbar = true; }
                if ((Visible ? f.ShowDialog(this) : f.ShowDialog()) != DialogResult.OK) return;
                var k = new DuyuruKaydi { Baslik = f.Baslik, Metin = f.Metin };
                if (ayarlar.Duyurular) DuyuruGecmisi.Ekle(k);
                DuyuruYolla(k, h);
            }
        }

        void DuyuruYolla(DuyuruKaydi k, List<Pc> h)
        {
            Uygula("Duyuru", h, p => { var x = new Mesaj("DUYURU"); x["baslik"] = k.Baslik; x["metin"] = k.Metin; x["id"] = k.Id; return x; }, 8000,
                p => { DuyuruGecmisi.Ulasti(k.Id, p.Ad); try { BeginInvoke((Action)(() => { if (sayfaDuyuru.Visible) DuyurulariCiz(); })); } catch { } });
        }

        void DuyurulariCiz()
        {
            var l = DuyuruGecmisi.Liste;
            var sec = duyurular.SelectedIndex;
            duyurular.BeginUpdate(); duyurular.Items.Clear();
            foreach (var k in l) duyurular.Items.Add(k);
            duyurular.EndUpdate();
            duyuruBos.Visible = l.Count == 0;
            duyurular.Parent.Visible = l.Count > 0;
        }

        string GorunenAd(string ad) { lock (pcKilit) return pcler.FirstOrDefault(p => p.Ad.Equals(ad, StringComparison.OrdinalIgnoreCase))?.Baslik ?? ad; }

        void DuyuruDetay(DuyuruKaydi k)
        {
            using (var f = new DuyuruDetayForm(k, GorunenAd))
            {
                if (f.ShowDialog(this) != DialogResult.OK || !f.TekrarGonder) return;
                List<Pc> h; lock (pcKilit) h = pcler.Where(p => p.Acik && k.Ulasan.Contains(p.Ad) && !k.Okuyan.ContainsKey(p.Ad)).ToList();
                if (h.Count == 0) { Tost("Okumayan bilgisayarların hepsi şu an kapalı."); return; }
                DuyuruYolla(k, h);
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

        /// <summary>Basılı tutularak onaylanır: ekranda 1 dakikalık geri sayım çıkar, sonra kapanır.</summary>
        void Kapat(bool yeniden, int gecikme)
        {
            var h = Hedefler(); if (h == null) return;
            Uygula(yeniden ? "Yeniden başlat (1 dk sonra)" : "Kapat (1 dk sonra)", h, p => { var m = new Mesaj("KAPAT"); m["tip"] = yeniden ? "yeniden" : "kapat"; m["gecikme"] = gecikme.ToString(); return m; });
        }

        void HemenKapat()
        {
            var h = Hedefler(); if (h == null) return;
            if (MK.Sor(this, "Hemen kapatılsın mı?", string.Join(", ", h.Select(p => p.Baslik)) + "\n\nUyarı gösterilmez, açık dosyalar kaydedilmeden kapanır.", "Hemen kapat", true) != true) return;
            Uygula("Hemen kapat", h, p => { var m = new Mesaj("KAPAT"); m["tip"] = "kapat"; m["gecikme"] = "0"; return m; });
        }

        void KapatmayiIptal()
        {
            var h = Hedefler(); if (h == null) return;
            Uygula("Bekleyen kapatmayı iptal", h, p => new Mesaj("KAPAT_IPTAL"));
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
                try { Baglanti.Uyandir(p.Mac); Kaydet(p.Baslik + ": açma sinyali gönderildi", 1); }
                catch (Exception e) { Kaydet(p.Baslik + ": " + e.Message, 2); }
            }
            Tost("Açma sinyali gönderildi. Bilgisayarın ayarlarında Wake-on-LAN açıksa 1-2 dakikada açılır.");
        }

        static bool YoneticiPc(Pc p) =>
            p.Ad.Equals(Environment.MachineName, StringComparison.OrdinalIgnoreCase) || p.SonDurum?["yonetici"] == "1";

        /// <summary>Günler, kapanma saatleri, sabah açılma ve her bilgisayarın açılsın/kapansın seçimi tek ekranda.</summary>
        void AcilisKapanis()
        {
            List<Pc> liste; lock (pcKilit) liste = pcler.Where(p => !YoneticiPc(p)).ToList();
            var a = YonetimAyar.Oku();
            if (!a.AcKapTanimli)
            {
                // İlk açılışta bilgisayarlardaki mevcut ayarlarla doldur
                var ornek = liste.Select(p => p.SonDurum).FirstOrDefault(d => d != null && d.Alan.ContainsKey("kapanma_pzt"));
                if (ornek != null)
                {
                    for (int g = 0; g < 7; g++) a.Kapanma[g] = ornek["kapanma_" + AjanAyar.GunKod[g]];
                    int n;
                    if (int.TryParse(ornek["uyari_dk"], out n)) a.UyariDk = n;
                    if (int.TryParse(ornek["ertele_dk"], out n)) a.ErteleDk = n;
                    if (int.TryParse(ornek["ertele_hak"], out n)) a.ErteleHak = n;
                }
                foreach (var p in liste.Where(p => p.SonDurum != null && p.SonDurum["otomatik"] == "0")) a.Kapanmayan.Add(p.Ad);
                var bu = BuPc()?.SonDurum; if (bu != null) a.SabahSaat = bu["sabah_saat"];
            }
            using (var f = new AcKapForm(a, liste))
            {
                if (f.ShowDialog(this) != DialogResult.OK) return;
                // Görünen özellikler (Ayarlar) bu arada değişmiş olabilir; yalnızca açılış/kapanış alanları alınır.
                var y = YonetimAyar.Oku();
                y.AcKapTanimli = true; y.Kapanma = a.Kapanma; y.UyariDk = a.UyariDk; y.ErteleDk = a.ErteleDk; y.ErteleHak = a.ErteleHak;
                y.AcilistaAc = a.AcilistaAc; y.SabahSaat = a.SabahSaat; y.Acilmayan = a.Acilmayan; y.Kapanmayan = a.Kapanmayan; y.Sabah = a.Sabah;
                y.Yaz(); ayarlar = y;
                OzellikleriUygula();
                Kaydet("Açılış ve kapanış ayarları kaydedildi", 1);
                Tost("Kaydedildi. Açık bilgisayarlara birkaç saniye içinde, kapalı olanlara açıldıklarında uygulanır.");
                Task.Run(() => { try { Yenile(); } catch { } });
            }
        }

        class AcKapPlan { public Mesaj Sabah; public string Imza; }

        /// <summary>Tüm bilgisayarlara gidecek sabah açılma planı (yalnızca yönetici bilgisayarı açılışta gönderir).</summary>
        static AcKapPlan AcKapPlani(YonetimAyar a, List<Pc> kopya)
        {
            if (!a.AcKapTanimli) return null;
            var m = new Mesaj("SABAH_PLANI");
            m["saat"] = a.SabahSaat; m["gunler"] = a.AcikGunler; m["acilis"] = a.AcilistaAc ? "1" : "0";
            m["macler"] = (a.AcilistaAc || a.SabahSaat != "")
                ? string.Join(";", kopya.Where(p => !YoneticiPc(p) && p.Mac != "" && !a.Acilmayan.Contains(p.Ad)).Select(p => p.Mac).Distinct().OrderBy(x => x))
                : "";
            return new AcKapPlan { Sabah = m, Imza = m["saat"] + "|" + m["gunler"] + "|" + m["macler"] + "|" + m["acilis"] };
        }

        void AcKapEsitle(YonetimAyar a, AcKapPlan plan, Pc p, Mesaj d)
        {
            if (d.Alan.ContainsKey("sabah_acilis"))
            {
                var imza = d["sabah_saat"] + "|" + d["sabah_gunler"] + "|" + d["sabah_macler"] + "|" + d["sabah_acilis"];
                if (imza != plan.Imza) try { Baglanti.Gonder(p.Ip, plan.Sabah, 4000); } catch { }
            }
            if (YoneticiPc(p) || !d.Alan.ContainsKey("kapanma_pzt")) return;
            string oto = a.Kapanmayan.Contains(p.Ad) ? "0" : "1";
            bool farkli = d["otomatik"] != oto || d["uyari_dk"] != a.UyariDk.ToString() || d["ertele_dk"] != a.ErteleDk.ToString() || d["ertele_hak"] != a.ErteleHak.ToString();
            for (int g = 0; g < 7; g++) if (d["kapanma_" + AjanAyar.GunKod[g]] != a.Kapanma[g]) farkli = true;
            if (!farkli) return;
            var m = new Mesaj("AYAR");
            for (int g = 0; g < 7; g++) m["kapanma_" + AjanAyar.GunKod[g]] = a.Kapanma[g];
            m["uyari_dk"] = a.UyariDk.ToString(); m["ertele_dk"] = a.ErteleDk.ToString(); m["ertele_hak"] = a.ErteleHak.ToString(); m["otomatik"] = oto;
            try { if (Baglanti.Gonder(p.Ip, m, 4000).Tamam) Kaydet(p.Baslik + ": açılış ve kapanış ayarları uygulandı", 1); } catch { }
        }

        void BugunKapatma()
        {
            var h = Hedefler(); if (h == null) return;
            int s = MK.Secim(this, "Bugünkü kapanma", "Seçili " + h.Count + " bilgisayar bugün ne yapsın?",
                ("Bugün kapanmasın", "Yarın normal saatine döner", Ik.Duraklat),
                ("Normal saatinde kapansın", "Daha önce verilen iptali geri alır", Ik.Guc));
            if (s < 0) return;
            if (s == 0) BugunKapanmasin(h);
            else Uygula("Normal saatinde kapansın", h, p => { var m = new Mesaj("BUGUN_KAPATMA"); m["deger"] = "0"; return m; });
        }

        void BugunKapanmasin(List<Pc> h)
        {
            if (h.Count == 0) { Tost("Açık bilgisayar yok."); return; }
            Uygula("Bugün kapanmasın", h, p => { var m = new Mesaj("BUGUN_KAPATMA"); m["deger"] = "1"; return m; });
            Uygula("Bekleyen kapatmayı iptal", h, p => new Mesaj("KAPAT_IPTAL"));
        }

        void DuvarKagidi()
        {
            var h = Hedefler(); if (h == null) return;
            int s = MK.Secim(this, "Duvar kâğıdı", "Seçili bilgisayarların duvar kâğıdı ve kilit ekranı. Biri değiştirirse program geri getirir.",
                ("Otomatik", "Mevsime göre değişir, özel günlerde (23 Nisan, 2 Nisan, 24 Kasım…) o güne özel tasarım", Ik.Parilti),
                ("Kurumsal", "Sade, logolu tasarım", Ik.Ev),
                ("Gece", "Çok koyu, neredeyse siyah; yalnızca seçtiğiniz bilgisayarlara", Ik.Yildiz),
                ("Bilgisayardan resim seç…", "JPG, PNG ya da BMP", Ik.Resim));
            if (s < 0) return;
            if (s == 3)
            {
                byte[] veri;
                using (var o = new OpenFileDialog { Filter = "Resimler|*.jpg;*.jpeg;*.png;*.bmp", Title = "Duvar kâğıdı seçin" })
                {
                    if (o.ShowDialog(this) != DialogResult.OK) return;
                    veri = File.ReadAllBytes(o.FileName);
                }
                Uygula("Duvar kâğıdı", h, p => { var m = new Mesaj("DUVAR") { Veri = veri }; m["koru"] = "1"; m["kilit"] = "1"; return m; }, 30000);
                return;
            }
            int o2 = MK.Secim(this, "Bilgisayarın adı", "Tasarımın sağ üst köşesinde bilgisayara verdiğiniz ad (ör. \"Terapi 2\") görünsün mü?",
                ("Adıyla birlikte", "Sorun bildirirken hangi bilgisayar olduğu karışmaz", Ik.Etiket),
                ("Sadece tasarım", "Köşede ad yazmaz", Ik.Resim));
            if (o2 < 0) return;
            if (s == 2)
            {
                Uygula("Duvar kâğıdı: gece", h, p => { var m = new Mesaj("DUVAR_TASARIM"); m["kip"] = "gece"; m["oda"] = o2 == 0 ? "1" : "0"; m["ton"] = "koyu"; m["kilit"] = "1"; m["koru"] = "1"; return m; }, 30000);
                return;
            }
            int o3 = MK.Secim(this, "Renk", "Duvar kâğıdı açık mı koyu mu olsun?",
                ("Otomatik", "Bilgisayar Windows'ta koyu temadaysa koyu, değilse açık", Ik.Parilti),
                ("Koyu", "Gece mavisi zemin, göz yormaz", Ik.Yildiz),
                ("Açık", "Aydınlık, sıcak tonlar", Ik.Gunes));
            if (o3 < 0) return;
            string kip = s == 0 ? "otomatik" : "kurumsal", ton = o3 == 0 ? "otomatik" : o3 == 1 ? "koyu" : "";
            Uygula("Duvar kâğıdı: " + (s == 0 ? "otomatik" : "kurumsal") + (ton == "koyu" ? ", koyu" : ton == "otomatik" ? ", tema" : ""), h, p => { var m = new Mesaj("DUVAR_TASARIM"); m["kip"] = kip; m["oda"] = o2 == 0 ? "1" : "0"; m["ton"] = ton; m["kilit"] = "1"; m["koru"] = "1"; return m; }, 30000);
        }

        /// <summary>
        /// Ana bilgisayarda kurulu Canon yazıcının sürücüsünü alır ve seçili bilgisayarlara aynı ad ve IP ile kurar.
        /// Ana bilgisayarda yazıcı yoksa elle sürücü klasörü seçilen eski pencereye düşer.
        /// </summary>
        void YaziciKur()
        {
            var h = Hedefler(); if (h == null) return;
            Tost("Yazıcı sürücüsü bu bilgisayardan alınıyor…");
            Task.Run(() =>
            {
                Mesaj paket = null; string hata = null;
                try { paket = Baglanti.Gonder("127.0.0.1", new Mesaj("YAZICI_PAKET"), 120000); if (!paket.Tamam) hata = paket["aciklama"]; }
                catch (Exception e) { hata = e.Message; }
                BeginInvoke((Action)(() => Guvenli(() => YaziciKurDevam(h, paket, hata))));
            });
        }

        void YaziciKurDevam(List<Pc> h, Mesaj paket, string hata)
        {
            tost.Visible = false;
            if (hata != null || paket?.Veri == null)
            {
                if (!string.IsNullOrEmpty(hata)) Kaydet("Yazıcı sürücüsü alınamadı: " + hata, 2);
                var secim = MK.Sor(this, "Sürücü alınamadı", "Bu bilgisayardaki Canon yazıcının sürücüsü okunamadı (ayrıntı Etkinlik sayfasında).\n\nCanon'un sitesinden indirdiğiniz sürücü klasörünü seçerek de kurabilirsiniz.", "Klasör seç");
                if (secim == true) YaziciKurElle(h);
                return;
            }
            string ad = paket["ad"], surucu = paket["surucu"], ip = paket["ip"];
            if (string.IsNullOrWhiteSpace(ip))
            {
                ip = Diyalog.Metin(this, "Yazıcının IP adresi", "Yazıcının IP adresi okunamadı. Doğruysa devam edin; değilse yazıcının ekranında Menü → Ağ ayarları bölümünde yazar.", YaziciForm.VarsayilanIp, "Devam");
                if (string.IsNullOrWhiteSpace(ip)) return;
            }
            var mb = paket.Veri.Length / (1024.0 * 1024.0);
            if (MK.Sor(this, "Yazıcı kurulsun mu?", ad + "\nSürücü: " + surucu + "\nIP: " + ip + "  ·  " + mb.ToString("0.#") + " MB\n\n" +
                h.Count + " bilgisayara bu bilgisayardaki sürücünün aynısı kurulacak.", "Kur") != true) return;
            var veri = paket.Veri;
            Uygula("Yazıcı kur", h, p => { var m = new Mesaj("YAZICI_KUR") { Veri = veri }; m["ip"] = ip; m["ad"] = ad; m["surucu"] = surucu; return m; }, 180000);
        }

        void YaziciSabitle()
        {
            var h = Hedefler(); if (h == null) return;
            int acik = h.Count(p => p.SonDurum?["yazici_kilit"] == "1");
            int s = MK.Secim(this, "Yazıcıyı sabitle",
                h.Count + " bilgisayar" + (acik > 0 ? " (" + acik + " tanesinde şu an açık)" : "") + ".\n\n" +
                "Açıkken yalnız Canon (" + YaziciForm.VarsayilanIp + ") ve Microsoft Print to PDF kalır; yönetici bilgisayarında USB yazıcılar (Brother) da kalır. " +
                "Eklenen diğer yazıcılar ve Canon kopyaları birkaç dakika içinde silinir, \"Yazıcı ekle\" kapanır, Canon sabit IP portuna alınır.",
                ("Sabitle", "Yalnız Canon ve PDF kalsın", Ik.Kalkan),
                ("Serbest bırak", "Yazıcı eklemeye yeniden izin ver", Ik.Kilit));
            if (s < 0) return;
            bool ac = s == 0;
            Uygula(ac ? "Yazıcıyı sabitle" : "Yazıcı serbest", h, p => { var m = new Mesaj("AYAR"); m["yazici_kilit"] = ac ? "1" : "0"; m["yazici_ip"] = YaziciForm.VarsayilanIp; return m; });
        }

        void YaziciKurElle(List<Pc> h)
        {
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
            using (var f = new ProgramForm(h))
            {
                f.ShowDialog(this);
                if (f.Sonuc == null) return;
                if (f.Sonuc.Value.dosyadan) { DosyadanKur(h); return; }
                foreach (var (id, ad, hedefler) in f.Isler)
                {
                    if (f.Kaldir) Uygula("Kaldır: " + ad, hedefler, p => { var m = new Mesaj("PROGRAM_KALDIR"); m["ad"] = ad; return m; });
                    else { var gn = f.Sonuc.Value.guncelle; Uygula((gn ? "Güncelle: " : "Kur: ") + ad, hedefler, p => { var m = new Mesaj("PROGRAM_WINGET"); m["id"] = id; m["ad"] = ad; m["islem"] = gn ? "guncelle" : "kur"; return m; }); }
                }
            }
        }

        void DosyadanKur(List<Pc> h)
        {
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
                Kaydet("Güncelleme bitince bu pencere de kendiliğinden yeni sürüme geçer (kapatıp açmanız yeterli).", 0);
            }
        }

        void SemGuncelle()
        {
            List<Pc> h;
            if (kartlar.Values.Any(k => k.Secili)) h = Hedefler();
            else { lock (pcKilit) h = pcler.Where(p => p.Acik).ToList(); }
            if (h == null || h.Count == 0) { Tost("Açık bilgisayar yok."); return; }
            string dosya;
            using (var o = new OpenFileDialog { Filter = "Sem Ajan|Sem.exe;*.exe", Title = "Yeni Sem.exe dosyasını seçin" })
            {
                if (o.ShowDialog(this) != DialogResult.OK) return;
                dosya = o.FileName;
            }
            var vi = FileVersionInfo.GetVersionInfo(dosya);
            if (vi.ProductName != "Sem Ajan") { MK.Bilgi(this, "Geçersiz dosya", "Bu dosya Sem Ajan programı değil.", true); return; }
            if (MK.Sor(this, "Sem Ajan güncellensin mi?", h.Count + " bilgisayardaki Sem Ajan, " + vi.ProductVersion + " sürümüne güncellenecek. Ayarlar korunur.", "Güncelle") != true) return;
            var veri = File.ReadAllBytes(dosya);
            Uygula("Sem Ajan güncelle (" + vi.ProductVersion + ")", h, p => new Mesaj("GUNCELLE") { Veri = veri }, 60000);
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
            if (MK.Sor(this, "Listeden çıkarılsın mı?", string.Join(", ", h.Select(p => p.Baslik)) + "\n\nSem Ajan açık olduğu sürece tekrar görünür. Tamamen kaldırmak için o bilgisayarda Sem.exe'yi çalıştırıp \"Kaldır\" deyin.", "Çıkar", true) != true) return;
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

        void UzakKontrol(Pc p)
        {
            if (!p.Acik) { Tost(p.Baslik + " kapalı."); return; }
            var f = new UzakForm(p); f.Show(this);
        }

        void Bilgiler(Pc p)
        {
            if (!p.Acik) { Tost(p.Baslik + " kapalı."); return; }
            using (var f = new BilgiForm(p)) f.ShowDialog(this);
        }

        void EkranGoster(Pc p)
        {
            if (!p.Acik) { Tost(p.Baslik + " kapalı."); return; }
            using (var f = new EkranGorForm(p)) f.ShowDialog(this);
        }

        void Duzenle(Pc p)
        {
            using (var f = new PcDuzenleForm(p))
            {
                if (f.ShowDialog(this) != DialogResult.OK) return;
                lock (pcKilit) { p.GorunenAd = f.GorunenAd; p.Grup = f.Grup; p.Simge = f.Simge ?? ""; Depo.ListeYaz(pcler); }
                ListeyiCiz();
            }
        }

        void GrupDegistir()
        {
            var h = Hedefler(false); if (h == null) return;
            int s = MK.Secim(this, "Grubunu değiştir", h.Count + " bilgisayar hangi gruba girsin?",
                Gruplar.Liste.Select(g => (g.Ad, "", Gruplar.Ikon(g.Kod))).ToArray());
            if (s < 0) return;
            lock (pcKilit) { foreach (var p in h) p.Grup = Gruplar.Liste[s].Kod; Depo.ListeYaz(pcler); }
            ListeyiCiz();
        }

        // ------------------------------------------------------------------ Sürükle-bırak: dosya gönderme ve grup değiştirme
        static readonly string KartVerisi = typeof(PcKart).FullName;

        void BirakmaHedefi(Control c)
        {
            c.AllowDrop = true;
            c.DragEnter += (s, e) => SurukleUzerinde(e);
            c.DragOver += (s, e) => SurukleUzerinde(e);
            c.DragLeave += (s, e) => HedefGrupVurgula(null);
            c.DragDrop += (s, e) => Guvenli(() => Birak(e));
        }

        void SurukleUzerinde(DragEventArgs e)
        {
            if (e.Data.GetDataPresent(KartVerisi))
            {
                var kod = HedefGrup(e.X, e.Y);
                HedefGrupVurgula(kod);
                e.Effect = kod != null ? DragDropEffects.Move : DragDropEffects.None;
            }
            else e.Effect = ayarlar.Dosya && e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        }

        /// <summary>İmlecin bulunduğu grup: üstündeki en yakın grup başlığı.</summary>
        string HedefGrup(int ekranX, int ekranY)
        {
            var p = izgara.PointToClient(new Point(ekranX, ekranY));
            var gorunen = basliklar.Values.Where(b => !b.IsDisposed && b.Parent == izgara).OrderBy(b => b.Top).ToList();
            if (gorunen.Count == 0) return null;
            var b0 = gorunen.LastOrDefault(b => b.Top <= p.Y) ?? gorunen[0];
            return b0.Kod;
        }

        void HedefGrupVurgula(string kod)
        {
            foreach (var b in basliklar.Values) b.Hedef = b.Kod == kod;
        }

        void Birak(DragEventArgs e)
        {
            HedefGrupVurgula(null);
            if (e.Data.GetDataPresent(KartVerisi))
            {
                var kart = e.Data.GetData(KartVerisi) as PcKart;
                var kod = HedefGrup(e.X, e.Y);
                if (kart == null || kod == null) return;
                // Seçili bir kart sürüklendiyse seçili olanların hepsi taşınır.
                var tasinan = kart.Secili ? kartlar.Values.Where(k => k.Secili).Select(k => k.Pc).ToList() : new List<Pc> { kart.Pc };
                tasinan = tasinan.Where(p => p.Grup != kod).ToList();
                if (tasinan.Count == 0) return;
                lock (pcKilit) { foreach (var p in tasinan) p.Grup = kod; Depo.ListeYaz(pcler); }
                ListeyiCiz();
                Tost((tasinan.Count == 1 ? tasinan[0].Baslik : tasinan.Count + " bilgisayar") + " → " + Gruplar.Ad(kod));
                return;
            }
            if (ayarlar.Dosya && e.Data.GetDataPresent(DataFormats.FileDrop)) DosyaGonder((string[])e.Data.GetData(DataFormats.FileDrop));
        }

        void GrupAdiDegistir(string kod, string ad)
        {
            if (Gruplar.Liste.Any(g => g.Kod != kod && g.Ad.Equals(ad, StringComparison.CurrentCultureIgnoreCase))) { Tost("Bu adda bir grup zaten var."); return; }
            Gruplar.Kaydet(Gruplar.Liste.Select(g => (g.Kod, g.Kod == kod ? ad : g.Ad)).ToList());
            ListeyiCiz();
            foreach (var b in basliklar.Values) b.Invalidate();
            Tost("Grup adı: " + ad);
        }

        void GruplariDuzenle()
        {
            using (var f = new GruplarForm())
            {
                if (f.ShowDialog(this) != DialogResult.OK) return;
                Gruplar.Kaydet(f.Sonuc);
                // Silinen gruptaki bilgisayarlar genel gruba geçer
                lock (pcKilit)
                {
                    foreach (var p in pcler) if (!Gruplar.Liste.Any(g => g.Kod == p.Grup)) p.Grup = Gruplar.Varsayilan;
                    Depo.ListeYaz(pcler);
                }
                ListeyiCiz();
                Tost("Gruplar kaydedildi.");
            }
        }

        Pc BuPc() { lock (pcKilit) return pcler.FirstOrDefault(p => p.Ad.Equals(Environment.MachineName, StringComparison.OrdinalIgnoreCase)); }

        void AyarlariAc()
        {
            var bu = BuPc()?.SonDurum;
            bool eskiPay = bu == null || bu["paylasim"] != "0", eskiTel = bu == null || bu["telefon"] != "0";
            using (var f = new AyarlarForm(YonetimAyar.Oku(), eskiPay, eskiTel, () => Guvenli(AcilisKapanis)))
            {
                if (f.ShowDialog(this) != DialogResult.OK) return;
                if (f.Paylasim != eskiPay || f.Telefon != eskiTel)
                    Uygula("Paylaşım ayarı", AcikHedefler(true), p => { var m = new Mesaj("AYAR"); m["paylasim"] = f.Paylasim ? "1" : "0"; m["telefon"] = f.Telefon ? "1" : "0"; return m; });
                // Açılış/kapanış bu arada (alt pencereden) kaydedilmiş olabilir: onları koru
                var y = YonetimAyar.Oku(); var a = f.Ayar;
                y.Onizleme = a.Onizleme; y.Dosya = a.Dosya; y.Yazici = a.Yazici; y.Program = a.Program; y.Duvar = a.Duvar; y.Duyurular = a.Duyurular; y.MesajYanit = a.MesajYanit; y.KlasikMenu = a.KlasikMenu;
                ayarlar = y; ayarlar.Yaz();
                OzellikleriUygula();
                if (ayarlar.Onizleme) OnizlemeYenile();
            }
        }

        string sonPlan;

        Mesaj SabahPlani(string saat, string gunler)
        {
            var m = new Mesaj("SABAH_PLANI"); m["saat"] = saat; m["gunler"] = gunler;
            lock (pcKilit) m["macler"] = string.Join(";", pcler.Where(p => p.Mac != "").Select(p => p.Mac).Distinct().OrderBy(x => x));
            return m;
        }

        /// <summary>Sabah açılma açıksa, listeye yeni bilgisayar eklendiğinde planı açık bilgisayarlara sessizce yeniden gönderir.</summary>
        void SabahPlaniTazele(List<Pc> kopya)
        {
            if (ayarlar.AcKapTanimli || !ayarlar.Sabah || !oturumAcik && !tepsiModu) return;
            var bu = kopya.FirstOrDefault(p => p.Ad.Equals(Environment.MachineName, StringComparison.OrdinalIgnoreCase))?.SonDurum;
            if (bu == null || bu["sabah_saat"] == "") return;
            var m = SabahPlani(bu["sabah_saat"], bu["sabah_gunler"]);
            var imza = m["saat"] + "|" + m["gunler"] + "|" + m["macler"];
            if (imza == sonPlan) return;
            sonPlan = imza;
            foreach (var p in kopya.Where(x => x.Acik))
                try { Baglanti.Gonder(p.Ip, m, 4000); } catch { }
        }
    }

    /// <summary>
    /// Çerçevesiz pencerede kenardan boyutlandırma: kenarın birkaç piksel yakınındaki fare olayları hangi denetimin
    /// üstünde olursa olsun yakalanır; imleç değişir, basınca Windows'un kendi boyutlandırması başlar.
    /// </summary>
    class KenarFiltresi : IMessageFilter
    {
        readonly Form f; readonly Func<int> pay;
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool ReleaseCapture();
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h, int m, IntPtr w, IntPtr l);
        public KenarFiltresi(Form f, Func<int> pay) { this.f = f; this.pay = pay; }

        public bool PreFilterMessage(ref Message m)
        {
            if (m.Msg != 0x200 && m.Msg != 0x201) return false; // WM_MOUSEMOVE, WM_LBUTTONDOWN
            if (f.IsDisposed || !f.Visible || f.WindowState != FormWindowState.Normal || Form.ActiveForm != f) return false;
            var c = Control.FromHandle(m.HWnd);
            if (c == null || (c != f && c.FindForm() != f)) return false;
            int ht = Tema.KenarBul(f, Cursor.Position, 1, pay());
            if (ht == 1) return false;
            if (m.Msg == 0x200)
            {
                Cursor.Current = ht == 10 || ht == 11 ? Cursors.SizeWE : ht == 12 || ht == 15 ? Cursors.SizeNS : ht == 13 || ht == 17 ? Cursors.SizeNWSE : Cursors.SizeNESW;
                return true;
            }
            ReleaseCapture();
            SendMessage(f.Handle, 0xA1, (IntPtr)ht, IntPtr.Zero); // WM_NCLBUTTONDOWN
            return true;
        }
    }
}
