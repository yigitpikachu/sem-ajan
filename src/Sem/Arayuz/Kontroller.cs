using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Sem.Arayuz
{
    public enum ButonTuru { Birincil, Ikincil, Sade, Tehlike, Basari, Uyari }

    /// <summary>Yuvarlak köşeli, ikonlu, üzerine gelince renk değiştiren düğme.</summary>
    public class Buton : Control
    {
        public string Ikon { get; set; } = "";
        public ButonTuru Tur { get; set; } = ButonTuru.Ikincil;
        public float Yaricap { get; set; } = 10;
        public bool Dikey { get; set; }  // ikon üstte, yazı altta (işlem çubuğu için)
        bool uzerinde, basili;

        /// <summary>true ise tıklamak yetmez; düğme dolana kadar basılı tutulunca <see cref="Onaylandi"/> çalışır.</summary>
        public bool BasiliTut { get; set; }
        public int TutSuresiMs { get; set; } = 900;
        public event EventHandler Onaylandi, KisaBasildi;
        float ilerleme; bool tamamlandi; DateTime tutBaslangic;
        Timer tutZaman;

        public Buton(string metin, string ikon = "", ButonTuru tur = ButonTuru.Ikincil)
        {
            Text = metin; Ikon = ikon; Tur = tur;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Font = Tema.YaziKalin(9.75f);
            Cursor = Cursors.Hand;
            Height = Tema.S(36);
            Margin = Tema.Pd(4, 4, 4, 4);
            BoyutAyarla();
        }

        public void BoyutAyarla()
        {
            using (var g = CreateGraphics())
            {
                var w = g.MeasureString(Text, Font).Width;
                if (Dikey) { Width = Math.Max(Tema.S(60), (int)w + Tema.S(14)); Height = Tema.S(62); }
                else Width = (int)w + Tema.S(Ikon != "" ? 52 : 32) - (Text == "" ? Tema.S(20) : 0);
            }
        }

        protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); Invalidate(); }
        protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }
        protected override void OnMouseEnter(EventArgs e) { uzerinde = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { uzerinde = basili = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            basili = true; Invalidate();
            if (BasiliTut && Enabled && e.Button == MouseButtons.Left)
            {
                tamamlandi = false; tutBaslangic = DateTime.Now;
                if (tutZaman == null) { tutZaman = new Timer { Interval = 15 }; tutZaman.Tick += (s, a) => TutAdim(); Disposed += (s, a) => tutZaman.Dispose(); }
                tutZaman.Start();
            }
            base.OnMouseDown(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            bool kisa = BasiliTut && !tamamlandi && basili && (DateTime.Now - tutBaslangic).TotalMilliseconds < 350;
            basili = false; Invalidate(); base.OnMouseUp(e);
            if (kisa) KisaBasildi?.Invoke(this, EventArgs.Empty);
        }

        void TutAdim()
        {
            if (basili && !tamamlandi)
            {
                ilerleme = (float)Math.Min(1, (DateTime.Now - tutBaslangic).TotalMilliseconds / TutSuresiMs);
                if (ilerleme >= 1) { tamamlandi = true; basili = false; Onaylandi?.Invoke(this, EventArgs.Empty); }
            }
            else
            {
                ilerleme -= tamamlandi ? 0.05f : 0.12f;
                if (ilerleme <= 0) { ilerleme = 0; tutZaman.Stop(); }
            }
            Invalidate();
        }

        protected override void OnClick(EventArgs e) { if (!BasiliTut) base.OnClick(e); }
        protected override void OnKeyDown(KeyEventArgs e) { if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space) OnClick(EventArgs.Empty); base.OnKeyDown(e); }

        (Color dolgu, Color yazi, Color? cizgi) Renkler()
        {
            Color ana;
            switch (Tur)
            {
                case ButonTuru.Birincil: ana = Renk.Mavi; break;
                case ButonTuru.Tehlike: ana = Renk.Pembe; break;
                case ButonTuru.Basari: ana = Renk.Yesil; break;
                case ButonTuru.Uyari: ana = Renk.Turuncu; break;
                default: ana = Color.Empty; break;
            }
            if (!Enabled) return (Tur == ButonTuru.Sade ? Color.Transparent : Color.FromArgb(238, 238, 234), Renk.Yazi3, null);
            if (ana != Color.Empty)
            {
                var d = basili ? Renk.Karistir(ana, Color.Black, 0.18) : uzerinde ? Renk.Karistir(ana, Color.Black, 0.08) : ana;
                return (d, Color.White, null);
            }
            if (Tur == ButonTuru.Sade)
                return (basili ? Renk.CizgiKoyu : uzerinde ? Renk.Hover : Color.Transparent, Renk.Yazi, null);
            return (basili ? Renk.Hover : uzerinde ? Color.FromArgb(249, 249, 247) : Renk.Yuzey, Renk.Yazi, Renk.Cizgi);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; Tema.Kaliteli(g);
            var (dolgu, yazi, cizgi) = Renkler();
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            using (var p = Tema.Yuvarlak(r, Tema.S(Yaricap)))
            {
                if (dolgu.A > 0) using (var b = new SolidBrush(dolgu)) g.FillPath(b, p);
                if (cizgi != null) using (var pen = new Pen(cizgi.Value)) g.DrawPath(pen, p);
                if (Focused && ShowFocusCues) using (var pen = new Pen(Renk.Acik(Renk.Mavi, 140), Tema.S(2))) g.DrawPath(pen, p);
                if (ilerleme > 0)
                {
                    // Basılı tutma: düğme alttan yukarı dolar
                    var eski = g.Clip; g.SetClip(p, CombineMode.Intersect);
                    float h = r.Height * ilerleme;
                    var renk = ForeColorOzel != Color.Empty ? ForeColorOzel : Renk.Pembe;
                    using (var b = new SolidBrush(Renk.Acik(renk, tamamlandi ? 70 : 46))) g.FillRectangle(b, r.X, r.Bottom - h, r.Width, h);
                    g.Clip = eski;
                }
            }
            var ikonRenk = yazi;
            if (Tur == ButonTuru.Sade || Tur == ButonTuru.Ikincil) ikonRenk = Enabled ? Renk.Yazi2 : Renk.Yazi3;
            if (Dikey)
            {
                Tema.IkonCiz(g, Ikon, 15f, Enabled && (Tur == ButonTuru.Sade) && ForeColorOzel != Color.Empty ? ForeColorOzel : ikonRenk, new RectangleF(0, Tema.S(6), Width, Tema.S(30)));
                Tema.YaziCiz(g, Text, Tema.Yazi(8.5f), Enabled ? yazi : Renk.Yazi3, new RectangleF(0, Tema.S(36), Width, Tema.S(20)), StringAlignment.Center);
                return;
            }
            if (Ikon != "" && Text != "")
            {
                // İkon + yazı grubunu ortala
                float yw = g.MeasureString(Text, Font).Width;
                float toplam = Tema.S(22) + yw;
                float x0 = Math.Max(Tema.S(10), (Width - toplam) / 2f);
                Tema.IkonCiz(g, Ikon, 12f, ikonRenk, new RectangleF(x0 - Tema.S(2), 0, Tema.S(22), Height));
                Tema.YaziCiz(g, Text, Font, yazi, new RectangleF(x0 + Tema.S(22), 0, Width - x0 - Tema.S(26), Height));
            }
            else if (Ikon != "") Tema.IkonCiz(g, Ikon, 13f, ikonRenk, new RectangleF(0, 0, Width, Height));
            else Tema.YaziCiz(g, Text, Font, yazi, new RectangleF(0, 0, Width, Height), StringAlignment.Center);
        }

        /// <summary>Sade düğmelerde ikonun özel rengi (ör. kapat için pembe).</summary>
        public Color ForeColorOzel { get; set; } = Color.Empty;
    }

    /// <summary>Yuvarlak kenarlı, odaklanınca mavi halkası çıkan metin kutusu.</summary>
    public class Giris : UserControl
    {
        public readonly TextBox Kutu;
        string ipucu = "";
        public string Ipucu { get => ipucu; set { ipucu = value; IpucuAyarla(); } }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, string l);

        public Giris(bool sifre = false, bool cokSatir = false)
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Kutu = new TextBox
            {
                BorderStyle = BorderStyle.None, Font = Tema.Yazi(cokSatir ? 11f : 10.5f), BackColor = Renk.Yuzey, ForeColor = Renk.Yazi,
                UseSystemPasswordChar = sifre, Multiline = cokSatir, AcceptsReturn = cokSatir, ScrollBars = ScrollBars.None
            };
            Controls.Add(Kutu);
            Height = cokSatir ? Tema.S(160) : Tema.S(42);
            Kutu.GotFocus += (s, e) => Invalidate();
            Kutu.LostFocus += (s, e) => Invalidate();
            Kutu.HandleCreated += (s, e) => IpucuAyarla();
            Cursor = Cursors.IBeam;
            Click += (s, e) => Kutu.Focus();
        }

        void IpucuAyarla()
        {
            try { if (Kutu.IsHandleCreated && Environment.OSVersion.Platform == PlatformID.Win32NT) SendMessage(Kutu.Handle, 0x1501, (IntPtr)1, ipucu); } catch { }
        }

        public override string Text { get => Kutu.Text; set => Kutu.Text = value; }
        public new event EventHandler TextChanged { add => Kutu.TextChanged += value; remove => Kutu.TextChanged -= value; }
        public new void Focus() => Kutu.Focus();

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            int px = Tema.S(14);
            if (Kutu.Multiline) Kutu.SetBounds(px, Tema.S(10), Width - px * 2 + Tema.S(4), Height - Tema.S(20));
            else Kutu.SetBounds(px, (Height - Kutu.PreferredHeight) / 2, Width - px * 2, Kutu.PreferredHeight);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; Tema.Kaliteli(g);
            bool odak = Kutu.Focused;
            var r = new RectangleF(1, 1, Width - 2.5f, Height - 2.5f);
            using (var p = Tema.Yuvarlak(r, Tema.S(10)))
            {
                using (var b = new SolidBrush(Renk.Yuzey)) g.FillPath(b, p);
                using (var pen = new Pen(odak ? Renk.Mavi : Renk.CizgiKoyu, odak ? Tema.S(2) : 1)) g.DrawPath(pen, p);
            }
        }
    }

    /// <summary>macOS tarzı açma/kapama anahtarı, yanında başlık ve açıklama.</summary>
    public class Anahtar : Control
    {
        bool acik;
        public string Aciklama { get; set; } = "";
        public event EventHandler Degisti;
        public bool Acik { get => acik; set { if (acik != value) { acik = value; Invalidate(); Degisti?.Invoke(this, EventArgs.Empty); } } }

        public Anahtar(string baslik, string aciklama = "")
        {
            Text = baslik; Aciklama = aciklama;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent; Cursor = Cursors.Hand;
            Height = Tema.S(aciklama == "" ? 32 : 60);
        }

        protected override void OnClick(EventArgs e) { if (Enabled) Acik = !Acik; base.OnClick(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; Tema.Kaliteli(g);
            int w = Tema.S(42), h = Tema.S(24);
            var r = new RectangleF(Width - w - 2, (Aciklama == "" ? (Height - h) / 2f : Tema.S(4)), w, h);
            using (var p = Tema.Yuvarlak(r, h / 2f))
            using (var b = new SolidBrush(!Enabled ? Renk.Cizgi : acik ? Renk.Yesil : Renk.CizgiKoyu)) g.FillPath(b, p);
            float d = h - Tema.S(4);
            var top = new RectangleF(acik ? r.Right - d - Tema.S(2) : r.X + Tema.S(2), r.Y + Tema.S(2), d, d);
            using (var b = new SolidBrush(Color.White)) g.FillEllipse(b, top);
            int yaziG = Width - w - Tema.S(12);
            if (Aciklama == "")
                Tema.YaziCiz(g, Text, Tema.Yazi(10.5f), Enabled ? Renk.Yazi : Renk.Yazi3, new RectangleF(0, 0, yaziG, Height));
            else
            {
                Tema.YaziCiz(g, Text, Tema.YaziKalin(10.5f), Renk.Yazi, new RectangleF(0, 0, yaziG, Tema.S(26)));
                Tema.YaziCiz(g, Aciklama, Tema.Yazi(9f), Renk.Yazi2, new RectangleF(0, Tema.S(24), yaziG, Height - Tema.S(24)), StringAlignment.Near, StringAlignment.Near, false);
            }
        }
    }

    /// <summary>[ − ] 15 [ + ] sayı seçici.</summary>
    public class Adim : Control
    {
        int deger, min, max;
        public string Birim { get; set; } = "";
        public int Deger { get => deger; set { deger = Math.Max(min, Math.Min(max, value)); Invalidate(); } }

        public Adim(int min, int max, int deger, string birim = "")
        {
            this.min = min; this.max = max; Deger = deger; Birim = birim;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent; Size = Tema.S(140, 36);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.X < Height) Deger--; else if (e.X > Width - Height) Deger++;
            base.OnMouseDown(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; Tema.Kaliteli(g);
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            using (var p = Tema.Yuvarlak(r, Tema.S(10)))
            {
                using (var b = new SolidBrush(Renk.Yuzey)) g.FillPath(b, p);
                using (var pen = new Pen(Renk.CizgiKoyu)) g.DrawPath(pen, p);
            }
            Tema.IkonCiz(g, Ik.Eksi, 11f, deger > min ? Renk.Yazi : Renk.Yazi3, new RectangleF(0, 0, Height, Height));
            Tema.IkonCiz(g, Ik.Ekle, 11f, deger < max ? Renk.Yazi : Renk.Yazi3, new RectangleF(Width - Height, 0, Height, Height));
            Tema.YaziCiz(g, deger + (Birim == "" ? "" : " " + Birim), Tema.YaziKalin(10.5f), Renk.Yazi, new RectangleF(Height, 0, Width - Height * 2, Height), StringAlignment.Center);
        }
    }

    /// <summary>Şeffaf zemin üzerinde yuvarlak beyaz kart çizen panel.</summary>
    public class KartPanel : Panel
    {
        public float Yaricap = 14;
        public Color Dolgu = Renk.Yuzey;
        public bool Golge = true;
        public bool Cizgisiz;

        public KartPanel()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            using (var b = new SolidBrush(Parent?.BackColor ?? Renk.Zemin)) e.Graphics.FillRectangle(b, ClientRectangle);
            Tema.Kaliteli(e.Graphics);
            if (Cizgisiz) Tema.YuzenKart(e.Graphics, new RectangleF(Tema.S(3), Tema.S(1), Width - Tema.S(6), Height - Tema.S(8)), Tema.S(Yaricap), Dolgu, 0);
            else Tema.Kart(e.Graphics, new Rectangle(0, 0, Width - 1, Height - Tema.S(Golge ? 4 : 1)), Tema.S(Yaricap), Dolgu, Renk.Cizgi, Golge);
        }
    }

    /// <summary>Basit etiket üretici.</summary>
    public static class Yazi
    {
        public static Label Etiket(string metin, float boyut = 10f, bool kalin = false, Color? renk = null, bool otomatik = true) => new Label
        {
            Text = metin, Font = kalin ? Tema.YaziKalin(boyut) : Tema.Yazi(boyut), ForeColor = renk ?? Renk.Yazi,
            AutoSize = otomatik, BackColor = Color.Transparent, UseMnemonic = false
        };

        /// <summary>Kurum sitesindeki gibi küçük, harf aralıklı, büyük harfli üst başlık.</summary>
        public static Label UstBaslik(string metin, Color? renk = null) => Etiket(metin.ToUpper(new System.Globalization.CultureInfo("tr-TR")), 8f, true, renk ?? Renk.Mavi);
    }

    /// <summary>
    /// Kenarlıksız, yuvarlak köşeli, gölgeli pencere. Üstte başlık ve kapat düğmesi vardır, başlıktan tutup taşınabilir.
    /// Diyalogların, uyarıların ve kurulum penceresinin temeli.
    /// </summary>
    public class ModernForm : Form
    {
        public readonly Panel Icerik;
        protected readonly Buton KapatDugmesi;
        readonly string baslik;
        readonly bool baslikVar;
        bool dwmYuvarlak;

        [DllImport("user32.dll")] static extern bool ReleaseCapture();
        [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h, int m, IntPtr w, IntPtr l);

        public ModernForm(string baslik, int genislik, int yukseklik, bool kapatilabilir = true)
        {
            this.baslik = baslik; baslikVar = baslik != null;
            FormBorderStyle = FormBorderStyle.None; StartPosition = FormStartPosition.CenterParent;
            BackColor = Renk.Zemin; ShowInTaskbar = false; KeyPreview = true;
            Font = Tema.Yazi(10f);
            ClientSize = Tema.S(genislik, yukseklik);
            DoubleBuffered = true;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            Text = baslik ?? "Sem Ajan";

            Icerik = new Panel { BackColor = Renk.Zemin, Location = new Point(Tema.S(24), Tema.S(baslikVar ? 64 : 24)) };
            Icerik.Size = new Size(ClientSize.Width - Tema.S(48), ClientSize.Height - Icerik.Top - Tema.S(20));
            Icerik.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            Controls.Add(Icerik);

            if (kapatilabilir)
            {
                KapatDugmesi = new Buton("", Ik.Kapat, ButonTuru.Sade) { Size = Tema.S(34, 34), Location = new Point(ClientSize.Width - Tema.S(48), Tema.S(14)), Anchor = AnchorStyles.Top | AnchorStyles.Right, TabStop = false };
                KapatDugmesi.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
                Controls.Add(KapatDugmesi);
                KapatDugmesi.BringToFront();
            }
            KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape && kapatilabilir) { DialogResult = DialogResult.Cancel; Close(); } };
            MouseDown += (s, e) => { if (e.Button == MouseButtons.Left && e.Y < Tema.S(60)) Tasi(); };
        }

        /// <summary>true ise pencere içeriğe göre kısaltılmaz.</summary>
        protected bool SabitBoyut;

        /// <summary>İçerik ile alttaki düğmeler arasındaki fazla boşluğu kaldırır; pencere içeriği kadar olur.</summary>
        protected override void OnLoad(EventArgs e)
        {
            if (!SabitBoyut)
            {
                int icerikAlt = 0, dugmeUst = int.MaxValue;
                foreach (Control c in Icerik.Controls)
                {
                    if ((c.Anchor & AnchorStyles.Bottom) != 0 && (c.Anchor & AnchorStyles.Top) == 0) { dugmeUst = Math.Min(dugmeUst, c.Top); continue; }
                    int alt = c.Bottom;
                    if (c is Label l && !l.AutoSize && l.Text != "")
                        using (var g = l.CreateGraphics()) alt = l.Top + Math.Min(l.Height, (int)Math.Ceiling(g.MeasureString(l.Text, l.Font, l.Width).Height) + Tema.S(4));
                    icerikAlt = Math.Max(icerikAlt, alt);
                }
                int fazla = dugmeUst == int.MaxValue ? 0 : dugmeUst - icerikAlt - Tema.S(24);
                if (fazla > 0) Height -= fazla;
            }
            base.OnLoad(e);
            Tema.Belir(this);
        }

        protected void Tasi() { if (Environment.OSVersion.Platform == PlatformID.Win32NT) { ReleaseCapture(); SendMessage(Handle, 0xA1, (IntPtr)2, IntPtr.Zero); } }

        protected override CreateParams CreateParams
        {
            get { var cp = base.CreateParams; cp.ClassStyle |= 0x20000; /* CS_DROPSHADOW */ return cp; }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            dwmYuvarlak = Tema.KoseleriYuvarla(Handle);
            BolgeAyarla();
        }

        protected override void OnResize(EventArgs e) { base.OnResize(e); BolgeAyarla(); Invalidate(); }

        void BolgeAyarla()
        {
            if (dwmYuvarlak) return;
            using (var p = Tema.Yuvarlak(new RectangleF(0, 0, Width, Height), Tema.S(12))) Region = new Region(p);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics; Tema.Kaliteli(g);
            using (var pen = new Pen(Renk.CizgiKoyu))
            using (var p = Tema.Yuvarlak(new RectangleF(0, 0, Width - 1, Height - 1), Tema.S(12))) g.DrawPath(pen, p);
            if (baslikVar)
                Tema.YaziCiz(g, baslik, Tema.YaziKalin(13f), Renk.Yazi, new RectangleF(Tema.S(24), Tema.S(14), Width - Tema.S(90), Tema.S(36)));
        }

        /// <summary>Alta sağa yaslı "Vazgeç / Tamam" düğmeleri ekler.</summary>
        protected Buton AltDugmeler(string tamamMetni = "Tamam", ButonTuru tur = ButonTuru.Birincil, string ikon = "")
        {
            var tamam = new Buton(tamamMetni, ikon, tur) { Anchor = AnchorStyles.Bottom | AnchorStyles.Right };
            var vazgec = new Buton("Vazgeç", "", ButonTuru.Sade) { Anchor = AnchorStyles.Bottom | AnchorStyles.Right };
            tamam.MinimumSize = new Size(Tema.S(110), 0); tamam.Width = Math.Max(tamam.Width, Tema.S(110));
            tamam.Location = new Point(Icerik.Width - tamam.Width, Icerik.Height - tamam.Height);
            vazgec.Location = new Point(tamam.Left - vazgec.Width - Tema.S(8), tamam.Top);
            vazgec.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
            Icerik.Controls.Add(tamam); Icerik.Controls.Add(vazgec);
            return tamam;
        }
    }

    /// <summary>Küçük, içerik kadar geniş açılır menü (Mac tarzı). Dışına tıklanınca kapanır.</summary>
    public class AcilirMenu : Form
    {
        class Oge { public string Metin, Ikon; public Action Tik; public bool Tehlike, Ayrac; public Rectangle Alan; }
        readonly List<Oge> ogeler = new List<Oge>();
        readonly Font yazi = Tema.Yazi(9.5f);
        int uzerinde = -1;

        public AcilirMenu()
        {
            FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; StartPosition = FormStartPosition.Manual;
            TopMost = true; BackColor = Renk.Yuzey; DoubleBuffered = true; KeyPreview = true;
            Deactivate += (s, e) => Close();
            KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) Close(); };
        }

        protected override CreateParams CreateParams
        {
            get { var c = base.CreateParams; c.ClassStyle |= 0x20000; /* CS_DROPSHADOW */ return c; }
        }

        public AcilirMenu Ekle(string metin, string ikon, Action tik, bool tehlike = false)
        { ogeler.Add(new Oge { Metin = metin, Ikon = ikon, Tik = tik, Tehlike = tehlike }); return this; }

        public AcilirMenu Ayrac() { ogeler.Add(new Oge { Ayrac = true }); return this; }

        void Yerlestir()
        {
            int pad = Tema.S(5), satir = Tema.S(30), ayrac = Tema.S(9), gen = 0;
            using (var g = CreateGraphics())
                foreach (var o in ogeler) if (!o.Ayrac) gen = Math.Max(gen, (int)Math.Ceiling(g.MeasureString(o.Metin, yazi).Width));
            int w = gen + Tema.S(12 + 18 + 10 + 18), y = pad;
            foreach (var o in ogeler) { int h = o.Ayrac ? ayrac : satir; o.Alan = new Rectangle(pad, y, w - 2 * pad, h); y += h; }
            ClientSize = new Size(w, y + pad);
            using (var yol = Tema.Yuvarlak(new RectangleF(0, 0, Width, Height), Tema.S(10))) Region = new Region(yol);
        }

        /// <summary>Menüyü bir kontrolün üstünde (yukari) ya da altında gösterir.</summary>
        public void Goster(Control yanina, bool yukari)
        {
            Yerlestir();
            var p = yanina.PointToScreen(Point.Empty);
            var ekran = Screen.FromControl(yanina).WorkingArea;
            int x = Math.Min(p.X + yanina.Width - Width, ekran.Right - Width);
            int y = yukari ? p.Y - Height - Tema.S(6) : p.Y + yanina.Height + Tema.S(6);
            Location = new Point(Math.Max(ekran.Left, x), Math.Max(ekran.Top, Math.Min(y, ekran.Bottom - Height)));
            Tema.Belir(this, 90);
            Show(yanina.FindForm());
        }

        /// <summary>Menüyü fare imlecinin olduğu yerde gösterir.</summary>
        public void ImlecteGoster(Control sahip)
        {
            Yerlestir();
            var p = Cursor.Position; var ekran = Screen.FromPoint(p).WorkingArea;
            Location = new Point(Math.Min(p.X, ekran.Right - Width), Math.Min(p.Y, ekran.Bottom - Height));
            Tema.Belir(this, 90);
            Show(sahip.FindForm());
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int i = ogeler.FindIndex(o => !o.Ayrac && o.Alan.Contains(e.Location));
            if (i != uzerinde) { uzerinde = i; Cursor = i >= 0 ? Cursors.Hand : Cursors.Default; Invalidate(); }
        }

        protected override void OnMouseLeave(EventArgs e) { uzerinde = -1; Invalidate(); }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            var o = ogeler.FirstOrDefault(x => !x.Ayrac && x.Alan.Contains(e.Location));
            if (o == null) return;
            Close();
            var sahip = Owner; // menü kapandıktan sonra çalışsın
            if (sahip != null && sahip.IsHandleCreated) sahip.BeginInvoke(o.Tik); else o.Tik();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; Tema.Kaliteli(g);
            for (int i = 0; i < ogeler.Count; i++)
            {
                var o = ogeler[i]; var r = o.Alan;
                if (o.Ayrac) { using (var pen = new Pen(Renk.Cizgi)) g.DrawLine(pen, r.X + Tema.S(8), r.Y + r.Height / 2, r.Right - Tema.S(8), r.Y + r.Height / 2); continue; }
                if (i == uzerinde)
                    using (var yol = Tema.Yuvarlak(r, Tema.S(6))) using (var b = new SolidBrush(o.Tehlike ? Renk.Acik(Renk.Pembe, 22) : Renk.Hover)) g.FillPath(b, yol);
                var renk = o.Tehlike ? Renk.Pembe : Renk.Yazi;
                Tema.IkonCiz(g, o.Ikon, 10f, o.Tehlike ? Renk.Pembe : Renk.Yazi2, new RectangleF(r.X + Tema.S(7), r.Y, Tema.S(20), r.Height));
                Tema.YaziCiz(g, o.Metin, yazi, renk, new RectangleF(r.X + Tema.S(35), r.Y, r.Width - Tema.S(40), r.Height));
            }
            using (var pen = new Pen(Renk.CizgiKoyu)) using (var yol = Tema.Yuvarlak(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), Tema.S(10))) g.DrawPath(pen, yol);
        }
    }

    /// <summary>
    /// Windows'un kalın kaydırma çubuğu yerine ince, yuvarlak bir çubuk (Mac tarzı).
    /// İçerik kontrolünün kendi çubuğu, kutunun dışına taşırılarak gizlenir; tekerlek ve dokunmatik kaydırma aynen çalışır.
    /// </summary>
    public class InceKaydirma : Control
    {
        readonly Func<int[]> oku;      // {değer, en çok, sayfa}
        readonly Action<int> yaz;
        readonly Control ic;
        readonly Timer zaman = new Timer { Interval = 50 };
        int sonDeger = -1; DateTime sonHareket = DateTime.MinValue;
        bool uzerinde, surukle; int surukleFark; float opaklik;

        InceKaydirma(Control ic, Func<int[]> oku, Action<int> yaz)
        {
            this.ic = ic; this.oku = oku; this.yaz = yaz;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Width = Tema.S(12); Cursor = Cursors.Default;
            zaman.Tick += (s, e) => Denetle(); zaman.Start();
            Disposed += (s, e) => zaman.Dispose();
        }

        /// <summary>Kaydırılabilir paneli (AutoScroll) ince çubuklu bir kutuya sarar; kutuyu döndürür.</summary>
        public static Panel Sar(ScrollableControl ic) =>
            Sar(ic, () => new[] { -ic.AutoScrollPosition.Y, ic.DisplayRectangle.Height, ic.ClientSize.Height }, v => ic.AutoScrollPosition = new Point(0, v));

        /// <summary>Liste kutusunu ince çubuklu bir kutuya sarar.</summary>
        public static Panel Sar(ListBox ic)
        {
            ic.ScrollAlwaysVisible = true; // satır genişliği sabit kalsın (gizli çubuk hep aynı yeri kaplar)
            return Sar(ic, () => new[] { ic.TopIndex * ic.ItemHeight, ic.Items.Count * ic.ItemHeight, ic.ClientSize.Height },
                    v => { if (ic.Items.Count > 0) ic.TopIndex = Math.Max(0, Math.Min(ic.Items.Count - 1, (int)Math.Round(v / (double)ic.ItemHeight))); });
        }

        static Panel Sar(Control ic, Func<int[]> oku, Action<int> yaz)
        {
            int sb = SystemInformation.VerticalScrollBarWidth;
            var kutu = new Panel { Dock = ic.Dock, BackColor = ic.BackColor, Margin = ic.Margin };
            ic.Dock = DockStyle.None;
            var cubuk = new InceKaydirma(ic, oku, yaz) { BackColor = ic.BackColor };
            kutu.Controls.Add(cubuk); kutu.Controls.Add(ic); cubuk.BringToFront();
            kutu.Resize += (s, e) =>
            {
                ic.SetBounds(0, 0, kutu.ClientSize.Width + sb, kutu.ClientSize.Height);
                cubuk.SetBounds(kutu.ClientSize.Width - cubuk.Width, 0, cubuk.Width, kutu.ClientSize.Height);
            };
            if (ic is ScrollableControl sc) sc.Padding = new Padding(sc.Padding.Left, sc.Padding.Top, sc.Padding.Right + sb, sc.Padding.Bottom);
            return kutu;
        }

        void Denetle()
        {
            if (!ic.IsHandleCreated || !Visible && !ic.Visible) return;
            var d = oku(); bool gerek = d[1] > d[2] + 1;
            if (d[0] != sonDeger) { if (sonDeger >= 0) sonHareket = DateTime.Now; sonDeger = d[0]; Invalidate(); }
            float hedef = !gerek ? 0f : (uzerinde || surukle) ? 1f : (DateTime.Now - sonHareket).TotalMilliseconds < 1200 ? 0.8f : 0.3f;
            if (Math.Abs(hedef - opaklik) > 0.01f) { opaklik += (hedef - opaklik) * 0.35f; Invalidate(); }
        }

        Rectangle Basparmak(int[] d)
        {
            int h = Height - Tema.S(8);
            if (d[1] <= d[2]) return Rectangle.Empty;
            int bh = Math.Max(Tema.S(36), (int)(h * (double)d[2] / d[1]));
            int y = Tema.S(4) + (int)((h - bh) * (double)d[0] / Math.Max(1, d[1] - d[2]));
            int w = uzerinde || surukle ? Tema.S(8) : Tema.S(5);
            return new Rectangle(Width - w - Tema.S(2), y, w, bh);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            using (var b = new SolidBrush(BackColor)) e.Graphics.FillRectangle(b, ClientRectangle);
            if (opaklik < 0.02f) return;
            var r = Basparmak(oku()); if (r.IsEmpty) return;
            Tema.Kaliteli(e.Graphics);
            using (var yol = Tema.Yuvarlak(r, r.Width / 2f))
            using (var b = new SolidBrush(Color.FromArgb((int)(opaklik * (uzerinde || surukle ? 150 : 110)), Renk.Yazi))) e.Graphics.FillPath(b, yol);
        }

        protected override void OnMouseEnter(EventArgs e) { uzerinde = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { uzerinde = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            var d = oku(); var r = Basparmak(d); if (r.IsEmpty) return;
            if (e.Y >= r.Top && e.Y <= r.Bottom) { surukle = true; surukleFark = e.Y - r.Top; Capture = true; }
            else yaz(Math.Max(0, Math.Min(d[1] - d[2], d[0] + (e.Y < r.Top ? -d[2] : d[2]))));
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (!surukle) return;
            var d = oku(); var r = Basparmak(d); int h = Height - Tema.S(8) - r.Height;
            double oran = Math.Max(0, Math.Min(1, (e.Y - surukleFark - Tema.S(4)) / (double)Math.Max(1, h)));
            yaz((int)(oran * (d[1] - d[2])));
        }

        protected override void OnMouseUp(MouseEventArgs e) { surukle = false; Capture = false; Invalidate(); }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            var d = oku(); yaz(Math.Max(0, Math.Min(d[1] - d[2], d[0] - e.Delta)));
        }
    }
}
