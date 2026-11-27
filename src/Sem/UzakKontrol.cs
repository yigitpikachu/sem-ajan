// Uzaktan kontrol: ekran tarafında fare/klavye olaylarını uygular ve kullanıcıya "bağlı" bandını gösterir.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Sem.Arayuz;

namespace Sem.Ajan
{
    static class Girdi
    {
        [StructLayout(LayoutKind.Sequential)] struct MOUSEINPUT { public int dx, dy; public int mouseData; public uint dwFlags, time; public IntPtr extra; }
        [StructLayout(LayoutKind.Sequential)] struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public IntPtr extra; }
        [StructLayout(LayoutKind.Explicit)] struct UNION { [FieldOffset(0)] public MOUSEINPUT mi; [FieldOffset(0)] public KEYBDINPUT ki; }
        [StructLayout(LayoutKind.Sequential)] struct INPUT { public uint type; public UNION u; }
        [StructLayout(LayoutKind.Sequential)] struct CURSORINFO { public int cbSize, flags; public IntPtr hCursor; public Point pt; }
        [StructLayout(LayoutKind.Sequential)] struct ICONINFO { public bool fIcon; public int x, y; public IntPtr mask, color; }

        [DllImport("user32.dll", SetLastError = true)] static extern uint SendInput(uint n, INPUT[] i, int size);
        [DllImport("user32.dll")] static extern bool GetCursorInfo(ref CURSORINFO ci);
        [DllImport("user32.dll")] static extern bool GetIconInfo(IntPtr h, out ICONINFO ii);
        [DllImport("user32.dll")] static extern bool DrawIcon(IntPtr hdc, int x, int y, IntPtr h);
        [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr h);

        const uint MOVE = 0x1, LDOWN = 0x2, LUP = 0x4, RDOWN = 0x8, RUP = 0x10, MDOWN = 0x20, MUP = 0x40, WHEEL = 0x800, ABS = 0x8000;
        const uint KEYUP = 0x2, EXT = 0x1;

        static INPUT Fare(uint f, int x = 0, int y = 0, int veri = 0) => new INPUT { type = 0, u = new UNION { mi = new MOUSEINPUT { dx = x, dy = y, dwFlags = f, mouseData = veri } } };

        /// <summary>Olaylar ';' ile ayrılır: m,x,y (0-65535) · d,b / u,b (0 sol, 1 sağ, 2 orta) · w,delta · k,vk,1|0 (bas/bırak)</summary>
        public static void Uygula(string olaylar)
        {
            var liste = new System.Collections.Generic.List<INPUT>();
            foreach (var o in (olaylar ?? "").Split(';'))
            {
                var a = o.Split(','); if (a.Length < 2) continue;
                int v1, v2 = 0; if (!int.TryParse(a[1], out v1)) continue; if (a.Length > 2) int.TryParse(a[2], out v2);
                switch (a[0])
                {
                    case "m": liste.Add(Fare(MOVE | ABS, Math.Max(0, Math.Min(65535, v1)), Math.Max(0, Math.Min(65535, v2)))); break;
                    case "d": liste.Add(Fare(v1 == 1 ? RDOWN : v1 == 2 ? MDOWN : LDOWN)); break;
                    case "u": liste.Add(Fare(v1 == 1 ? RUP : v1 == 2 ? MUP : LUP)); break;
                    case "w": liste.Add(Fare(WHEEL, 0, 0, v1)); break;
                    case "k":
                    {
                        var vk = (ushort)v1;
                        bool ext = vk >= 33 && vk <= 46 || vk == 91 || vk == 92 || vk == 93 || vk == 111 || vk == 144 || vk == 163 || vk == 165;
                        liste.Add(new INPUT { type = 1, u = new UNION { ki = new KEYBDINPUT { wVk = vk, dwFlags = (v2 == 1 ? 0 : KEYUP) | (ext ? EXT : 0) } } });
                        break;
                    }
                }
            }
            if (liste.Count > 0) try { SendInput((uint)liste.Count, liste.ToArray(), Marshal.SizeOf(typeof(INPUT))); } catch { }
        }

        /// <summary>Uzak görüntüye fare imlecini çizer (CopyFromScreen imleci içermez).</summary>
        public static void ImlecCiz(Graphics g, Rectangle ekran)
        {
            try
            {
                var ci = new CURSORINFO { cbSize = Marshal.SizeOf(typeof(CURSORINFO)) };
                if (!GetCursorInfo(ref ci) || ci.flags != 1 || ci.hCursor == IntPtr.Zero) return;
                int hx = 0, hy = 0;
                ICONINFO ii;
                if (GetIconInfo(ci.hCursor, out ii)) { hx = ii.x; hy = ii.y; if (ii.mask != IntPtr.Zero) DeleteObject(ii.mask); if (ii.color != IntPtr.Zero) DeleteObject(ii.color); }
                var hdc = g.GetHdc();
                try { DrawIcon(hdc, ci.pt.X - ekran.X - hx, ci.pt.Y - ekran.Y - hy, ci.hCursor); } finally { g.ReleaseHdc(hdc); }
            }
            catch { }
        }
    }

    /// <summary>Ekranın üst ortasında küçük "Yönetici bu bilgisayara bağlı" bandı.</summary>
    class UzakBandi : Form
    {
        static UzakBandi acik;
        protected override bool ShowWithoutActivation => true;
        protected override CreateParams CreateParams { get { var c = base.CreateParams; c.ExStyle |= 0x08000000 | 0x80 | 0x20; return c; } } // noactivate, toolwindow, transparent

        public static void Goster(bool goster)
        {
            if (goster) { if (acik == null || acik.IsDisposed) { acik = new UzakBandi(); acik.Show(); } }
            else if (acik != null) { acik.Close(); acik = null; }
        }

        UzakBandi()
        {
            FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; TopMost = true; StartPosition = FormStartPosition.Manual;
            BackColor = Color.FromArgb(32, 33, 36); Size = Tema.S(300, 34); Opacity = 0.92;
            var e = Screen.PrimaryScreen.WorkingArea;
            Location = new Point(e.Left + (e.Width - Width) / 2, e.Top + Tema.S(8));
            Region = Region.FromHrgn(Yuvarlak(Width, Height, Height));
        }

        [DllImport("gdi32.dll")] static extern IntPtr CreateRoundRectRgn(int x1, int y1, int x2, int y2, int w, int h);
        static IntPtr Yuvarlak(int w, int h, int r) => CreateRoundRectRgn(0, 0, w + 1, h + 1, r, r);

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; Tema.Kaliteli(g);
            using (var b = new SolidBrush(Color.FromArgb(52, 199, 89))) g.FillEllipse(b, Tema.S(16), (Height - Tema.S(8)) / 2f, Tema.S(8), Tema.S(8));
            Tema.YaziCiz(g, "Yönetici bu bilgisayara bağlı", Tema.YaziKalin(9f), Color.White, new RectangleF(Tema.S(32), 0, Width - Tema.S(40), Height));
        }
    }

    /// <summary>
    /// Uzaktan kontrolde yalnızca değişen ekran bölgelerini gönderir. Ekran 64 piksellik karelere bölünür,
    /// önceki kareyle karşılaştırılır; değişen kareler yatay ve dikey birleştirilip ayrı JPEG parçaları olarak paketlenir.
    /// Paket: "SK" · sürüm(1) · tür (0 aynı, 1 tam, 2 fark) · sıra(int) · genişlik, yükseklik, parça sayısı (short)
    ///        · her parça için x, y, w, h (short) · uzunluk (int) · JPEG baytları.
    /// </summary>
    public class FarkKodlayici
    {
        public const int Kare = 64;
        readonly object kilit = new object();
        byte[] onceki;
        int oncekiW, oncekiH, sira;
        Bitmap ham, kucuk;
        static readonly ImageCodecInfo jpeg = ImageCodecInfo.GetImageEncoders().First(c => c.FormatID == ImageFormat.Jpeg.Guid);

        /// <summary>Ekranı yakalar, küçültür ve paketler. temel: yönetim ekranındaki son karenin sırası (0 = yok).</summary>
        public byte[] EkranPaketi(int genislik, long kalite, int temel, bool imlec)
        {
            lock (kilit)
            {
                var b = Screen.PrimaryScreen.Bounds;
                int w = Math.Max(160, Math.Min(Math.Min(1920, b.Width), genislik));
                int h = Math.Max(1, (int)Math.Round(b.Height * (double)w / b.Width));
                if (ham == null || ham.Width != b.Width || ham.Height != b.Height) { ham?.Dispose(); ham = new Bitmap(b.Width, b.Height, PixelFormat.Format24bppRgb); }
                using (var g = Graphics.FromImage(ham)) { g.CopyFromScreen(b.Location, Point.Empty, b.Size); if (imlec) Girdi.ImlecCiz(g, b); }
                Bitmap kare = ham;
                if (w != b.Width)
                {
                    if (kucuk == null || kucuk.Width != w || kucuk.Height != h) { kucuk?.Dispose(); kucuk = new Bitmap(w, h, PixelFormat.Format24bppRgb); }
                    using (var g = Graphics.FromImage(kucuk))
                    {
                        // Her karede tüm ekran küçültülür: yüksek kalite yerine hızlı çift doğrusal yeterli.
                        g.InterpolationMode = InterpolationMode.Bilinear; g.PixelOffsetMode = PixelOffsetMode.Half; g.CompositingMode = CompositingMode.SourceCopy;
                        g.DrawImage(ham, new Rectangle(0, 0, w, h));
                    }
                    kare = kucuk;
                }
                return Paketle(kare, kalite, temel);
            }
        }

        /// <summary>Verilen kareyi önceki kareyle karşılaştırıp paketler (ekran yakalamadan bağımsız; test edilebilir).</summary>
        public byte[] Paketle(Bitmap kare, long kalite, int temel)
        {
            lock (kilit)
            {
                int w = kare.Width, h = kare.Height;
                var bd = kare.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
                int adim = bd.Stride;
                byte[] simdi = new byte[adim * h];
                Marshal.Copy(bd.Scan0, simdi, 0, simdi.Length);
                kare.UnlockBits(bd);

                bool tam = onceki == null || temel == 0 || temel != sira || oncekiW != w || oncekiH != h;
                var parcalar = new List<Rectangle>();
                if (!tam)
                {
                    parcalar = Degisenler(onceki, simdi, w, h, adim);
                    long alan = parcalar.Sum(r => (long)r.Width * r.Height);
                    if (alan * 100 > (long)w * h * 55) tam = true; // çoğu değiştiyse tek parça daha ucuz
                }
                onceki = simdi; oncekiW = w; oncekiH = h;
                byte tur = tam ? (byte)1 : parcalar.Count == 0 ? (byte)0 : (byte)2;
                if (tam) parcalar = new List<Rectangle> { new Rectangle(0, 0, w, h) };
                sira = sira >= int.MaxValue - 1 ? 1 : sira + 1;

                using (var ms = new MemoryStream())
                using (var bw = new BinaryWriter(ms))
                {
                    bw.Write((byte)'S'); bw.Write((byte)'K'); bw.Write((byte)1); bw.Write(tur); bw.Write(sira);
                    bw.Write((short)w); bw.Write((short)h); bw.Write((short)parcalar.Count);
                    using (var ep = new EncoderParameters(1))
                    {
                        ep.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, Math.Max(30L, Math.Min(90L, kalite)));
                        foreach (var r in parcalar)
                        {
                            byte[] jb;
                            using (var js = new MemoryStream())
                            {
                                if (r.Width == w && r.Height == h) kare.Save(js, jpeg, ep);
                                else using (var p = kare.Clone(r, PixelFormat.Format24bppRgb)) p.Save(js, jpeg, ep);
                                jb = js.ToArray();
                            }
                            bw.Write((short)r.X); bw.Write((short)r.Y); bw.Write((short)r.Width); bw.Write((short)r.Height);
                            bw.Write(jb.Length); bw.Write(jb);
                        }
                    }
                    bw.Flush();
                    return ms.ToArray();
                }
            }
        }

        public void Sifirla() { lock (kilit) { onceki = null; sira = 0; ham?.Dispose(); ham = null; kucuk?.Dispose(); kucuk = null; } }

        /// <summary>Değişen 64x64 kareleri bulur, yatayda şeritlere, dikeyde aynı genişlikteki şeritlerle birleştirir.</summary>
        static List<Rectangle> Degisenler(byte[] a, byte[] b, int w, int h, int adim)
        {
            int kx = (w + Kare - 1) / Kare, ky = (h + Kare - 1) / Kare;
            var kirli = new bool[kx * ky];
            for (int ty = 0; ty < ky; ty++)
            {
                int y0 = ty * Kare, y1 = Math.Min(h, y0 + Kare);
                for (int tx = 0; tx < kx; tx++)
                {
                    int bas = tx * Kare * 3, son = Math.Min(w, (tx + 1) * Kare) * 3;
                    for (int y = y0; y < y1 && !kirli[ty * kx + tx]; y++)
                    {
                        int o = y * adim + bas, bitis = y * adim + son, i = o;
                        for (; i + 8 <= bitis; i += 8) if (BitConverter.ToInt64(a, i) != BitConverter.ToInt64(b, i)) { kirli[ty * kx + tx] = true; break; }
                        if (!kirli[ty * kx + tx]) for (; i < bitis; i++) if (a[i] != b[i]) { kirli[ty * kx + tx] = true; break; }
                    }
                }
            }
            var sonuc = new List<Rectangle>();
            var acik = new List<Rectangle>(); // bir önceki satırdan uzatılabilecek şeritler
            for (int ty = 0; ty < ky; ty++)
            {
                var yeniAcik = new List<Rectangle>();
                int tx = 0;
                while (tx < kx)
                {
                    if (!kirli[ty * kx + tx]) { tx++; continue; }
                    int bas = tx; while (tx < kx && kirli[ty * kx + tx]) tx++;
                    int x = bas * Kare, xw = Math.Min(w, tx * Kare) - x, y = ty * Kare, yh = Math.Min(h, y + Kare) - y;
                    int j = acik.FindIndex(r => r.X == x && r.Width == xw && r.Bottom == y);
                    if (j >= 0) { var r = acik[j]; acik.RemoveAt(j); yeniAcik.Add(new Rectangle(r.X, r.Y, r.Width, r.Height + yh)); }
                    else yeniAcik.Add(new Rectangle(x, y, xw, yh));
                }
                sonuc.AddRange(acik); // uzatılamayanlar kapandı
                acik = yeniAcik;
            }
            sonuc.AddRange(acik);
            return sonuc;
        }
    }

    /// <summary>Yönetim ekranı tarafı: paketi çözer, parçaları elindeki görüntünün üstüne çizer.</summary>
    public static class FarkCozucu
    {
        public class Sonuc { public int Tur, Sira, W, H; public List<(Rectangle Alan, Image Resim)> Parcalar = new List<(Rectangle, Image)>(); }

        public static bool PaketMi(byte[] v) => v != null && v.Length >= 14 && v[0] == (byte)'S' && v[1] == (byte)'K';

        public static Sonuc Coz(byte[] v)
        {
            var s = new Sonuc();
            using (var br = new BinaryReader(new MemoryStream(v)))
            {
                br.ReadBytes(3); s.Tur = br.ReadByte(); s.Sira = br.ReadInt32();
                s.W = br.ReadInt16(); s.H = br.ReadInt16(); int n = br.ReadInt16();
                for (int i = 0; i < n; i++)
                {
                    var r = new Rectangle(br.ReadInt16(), br.ReadInt16(), br.ReadInt16(), br.ReadInt16());
                    var jb = br.ReadBytes(br.ReadInt32());
                    s.Parcalar.Add((r, Image.FromStream(new MemoryStream(jb))));
                }
            }
            return s;
        }

        /// <summary>Parçaları tuvale uygular; tam karede yeni tuval döner. Tuval yoksa ya da boyut uymuyorsa null döner (tam kare istenmeli).</summary>
        public static Bitmap Uygula(Bitmap tuval, Sonuc s)
        {
            if (s.Tur == 1)
            {
                var yeni = new Bitmap(s.W, s.H, PixelFormat.Format32bppPArgb);
                using (var g = Graphics.FromImage(yeni)) { g.InterpolationMode = InterpolationMode.NearestNeighbor; foreach (var p in s.Parcalar) g.DrawImage(p.Resim, p.Alan); }
                return yeni;
            }
            if (tuval == null || tuval.Width != s.W || tuval.Height != s.H) return null;
            using (var g = Graphics.FromImage(tuval)) { g.InterpolationMode = InterpolationMode.NearestNeighbor; foreach (var p in s.Parcalar) g.DrawImage(p.Resim, p.Alan); }
            return tuval;
        }
    }
}
