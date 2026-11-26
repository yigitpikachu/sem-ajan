using System;
using System.ComponentModel;
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

        public Buton(string metin, string ikon = "", ButonTuru tur = ButonTuru.Ikincil)
        {
            Text = metin; Ikon = ikon; Tur = tur;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Font = Tema.YaziKalin(9.75f);
            Cursor = Cursors.Hand;
            Height = Tema.S(40);
            Margin = Tema.Pd(4, 4, 4, 4);
            BoyutAyarla();
        }

        public void BoyutAyarla()
        {
            using (var g = CreateGraphics())
            {
                var w = g.MeasureString(Text, Font).Width;
                if (Dikey) { Width = Math.Max(Tema.S(72), (int)w + Tema.S(16)); Height = Tema.S(62); }
                else Width = (int)w + Tema.S(Ikon != "" ? 52 : 32) - (Text == "" ? Tema.S(20) : 0);
            }
        }

        protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); Invalidate(); }
        protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }
        protected override void OnMouseEnter(EventArgs e) { uzerinde = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { uzerinde = basili = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { basili = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { basili = false; Invalidate(); base.OnMouseUp(e); }
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
            if (!Enabled) return (Tur == ButonTuru.Sade ? Color.Transparent : Color.FromArgb(240, 237, 231), Renk.Yazi3, null);
            if (ana != Color.Empty)
            {
                var d = basili ? Renk.Karistir(ana, Color.Black, 0.18) : uzerinde ? Renk.Karistir(ana, Color.Black, 0.08) : ana;
                return (d, Color.White, null);
            }
            if (Tur == ButonTuru.Sade)
                return (basili ? Renk.CizgiKoyu : uzerinde ? Renk.Hover : Color.Transparent, Renk.Yazi, null);
            return (basili ? Renk.Hover : uzerinde ? Color.FromArgb(250, 248, 243) : Renk.Yuzey, Renk.Yazi, Renk.Cizgi);
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
                UseSystemPasswordChar = sifre, Multiline = cokSatir, AcceptsReturn = cokSatir, ScrollBars = cokSatir ? ScrollBars.Vertical : ScrollBars.None
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

        public KartPanel()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            using (var b = new SolidBrush(Parent?.BackColor ?? Renk.Zemin)) e.Graphics.FillRectangle(b, ClientRectangle);
            Tema.Kaliteli(e.Graphics);
            Tema.Kart(e.Graphics, new Rectangle(0, 0, Width - 1, Height - Tema.S(Golge ? 4 : 1)), Tema.S(Yaricap), Dolgu, Renk.Cizgi, Golge);
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

    /// <summary>Menüleri (sağ tık, "Diğer") yuvarlak ve sade gösteren çizici.</summary>
    public class MenuCizici : ToolStripProfessionalRenderer
    {
        public MenuCizici() : base(new Renkler()) { RoundedEdges = true; }

        class Renkler : ProfessionalColorTable
        {
            public override Color MenuItemSelected => Renk.Hover;
            public override Color MenuItemBorder => Renk.Hover;
            public override Color MenuBorder => Renk.CizgiKoyu;
            public override Color ToolStripDropDownBackground => Renk.Yuzey;
            public override Color ImageMarginGradientBegin => Renk.Yuzey;
            public override Color ImageMarginGradientMiddle => Renk.Yuzey;
            public override Color ImageMarginGradientEnd => Renk.Yuzey;
            public override Color SeparatorDark => Renk.Cizgi;
            public override Color SeparatorLight => Renk.Yuzey;
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Enabled ? Renk.Yazi : Renk.Yazi3;
            base.OnRenderItemText(e);
        }

        protected override void OnRenderItemImage(ToolStripItemImageRenderEventArgs e)
        {
            if (e.Item.Tag is string ikon)
            {
                Tema.Kaliteli(e.Graphics);
                Tema.IkonCiz(e.Graphics, ikon, 11f, e.Item.ForeColor == Renk.Pembe ? Renk.Pembe : Renk.Yazi2, e.ImageRectangle);
            }
            else base.OnRenderItemImage(e);
        }

        /// <summary>İkonlu menü öğesi (ikon çizimi için yer tutucu boş resim kullanılır).</summary>
        public static ToolStripMenuItem Oge(string metin, string ikon, EventHandler tik, bool tehlike = false)
        {
            var bos = new Bitmap(Tema.S(18), Tema.S(18));
            var o = new ToolStripMenuItem(metin, bos, tik) { Tag = ikon, Padding = Tema.Pd(4, 6, 4, 6), Font = Tema.Yazi(10f) };
            if (tehlike) o.ForeColor = Renk.Pembe;
            return o;
        }
    }
}
