using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Media;
using System.Windows.Forms;
using Sem.Arayuz;

namespace Sem.Ajan
{
    /// <summary>
    /// Duyuru: ekranın tamamı bulanıklaşıp kararır, ortada logolu bir kart çıkar. "Tamam"a basılınca kapanır.
    /// </summary>
    class DuyuruForm : Form
    {
        public DuyuruForm(string metin, string baslik, Action okundu = null)
        {
            FormBorderStyle = FormBorderStyle.None; StartPosition = FormStartPosition.Manual; TopMost = true; ShowInTaskbar = true;
            Text = "Sem Duyuru"; KeyPreview = true; DoubleBuffered = true;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            var ekran = Screen.PrimaryScreen.Bounds;
            Bounds = ekran;
            if (string.IsNullOrWhiteSpace(baslik)) baslik = "Duyuru";

            // Kart ölçüleri
            int kg = Math.Min(Tema.S(640), ekran.Width - Tema.S(40));
            var metinFont = Tema.Yazi(15f);
            var baslikFont = Tema.YaziKalin(22f);
            int metinG = kg - Tema.S(80);
            int metinY;
            using (var g = CreateGraphics()) metinY = (int)Math.Ceiling(g.MeasureString(metin, metinFont, metinG).Height);
            int maksMetin = (int)(ekran.Height * 0.55);
            metinY = Math.Min(metinY, maksMetin);
            int ky = Tema.S(40 + 72 + 24) + metinY + Tema.S(40 + 52 + 36);
            var kart = new Rectangle((ekran.Width - kg) / 2, (ekran.Height - ky) / 2, kg, ky);

            var zemin = new Bitmap(ekran.Width, ekran.Height);
            using (var g = Graphics.FromImage(zemin))
            {
                Tema.Kaliteli(g);
                try
                {
                    using (var ham = new Bitmap(ekran.Width, ekran.Height))
                    {
                        using (var hg = Graphics.FromImage(ham)) hg.CopyFromScreen(ekran.Location, Point.Empty, ekran.Size);
                        // Kademeli küçült-büyüt: yumuşak bir bulanıklık verir
                        Bitmap kaynak = ham;
                        foreach (int bol in new[] { 4, 12, 24, 12, 4 })
                        {
                            var hedef = new Bitmap(Math.Max(1, ekran.Width / bol), Math.Max(1, ekran.Height / bol));
                            using (var hg2 = Graphics.FromImage(hedef))
                            {
                                hg2.InterpolationMode = InterpolationMode.HighQualityBilinear;
                                hg2.PixelOffsetMode = PixelOffsetMode.HighQuality;
                                hg2.DrawImage(kaynak, new Rectangle(0, 0, hedef.Width, hedef.Height));
                            }
                            if (kaynak != ham) kaynak.Dispose();
                            kaynak = hedef;
                        }
                        g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                        g.DrawImage(kaynak, new Rectangle(-Tema.S(4), -Tema.S(4), ekran.Width + Tema.S(8), ekran.Height + Tema.S(8)));
                        kaynak.Dispose();
                    }
                }
                catch { g.Clear(Color.FromArgb(60, 64, 70)); }
                using (var b = new SolidBrush(Color.FromArgb(120, 24, 22, 18))) g.FillRectangle(b, 0, 0, ekran.Width, ekran.Height);

                // Kartın gölgesi ve kendisi
                for (int i = 16; i >= 1; i--)
                    using (var p = Tema.Yuvarlak(new RectangleF(kart.X - i, kart.Y - i + Tema.S(8), kart.Width + i * 2, kart.Height + i * 2), Tema.S(22) + i))
                    using (var b = new SolidBrush(Color.FromArgb(3, 0, 0, 0))) g.FillPath(b, p);
                using (var p = Tema.Yuvarlak(kart, Tema.S(22)))
                {
                    using (var b = new SolidBrush(Renk.Yuzey)) g.FillPath(b, p);
                }

                int x = kart.X + Tema.S(40), y = kart.Y + Tema.S(40);
                using (var logo = Tema.Logo()) if (logo != null) g.DrawImage(logo, new Rectangle(x, y, Tema.S(72), Tema.S(72)));
                Tema.YaziCiz(g, "DUYURU  ·  " + DateTime.Now.ToString("dd.MM.yyyy HH:mm"), Tema.YaziKalin(8.5f), Renk.Mavi, new RectangleF(x + Tema.S(90), y + Tema.S(6), kg - Tema.S(170), Tema.S(20)));
                Tema.YaziCiz(g, baslik, baslikFont, Renk.Yazi, new RectangleF(x + Tema.S(88), y + Tema.S(26), kg - Tema.S(170), Tema.S(44)));
                y += Tema.S(72 + 24);
                using (var pen = new Pen(Renk.Cizgi)) g.DrawLine(pen, x, y, kart.Right - Tema.S(40), y);
                y += Tema.S(24);
                Tema.YaziCiz(g, metin, metinFont, Renk.Yazi, new RectangleF(x, y, metinG, metinY), StringAlignment.Near, StringAlignment.Near, false);
                Tema.YaziCiz(g, "Sem Özel Eğitim ve Rehabilitasyon Merkezi", Tema.Yazi(9f), Renk.Yazi3,
                    new RectangleF(x, kart.Bottom - Tema.S(36 + 52), kg / 2f, Tema.S(52)));
            }
            BackgroundImage = zemin; BackgroundImageLayout = ImageLayout.None;

            var tamam = new Buton("Tamam", Ik.Tik, ButonTuru.Birincil) { Font = Tema.YaziKalin(12f), Size = Tema.S(170, 52), Yaricap = 14 };
            tamam.Location = new Point(kart.Right - Tema.S(40) - tamam.Width, kart.Bottom - Tema.S(36) - tamam.Height);
            bool bitti = false;
            Action kapat = () => { if (bitti) return; bitti = true; try { okundu?.Invoke(); } catch { } Close(); };
            tamam.Click += (s, e) => kapat();
            Controls.Add(tamam);
            AcceptButton = null;
            KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) kapat(); };
            Shown += (s, e) => { Activate(); tamam.Focus(); SystemSounds.Asterisk.Play(); };
            FormClosed += (s, e) => zemin.Dispose();
        }
    }

    /// <summary>Kapanmadan önce çıkan, halka şeklinde geri sayımlı uyarı kartı.</summary>
    class UyariForm : ModernForm
    {
        readonly Ekran ekran;
        readonly DateTime hedef;
        readonly double toplamSn;
        readonly Halka halka;
        readonly Timer t;
        readonly Panel sifrePanel;
        readonly Giris sifre;
        readonly Label hata;

        public UyariForm(Ekran ekran, int kalanSn, int erteleHak, int erteleDk, string neden) : base(null, 440, 470, false)
        {
            this.ekran = ekran;
            hedef = DateTime.Now.AddSeconds(kalanSn); toplamSn = Math.Max(1, kalanSn);
            Text = "Sem - Bilgisayar kapanıyor"; TopMost = true; ShowInTaskbar = true; StartPosition = FormStartPosition.CenterScreen;
            int g = Icerik.Width;

            var ust = Yazi.UstBaslik("Bilgisayar kapanıyor", Renk.Pembe);
            ust.Location = new Point((g - ust.PreferredWidth) / 2, 0);
            var bas = Yazi.Etiket(neden, 13f, true, Renk.Yazi, false);
            bas.TextAlign = ContentAlignment.MiddleCenter; bas.SetBounds(0, Tema.S(22), g, Tema.S(56));
            var not = Yazi.Etiket("Lütfen açık çalışmalarınızı kaydedin.", 10f, false, Renk.Yazi2, false);
            not.TextAlign = ContentAlignment.MiddleCenter; not.SetBounds(0, Tema.S(78), g, Tema.S(22));
            halka = new Halka { Size = Tema.S(170, 170) };
            halka.Location = new Point((g - halka.Width) / 2, Tema.S(112));
            Icerik.Controls.AddRange(new Control[] { ust, bas, not, halka });

            int y = Tema.S(300);
            if (erteleHak > 0)
            {
                var ertele = new Buton(erteleDk + " dakika ertele", Ik.Saat, ButonTuru.Basari) { Width = g, Location = new Point(0, y), Height = Tema.S(46) };
                ertele.Click += (s, e) => { ekran.Gonder("ERTELE"); Close(); };
                var hak = Yazi.Etiket(erteleHak == 1 ? "Son erteleme hakkı" : erteleHak + " erteleme hakkı kaldı", 9f, false, Renk.Yazi2, false);
                hak.TextAlign = ContentAlignment.MiddleCenter; hak.SetBounds(0, y + Tema.S(50), g, Tema.S(20));
                Icerik.Controls.Add(ertele); Icerik.Controls.Add(hak);
            }
            y += Tema.S(78);

            var yonetici = new Buton("Yönetici: bugün kapatma", Ik.Anahtar, ButonTuru.Sade) { Font = Tema.Yazi(9.5f) };
            yonetici.BoyutAyarla(); yonetici.Location = new Point((g - yonetici.Width) / 2, y);
            Icerik.Controls.Add(yonetici);

            sifrePanel = new Panel { Location = new Point(0, y), Size = new Size(g, Tema.S(70)), Visible = false, BackColor = Renk.Zemin };
            sifre = new Giris(true) { Width = g - Tema.S(110), Location = new Point(0, 0), Ipucu = "Yönetici şifresi" };
            var onay = new Buton("Onayla", "", ButonTuru.Birincil) { Location = new Point(g - Tema.S(100), 0), Size = Tema.S(100, 42) };
            hata = Yazi.Etiket("", 9f, false, Renk.Pembe);
            hata.Location = new Point(Tema.S(4), Tema.S(48));
            sifrePanel.Controls.AddRange(new Control[] { sifre, onay, hata });
            Icerik.Controls.Add(sifrePanel);

            yonetici.Click += (s, e) => { yonetici.Visible = false; sifrePanel.Visible = true; sifre.Focus(); };
            EventHandler gonder = (s, e) => { if (sifre.Text != "") { hata.Text = "Kontrol ediliyor…"; ekran.Gonder("IPTAL", sifre.Text); } };
            onay.Click += gonder;
            sifre.Kutu.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; gonder(s, e); } };

            t = new Timer { Interval = 250 };
            t.Tick += (s, e) => Guncelle();
            t.Start();
            Guncelle();
            Shown += (s, e) => { Activate(); SystemSounds.Exclamation.Play(); };
            FormClosed += (s, e) => t.Dispose();
        }

        void Guncelle()
        {
            var k = hedef - DateTime.Now;
            if (k < TimeSpan.Zero) k = TimeSpan.Zero;
            halka.Oran = (float)(k.TotalSeconds / toplamSn);
            halka.Metin = k.TotalSeconds <= 0 ? "…" : string.Format("{0}:{1:00}", (int)k.TotalMinutes, k.Seconds);
            halka.AltMetin = k.TotalSeconds <= 0 ? "kapanıyor" : "kalan süre";
            halka.Invalidate();
            if (!TopMost) TopMost = true;
        }

        public void IptalSonucu(bool dogru)
        {
            if (dogru) { Close(); new BildirimForm("Kapanma iptal edildi", "Bu bilgisayar bugün otomatik kapanmayacak.", Ik.TikDaire, Renk.Yesil).Show(); }
            else { hata.Text = "Şifre yanlış, tekrar deneyin."; sifre.Text = ""; sifre.Focus(); }
        }

        /// <summary>Geri sayım halkası.</summary>
        class Halka : Control
        {
            public float Oran = 1; public string Metin = "", AltMetin = "";
            public Halka() { SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor, true); BackColor = Color.Transparent; }
            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics; Tema.Kaliteli(g);
                float k = Tema.S(10);
                var r = new RectangleF(k, k, Width - k * 2 - 1, Height - k * 2 - 1);
                using (var pen = new Pen(Renk.Acik(Renk.Pembe, 30), k)) g.DrawEllipse(pen, r);
                using (var pen = new Pen(Renk.Pembe, k) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                    if (Oran > 0.002f) g.DrawArc(pen, r, -90, 360 * Math.Min(1, Oran));
                Tema.YaziCiz(g, Metin, Tema.YaziKalin(28f), Renk.Yazi, new RectangleF(0, -Tema.S(10), Width, Height), StringAlignment.Center);
                Tema.YaziCiz(g, AltMetin, Tema.Yazi(9f), Renk.Yazi2, new RectangleF(0, Tema.S(34), Width, Height), StringAlignment.Center);
            }
        }

    }

    /// <summary>Sağ altta birkaç saniye görünüp kaybolan bildirim kartı.</summary>
    class BildirimForm : ModernForm
    {
        protected override bool ShowWithoutActivation => true;

        public BildirimForm(string baslik, string metin, string ikon = Ik.Zil, Color? renk = null, Action tikla = null) : base(null, 380, 96, false)
        {
            TopMost = true; StartPosition = FormStartPosition.Manual; BackColor = Renk.Yuzey; Icerik.BackColor = Renk.Yuzey;
            var alan = Screen.PrimaryScreen.WorkingArea;
            Location = new Point(alan.Right - Width - Tema.S(16), alan.Bottom - Height - Tema.S(16));
            Icerik.SetBounds(Tema.S(16), Tema.S(16), Width - Tema.S(32), Height - Tema.S(32));
            var r = renk ?? Renk.Mavi;
            var ik = new Panel { Size = Tema.S(44, 44), Location = new Point(0, Tema.S(4)), BackColor = Renk.Yuzey };
            ik.Paint += (s, e) => { Tema.Kaliteli(e.Graphics); using (var b = new SolidBrush(Renk.Acik(r, 28))) e.Graphics.FillEllipse(b, 0, 0, ik.Width - 1, ik.Height - 1); Tema.IkonCiz(e.Graphics, ikon, 14f, r, new RectangleF(0, 0, ik.Width, ik.Height)); };
            var b1 = Yazi.Etiket(baslik, 10.5f, true); b1.Location = new Point(Tema.S(58), 0);
            var m = Yazi.Etiket(metin, 9f, false, Renk.Yazi2, false); m.SetBounds(Tema.S(58), Tema.S(22), Icerik.Width - Tema.S(58), Tema.S(44));
            Icerik.Controls.AddRange(new Control[] { ik, b1, m });
            foreach (Control c in new Control[] { this, Icerik, ik, b1, m }) { c.Click += (s, e) => { try { tikla?.Invoke(); } catch { } Close(); }; if (tikla != null) c.Cursor = Cursors.Hand; }
            var t = new Timer { Interval = 9000 };
            t.Tick += (s, e) => { t.Dispose(); Close(); };
            t.Start();
        }
    }
}
