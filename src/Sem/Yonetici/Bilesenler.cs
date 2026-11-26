using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Sem.Arayuz;

namespace Sem.Yonetici
{
    /// <summary>Bir bilgisayarı gösteren kart: durum, IP, açık süresi, kapanma saati, yazıcı. Tıklayınca seçilir.</summary>
    class PcKart : Control
    {
        public Pc Pc;
        bool secili, uzerinde;
        public event EventHandler SecimDegisti;
        public bool Secili { get => secili; set { if (secili != value) { secili = value; Invalidate(); SecimDegisti?.Invoke(this, EventArgs.Empty); } } }

        public PcKart(Pc pc)
        {
            Pc = pc;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent; Cursor = Cursors.Hand;
            Size = Tema.S(262, 176); Margin = Tema.Pd(0, 0, 16, 16);
        }

        protected override void OnMouseEnter(EventArgs e) { uzerinde = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { uzerinde = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseUp(MouseEventArgs e) { if (e.Button == MouseButtons.Left) Secili = !Secili; base.OnMouseUp(e); }

        public static string Sure(string sn)
        {
            long s; if (!long.TryParse(sn, out s)) return "";
            var t = TimeSpan.FromSeconds(s);
            return t.TotalDays >= 1 ? $"{(int)t.TotalDays} gün {t.Hours} sa" : t.TotalHours >= 1 ? $"{t.Hours} sa {t.Minutes} dk" : $"{t.Minutes} dk";
        }

        public static string Yazici(string y)
        {
            var l = (y ?? "").Split(';').Where(x => x != "" && !x.Contains("PDF") && !x.Contains("XPS") && !x.Contains("OneNote") && !x.StartsWith("Fax") && !x.Contains("Faks")).ToList();
            return l.Count == 0 ? "Yazıcı yok" : l.Count == 1 ? l[0] : l[0] + " +" + (l.Count - 1);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; Tema.Kaliteli(g);
            var p = Pc; var d = p.SonDurum;
            bool sifreHata = !p.Acik && p.Hata != "";
            var r = new Rectangle(0, 0, Width - 1, Height - Tema.S(4));
            Tema.Kart(g, r, Tema.S(16), secili ? Color.FromArgb(244, 250, 254) : Renk.Yuzey, secili ? Renk.Mavi : uzerinde ? Renk.CizgiKoyu : Renk.Cizgi, true);
            if (secili) using (var pen = new Pen(Renk.Mavi, Tema.S(2))) using (var yol = Tema.Yuvarlak(new RectangleF(1, 1, r.Width - 2, r.Height - 2), Tema.S(15))) g.DrawPath(pen, yol);

            Color ana = p.Acik ? Renk.Yesil : sifreHata ? Renk.Turuncu : Renk.Yazi3;
            var ik = new RectangleF(Tema.S(18), Tema.S(18), Tema.S(46), Tema.S(46));
            using (var b = new SolidBrush(Renk.Acik(ana, p.Acik ? 34 : 26))) g.FillEllipse(b, ik);
            Tema.IkonCiz(g, Ik.Masaustu, 17f, p.Acik ? Renk.YesilKoyu : ana, ik);

            // Seçim dairesi
            var sd = new RectangleF(Width - Tema.S(40), Tema.S(18), Tema.S(22), Tema.S(22));
            if (secili)
            {
                using (var b = new SolidBrush(Renk.Mavi)) g.FillEllipse(b, sd);
                Tema.IkonCiz(g, Ik.Tik, 9f, Color.White, sd);
            }
            else if (uzerinde) using (var pen = new Pen(Renk.CizgiKoyu, Tema.S(1.5f))) g.DrawEllipse(pen, sd);

            bool bu = p.Ad.Equals(Environment.MachineName, StringComparison.OrdinalIgnoreCase);
            float x = Tema.S(76), w = Width - x - Tema.S(48);
            Tema.YaziCiz(g, p.Ad, Tema.YaziKalin(12f), p.Acik ? Renk.Yazi : Renk.Yazi2, new RectangleF(x, Tema.S(18), w, Tema.S(26)));
            Tema.YaziCiz(g, p.Ip + (bu ? "  ·  bu bilgisayar" : ""), Tema.Yazi(9f), Renk.Yazi2, new RectangleF(x, Tema.S(43), w + Tema.S(30), Tema.S(20)));

            // Durum hapı
            string hap = p.Acik ? "Açık" : sifreHata ? "Şifre uyuşmuyor" : "Kapalı";
            using (var f = Tema.YaziKalin(8.5f))
            {
                var hw = g.MeasureString(hap, f).Width + Tema.S(26);
                var hr = new RectangleF(Tema.S(18), Tema.S(78), hw, Tema.S(24));
                using (var yol = Tema.Yuvarlak(hr, hr.Height / 2))
                using (var b = new SolidBrush(Renk.Acik(ana, 30))) g.FillPath(b, yol);
                using (var b = new SolidBrush(p.Acik ? Renk.YesilKoyu : ana)) g.FillEllipse(b, hr.X + Tema.S(9), hr.Y + hr.Height / 2 - Tema.S(3), Tema.S(6), Tema.S(6));
                Tema.YaziCiz(g, hap, f, p.Acik ? Renk.YesilKoyu : sifreHata ? Renk.Turuncu : Renk.Yazi2, new RectangleF(hr.X + Tema.S(19), hr.Y, hw, hr.Height));
            }

            using (var pen = new Pen(Renk.Cizgi)) g.DrawLine(pen, Tema.S(18), Tema.S(114), Width - Tema.S(18), Tema.S(114));
            var kucuk = Tema.Yazi(9f);
            if (p.Acik && d != null)
            {
                float yy = Tema.S(122), yarim = (Width - Tema.S(36)) / 2f;
                Bilgi(g, Ik.Saat, Sure(d["acik_sure"]), Tema.S(18), yy, yarim, kucuk);
                Bilgi(g, Ik.Guc, d["kapanma"], Tema.S(18) + yarim, yy, yarim, kucuk);
                Bilgi(g, Ik.Yazici, Yazici(d["yazicilar"]), Tema.S(18), yy + Tema.S(22), Width - Tema.S(36), kucuk);
            }
            else
            {
                string s = sifreHata ? "Bu bilgisayar farklı bir şifreyle kurulmuş" :
                           p.SonGorulme > DateTime.MinValue ? "Son görülme " + p.SonGorulme.ToString("dd.MM HH:mm") : "Kapalı ya da ağa bağlı değil";
                Bilgi(g, sifreHata ? Ik.Uyari : Ik.Kopuk, s, Tema.S(18), Tema.S(122), Width - Tema.S(36), kucuk);
            }
        }

        static void Bilgi(Graphics g, string ikon, string metin, float x, float y, float w, Font f)
        {
            Tema.IkonCiz(g, ikon, 9.5f, Renk.Yazi3, new RectangleF(x - Tema.S(2), y, Tema.S(20), Tema.S(20)));
            Tema.YaziCiz(g, metin, f, Renk.Yazi2, new RectangleF(x + Tema.S(20), y, w - Tema.S(24), Tema.S(20)));
        }
    }

    /// <summary>Üstteki özet kartı (ör. "5 Açık").</summary>
    class OzetKart : Control
    {
        public string Deger = "", Etiket = "", Ikon = "";
        public Color Ton = Renk.Mavi;

        public OzetKart(string etiket, string ikon, Color ton)
        {
            Etiket = etiket; Ikon = ikon; Ton = ton;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent; Size = Tema.S(220, 84); Margin = Tema.Pd(0, 0, 16, 0);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; Tema.Kaliteli(g);
            Tema.Kart(g, new Rectangle(0, 0, Width - 1, Height - Tema.S(4)), Tema.S(16), Renk.Yuzey, Renk.Cizgi, true);
            var d = new RectangleF(Tema.S(18), (Height - Tema.S(4) - Tema.S(42)) / 2f, Tema.S(42), Tema.S(42));
            using (var b = new SolidBrush(Renk.Acik(Ton, 30))) g.FillEllipse(b, d);
            Tema.IkonCiz(g, Ikon, 16f, Ton, d);
            Tema.YaziCiz(g, Deger, Tema.YaziKalin(17f), Renk.Yazi, new RectangleF(Tema.S(74), Tema.S(12), Width - Tema.S(84), Tema.S(34)));
            Tema.YaziCiz(g, Etiket, Tema.Yazi(9f), Renk.Yazi2, new RectangleF(Tema.S(75), Tema.S(44), Width - Tema.S(84), Tema.S(22)));
        }
    }

    /// <summary>Yan menü öğesi.</summary>
    class MenuOgesi : Control
    {
        readonly string ikon;
        bool uzerinde, aktif;
        public string Rozet = "";
        public bool Aktif { get => aktif; set { aktif = value; Invalidate(); } }

        public MenuOgesi(string metin, string ikon)
        {
            Text = metin; this.ikon = ikon;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent; Cursor = Cursors.Hand; Height = Tema.S(42);
        }

        protected override void OnMouseEnter(EventArgs e) { uzerinde = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { uzerinde = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; Tema.Kaliteli(g);
            if (aktif || uzerinde)
                using (var p = Tema.Yuvarlak(new RectangleF(0, 0, Width - 1, Height - 1), Tema.S(10)))
                using (var b = new SolidBrush(aktif ? Renk.Yuzey : Renk.Hover)) g.FillPath(b, p);
            if (aktif)
                using (var p = Tema.Yuvarlak(new RectangleF(0, Tema.S(11), Tema.S(4), Height - Tema.S(22)), Tema.S(2)))
                using (var b = new SolidBrush(Renk.Mavi)) g.FillPath(b, p);
            Tema.IkonCiz(g, ikon, 13f, aktif ? Renk.Mavi : Renk.Yazi2, new RectangleF(Tema.S(10), 0, Tema.S(30), Height));
            Tema.YaziCiz(g, Text, aktif ? Tema.YaziKalin(10.5f) : Tema.Yazi(10.5f), Renk.Yazi, new RectangleF(Tema.S(46), 0, Width - Tema.S(90), Height));
            if (Rozet != "")
            {
                using (var f = Tema.YaziKalin(8f))
                {
                    var w = Math.Max(Tema.S(22), g.MeasureString(Rozet, f).Width + Tema.S(10));
                    var r = new RectangleF(Width - w - Tema.S(10), (Height - Tema.S(20)) / 2f, w, Tema.S(20));
                    using (var p = Tema.Yuvarlak(r, r.Height / 2)) using (var b = new SolidBrush(Renk.Acik(Renk.Mavi, 36))) g.FillPath(b, p);
                    Tema.YaziCiz(g, Rozet, f, Renk.MaviKoyu, r, StringAlignment.Center);
                }
            }
        }
    }

    /// <summary>Etkinlik kaydı satırları.</summary>
    class KayitListesi : ListBox
    {
        public class Oge { public DateTime Zaman; public string Metin; public int Tur; /* 0 bilgi, 1 başarılı, 2 hata */ }

        public KayitListesi()
        {
            DrawMode = DrawMode.OwnerDrawFixed; ItemHeight = Tema.S(46); BorderStyle = BorderStyle.None;
            BackColor = Renk.Yuzey; IntegralHeight = false; SelectionMode = SelectionMode.None;
        }

        protected override void OnDrawItem(DrawItemEventArgs e)
        {
            if (e.Index < 0) return;
            var o = (Oge)Items[e.Index];
            var g = e.Graphics; Tema.Kaliteli(g);
            using (var b = new SolidBrush(Renk.Yuzey)) g.FillRectangle(b, e.Bounds);
            var b0 = e.Bounds;
            Color c = o.Tur == 1 ? Renk.Yesil : o.Tur == 2 ? Renk.Pembe : Renk.Mavi;
            string ik = o.Tur == 1 ? Ik.TikDaire : o.Tur == 2 ? Ik.HataDaire : Ik.Bilgi;
            Tema.IkonCiz(g, ik, 12f, c, new RectangleF(b0.X + Tema.S(16), b0.Y, Tema.S(28), b0.Height));
            Tema.YaziCiz(g, o.Zaman.ToString("HH:mm:ss"), Tema.Yazi(9f), Renk.Yazi3, new RectangleF(b0.X + Tema.S(52), b0.Y, Tema.S(70), b0.Height));
            Tema.YaziCiz(g, o.Metin, o.Tur == 0 ? Tema.YaziKalin(10f) : Tema.Yazi(10f), Renk.Yazi, new RectangleF(b0.X + Tema.S(124), b0.Y, b0.Width - Tema.S(140), b0.Height));
            using (var pen = new Pen(Renk.Cizgi)) g.DrawLine(pen, b0.X + Tema.S(16), b0.Bottom - 1, b0.Right - Tema.S(16), b0.Bottom - 1);
        }
    }

    /// <summary>Kaydırılabilir alan; kaydırma çubuğu görünse de sade dursun diye zemin rengini korur.</summary>
    class Izgara : FlowLayoutPanel
    {
        public Izgara()
        {
            AutoScroll = true; BackColor = Renk.Zemin; WrapContents = true;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
        }
    }
}
