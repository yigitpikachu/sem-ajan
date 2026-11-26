// Uzaktan kontrol: ekran tarafında fare/klavye olaylarını uygular ve kullanıcıya "bağlı" bandını gösterir.
using System;
using System.Drawing;
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
}
