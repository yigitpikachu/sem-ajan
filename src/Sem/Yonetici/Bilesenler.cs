using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Sem.Arayuz;

namespace Sem.Yonetici
{
    /// <summary>
    /// Bir bilgisayarı gösteren kart (ya da liste satırı): ekran önizlemesi, ad, durum, IP, kapanma saati.
    /// Önizlemeye tıklayınca büyük görüntü açılır, kartın geri kalanına tıklayınca seçilir; çift tık adı/grubu düzenler.
    /// </summary>
    class PcKart : Control
    {
        public Pc Pc;
        bool secili, uzerinde, onizlemeUzerinde, liste, onizleme;
        float kalkma, kalkmaHedef;
        readonly Timer anim = new Timer { Interval = 15 };
        RectangleF onizlemeAlan, secimAlan;
        public event EventHandler SecimDegisti, BuyutIstendi, DuzenleIstendi;

        public bool Secili { get => secili; set { if (secili != value) { secili = value; Invalidate(); SecimDegisti?.Invoke(this, EventArgs.Empty); } } }
        public bool ListeModu { get => liste; set { liste = value; Boyutla(); Invalidate(); } }
        public bool OnizlemeGoster { get => onizleme; set { onizleme = value; Boyutla(); Invalidate(); } }

        public PcKart(Pc pc, bool liste, bool onizleme)
        {
            Pc = pc; this.liste = liste; this.onizleme = onizleme;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent; Cursor = Cursors.Hand;
            anim.Tick += (s, e) =>
            {
                kalkma += (kalkmaHedef - kalkma) * 0.3f;
                if (Math.Abs(kalkmaHedef - kalkma) < 0.02f) { kalkma = kalkmaHedef; anim.Stop(); }
                Invalidate();
            };
            Disposed += (s, e) => anim.Dispose();
            Boyutla();
        }

        void Boyutla()
        {
            if (liste) { Height = Tema.S(60); Margin = Tema.Pd(0, 0, 0, 2); }
            else { Size = onizleme ? Tema.S(236, 216) : Tema.S(236, 98); Margin = Tema.Pd(0, 0, 8, 8); }
        }

        void Kalk(bool k) { kalkmaHedef = k ? 1 : 0; anim.Start(); }
        protected override void OnMouseEnter(EventArgs e) { uzerinde = true; Kalk(true); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { uzerinde = onizlemeUzerinde = false; Kalk(false); base.OnMouseLeave(e); }

        bool OnizlemeVar => onizleme && !liste && Pc.Acik && Pc.Onizleme != null;

        protected override void OnMouseMove(MouseEventArgs e)
        {
            bool o = OnizlemeVar && onizlemeAlan.Contains(e.Location) && !secimAlan.Contains(e.Location);
            if (o != onizlemeUzerinde) { onizlemeUzerinde = o; Invalidate(); }
            base.OnMouseMove(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                if (onizlemeUzerinde) BuyutIstendi?.Invoke(this, EventArgs.Empty);
                else Secili = !Secili;
            }
            base.OnMouseUp(e);
        }

        protected override void OnDoubleClick(EventArgs e) { if (!onizlemeUzerinde) DuzenleIstendi?.Invoke(this, EventArgs.Empty); base.OnDoubleClick(e); }

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

        bool SifreHata => !Pc.Acik && Pc.Hata != "";
        Color DurumRenk => Pc.Acik ? Renk.Yesil : SifreHata ? Renk.Turuncu : Renk.CizgiKoyu;
        bool BuBilgisayar => Pc.Ad.Equals(Environment.MachineName, StringComparison.OrdinalIgnoreCase);

        string AltYazi()
        {
            if (Pc.Acik && Saglik != "") return Saglik;
            var l = new List<string>();
            if (Pc.GorunenAd != "") l.Add(Pc.Ad);
            l.Add(Pc.Ip);
            if (BuBilgisayar) l.Add("bu bilgisayar");
            return string.Join("  ·  ", l);
        }

        string KapaliYazi() => SifreHata ? "Farklı bir şifreyle kurulmuş" :
            Pc.SonGorulme > DateTime.MinValue ? "Son görülme " + Pc.SonGorulme.ToString("dd.MM HH:mm") : "Kapalı";

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; Tema.Kaliteli(g);
            if (liste) SatirCiz(g); else KartCiz(g);
        }

        void SecimDairesi(Graphics g, RectangleF sd, bool zeminli)
        {
            secimAlan = RectangleF.Inflate(sd, Tema.S(6), Tema.S(6));
            if (secili)
            {
                using (var b = new SolidBrush(Renk.Mavi)) g.FillEllipse(b, sd);
                Tema.IkonCiz(g, Ik.Tik, 8.5f, Color.White, sd);
            }
            else if (uzerinde || liste)
            {
                if (zeminli) using (var b = new SolidBrush(Color.FromArgb(220, 255, 255, 255))) g.FillEllipse(b, sd);
                using (var pen = new Pen(uzerinde ? Renk.Yazi3 : Renk.CizgiKoyu, Tema.S(1.5f))) g.DrawEllipse(pen, sd);
            }
        }

        void AdCiz(Graphics g, Font f, float x, float y, float w, float h)
        {
            var ad = Pc.Baslik;
            float aw = Math.Min(w - Tema.S(14), g.MeasureString(ad, f).Width);
            Tema.YaziCiz(g, ad, f, Pc.Acik ? Renk.Yazi : Renk.Yazi2, new RectangleF(x, y, aw + Tema.S(4), h));
            using (var b = new SolidBrush(DurumRenk)) g.FillEllipse(b, x + aw + Tema.S(6), y + h / 2 - Tema.S(3.5f), Tema.S(7), Tema.S(7));
            if (Pc.Acik && Saglik != "") Tema.IkonCiz(g, Ik.Uyari, 9.5f, Renk.Turuncu, new RectangleF(x + aw + Tema.S(15), y, Tema.S(18), h));
        }

        /// <summary>Sağlık uyarısı varsa ilkinin metni (disk dolu, disk arızası).</summary>
        string Saglik
        {
            get
            {
                var s = Pc.SonDurum?["saglik"] ?? "";
                if (s == "") return "";
                var ilk = s.Split(';')[0]; var i = ilk.IndexOf('|');
                return i >= 0 ? ilk.Substring(i + 1) : ilk;
            }
        }

        Color AltRenk => Pc.Acik && Saglik != "" ? Renk.Turuncu : Renk.Yazi3;

        void KartCiz(Graphics g)
        {
            var p = Pc; var d = p.SonDurum;
            float lift = kalkma * Tema.S(3);
            var r = new RectangleF(Tema.S(4), Tema.S(4) - lift, Width - Tema.S(8), Height - Tema.S(12));
            Tema.YuzenKart(g, r, Tema.S(14), secili ? Color.FromArgb(245, 250, 254) : Renk.Yuzey, kalkma);
            if (secili) using (var pen = new Pen(Renk.Mavi, Tema.S(1.5f))) using (var yol = Tema.Yuvarlak(RectangleF.Inflate(r, -0.75f, -0.75f), Tema.S(13.5f))) g.DrawPath(pen, yol);

            float y0;
            if (onizleme)
            {
                var pr = new RectangleF(r.X + Tema.S(8), r.Y + Tema.S(8), r.Width - Tema.S(16), (r.Width - Tema.S(16)) * 9f / 16f);
                onizlemeAlan = pr;
                using (var yol = Tema.Yuvarlak(pr, Tema.S(8)))
                {
                    var eski = g.Clip; g.SetClip(yol, System.Drawing.Drawing2D.CombineMode.Intersect);
                    if (OnizlemeVar)
                    {
                        var img = p.Onizleme;
                        float oran = Math.Max(pr.Width / img.Width, pr.Height / img.Height);
                        float iw = img.Width * oran, ih = img.Height * oran;
                        g.DrawImage(img, pr.X + (pr.Width - iw) / 2, pr.Y + (pr.Height - ih) / 2, iw, ih);
                        if (onizlemeUzerinde)
                        {
                            using (var b = new SolidBrush(Color.FromArgb(40, 0, 0, 0))) g.FillRectangle(b, pr);
                            var cip = new RectangleF(pr.Right - Tema.S(40), pr.Bottom - Tema.S(40), Tema.S(30), Tema.S(30));
                            using (var cy = Tema.Yuvarlak(cip, Tema.S(8))) using (var b = new SolidBrush(Color.FromArgb(170, 20, 22, 26))) g.FillPath(b, cy);
                            Tema.IkonCiz(g, Ik.Buyut, 10f, Color.White, cip);
                        }
                    }
                    else
                    {
                        using (var b = new SolidBrush(p.Acik ? Color.FromArgb(243, 243, 240) : Color.FromArgb(237, 237, 233))) g.FillRectangle(b, pr);
                        Tema.IkonCiz(g, p.Acik ? Ik.Kamera : p.Ikon, 20f, Renk.Acik(Renk.Yazi3, 150), new RectangleF(pr.X, pr.Y - Tema.S(10), pr.Width, pr.Height));
                        string m = p.Acik ? (p.OnizlemeHata != "" ? p.OnizlemeHata : "Görüntü bekleniyor…") : KapaliYazi();
                        Tema.YaziCiz(g, m, Tema.Yazi(8.5f), Renk.Yazi3, new RectangleF(pr.X + Tema.S(10), pr.Y + pr.Height / 2 + Tema.S(14), pr.Width - Tema.S(20), Tema.S(18)), StringAlignment.Center);
                    }
                    g.Clip = eski;
                }
                SecimDairesi(g, new RectangleF(pr.Right - Tema.S(30), pr.Y + Tema.S(8), Tema.S(22), Tema.S(22)), true);
                y0 = pr.Bottom + Tema.S(10);
            }
            else
            {
                onizlemeAlan = RectangleF.Empty;
                SecimDairesi(g, new RectangleF(r.Right - Tema.S(34), r.Y + Tema.S(14), Tema.S(22), Tema.S(22)), false);
                y0 = r.Y + Tema.S(14);
            }

            float x = r.X + Tema.S(12);
            Tema.IkonCiz(g, p.Ikon, 11f, p.Acik ? Renk.Yazi2 : Renk.Yazi3, new RectangleF(x - Tema.S(3), y0, Tema.S(22), Tema.S(22)));
            AdCiz(g, Tema.YaziKalin(10f), x + Tema.S(22), y0, r.Right - x - Tema.S(22) - Tema.S(onizleme ? 12 : 44), Tema.S(22));
            Tema.YaziCiz(g, AltYazi(), Tema.Yazi(8.25f), AltRenk, new RectangleF(x + Tema.S(22), y0 + Tema.S(20), r.Right - x - Tema.S(36), Tema.S(17)));

            float y2 = y0 + Tema.S(42), yarim = (r.Width - Tema.S(28)) / 2f;
            var kucuk = Tema.Yazi(8.25f);
            if (p.Acik && d != null)
            {
                Bilgi(g, Ik.Guc, d["kapanma"], x, y2, yarim, kucuk);
                Bilgi(g, Ik.Saat, Sure(d["acik_sure"]), x + yarim, y2, yarim, kucuk);
            }
            else if (!onizleme) Bilgi(g, SifreHata ? Ik.Uyari : Ik.Kopuk, KapaliYazi(), x, y2, r.Width - Tema.S(28), kucuk);
            else if (SifreHata) Bilgi(g, Ik.Uyari, "Şifre uyuşmuyor", x, y2, r.Width - Tema.S(28), kucuk);
        }

        void SatirCiz(Graphics g)
        {
            var p = Pc; var d = p.SonDurum;
            var r = new RectangleF(0, Tema.S(2), Width - 1, Height - Tema.S(6));
            if (secili || uzerinde)
                using (var yol = Tema.Yuvarlak(r, Tema.S(12)))
                using (var b = new SolidBrush(secili ? Color.FromArgb(236, 246, 252) : Renk.Hover)) g.FillPath(b, yol);
            SecimDairesi(g, new RectangleF(Tema.S(14), r.Y + (r.Height - Tema.S(20)) / 2, Tema.S(20), Tema.S(20)), false);
            Tema.IkonCiz(g, p.Ikon, 12f, p.Acik ? Renk.Yazi2 : Renk.Yazi3, new RectangleF(Tema.S(44), r.Y, Tema.S(26), r.Height));
            float adG = Math.Min(Tema.S(250), Width * 0.28f);
            AdCiz(g, Tema.YaziKalin(10.5f), Tema.S(78), r.Y + Tema.S(7), adG, Tema.S(22));
            Tema.YaziCiz(g, AltYazi(), Tema.Yazi(8.5f), AltRenk, new RectangleF(Tema.S(78), r.Y + Tema.S(28), adG, Tema.S(18)));

            float x = Tema.S(78) + adG + Tema.S(20), kalan = Width - x - Tema.S(16);
            var f = Tema.Yazi(9f);
            if (p.Acik && d != null)
            {
                float k1 = kalan * 0.26f, k2 = kalan * 0.22f;
                Bilgi(g, Ik.Guc, d["kapanma"], x, r.Y + (r.Height - Tema.S(20)) / 2, k1, f);
                Bilgi(g, Ik.Saat, Sure(d["acik_sure"]), x + k1, r.Y + (r.Height - Tema.S(20)) / 2, k2, f);
                Bilgi(g, Ik.Yazici, Yazici(d["yazicilar"]), x + k1 + k2, r.Y + (r.Height - Tema.S(20)) / 2, kalan - k1 - k2, f);
            }
            else Bilgi(g, SifreHata ? Ik.Uyari : Ik.Kopuk, KapaliYazi(), x, r.Y + (r.Height - Tema.S(20)) / 2, kalan, f);
        }

        static void Bilgi(Graphics g, string ikon, string metin, float x, float y, float w, Font f)
        {
            Tema.IkonCiz(g, ikon, 9f, Renk.Yazi3, new RectangleF(x - Tema.S(2), y, Tema.S(20), Tema.S(20)));
            Tema.YaziCiz(g, metin, f, Renk.Yazi2, new RectangleF(x + Tema.S(20), y, w - Tema.S(24), Tema.S(20)));
        }
    }

    /// <summary>Izgarada grup başlığı: renkli nokta, grup adı, sayı ve "Seç" bağlantısı.</summary>
    class GrupBaslik : Control
    {
        public readonly string Kod;
        public string Bilgi = "";
        public bool TumuSecili;
        bool linkUzerinde;
        RectangleF link;
        public event EventHandler SecTiklandi;

        public GrupBaslik(string kod)
        {
            Kod = kod;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent; Height = Tema.S(40); Margin = Tema.Pd(0, 6, 0, 2);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            bool l = link.Contains(e.Location);
            if (l != linkUzerinde) { linkUzerinde = l; Cursor = l ? Cursors.Hand : Cursors.Default; Invalidate(); }
        }
        protected override void OnMouseLeave(EventArgs e) { linkUzerinde = false; Invalidate(); }
        protected override void OnMouseUp(MouseEventArgs e) { if (e.Button == MouseButtons.Left && link.Contains(e.Location)) SecTiklandi?.Invoke(this, EventArgs.Empty); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; Tema.Kaliteli(g);
            var renk = Gruplar.Renk_(Kod);
            float ort = Height / 2f + Tema.S(2);
            using (var b = new SolidBrush(renk)) g.FillEllipse(b, Tema.S(4), ort - Tema.S(5), Tema.S(10), Tema.S(10));
            using (var f = Tema.YaziKalin(11.5f))
            {
                var ad = Gruplar.Ad(Kod);
                float w = g.MeasureString(ad, f).Width;
                Tema.YaziCiz(g, ad, f, Renk.Yazi, new RectangleF(Tema.S(22), 0, w + Tema.S(4), Height + Tema.S(4)));
                Tema.YaziCiz(g, Bilgi, Tema.Yazi(9f), Renk.Yazi3, new RectangleF(Tema.S(26) + w, Tema.S(1), Width / 2f, Height + Tema.S(4)));
            }
            using (var f = Tema.YaziKalin(9f))
            {
                var t = TumuSecili ? "Seçimi kaldır" : "Hepsini seç";
                float w = g.MeasureString(t, f).Width + Tema.S(20);
                link = new RectangleF(Width - w - Tema.S(14), ort - Tema.S(14), w, Tema.S(28));
                if (linkUzerinde) using (var yol = Tema.Yuvarlak(link, Tema.S(8))) using (var b = new SolidBrush(Renk.Hover)) g.FillPath(b, yol);
                Tema.YaziCiz(g, t, f, Renk.MaviKoyu, link, StringAlignment.Center);
            }
        }
    }

    /// <summary>Gönderilen duyurular listesi.</summary>
    class DuyuruListesi : ListBox
    {
        public DuyuruListesi()
        {
            DrawMode = DrawMode.OwnerDrawFixed; ItemHeight = Tema.S(72); BorderStyle = BorderStyle.None;
            BackColor = Renk.Yuzey; IntegralHeight = false; Cursor = Cursors.Hand;
        }

        protected override void OnDrawItem(DrawItemEventArgs e)
        {
            if (e.Index < 0) return;
            var k = (DuyuruKaydi)Items[e.Index];
            var g = e.Graphics; Tema.Kaliteli(g);
            var b0 = e.Bounds;
            using (var b = new SolidBrush(Renk.Yuzey)) g.FillRectangle(b, b0);
            if ((e.State & DrawItemState.Selected) != 0)
                using (var yol = Tema.Yuvarlak(new RectangleF(b0.X + Tema.S(6), b0.Y + Tema.S(3), b0.Width - Tema.S(12), b0.Height - Tema.S(6)), Tema.S(10)))
                using (var b = new SolidBrush(Renk.Hover)) g.FillPath(b, yol);
            Tema.IkonCiz(g, Ik.Megafon, 13f, Renk.Yazi2, new RectangleF(b0.X + Tema.S(14), b0.Y, Tema.S(32), b0.Height));
            float x = b0.X + Tema.S(56), sag = Tema.S(220);
            Tema.YaziCiz(g, k.Baslik == "" ? "Duyuru" : k.Baslik, Tema.YaziKalin(10.5f), Renk.Yazi, new RectangleF(x, b0.Y + Tema.S(12), b0.Width - x - sag, Tema.S(24)));
            Tema.YaziCiz(g, k.Metin.Replace("\r", " ").Replace("\n", " "), Tema.Yazi(9.5f), Renk.Yazi2, new RectangleF(x, b0.Y + Tema.S(36), b0.Width - x - sag, Tema.S(22)));

            int ulasan = k.Ulasan.Count, okuyan = k.Okuyan.Count(o => k.Ulasan.Contains(o.Key, StringComparer.OrdinalIgnoreCase));
            bool hepsi = ulasan > 0 && okuyan >= ulasan;
            var hap = ulasan == 0 ? "Ulaşmadı" : okuyan + " / " + ulasan + " okudu";
            using (var f = Tema.YaziKalin(8.5f))
            {
                float w = g.MeasureString(hap, f).Width + Tema.S(30);
                var hr = new RectangleF(b0.Right - w - Tema.S(18), b0.Y + Tema.S(12), w, Tema.S(24));
                var c = hepsi ? Renk.Yesil : ulasan == 0 ? Renk.Pembe : Renk.Turuncu;
                using (var yol = Tema.Yuvarlak(hr, hr.Height / 2)) using (var b = new SolidBrush(Renk.Acik(c, 30))) g.FillPath(b, yol);
                Tema.IkonCiz(g, hepsi ? Ik.Okundu : Ik.Saat, 8.5f, hepsi ? Renk.YesilKoyu : c, new RectangleF(hr.X + Tema.S(4), hr.Y, Tema.S(22), hr.Height));
                Tema.YaziCiz(g, hap, f, hepsi ? Renk.YesilKoyu : Renk.Karistir(c, Color.Black, 0.25), new RectangleF(hr.X + Tema.S(24), hr.Y, w - Tema.S(26), hr.Height));
            }
            Tema.YaziCiz(g, k.Zaman.Date == DateTime.Today ? "Bugün " + k.Zaman.ToString("HH:mm") : k.Zaman.ToString("dd.MM.yyyy HH:mm"), Tema.Yazi(8.75f), Renk.Yazi3,
                new RectangleF(b0.Right - Tema.S(200), b0.Y + Tema.S(38), Tema.S(182), Tema.S(20)), StringAlignment.Far);
            using (var pen = new Pen(Renk.Cizgi)) g.DrawLine(pen, b0.X + Tema.S(56), b0.Bottom - 1, b0.Right - Tema.S(18), b0.Bottom - 1);
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
            BackColor = Color.Transparent; Size = Tema.S(200, 72); Margin = Tema.Pd(0, 0, 12, 0);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; Tema.Kaliteli(g);
            Tema.YuzenKart(g, new RectangleF(Tema.S(2), Tema.S(1), Width - Tema.S(4), Height - Tema.S(8)), Tema.S(14), Renk.Yuzey, 0);
            var d = new RectangleF(Tema.S(16), (Height - Tema.S(6) - Tema.S(36)) / 2f, Tema.S(36), Tema.S(36));
            using (var b = new SolidBrush(Renk.Acik(Ton, 26))) g.FillEllipse(b, d);
            Tema.IkonCiz(g, Ikon, 13f, Ton, d);
            Tema.YaziCiz(g, Deger, Tema.YaziKalin(14f), Renk.Yazi, new RectangleF(Tema.S(64), Tema.S(9), Width - Tema.S(72), Tema.S(28)));
            Tema.YaziCiz(g, Etiket, Tema.Yazi(8.5f), Renk.Yazi2, new RectangleF(Tema.S(65), Tema.S(36), Width - Tema.S(72), Tema.S(20)));
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
            BackColor = Color.Transparent; Cursor = Cursors.Hand; Height = Tema.S(38);
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
            Tema.YaziCiz(g, Text, aktif ? Tema.YaziKalin(10.5f) : Tema.Yazi(10.5f), Renk.Yazi, new RectangleF(Tema.S(46), 0, Width - Tema.S(Rozet != "" ? 84 : 50), Height));
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

    /// <summary>Kartların dizildiği, yalnızca dikey kaydırılan alan.</summary>
    class Izgara : FlowLayoutPanel
    {
        public Izgara()
        {
            AutoScroll = false; HorizontalScroll.Maximum = 0; HorizontalScroll.Visible = false; VerticalScroll.Visible = false; AutoScroll = true; // yalnızca dikey kaydırma
            BackColor = Renk.Zemin; WrapContents = true;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
        }
    }
}
