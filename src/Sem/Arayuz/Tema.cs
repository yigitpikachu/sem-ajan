// Sem arayüz tasarım dili: sıcak kırık beyaz zemin, logodaki dört renk, yuvarlak köşeler, Fluent ikonlar.
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Sem.Arayuz
{
    public static class Renk
    {
        // Logo renkleri
        public static readonly Color Mavi = Color.FromArgb(28, 154, 214);
        public static readonly Color MaviKoyu = Color.FromArgb(18, 126, 182);
        public static readonly Color Yesil = Color.FromArgb(122, 184, 48);
        public static readonly Color YesilKoyu = Color.FromArgb(98, 156, 32);
        public static readonly Color Turuncu = Color.FromArgb(240, 152, 40);
        public static readonly Color Pembe = Color.FromArgb(222, 0, 122);
        public static readonly Color PembeKoyu = Color.FromArgb(186, 0, 102);

        // Zemin ve yüzeyler (sıcak kırık beyaz, kurum sitesindeki #fffdf8 tonundan)
        public static readonly Color Zemin = Color.FromArgb(247, 247, 244);
        public static readonly Color YanMenu = Color.FromArgb(240, 240, 236);
        public static readonly Color Yuzey = Color.White;
        public static readonly Color Cizgi = Color.FromArgb(231, 230, 226);
        public static readonly Color CizgiKoyu = Color.FromArgb(212, 211, 205);
        public static readonly Color Hover = Color.FromArgb(236, 235, 231);

        // Yazı
        public static readonly Color Yazi = Color.FromArgb(30, 36, 44);
        public static readonly Color Yazi2 = Color.FromArgb(104, 110, 120);
        public static readonly Color Yazi3 = Color.FromArgb(150, 154, 160);

        public static Color Acik(Color c, int alfa) => Color.FromArgb(alfa, c);
        public static Color Karistir(Color a, Color b, double t) =>
            Color.FromArgb((int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));
    }

    /// <summary>Fluent UI System Icons (MIT) karakter kodları.</summary>
    public static class Ik
    {
        public const string Masaustu = "", Megafon = "", Yukle = "", Indir = "", Kilit = "",
            Guc = "", Yenile = "", Gunes = "", Saat = "", Takvim = "", Duraklat = "",
            Resim = "", Yazici = "", Anahtar_ = "", ProgramEkle = "", Esitle = "", Ekle = "",
            Sil = "", Anahtar = "", Gecmis = "", Tik = "", TikDaire = "", Kapat = "",
            Daha = "", Klasor = "", Belge = "", Ara = "", Ayar = "", Uyari = "",
            Bilgi = "", Kisi = "", Yukari = "", Ev = "", Gonder = "", Zamanlayici = "",
            Kalkan = "", HepsiniSec = "", Dizustu = "", Zil = "", Sag = "", Kopuk = "",
            Daire = "", Uygulamalar = "", HataDaire = "", Goz = "", Sol = "", Eksi = "",
            KapatDaire = "",
            Kamera = "\uf255", Liste = "\uf4ee", Kareler = "\uf463", Kisiler = "\uf5a9", Duzenle = "\uf3de",
            Mezuniyet = "\ue6e7", Kalp = "\uf47a", Bina = "\uf205", Kitap = "\uf8fe", Yapboz = "\ue9ea",
            Muzik = "\ue855", Firca = "\uf593", Sohbet = "\uf287", Secenekler = "\uf588", Buyut = "\uf160",
            Ac = "\uf583", Etiket = "\uf77d", Yildiz = "\uf710", Ogretmen = "\uf5ce", Oyun = "\uf451",
            Okundu = "\uf522", AsagiOk = "\uf2a4", Pencere = "\uee5e", GozKapali = "\ue5f6", Parilti = "\ueb34",
            Telefon = "\uf5e1", YaziciIk = "\uf62b", GonderOk = "\uf69a", Kopyala = "\uf32c", BulutYukle = "\ue394",
            Dosya = "\uf379", KlasorAc = "\uf42f", Fotograf = "\uf489", DuvarKagidi = "\uf867", Paylas = "\uf6b0", Esitle2 = "\uf191";
    }

    public static class Tema
    {
        public static readonly float Olcek;
        static readonly PrivateFontCollection ikonlar = new PrivateFontCollection();
        static readonly string yaziAilesi;
        static IntPtr ikonBellek;

        static Tema()
        {
            using (var g = Graphics.FromHwnd(IntPtr.Zero)) Olcek = Math.Max(1f, g.DpiX / 96f);
            var mevcut = FontFamily.Families.Select(f => f.Name).ToList();
            yaziAilesi = new[] { "Segoe UI Variable Text", "Segoe UI", "Inter", "DejaVu Sans" }.FirstOrDefault(mevcut.Contains) ?? FontFamily.GenericSansSerif.Name;
            try
            {
                byte[] b;
                using (var s = typeof(Tema).Assembly.GetManifestResourceStream("ikon.ttf"))
                using (var ms = new MemoryStream()) { s.CopyTo(ms); b = ms.ToArray(); }
                // Dosyadan yüklemek her ortamda en güvenilir yol; olmazsa bellekten dene.
                try
                {
                    var yol = Path.Combine(Path.GetTempPath(), "sem_ikon_" + b.Length + ".ttf");
                    if (!File.Exists(yol) || new FileInfo(yol).Length != b.Length) File.WriteAllBytes(yol, b);
                    ikonlar.AddFontFile(yol);
                }
                catch
                {
                    ikonBellek = Marshal.AllocCoTaskMem(b.Length);
                    Marshal.Copy(b, 0, ikonBellek, b.Length);
                    ikonlar.AddMemoryFont(ikonBellek, b.Length);
                }
            }
            catch { }
        }

        public static int S(float x) => (int)Math.Round(x * Olcek);
        public static Size S(int w, int h) => new Size(S(w), S(h));
        public static Point P(int x, int y) => new Point(S(x), S(y));
        public static Padding Pd(int l, int t, int r, int b) => new Padding(S(l), S(t), S(r), S(b));

        /// <summary>Yazı tipi; boyut punto cinsinden (DPI'ya Windows kendisi ölçekler).</summary>
        public static Font Yazi(float boyut, FontStyle stil = FontStyle.Regular) => new Font(yaziAilesi, boyut, stil);
        public static Font YaziKalin(float boyut) =>
            FontFamily.Families.Any(f => f.Name == "Segoe UI Semibold") ? new Font("Segoe UI Semibold", boyut) : new Font(yaziAilesi, boyut, FontStyle.Bold);
        static readonly bool sistemdeIkon = FontFamily.Families.Any(f => f.Name == "FluentSystemIcons-Regular");
        public static Font Ikon(float boyut) =>
            sistemdeIkon ? new Font("FluentSystemIcons-Regular", boyut, GraphicsUnit.Point)
            : ikonlar.Families.Length > 0 ? new Font(ikonlar.Families[0], boyut, GraphicsUnit.Point) : new Font(yaziAilesi, boyut);

        public static GraphicsPath Yuvarlak(RectangleF r, float yaricap)
        {
            var p = new GraphicsPath();
            float d = Math.Min(yaricap * 2, Math.Min(r.Width, r.Height));
            if (d <= 0) { p.AddRectangle(r); return p; }
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static void Kaliteli(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        }

        public static void Kart(Graphics g, Rectangle r, float yaricap, Color dolgu, Color? cizgi = null, bool golge = true)
        {
            if (golge)
            {
                for (int i = 3; i >= 1; i--)
                {
                    var gr = new RectangleF(r.X, r.Y + S(i), r.Width, r.Height);
                    using (var gp = Yuvarlak(gr, yaricap + i))
                    using (var b = new SolidBrush(Color.FromArgb(7, 20, 24, 32))) g.FillPath(b, gp);
                }
            }
            using (var p = Yuvarlak(r, yaricap))
            {
                using (var b = new SolidBrush(dolgu)) g.FillPath(b, p);
                if (cizgi != null) using (var pen = new Pen(cizgi.Value, 1f)) g.DrawPath(pen, p);
            }
        }

        /// <summary>Kenarlıksız, gölgeli kart. yukselti 0..1 arası: üzerine gelince kart hafifçe kalkar, gölge büyür.</summary>
        public static void YuzenKart(Graphics g, RectangleF r, float yaricap, Color dolgu, float yukselti, Color? cizgi = null)
        {
            int kat = 3 + (int)Math.Round(yukselti * 4);
            for (int i = kat; i >= 1; i--)
            {
                var gr = new RectangleF(r.X - i * 0.5f, r.Y + S(i) * (0.6f + yukselti * 0.6f), r.Width + i, r.Height);
                using (var gp = Yuvarlak(gr, yaricap + i))
                using (var b = new SolidBrush(Color.FromArgb((int)(6 + yukselti * 3), 20, 24, 32))) g.FillPath(b, gp);
            }
            using (var p = Yuvarlak(r, yaricap))
            {
                using (var b = new SolidBrush(dolgu)) g.FillPath(b, p);
                if (cizgi != null) using (var pen = new Pen(cizgi.Value, 1f)) g.DrawPath(pen, p);
            }
        }

        public static void IkonCiz(Graphics g, string ikon, float boyut, Color renk, RectangleF alan)
        {
            using (var f = Ikon(boyut))
            using (var b = new SolidBrush(renk))
            using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                g.DrawString(ikon, f, b, alan, sf);
        }

        public static void YaziCiz(Graphics g, string metin, Font f, Color renk, RectangleF alan,
            StringAlignment yatay = StringAlignment.Near, StringAlignment dikey = StringAlignment.Center, bool tekSatir = true)
        {
            using (var b = new SolidBrush(renk))
            using (var sf = new StringFormat { Alignment = yatay, LineAlignment = dikey, Trimming = StringTrimming.EllipsisCharacter })
            {
                if (tekSatir) sf.FormatFlags |= StringFormatFlags.NoWrap;
                g.DrawString(metin ?? "", f, b, alan, sf);
            }
        }

        // ---------------------------------------------------------------- Windows 11 pencere süsleri
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int val, int size);

        public static bool Win11 => Environment.OSVersion.Platform == PlatformID.Win32NT && Environment.OSVersion.Version.Major >= 10 && Environment.OSVersion.Version.Build >= 22000;

        public static bool KoseleriYuvarla(IntPtr h)
        {
            if (!Win11) return false;
            try { int v = 2; return DwmSetWindowAttribute(h, 33, ref v, 4) == 0; } catch { return false; }
        }

        /// <summary>Kenarlıksız, boyutlandırılabilir pencerede kenara yakın noktayı boyutlandırma koduna çevirir (WM_NCHITTEST).</summary>
        public static int KenarBul(Form f, Point ekran, int sonuc, int k)
        {
            if (sonuc != 1 /* HTCLIENT */ || f.WindowState != FormWindowState.Normal) return sonuc;
            var p = f.PointToClient(ekran); int w = f.ClientSize.Width, h = f.ClientSize.Height;
            bool sol = p.X < k, sag = p.X >= w - k, ust = p.Y < k, alt = p.Y >= h - k;
            // Köşe alanı geniş tutulur: yuvarlak köşede asıl köşe pikselleri kesik olduğundan kenar boyunca köşeye yakın bölge de çapraz boyutlar
            int c = k * 4;
            bool solK = p.X < c, sagK = p.X >= w - c, ustK = p.Y < c, altK = p.Y >= h - c;
            if ((ust || sol) && ustK && solK) return 13; if ((ust || sag) && ustK && sagK) return 14;
            if ((alt || sol) && altK && solK) return 16; if ((alt || sag) && altK && sagK) return 17;
            if (sol) return 10; if (sag) return 11; if (ust) return 12; if (alt) return 15;
            return sonuc;
        }

        public static void BaslikRengi(IntPtr h, Color c)
        {
            if (!Win11) return;
            try
            {
                int v = c.R | (c.G << 8) | (c.B << 16);
                DwmSetWindowAttribute(h, 35, ref v, 4);       // başlık çubuğu rengi
                int y = Renk.Yazi.R | (Renk.Yazi.G << 8) | (Renk.Yazi.B << 16);
                DwmSetWindowAttribute(h, 36, ref y, 4);       // başlık yazı rengi
                int k = Renk.Cizgi.R | (Renk.Cizgi.G << 8) | (Renk.Cizgi.B << 16);
                DwmSetWindowAttribute(h, 34, ref k, 4);       // kenarlık rengi
            }
            catch { }
        }

        /// <summary>Pencereyi kısa bir saydamlık geçişiyle açar.</summary>
        public static void Belir(Form f, int sureMs = 140)
        {
            if (Environment.OSVersion.Platform != PlatformID.Win32NT) return;
            try
            {
                f.Opacity = 0;
                var bas = DateTime.Now;
                var t = new Timer { Interval = 15 };
                t.Tick += (s, e) =>
                {
                    double o = Math.Min(1, (DateTime.Now - bas).TotalMilliseconds / sureMs);
                    try { f.Opacity = o < 1 ? o : 1; } catch { }
                    if (o >= 1) { t.Stop(); t.Dispose(); }
                };
                f.FormClosed += (s, e) => t.Dispose();
                t.Start();
            }
            catch { }
        }

        public static Image Logo()
        {
            var s = typeof(Tema).Assembly.GetManifestResourceStream("logo.png");
            return s == null ? null : Image.FromStream(s);
        }
    }
}
