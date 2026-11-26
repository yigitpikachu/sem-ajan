using System;
using System.Drawing;
using System.Windows.Forms;

namespace Sem.Arayuz
{
    /// <summary>Windows'un eski MessageBox'ı yerine sade, yuvarlak köşeli mesaj kutusu.</summary>
    public static class MesajKutusu
    {
        public static void Bilgi(IWin32Window sahip, string baslik, string metin, bool uyari = false)
        {
            using (var f = Olustur(baslik, metin, uyari ? Ik.Uyari : Ik.TikDaire, uyari ? Renk.Turuncu : Renk.Yesil))
            {
                var ok = new Buton("Tamam", "", ButonTuru.Birincil) { Width = Tema.S(120) };
                ok.Location = new Point(f.Icerik.Width - ok.Width, f.Icerik.Height - ok.Height);
                ok.Click += (s, e) => f.Close();
                f.Icerik.Controls.Add(ok);
                f.Shown += (s, e) => ok.Focus();
                Goster(f, sahip);
            }
        }

        public static bool? Sor(IWin32Window sahip, string baslik, string metin, string evet = "Evet", bool tehlike = false)
        {
            using (var f = Olustur(baslik, metin, tehlike ? Ik.Uyari : Ik.Bilgi, tehlike ? Renk.Pembe : Renk.Mavi))
            {
                bool? sonuc = null;
                var ok = new Buton(evet, "", tehlike ? ButonTuru.Tehlike : ButonTuru.Birincil);
                ok.Width = Math.Max(ok.Width, Tema.S(120));
                ok.Location = new Point(f.Icerik.Width - ok.Width, f.Icerik.Height - ok.Height);
                var hayir = new Buton("Vazgeç", "", ButonTuru.Sade);
                hayir.Location = new Point(ok.Left - hayir.Width - Tema.S(8), ok.Top);
                ok.Click += (s, e) => { sonuc = true; f.Close(); };
                hayir.Click += (s, e) => { sonuc = false; f.Close(); };
                f.Icerik.Controls.Add(ok); f.Icerik.Controls.Add(hayir);
                f.Shown += (s, e) => ok.Focus();
                Goster(f, sahip);
                return sonuc;
            }
        }

        /// <summary>Alt alta seçenek kartları; seçilenin sırasını döndürür, kapatılırsa -1.</summary>
        public static int Secim(IWin32Window sahip, string baslik, string metin, params (string metin, string aciklama, string ikon)[] secenekler)
        {
            int yuk = 56 + secenekler.Length * 70;
            using (var f = new ModernForm(baslik, 460, yuk + 64))
            {
                int secilen = -1;
                var m = Yazi.Etiket(metin, 10f, false, Renk.Yazi2, false);
                m.SetBounds(0, 0, f.Icerik.Width, Tema.S(46));
                f.Icerik.Controls.Add(m);
                int y = Tema.S(50);
                for (int i = 0; i < secenekler.Length; i++)
                {
                    int n = i;
                    var k = new SecenekKarti(secenekler[i].metin, secenekler[i].aciklama, secenekler[i].ikon) { Location = new Point(0, y), Width = f.Icerik.Width };
                    k.Click += (s, e) => { secilen = n; f.Close(); };
                    f.Icerik.Controls.Add(k);
                    y += Tema.S(70);
                }
                Goster(f, sahip);
                return secilen;
            }
        }

        static ModernForm Olustur(string baslik, string metin, string ikon, Color renk)
        {
            int satir;
            using (var bmp = new Bitmap(1, 1))
            using (var g = Graphics.FromImage(bmp))
                satir = (int)g.MeasureString(metin, Tema.Yazi(10.5f), Tema.S(330)).Height;
            var f = new ModernForm(null, 440, Math.Max(190, 120 + (int)(satir / Tema.Olcek)));
            f.StartPosition = FormStartPosition.CenterParent;
            var ik = new Panel { Size = Tema.S(44, 44), Location = new Point(0, 0), BackColor = Renk.Zemin };
            ik.Paint += (s, e) =>
            {
                Tema.Kaliteli(e.Graphics);
                using (var b = new SolidBrush(Renk.Acik(renk, 30))) e.Graphics.FillEllipse(b, 0, 0, ik.Width - 1, ik.Height - 1);
                Tema.IkonCiz(e.Graphics, ikon, 14f, renk, new RectangleF(0, 0, ik.Width, ik.Height));
            };
            var b1 = Yazi.Etiket(baslik, 12.5f, true); b1.Location = new Point(Tema.S(60), Tema.S(8));
            var m = Yazi.Etiket(metin, 10.5f, false, Renk.Yazi2, false);
            m.SetBounds(Tema.S(60), Tema.S(40), f.Icerik.Width - Tema.S(60), satir + Tema.S(8));
            f.Icerik.Controls.AddRange(new Control[] { ik, b1, m });
            return f;
        }

        static void Goster(Form f, IWin32Window sahip)
        {
            if (sahip == null) { f.StartPosition = FormStartPosition.CenterScreen; f.TopMost = true; f.ShowInTaskbar = true; f.ShowDialog(); }
            else f.ShowDialog(sahip);
        }
    }

    /// <summary>İkonlu, başlıklı ve açıklamalı tıklanabilir seçenek kartı.</summary>
    public class SecenekKarti : Control
    {
        readonly string aciklama, ikon;
        bool uzerinde;

        public SecenekKarti(string metin, string aciklama, string ikon)
        {
            Text = metin; this.aciklama = aciklama; this.ikon = ikon;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent; Cursor = Cursors.Hand; Height = Tema.S(62);
        }

        protected override void OnMouseEnter(EventArgs e) { uzerinde = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { uzerinde = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; Tema.Kaliteli(g);
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            using (var p = Tema.Yuvarlak(r, Tema.S(12)))
            {
                using (var b = new SolidBrush(uzerinde ? Color.FromArgb(246, 251, 254) : Renk.Yuzey)) g.FillPath(b, p);
                using (var pen = new Pen(uzerinde ? Renk.Mavi : Renk.Cizgi, uzerinde ? 1.5f : 1f)) g.DrawPath(pen, p);
            }
            var d = new RectangleF(Tema.S(12), (Height - Tema.S(38)) / 2f, Tema.S(38), Tema.S(38));
            using (var b = new SolidBrush(Renk.Acik(Renk.Mavi, 26))) g.FillEllipse(b, d);
            Tema.IkonCiz(g, ikon, 13f, Renk.Mavi, d);
            float x = Tema.S(62);
            if (aciklama == "") Tema.YaziCiz(g, Text, Tema.YaziKalin(10.5f), Renk.Yazi, new RectangleF(x, 0, Width - x - Tema.S(36), Height));
            else
            {
                Tema.YaziCiz(g, Text, Tema.YaziKalin(10.5f), Renk.Yazi, new RectangleF(x, Tema.S(10), Width - x - Tema.S(36), Tema.S(22)));
                Tema.YaziCiz(g, aciklama, Tema.Yazi(9f), Renk.Yazi2, new RectangleF(x, Tema.S(32), Width - x - Tema.S(36), Tema.S(20)));
            }
            Tema.IkonCiz(g, Ik.Sag, 10f, uzerinde ? Renk.Mavi : Renk.Yazi3, new RectangleF(Width - Tema.S(36), 0, Tema.S(28), Height));
        }
    }
}
