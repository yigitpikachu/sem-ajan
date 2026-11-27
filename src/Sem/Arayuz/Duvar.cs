// Duvar kâğıtları: koddan çizilir, her ekran çözünürlüğüne göre net olur.
// "otomatik" kipte o gün özel bir gün varsa onun tasarımı, yoksa mevsimin tasarımı kullanılır.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Linq;

namespace Sem.Arayuz
{
    public static class Duvar
    {
        public class Tasarim
        {
            public string Kod, Ust, Baslik, Alt;
            public Color Zemin, Yazi, Yazi2, Vurgu;
            public Color[] Lekeler;
            public bool Koyu;
            public string Motif = ""; // kar, yaprak, cicek, gunes, yildiz, yapboz, kalp, bayrak
            public bool BuyukTarih;
            public string Tarih = "";
        }

        static readonly Color Mavi = Color.FromArgb(37, 170, 225), Yesil = Color.FromArgb(140, 198, 63), Turuncu = Color.FromArgb(251, 176, 64), Pembe = Color.FromArgb(236, 0, 140);
        static readonly Color Murekkep = Color.FromArgb(22, 27, 34);
        const string Motto = "Sevgi ve Emek ile Mutluluk";
        const string Kurum = "SEM ÖZEL EĞİTİM VE REHABİLİTASYON MERKEZİ";

        static readonly string[] Aylar = { "Ocak", "Şubat", "Mart", "Nisan", "Mayıs", "Haziran", "Temmuz", "Ağustos", "Eylül", "Ekim", "Kasım", "Aralık" };

        /// <summary>Kip: "kurumsal" ya da "otomatik" (özel gün / mevsim).</summary>
        public static Tasarim Sec(string kip, DateTime t)
        {
            if (kip == "otomatik") return OzelGun(t) ?? Mevsim(t);
            return Kurumsal();
        }

        /// <summary>
        /// Karanlık tema sürümü: aynı tasarım, gece mavisi zemin. Vurgu rengi zeminin tonunu belirler,
        /// lekeler koyu zeminde yumuşak birer ışık gibi durur; yazılar saf beyaz değil, gözü yormayan kırık beyaz.
        /// </summary>
        public static Tasarim Koyulastir(Tasarim t)
        {
            if (t.Koyu) return t;
            var gece = Color.FromArgb(13, 16, 23);
            return new Tasarim
            {
                Kod = t.Kod, Ust = t.Ust, Baslik = t.Baslik, Alt = t.Alt, Motif = t.Motif, BuyukTarih = t.BuyukTarih, Tarih = t.Tarih,
                Koyu = true,
                Zemin = Karistir(gece, t.Vurgu, 0.05),
                Yazi = Color.FromArgb(232, 234, 239),
                Yazi2 = Color.FromArgb(140, 148, 162),
                Vurgu = Karistir(t.Vurgu, Color.White, 0.12),
                // Lekeler: rengi doygunlaştırılıp karartılır (griye kaçmasın), koyu zeminde derin bir ışıma olur
                Lekeler = t.Lekeler.Select(c => Kis(Doygun(c, 1.6), 0.42)).ToArray(),
            };
        }

        static Color Kis(Color c, double k) => Color.FromArgb((int)(c.R * k), (int)(c.G * k), (int)(c.B * k));

        static Color Doygun(Color c, double k)
        {
            double ort = (c.R + c.G + c.B) / 3.0;
            Func<double, int> s = v => (int)Math.Max(0, Math.Min(255, ort + (v - ort) * k));
            return Color.FromArgb(s(c.R), s(c.G), s(c.B));
        }

        public static Tasarim Kurumsal() => new Tasarim
        {
            Kod = "kurumsal", Ust = Kurum, Baslik = "Sevgi ve Emek\nile Mutluluk", Alt = "",
            Zemin = Color.FromArgb(246, 245, 241), Yazi = Murekkep, Yazi2 = Color.FromArgb(108, 114, 124), Vurgu = Mavi,
            Lekeler = new[] { Color.FromArgb(150, 210, 238), Color.FromArgb(196, 228, 160), Color.FromArgb(253, 214, 160), Color.FromArgb(214, 236, 250) },
        };

        public static Tasarim Mevsim(DateTime t)
        {
            switch (t.Month)
            {
                case 12: case 1: case 2:
                    return new Tasarim
                    {
                        Kod = "kis", Ust = "KIŞ", Baslik = "Sıcacık bir kış\ndileriz", Alt = Motto, Motif = "kar",
                        Zemin = Color.FromArgb(238, 243, 249), Yazi = Color.FromArgb(24, 36, 58), Yazi2 = Color.FromArgb(96, 112, 136), Vurgu = Color.FromArgb(70, 130, 210),
                        Lekeler = new[] { Color.FromArgb(178, 206, 240), Color.FromArgb(206, 196, 240), Color.FromArgb(230, 240, 252), Color.FromArgb(160, 214, 236) },
                    };
                case 3: case 4: case 5:
                    return new Tasarim
                    {
                        Kod = "ilkbahar", Ust = "İLKBAHAR", Baslik = "Her şey yeniden\nyeşeriyor", Alt = Motto, Motif = "cicek",
                        Zemin = Color.FromArgb(247, 248, 240), Yazi = Color.FromArgb(28, 44, 30), Yazi2 = Color.FromArgb(104, 118, 100), Vurgu = Color.FromArgb(110, 170, 50),
                        Lekeler = new[] { Color.FromArgb(200, 232, 160), Color.FromArgb(250, 200, 222), Color.FromArgb(255, 236, 180), Color.FromArgb(180, 224, 200) },
                    };
                case 6: case 7: case 8:
                    return new Tasarim
                    {
                        Kod = "yaz", Ust = "YAZ", Baslik = "Güneşli günler,\nmutlu çocuklar", Alt = Motto, Motif = "gunes",
                        Zemin = Color.FromArgb(252, 247, 238), Yazi = Color.FromArgb(48, 30, 18), Yazi2 = Color.FromArgb(130, 108, 92), Vurgu = Color.FromArgb(240, 140, 40),
                        Lekeler = new[] { Color.FromArgb(255, 210, 150), Color.FromArgb(255, 180, 160), Color.FromArgb(170, 226, 230), Color.FromArgb(255, 232, 170) },
                    };
                default:
                    return new Tasarim
                    {
                        Kod = "sonbahar", Ust = "SONBAHAR", Baslik = "Yeni bir dönem,\nyeni umutlar", Alt = Motto, Motif = "yaprak",
                        Zemin = Color.FromArgb(249, 244, 236), Yazi = Color.FromArgb(52, 32, 20), Yazi2 = Color.FromArgb(134, 110, 92), Vurgu = Color.FromArgb(206, 110, 40),
                        Lekeler = new[] { Color.FromArgb(244, 196, 150), Color.FromArgb(230, 170, 120), Color.FromArgb(214, 206, 150), Color.FromArgb(250, 220, 180) },
                    };
            }
        }

        /// <summary>O güne ait özel gün tasarımı; yoksa null.</summary>
        public static Tasarim OzelGun(DateTime t)
        {
            int a = t.Month, g = t.Day;
            Func<string, string, string, Color, Color[], bool, string, Tasarim> yap = (ust, baslik, alt, vurgu, leke, koyu, motif) => new Tasarim
            {
                Kod = "ozel", Ust = ust, Baslik = baslik, Alt = alt, Vurgu = vurgu, Lekeler = leke, Koyu = koyu, Motif = motif, BuyukTarih = true,
                Tarih = g + " " + Aylar[a - 1],
                Zemin = koyu ? Color.FromArgb(18, 22, 30) : Color.FromArgb(247, 246, 242),
                Yazi = koyu ? Color.FromArgb(246, 246, 248) : Murekkep, Yazi2 = koyu ? Color.FromArgb(170, 176, 188) : Color.FromArgb(108, 114, 124),
            };
            var kirmizi = Color.FromArgb(214, 40, 48);
            if (a == 1 && g == 1) return yap("YENİ YIL", "Mutlu yıllar!", "Yeni yılda sevgiyle, emekle, birlikte.", Mavi, new[] { Color.FromArgb(60, 90, 170), Color.FromArgb(120, 70, 160), Color.FromArgb(40, 120, 170), Color.FromArgb(90, 60, 130) }, true, "yildiz");
            if (a == 3 && g == 21) return yap("DÜNYA DOWN SENDROMU GÜNÜ", "Farklılıklarımızla\nbirlikteyiz", "21 Mart: üç kromozom, sonsuz sevgi.", Color.FromArgb(40, 120, 210), new[] { Color.FromArgb(120, 180, 240), Color.FromArgb(255, 214, 90), Color.FromArgb(170, 210, 250), Color.FromArgb(255, 232, 150) }, false, "kalp");
            if (a == 4 && g == 2) return yap("DÜNYA OTİZM FARKINDALIK GÜNÜ", "Her parça farklı,\nher parça değerli", "Farkındalıkla, sabırla, sevgiyle.", Color.FromArgb(30, 110, 200), new[] { Color.FromArgb(100, 160, 240), Color.FromArgb(150, 200, 250), Color.FromArgb(60, 120, 220), Color.FromArgb(190, 220, 255) }, false, "yapboz");
            if (a == 4 && g == 23) return yap("ULUSAL EGEMENLİK VE ÇOCUK BAYRAMI", "23 Nisan\nkutlu olsun", "Egemenlik kayıtsız şartsız milletindir.", kirmizi, new[] { Color.FromArgb(240, 120, 120), Color.FromArgb(255, 190, 180), Color.FromArgb(250, 150, 140), Color.FromArgb(255, 220, 210) }, false, "bayrak");
            if (a == 5 && g >= 10 && g <= 16) return yap("ENGELLİLER HAFTASI", "Engelleri birlikte\nkaldırıyoruz", "10–16 Mayıs Engelliler Haftası", Mavi, new[] { Color.FromArgb(150, 210, 238), Color.FromArgb(196, 228, 160), Color.FromArgb(253, 214, 160), Color.FromArgb(240, 180, 214) }, false, "kalp");
            if (a == 5 && g == 19) return yap("ATATÜRK'Ü ANMA, GENÇLİK VE SPOR BAYRAMI", "19 Mayıs\nkutlu olsun", "Gençliğe hitabe hep yolumuzu aydınlatsın.", kirmizi, new[] { Color.FromArgb(240, 120, 120), Color.FromArgb(255, 190, 180), Color.FromArgb(250, 150, 140), Color.FromArgb(255, 220, 210) }, false, "bayrak");
            if (a == 10 && g == 29) return yap("CUMHURİYET BAYRAMI", "Cumhuriyetimiz\nnice yıllara", "29 Ekim 1923", kirmizi, new[] { Color.FromArgb(150, 20, 30), Color.FromArgb(200, 30, 40), Color.FromArgb(110, 10, 20), Color.FromArgb(170, 40, 50) }, true, "bayrak");
            if (a == 11 && g == 10) return yap("10 KASIM", "Saygı ve\nözlemle anıyoruz", "Mustafa Kemal Atatürk (1881–1938)", Color.FromArgb(200, 200, 205), new[] { Color.FromArgb(50, 54, 62), Color.FromArgb(36, 40, 48), Color.FromArgb(64, 68, 76), Color.FromArgb(30, 32, 38) }, true, "");
            if (a == 11 && g == 24) return yap("ÖĞRETMENLER GÜNÜ", "Emeğiniz için\nteşekkürler", "Tüm öğretmenlerimizin günü kutlu olsun.", Color.FromArgb(226, 120, 40), new[] { Color.FromArgb(255, 200, 150), Color.FromArgb(250, 170, 190), Color.FromArgb(255, 226, 170), Color.FromArgb(200, 226, 170) }, false, "cicek");
            if (a == 12 && g == 3) return yap("DÜNYA ENGELLİLER GÜNÜ", "Engelleri sevgiyle\nkaldırıyoruz", "Herkes için erişilebilir bir dünya.", Color.FromArgb(120, 80, 200), new[] { Color.FromArgb(190, 170, 240), Color.FromArgb(150, 210, 238), Color.FromArgb(230, 200, 250), Color.FromArgb(170, 150, 230) }, false, "kalp");
            return null;
        }

        static readonly string Aile = AileSec();
        static string AileSec()
        {
            try
            {
                var m = new InstalledFontCollection().Families.Select(f => f.Name).ToList();
                return new[] { "Segoe UI Variable Display", "Segoe UI", "Inter", "DejaVu Sans" }.FirstOrDefault(m.Contains) ?? FontFamily.GenericSansSerif.Name;
            }
            catch { return FontFamily.GenericSansSerif.Name; }
        }
        static readonly string AileYari = AileYariSec();
        static string AileYariSec()
        {
            try
            {
                var m = new InstalledFontCollection().Families.Select(f => f.Name).ToList();
                return new[] { "Segoe UI Variable Display Semib", "Segoe UI Semibold", "Inter SemiBold", "Inter Display SemiBold" }.FirstOrDefault(m.Contains);
            }
            catch { return null; }
        }
        static readonly string AileInce = AileInceSec();
        static string AileInceSec()
        {
            try
            {
                var m = new InstalledFontCollection().Families.Select(f => f.Name).ToList();
                return new[] { "Segoe UI Variable Display Light", "Segoe UI Light", "Inter Light", "Inter Display Light" }.FirstOrDefault(m.Contains);
            }
            catch { return null; }
        }

        static Font F(float px, string tur = "normal")
        {
            if (tur == "ince" && AileInce != null) return new Font(AileInce, px, FontStyle.Regular, GraphicsUnit.Pixel);
            if (tur == "yari" && AileYari != null) return new Font(AileYari, px, FontStyle.Regular, GraphicsUnit.Pixel);
            if (tur == "kalin" || tur == "yari") return new Font(Aile, px, FontStyle.Bold, GraphicsUnit.Pixel);
            return new Font(Aile, px, FontStyle.Regular, GraphicsUnit.Pixel);
        }

        /// <summary>Duvar kâğıdını çizer. oda boş değilse sağ üstte "Bu bilgisayar: …" etiketi çıkar.</summary>
        public static Bitmap Ciz(Tasarim t, int w, int h, string oda, Image logo)
        {
            var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            float s = h / 1080f;
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias; g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality; g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                g.Clear(t.Zemin);
                Lekeler(g, t, w, h);
                Doku(bmp, t.Koyu ? 7 : 5);
                Motif(g, t, w, h, s);

                // Sağ yarıda içerik bloğu (sol üst masaüstü simgelerine, sağ alt saate yer kalır)
                float sag = w - 110 * s, sol = Math.Max(w * 0.44f, w - 960 * s);
                float y = h * 0.30f;
                var yaziRenk = new SolidBrush(t.Yazi); var yazi2 = new SolidBrush(t.Yazi2); var vurgu = new SolidBrush(t.Vurgu);
                var sf = new StringFormat { Alignment = StringAlignment.Near, LineAlignment = StringAlignment.Near };

                // Logo: kartsız, doğrudan zeminde. Koyu tasarımda tek renk (kırık beyaz), açıkta kendi renkleriyle.
                float lk = 120 * s;
                var lr = new RectangleF(sol - 6 * s, y, lk, lk);
                if (logo != null) LogoCiz(g, logo, lr, t.Koyu && LogoTekRenk ? t.Yazi : (Color?)null, t.Koyu ? 0.92f : 1f);
                y += lk + 34 * s;

                if (t.BuyukTarih && t.Tarih != "")
                {
                    using (var f = F(34 * s, "yari")) g.DrawString(t.Tarih, f, vurgu, sol - 2 * s, y, sf);
                    y += 52 * s;
                }
                using (var f = F(19 * s, "yari")) Aralikli(g, t.Ust, f, yazi2, sol, y, 2.4f * s);
                y += 40 * s;
                using (var f = F(94 * s, "ince"))
                {
                    var sz = g.MeasureString(t.Baslik, f, (int)(sag - sol), sf);
                    g.DrawString(t.Baslik, f, yaziRenk, new RectangleF(sol - 4 * s, y, sag - sol + 8 * s, sz.Height + 10), sf);
                    y += sz.Height + 18 * s;
                }
                using (var p = new Pen(t.Vurgu, 4 * s) { StartCap = LineCap.Round, EndCap = LineCap.Round }) g.DrawLine(p, sol + 2 * s, y, sol + 56 * s, y);
                y += 26 * s;
                if (!string.IsNullOrEmpty(t.Alt)) using (var f = F(24 * s)) g.DrawString(t.Alt, f, yazi2, new RectangleF(sol, y, sag - sol, 80 * s), sf);

                // Oda etiketi (sağ üst)
                if (!string.IsNullOrWhiteSpace(oda))
                {
                    using (var fu = F(13 * s, "yari")) using (var fa = F(30 * s, "yari"))
                    {
                        var aw = g.MeasureString(oda, fa).Width;
                        float pw = Math.Max(aw, 150 * s) + 56 * s, ph = 88 * s;
                        var pr = new RectangleF(w - 64 * s - pw, 56 * s, pw, ph);
                        Cam(g, pr, 22 * s, t.Koyu);
                        using (var b = new SolidBrush(t.Vurgu)) g.FillEllipse(b, pr.X + 28 * s, pr.Y + 25 * s, 8 * s, 8 * s);
                        Aralikli(g, "BU BİLGİSAYAR", fu, yazi2, pr.X + 44 * s, pr.Y + 20 * s, 1.6f * s);
                        g.DrawString(oda, fa, yaziRenk, pr.X + 25 * s, pr.Y + 38 * s);
                    }
                }
                yaziRenk.Dispose(); yazi2.Dispose(); vurgu.Dispose(); sf.Dispose();
            }
            return bmp;
        }

        /// <summary>Önizleme için: koyu tasarımda logo tek renk mi çizilsin.</summary>
        public static bool LogoTekRenk = true;

        static void LogoCiz(Graphics g, Image logo, RectangleF r, Color? tekRenk, float opaklik)
        {
            using (var ia = new ImageAttributes())
            {
                ColorMatrix cm;
                if (tekRenk is Color c)
                    cm = new ColorMatrix(new[]
                    {
                        new float[] { 0, 0, 0, 0, 0 }, new float[] { 0, 0, 0, 0, 0 }, new float[] { 0, 0, 0, 0, 0 },
                        new float[] { 0, 0, 0, opaklik, 0 },
                        new float[] { c.R / 255f, c.G / 255f, c.B / 255f, 0, 1 },
                    });
                else cm = new ColorMatrix { Matrix33 = opaklik };
                ia.SetColorMatrix(cm);
                g.DrawImage(logo, Rectangle.Round(r), 0, 0, logo.Width, logo.Height, GraphicsUnit.Pixel, ia);
            }
        }

        /// <summary>Yumuşak renk lekeleri: küçük çizilip büyütülür, doğal bir bulanıklık verir.</summary>
        static void Lekeler(Graphics g, Tasarim t, int w, int h)
        {
            int kw = Math.Max(32, w / 6), kh = Math.Max(18, h / 6);
            using (var k = new Bitmap(kw, kh, PixelFormat.Format32bppArgb))
            {
                using (var kg = Graphics.FromImage(k))
                {
                    kg.SmoothingMode = SmoothingMode.AntiAlias;
                    kg.Clear(Color.Transparent);
                    var c = t.Lekeler;
                    int alfa = t.Koyu ? 210 : 190;
                    Leke(kg, kw * 0.92f, kh * 0.18f, kw * 0.55f, kh * 0.75f, c[0], alfa);
                    Leke(kg, kw * 0.70f, kh * 0.95f, kw * 0.50f, kh * 0.62f, c[1], alfa);
                    Leke(kg, kw * 0.08f, kh * 1.02f, kw * 0.42f, kh * 0.55f, c[2], (int)(alfa * 0.85));
                    Leke(kg, kw * 0.30f, kh * 0.05f, kw * 0.38f, kh * 0.45f, c[3], (int)(alfa * 0.7));
                }
                var eski = g.InterpolationMode;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                using (var ia = new ImageAttributes())
                {
                    ia.SetWrapMode(WrapMode.TileFlipXY);
                    g.DrawImage(k, new Rectangle(0, 0, w, h), 0, 0, kw, kh, GraphicsUnit.Pixel, ia);
                }
                g.InterpolationMode = eski;
            }
        }

        static void Leke(Graphics g, float cx, float cy, float rx, float ry, Color c, int alfa)
        {
            // Eş merkezli, giderek koyulaşan elipsler: küçük tuvalde çizilip büyütülünce pürüzsüz bir geçiş olur.
            const int adim = 28;
            for (int i = 0; i < adim; i++)
            {
                float k = 1f - i / (float)adim;
                int a = (int)(alfa * 0.075f * (1 - k * k * 0.5f));
                using (var b = new SolidBrush(Color.FromArgb(Math.Max(1, Math.Min(255, a)), c)))
                    g.FillEllipse(b, cx - rx * k, cy - ry * k, rx * 2 * k, ry * 2 * k);
            }
        }

        /// <summary>İnce kumlu doku (düz renk bantlaşmasını kırar).</summary>
        static void Doku(Bitmap bmp, int guc)
        {
            var r = new Rectangle(0, 0, bmp.Width, bmp.Height);
            var d = bmp.LockBits(r, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
            try
            {
                int n = d.Stride * d.Height;
                var b = new byte[n];
                System.Runtime.InteropServices.Marshal.Copy(d.Scan0, b, 0, n);
                var rnd = new Random(7);
                for (int i = 0; i < n; i += 4)
                {
                    int v = rnd.Next(-guc, guc + 1);
                    b[i] = (byte)Math.Max(0, Math.Min(255, b[i] + v));
                    b[i + 1] = (byte)Math.Max(0, Math.Min(255, b[i + 1] + v));
                    b[i + 2] = (byte)Math.Max(0, Math.Min(255, b[i + 2] + v));
                }
                System.Runtime.InteropServices.Marshal.Copy(b, 0, d.Scan0, n);
            }
            finally { bmp.UnlockBits(d); }
        }

        /// <summary>Yarı saydam beyaz "cam" kart, yumuşak gölgeli.</summary>
        static void Cam(Graphics g, RectangleF r, float yaricap, bool koyu)
        {
            for (int i = 10; i >= 1; i--)
            {
                var gr = new RectangleF(r.X - i * 0.6f, r.Y + i * 1.1f, r.Width + i * 1.2f, r.Height + i * 0.6f);
                using (var p = Yuvarlak(gr, yaricap + i)) using (var b = new SolidBrush(Color.FromArgb(koyu ? 10 : 5, 20, 20, 40))) g.FillPath(b, p);
            }
            using (var p = Yuvarlak(r, yaricap))
            {
                using (var b = new SolidBrush(koyu ? Color.FromArgb(44, 255, 255, 255) : Color.FromArgb(205, 255, 255, 255))) g.FillPath(b, p);
                using (var pen = new Pen(koyu ? Color.FromArgb(50, 255, 255, 255) : Color.FromArgb(230, 255, 255, 255), 1.2f)) g.DrawPath(pen, p);
            }
            if (koyu) using (var p = Yuvarlak(RectangleF.Inflate(r, -6, -6), yaricap - 6)) using (var b = new SolidBrush(Color.FromArgb(235, 255, 255, 255))) { } // logo kendi zemininde okunur
        }

        static GraphicsPath Yuvarlak(RectangleF r, float y)
        {
            var p = new GraphicsPath(); float d = Math.Min(y * 2, Math.Min(r.Width, r.Height));
            p.AddArc(r.X, r.Y, d, d, 180, 90); p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure(); return p;
        }

        static void Aralikli(Graphics g, string metin, Font f, Brush b, float x, float y, float aralik)
        {
            using (var sf = StringFormat.GenericTypographic)
                foreach (var c in metin)
                {
                    var s = c.ToString();
                    g.DrawString(s, f, b, x, y, sf);
                    x += (c == ' ' ? f.Size * 0.32f : g.MeasureString(s, f, PointF.Empty, sf).Width) + aralik;
                }
        }

        /// <summary>Tasarıma özgü ince süslemeler: sol yarıda, düşük belirginlikte.</summary>
        static void Motif(Graphics g, Tasarim t, int w, int h, float s)
        {
            var rnd = new Random(t.Kod.GetHashCode() ^ t.Motif.GetHashCode());
            int a = t.Koyu ? 60 : 70;
            switch (t.Motif)
            {
                case "kar":
                    for (int i = 0; i < 26; i++)
                    {
                        float x = (float)(w * 0.08 + rnd.NextDouble() * w * 0.84), y = (float)(rnd.NextDouble() * h * 0.82), r = (float)(6 + rnd.NextDouble() * 14) * s;
                        if (x > w * 0.44 && x < w * 0.95 && y > h * 0.24 && y < h * 0.78) continue;
                        using (var p = new Pen(Color.FromArgb(a + 40, 255, 255, 255), 1.6f * s) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                            for (int k = 0; k < 3; k++)
                            {
                                double aci = Math.PI / 3 * k;
                                g.DrawLine(p, x - (float)Math.Cos(aci) * r, y - (float)Math.Sin(aci) * r, x + (float)Math.Cos(aci) * r, y + (float)Math.Sin(aci) * r);
                            }
                    }
                    break;
                case "cicek":
                    for (int i = 0; i < 14; i++)
                    {
                        float x = (float)(w * 0.06 + rnd.NextDouble() * w * 0.36), y = (float)(h * 0.12 + rnd.NextDouble() * h * 0.76), r = (float)(10 + rnd.NextDouble() * 16) * s;
                        var c = t.Lekeler[rnd.Next(t.Lekeler.Length)];
                        using (var b = new SolidBrush(Color.FromArgb(a + 30, Karistir(c, Color.White, 0.1))))
                            for (int k = 0; k < 5; k++)
                            {
                                var st = g.Save(); g.TranslateTransform(x, y); g.RotateTransform(72 * k + i * 11);
                                g.FillEllipse(b, -r * 0.32f, -r, r * 0.64f, r); g.Restore(st);
                            }
                        using (var b = new SolidBrush(Color.FromArgb(a + 60, 255, 255, 255))) g.FillEllipse(b, x - r * 0.18f, y - r * 0.18f, r * 0.36f, r * 0.36f);
                    }
                    break;
                case "yaprak":
                    for (int i = 0; i < 12; i++)
                    {
                        float x = (float)(w * 0.05 + rnd.NextDouble() * w * 0.36), y = (float)(h * 0.1 + rnd.NextDouble() * h * 0.78), r = (float)(18 + rnd.NextDouble() * 22) * s;
                        var c = t.Lekeler[rnd.Next(t.Lekeler.Length)];
                        var st = g.Save(); g.TranslateTransform(x, y); g.RotateTransform(rnd.Next(360));
                        using (var p = new GraphicsPath())
                        {
                            p.AddBezier(0, -r, r * 0.7f, -r * 0.4f, r * 0.7f, r * 0.4f, 0, r);
                            p.AddBezier(0, r, -r * 0.7f, r * 0.4f, -r * 0.7f, -r * 0.4f, 0, -r);
                            using (var b = new SolidBrush(Color.FromArgb(a + 20, Karistir(c, Color.FromArgb(180, 90, 30), 0.35)))) g.FillPath(b, p);
                        }
                        using (var pen = new Pen(Color.FromArgb(a, 255, 255, 255), 1.2f * s)) g.DrawLine(pen, 0, -r * 0.85f, 0, r * 0.85f);
                        g.Restore(st);
                    }
                    break;
                case "gunes":
                {
                    float cx = w * 0.2f, cy = h * 0.3f;
                    for (int k = 1; k <= 6; k++)
                        using (var p = new Pen(Color.FromArgb(Math.Max(10, a - k * 8), t.Vurgu), 1.6f * s)) { float r = k * 70 * s; g.DrawEllipse(p, cx - r, cy - r, r * 2, r * 2); }
                    using (var b = new SolidBrush(Color.FromArgb(a + 30, Turuncu))) g.FillEllipse(b, cx - 46 * s, cy - 46 * s, 92 * s, 92 * s);
                    break;
                }
                case "yildiz":
                    for (int i = 0; i < 60; i++)
                    {
                        float x = (float)(rnd.NextDouble() * w), y = (float)(rnd.NextDouble() * h), r = (float)(0.8 + rnd.NextDouble() * 2.4) * s;
                        using (var b = new SolidBrush(Color.FromArgb(60 + rnd.Next(120), 255, 255, 255))) g.FillEllipse(b, x - r, y - r, r * 2, r * 2);
                    }
                    break;
                case "kalp":
                    for (int i = 0; i < 10; i++)
                    {
                        float x = (float)(w * 0.06 + rnd.NextDouble() * w * 0.34), y = (float)(h * 0.12 + rnd.NextDouble() * h * 0.74), r = (float)(14 + rnd.NextDouble() * 20) * s;
                        var c = t.Lekeler[rnd.Next(t.Lekeler.Length)];
                        using (var p = new GraphicsPath())
                        {
                            p.AddBezier(x, y + r * 0.35f, x - r * 1.1f, y - r * 0.5f, x - r * 0.4f, y - r * 1.1f, x, y - r * 0.45f);
                            p.AddBezier(x, y - r * 0.45f, x + r * 0.4f, y - r * 1.1f, x + r * 1.1f, y - r * 0.5f, x, y + r * 0.35f);
                            using (var b = new SolidBrush(Color.FromArgb(a + 30, Karistir(c, t.Vurgu, 0.3)))) g.FillPath(b, p);
                        }
                    }
                    break;
                case "yapboz":
                {
                    var c = new[] { Mavi, Color.FromArgb(70, 120, 220), Color.FromArgb(120, 180, 240), Color.FromArgb(40, 90, 180) };
                    float k = 84 * s, ara = k * 1.32f, x0 = w * 0.07f, y0 = h * 0.2f;
                    for (int i = 0; i < 3; i++)
                        for (int j = 0; j < 4; j++)
                        {
                            if ((i * 3 + j) % 4 == 1) continue;
                            using (var p = Parca(x0 + i * ara, y0 + j * ara, k))
                            using (var b = new SolidBrush(Color.FromArgb(70 + (i * 4 + j) % 3 * 20, c[(i + j) % 4]))) g.FillPath(b, p);
                        }
                    break;
                }
                case "bayrak":
                {
                    // Ay yıldız, büyük ve çok soluk
                    float cx = w * 0.2f, cy = h * 0.46f, r = 230 * s;
                    var renk = t.Koyu ? Color.FromArgb(40, 255, 255, 255) : Color.FromArgb(34, 214, 40, 48);
                    using (var p = new GraphicsPath())
                    {
                        p.AddEllipse(cx - r, cy - r, r * 2, r * 2);
                        using (var ic = new GraphicsPath()) { float r2 = r * 0.8f; ic.AddEllipse(cx - r2 + r * 0.25f, cy - r2, r2 * 2, r2 * 2); using (var reg = new Region(p)) { reg.Exclude(ic); using (var b = new SolidBrush(renk)) g.FillRegion(b, reg); } }
                    }
                    using (var b = new SolidBrush(renk)) g.FillPolygon(b, Yildiz(cx + r * 0.95f, cy, r * 0.32f));
                    break;
                }
            }
        }

        static GraphicsPath Parca(float x, float y, float k)
        {
            float t = k * 0.18f, m = k / 2;
            var p = new GraphicsPath();
            p.AddLine(x, y, x + m - t, y);
            p.AddArc(x + m - t, y - t, t * 2, t * 2, 180, 180);
            p.AddLine(x + m + t, y, x + k, y);
            p.AddLine(x + k, y, x + k, y + m - t);
            p.AddArc(x + k - t, y + m - t, t * 2, t * 2, 270, 180);
            p.AddLine(x + k, y + m + t, x + k, y + k);
            p.AddLine(x + k, y + k, x, y + k);
            p.CloseFigure();
            return p;
        }

        static PointF[] Yildiz(float cx, float cy, float r)
        {
            var l = new PointF[10];
            for (int i = 0; i < 10; i++)
            {
                double a = -Math.PI / 2 + i * Math.PI / 5 + Math.PI / 2; // bir ucu sola baksın
                float rr = i % 2 == 0 ? r : r * 0.4f;
                l[i] = new PointF(cx + (float)(Math.Cos(a) * rr), cy + (float)(Math.Sin(a) * rr));
            }
            return l;
        }

        static Color Karistir(Color a, Color b, double t) => Color.FromArgb((int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));
    }
}
