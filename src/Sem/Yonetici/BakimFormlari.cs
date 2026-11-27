// Uzaktan kontrol, bilgisayar bilgileri (envanter) ve program kurma (winget) pencereleri.
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Sem.Arayuz;
using Sem.Ortak;

namespace Sem.Yonetici
{
    /// <summary>Uzaktaki ekranı gösterir; fare ve klavye o bilgisayara gider.</summary>
    class UzakForm : Form
    {
        readonly Pc pc;
        readonly Gorunum gor;
        readonly Panel ust;
        readonly Label baslik, bilgi;
        readonly Buton kaliteBtn;
        volatile bool kapali, net, kucuk_;
        volatile int gorGenislik = 1366;
        readonly object kuyrukKilit = new object();
        readonly List<string> kuyruk = new List<string>();
        readonly AutoResetEvent girdiVar = new AutoResetEvent(false);

        public UzakForm(Pc pc)
        {
            this.pc = pc;
            Text = "Uzaktan kontrol · " + pc.Baslik;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            BackColor = Color.FromArgb(24, 24, 27); ForeColor = Color.White; Font = Tema.Yazi(9.5f);
            var e = Screen.FromPoint(Cursor.Position).WorkingArea;
            Size = new Size(Math.Min(e.Width - 80, Tema.S(1280)), Math.Min(e.Height - 60, Tema.S(800)));
            StartPosition = FormStartPosition.CenterScreen; KeyPreview = true; MinimumSize = Tema.S(640, 420);

            ust = new Panel { Dock = DockStyle.Top, Height = Tema.S(44), BackColor = Color.FromArgb(32, 33, 36) };
            baslik = new Label { Text = pc.Baslik, ForeColor = Color.White, Font = Tema.YaziKalin(10f), AutoSize = true, Location = new Point(Tema.S(14), Tema.S(12)) };
            bilgi = new Label { Text = "Bağlanıyor…", ForeColor = Color.FromArgb(160, 160, 166), Font = Tema.Yazi(9f), AutoSize = true, Location = new Point(Tema.S(14) + TextRenderer.MeasureText(pc.Baslik, Tema.YaziKalin(10f)).Width + Tema.S(10), Tema.S(14)) };
            var kes = new Buton("Bağlantıyı kes", Ik.Kapat, ButonTuru.Ikincil) { Height = Tema.S(32), Anchor = AnchorStyles.Top | AnchorStyles.Right };
            kaliteBtn = new Buton("Daha net", Ik.Goz, ButonTuru.Ikincil) { Height = Tema.S(32), Anchor = AnchorStyles.Top | AnchorStyles.Right };
            var buyut = new Buton("", Ik.Buyut, ButonTuru.Ikincil) { Size = Tema.S(32, 32), Anchor = AnchorStyles.Top | AnchorStyles.Right };
            ust.Controls.AddRange(new Control[] { baslik, bilgi, kes, kaliteBtn, buyut });
            ust.Layout += (s, a) =>
            {
                kes.Location = new Point(ust.Width - kes.Width - Tema.S(8), Tema.S(6));
                kaliteBtn.Location = new Point(kes.Left - kaliteBtn.Width - Tema.S(6), Tema.S(6));
                buyut.Location = new Point(kaliteBtn.Left - buyut.Width - Tema.S(6), Tema.S(6));
            };
            kes.Click += (s, a) => Close();
            buyut.Click += (s, a) => WindowState = WindowState == FormWindowState.Maximized ? FormWindowState.Normal : FormWindowState.Maximized;
            kaliteBtn.Click += (s, a) => { net = !net; kaliteBtn.Text = net ? "Daha hızlı" : "Daha net"; kaliteBtn.BoyutAyarla(); ust.PerformLayout(); gor.Focus(); };

            gor = new Gorunum { Dock = DockStyle.Fill };
            Controls.Add(gor); Controls.Add(ust);
            gor.Olay += Ekle;
            gor.Resize += (s, a) => gorGenislik = gor.Width;
            Resize += (s, a) => kucuk_ = WindowState == FormWindowState.Minimized;

            Shown += (s, a) => { gor.Focus(); new Thread(KareDongu) { IsBackground = true }.Start(); new Thread(GirdiDongu) { IsBackground = true }.Start(); };
            FormClosed += (s, a) =>
            {
                kapali = true; girdiVar.Set();
                Task.Run(() => { try { Baglanti.Gonder(pc.Ip, new Mesaj("UZAK_BITTI"), 2000); } catch { } });
                gor.Resim?.Dispose();
            };
        }

        // Klavye: tüm tuşlar (Tab, oklar dahil) uzak bilgisayara gitsin
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            const int WM_KEYDOWN = 0x100, WM_SYSKEYDOWN = 0x104;
            if (gor.Focused && (msg.Msg == WM_KEYDOWN || msg.Msg == WM_SYSKEYDOWN)) { Ekle("k," + (int)(keyData & Keys.KeyCode) + ",1"); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        protected override void OnKeyUp(KeyEventArgs e) { if (gor.Focused) { Ekle("k," + (int)e.KeyCode + ",0"); e.Handled = true; } base.OnKeyUp(e); }

        void Ekle(string olay)
        {
            lock (kuyrukKilit)
            {
                if (olay.StartsWith("m,") && kuyruk.Count > 0 && kuyruk[kuyruk.Count - 1].StartsWith("m,")) kuyruk[kuyruk.Count - 1] = olay; // fare hareketini birleştir
                else kuyruk.Add(olay);
            }
            girdiVar.Set();
        }

        void GirdiDongu()
        {
            while (!kapali)
            {
                girdiVar.WaitOne(500);
                string paket;
                lock (kuyrukKilit) { if (kuyruk.Count == 0) continue; paket = string.Join(";", kuyruk); kuyruk.Clear(); }
                try { var m = new Mesaj("UZAK_GIRDI"); m["olaylar"] = paket; Baglanti.Gonder(pc.Ip, m, 3000); } catch { }
                Thread.Sleep(15);
            }
        }

        void KareDongu()
        {
            int hata = 0; var sayac = System.Diagnostics.Stopwatch.StartNew(); int kare = 0; long bayt = 0;
            var adim = System.Diagnostics.Stopwatch.StartNew();
            int temel = 0; // elimizdeki son karenin sırası; 0 ise tam kare istenir
            while (!kapali)
            {
                // Yalnızca değişen bölgeler geldiği için saniyede 20 kareye (net kipte 15) kadar çıkılır;
                // pencere küçültülmüşse saniyede 1 kez bağlantı tazelenir.
                bool kucuk = kucuk_;
                int bekle = (kucuk ? 1000 : net ? 66 : 50) - (int)adim.ElapsedMilliseconds;
                if (bekle > 0) Thread.Sleep(bekle);
                adim.Restart();
                if (kucuk) { temel = 0; try { Baglanti.Gonder(pc.Ip, new Mesaj("UZAK_GIRDI"), 2000); } catch { } continue; } // bağlantı bandı açık kalsın
                try
                {
                    var m = new Mesaj("UZAK_KARE");
                    m["genislik"] = Math.Max(640, Math.Min(net ? 1920 : 1366, gorGenislik)).ToString();
                    m["kalite"] = net ? "80" : "55";
                    m["temel"] = temel.ToString();
                    var c = Baglanti.Gonder(pc.Ip, m, 8000);
                    if (!c.Tamam) { Durum(c["aciklama"]); temel = 0; Thread.Sleep(1500); continue; }
                    hata = 0; kare++; bayt += c.Veri.Length;
                    if (Ajan.FarkCozucu.PaketMi(c.Veri))
                    {
                        var f = Ajan.FarkCozucu.Coz(c.Veri);
                        temel = f.Sira;
                        if (f.Tur != 0)
                        {
                            var bitti = new ManualResetEventSlim();
                            BeginInvoke((Action)(() =>
                            {
                                try
                                {
                                    if (kapali) return;
                                    var eski = gor.Resim as Bitmap;
                                    var yeni = Ajan.FarkCozucu.Uygula(eski, f);
                                    if (yeni == null) { temel = 0; return; } // elimizdeki görüntü uymuyor: tam kare iste
                                    if (yeni != eski) { gor.Resim = yeni; eski?.Dispose(); gor.Invalidate(); }
                                    else foreach (var p in f.Parcalar) gor.AlaniYenile(p.Alan);
                                }
                                finally { foreach (var p in f.Parcalar) p.Resim.Dispose(); bitti.Set(); }
                            }));
                            bitti.Wait(2000); // sıradaki istek uygulanmış kareye göre gitsin
                        }
                    }
                    else
                    {
                        // Eski sürüm (1.5.1 ve öncesi): her seferinde tam JPEG gelir
                        temel = 0;
                        var img = Image.FromStream(new MemoryStream(c.Veri));
                        BeginInvoke((Action)(() =>
                        {
                            if (kapali) { img.Dispose(); return; }
                            var eski = gor.Resim; gor.Resim = img; eski?.Dispose(); gor.Invalidate();
                        }));
                    }
                    if (sayac.ElapsedMilliseconds > 2000)
                    {
                        Durum("bağlı  ·  " + (kare * 1000 / sayac.ElapsedMilliseconds) + " kare/sn  ·  " + (bayt * 8 / 1000 / sayac.ElapsedMilliseconds) + " Mbit/sn");
                        kare = 0; bayt = 0; sayac.Restart();
                    }
                }
                catch (Exception e) { temel = 0; if (++hata > 2) Durum("Bağlantı sorunu: " + e.Message); Thread.Sleep(1000); }
            }
        }

        void Durum(string t) { try { BeginInvoke((Action)(() => bilgi.Text = t)); } catch { } }

        /// <summary>Görüntüyü oranını koruyarak çizer ve fare olaylarını 0-65535 koordinata çevirir.</summary>
        class Gorunum : Control
        {
            public Image Resim;
            public event Action<string> Olay;

            public Gorunum()
            {
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.Selectable, true);
                BackColor = Color.FromArgb(24, 24, 27); TabStop = true;
            }

            protected override bool IsInputKey(Keys keyData) => true;

            /// <summary>Görüntünün değişen bölgesini ekranda yeniden çizer.</summary>
            public void AlaniYenile(Rectangle r)
            {
                var a = Alan(); if (a.Width <= 0) return;
                float o = a.Width / Resim.Width;
                Invalidate(Rectangle.Inflate(Rectangle.Round(new RectangleF(a.X + r.X * o, a.Y + r.Y * o, r.Width * o, r.Height * o)), 2, 2));
            }

            RectangleF Alan()
            {
                if (Resim == null) return RectangleF.Empty;
                float o = Math.Min(Width / (float)Resim.Width, Height / (float)Resim.Height);
                float w = Resim.Width * o, h = Resim.Height * o;
                return new RectangleF((Width - w) / 2, (Height - h) / 2, w, h);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                e.Graphics.Clear(BackColor);
                if (Resim == null)
                {
                    Tema.YaziCiz(e.Graphics, "Ekran bekleniyor…", Tema.Yazi(11f), Color.FromArgb(160, 160, 166), ClientRectangle, StringAlignment.Center);
                    return;
                }
                e.Graphics.InterpolationMode = InterpolationMode.HighQualityBilinear; e.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                e.Graphics.DrawImage(Resim, Alan());
            }

            bool Koordinat(Point p, out int x, out int y)
            {
                var a = Alan(); x = y = 0;
                if (a.Width <= 0 || !a.Contains(p)) return false;
                x = (int)((p.X - a.X) / a.Width * 65535); y = (int)((p.Y - a.Y) / a.Height * 65535);
                return true;
            }

            static int Dugme(MouseButtons b) => b == MouseButtons.Right ? 1 : b == MouseButtons.Middle ? 2 : 0;

            protected override void OnMouseMove(MouseEventArgs e) { int x, y; if (Koordinat(e.Location, out x, out y)) Olay?.Invoke("m," + x + "," + y); }
            protected override void OnMouseDown(MouseEventArgs e) { Focus(); int x, y; if (Koordinat(e.Location, out x, out y)) { Olay?.Invoke("m," + x + "," + y); Olay?.Invoke("d," + Dugme(e.Button)); } }
            protected override void OnMouseUp(MouseEventArgs e) { int x, y; if (Koordinat(e.Location, out x, out y)) Olay?.Invoke("m," + x + "," + y); Olay?.Invoke("u," + Dugme(e.Button)); }
            protected override void OnMouseWheel(MouseEventArgs e) => Olay?.Invoke("w," + e.Delta);
        }
    }

    /// <summary>Donanım, Windows, lisans, diskler ve kurulu programlar.</summary>
    class BilgiForm : ModernForm
    {
        readonly Pc pc;
        readonly Panel govde, kutu;
        readonly Label durum;
        Mesaj veri;

        public BilgiForm(Pc pc) : base("Bilgiler  ·  " + pc.Baslik, 560, 640)
        {
            this.pc = pc;
            int g = Icerik.Width;
            govde = new Panel { AutoScroll = true, BackColor = Renk.Zemin };
            kutu = InceKaydirma.Sar(govde); kutu.SetBounds(0, 0, g, Icerik.Height - Tema.S(52));
            durum = Yazi.Etiket("Bilgiler alınıyor…", 9.5f, false, Renk.Yazi2, false);
            durum.SetBounds(0, Icerik.Height - Tema.S(40), g - Tema.S(160), Tema.S(40)); durum.TextAlign = ContentAlignment.MiddleLeft;
            var aktar = new Buton("Dışa aktar", Ik.Indir, ButonTuru.Ikincil);
            aktar.Location = new Point(g - aktar.Width, Icerik.Height - aktar.Height);
            aktar.Click += (s, e) => DisaAktar();
            Icerik.Controls.AddRange(new Control[] { kutu, durum, aktar });
            Shown += (s, e) => Task.Run(() =>
            {
                try
                {
                    var c = Baglanti.Gonder(pc.Ip, new Mesaj("ENVANTER"), 30000);
                    BeginInvoke((Action)(() => { if (!c.Tamam) { durum.Text = c["aciklama"]; return; } veri = c; Doldur(); }));
                }
                catch (Exception ex) { BeginInvoke((Action)(() => durum.Text = "Hata: " + ex.Message)); }
            });
        }

        void Doldur()
        {
            int g = kutu.ClientSize.Width - Tema.S(26), y = 0;
            void Satir(string ad, string deger)
            {
                if (string.IsNullOrWhiteSpace(deger)) return;
                var a = Yazi.Etiket(ad, 9f, false, Renk.Yazi3, false); a.SetBounds(0, y, Tema.S(120), Tema.S(22));
                var d = Yazi.Etiket(deger, 9.5f, false, Renk.Yazi, false); d.SetBounds(Tema.S(124), y, g - Tema.S(124), Tema.S(22)); d.AutoEllipsis = true;
                govde.Controls.Add(a); govde.Controls.Add(d); y += Tema.S(26);
            }
            Satir("Windows", veri["windows"]);
            Satir("Lisans", veri["lisans"]);
            Satir("Model", veri["model"]);
            Satir("Seri no", veri["seri"]);
            Satir("İşlemci", veri["islemci"]);
            Satir("Bellek", veri["ram"]);
            foreach (var d in veri["diskler"].Split('\n').Where(x => x.Length > 2))
            {
                var a = d.Split('|');
                Satir("Disk", string.Join("  ·  ", a.Where(x => x != "")));
            }
            Satir("C: sürücüsü", veri["c_bos"]);
            Satir("IP / MAC", pc.Ip + (pc.Mac != "" ? "  ·  " + pc.Mac : ""));
            var progs = veri["programlar"].Split('\n').Where(x => x.Length > 1).ToList();
            y += Tema.S(10);
            var ust = Yazi.UstBaslik("Kurulu programlar (" + progs.Count + ")", Renk.Yazi3); ust.Location = new Point(0, y); govde.Controls.Add(ust);
            y += Tema.S(24);
            foreach (var p in progs)
            {
                var a = p.Split('|');
                var ad = Yazi.Etiket(a[0], 9f, false, Renk.Yazi, false); ad.SetBounds(0, y, g - Tema.S(130), Tema.S(20)); ad.AutoEllipsis = true;
                var sr = Yazi.Etiket(a.Length > 1 ? a[1] : "", 8.5f, false, Renk.Yazi3, false); sr.SetBounds(g - Tema.S(126), y, Tema.S(126), Tema.S(20)); sr.TextAlign = ContentAlignment.TopRight;
                govde.Controls.Add(ad); govde.Controls.Add(sr); y += Tema.S(21);
            }
            durum.Text = "";
        }

        void DisaAktar()
        {
            if (veri == null) return;
            using (var d = new SaveFileDialog { Filter = "Excel (CSV)|*.csv", FileName = pc.Baslik + " bilgileri.csv" })
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                string Q(string x) => "\"" + (x ?? "").Replace("\"", "\"\"") + "\"";
                var sb = new StringBuilder("sep=;\r\n");
                foreach (var k in new[] { ("Bilgisayar", pc.Baslik), ("Windows", veri["windows"]), ("Lisans", veri["lisans"]), ("Model", veri["model"]), ("Seri no", veri["seri"]),
                    ("İşlemci", veri["islemci"]), ("Bellek", veri["ram"]), ("Diskler", veri["diskler"].Replace("\n", " / ")), ("C:", veri["c_bos"]), ("IP", pc.Ip), ("MAC", pc.Mac) })
                    sb.Append(Q(k.Item1) + ";" + Q(k.Item2) + "\r\n");
                sb.Append("\r\n" + Q("Program") + ";" + Q("Sürüm") + "\r\n");
                foreach (var p in veri["programlar"].Split('\n').Where(x => x.Length > 1)) { var a = p.Split('|'); sb.Append(Q(a[0]) + ";" + Q(a.Length > 1 ? a[1] : "") + "\r\n"); }
                File.WriteAllText(d.FileName, sb.ToString(), new UTF8Encoding(true));
                durum.Text = "✓ Kaydedildi";
            }
        }
    }

    /// <summary>Hazır listeden program kurma ve güncelleme (Windows'un winget paket sistemiyle). Kurulu olanlar önceden gösterilir.</summary>
    class ProgramForm : ModernForm
    {
        public static readonly (string Id, string Ad, string Desen, string Aciklama)[] Katalog =
        {
            ("Google.Chrome", "Google Chrome", "Google Chrome", "İnternet tarayıcısı"),
            ("Microsoft.Office", "Microsoft 365 (Office)", "Microsoft 365", "Word, Excel, PowerPoint"),
            ("Adobe.Acrobat.Reader.64-bit", "Adobe Acrobat Reader", "Adobe Acrobat", "PDF okuyucu"),
            ("Zoom.Zoom", "Zoom", "Zoom", "Görüntülü görüşme"),
            ("7zip.7zip", "7-Zip", "7-Zip", "Sıkıştırılmış dosyalar (zip, rar)"),
            ("VideoLAN.VLC", "VLC", "VLC media player", "Video ve müzik oynatıcı"),
        };

        public (string id, string ad, bool guncelle, List<Pc> hedefler, bool dosyadan)? Sonuc;
        readonly List<Pc> hedefler;
        readonly ConcurrentDictionary<Pc, Dictionary<string, (string ad, string surum)>> kurulu = new ConcurrentDictionary<Pc, Dictionary<string, (string, string)>>();
        readonly ConcurrentDictionary<Pc, string> hata = new ConcurrentDictionary<Pc, string>();
        readonly ListBox liste;
        readonly Label durum;
        readonly Buton kur, guncelle;
        bool yuklendi;

        public ProgramForm(List<Pc> hedefler) : base("Program kur", 600, 620)
        {
            this.hedefler = hedefler;
            int g = Icerik.Width;
            var ust = Yazi.Etiket(hedefler.Count == 1 ? hedefler[0].Baslik : hedefler.Count + " bilgisayar seçili", 9.5f, false, Renk.Yazi2); ust.Location = new Point(0, 0);
            var kart = new KartPanel { Location = new Point(0, Tema.S(28)), Size = new Size(g, Icerik.Height - Tema.S(28 + 140)), Padding = Tema.Pd(6, 8, 6, 8), BackColor = Renk.Zemin, Golge = false };
            liste = new ListBox { Dock = DockStyle.Fill, DrawMode = DrawMode.OwnerDrawFixed, ItemHeight = Tema.S(46), BorderStyle = BorderStyle.None, BackColor = Renk.Yuzey, IntegralHeight = false };
            foreach (var k in Katalog) liste.Items.Add(k.Ad);
            liste.DrawItem += CizOge;
            liste.SelectedIndexChanged += (s, e) => Guncelle();
            kart.Controls.Add(InceKaydirma.Sar(liste));
            durum = Yazi.Etiket("Bilgisayarlardaki programlara bakılıyor…", 9.5f, false, Renk.Yazi2, false);
            durum.SetBounds(0, kart.Bottom + Tema.S(10), g, Tema.S(70));
            var dosyadan = new Buton("Dosyadan kur…", Ik.Belge, ButonTuru.Sade);
            dosyadan.Location = new Point(-Tema.S(8), Icerik.Height - dosyadan.Height);
            dosyadan.Click += (s, e) => { Sonuc = (null, null, false, null, true); Close(); };
            kur = new Buton("Kur", Ik.Indir, ButonTuru.Birincil) { Enabled = false };
            guncelle = new Buton("Güncelle", Ik.Yenile, ButonTuru.Ikincil) { Enabled = false };
            kur.Width = Math.Max(kur.Width, Tema.S(110));
            kur.Location = new Point(g - kur.Width, Icerik.Height - kur.Height);
            guncelle.Location = new Point(kur.Left - guncelle.Width - Tema.S(8), kur.Top);
            kur.Click += (s, e) => Bitir(false);
            guncelle.Click += (s, e) => Bitir(true);
            Icerik.Controls.AddRange(new Control[] { ust, kart, durum, dosyadan, kur, guncelle });
            Shown += (s, e) => Task.Run(Tara);
        }

        void Tara()
        {
            var adlar = string.Join("\n", Katalog.Select(k => k.Id + "=" + k.Desen));
            Parallel.ForEach(hedefler, p =>
            {
                try
                {
                    var m = new Mesaj("PROGRAM_DURUM"); m["adlar"] = adlar;
                    var c = Baglanti.Gonder(p.Ip, m, 15000);
                    if (!c.Tamam) { hata[p] = c["aciklama"]; return; }
                    if (c["winget"] == "0") hata[p] = "winget yok";
                    var d = new Dictionary<string, (string, string)>();
                    foreach (var s in c["kurulu"].Split('\n').Where(x => x.Contains('|'))) { var a = s.Split('|'); d[a[0]] = (a[1], a.Length > 2 ? a[2] : ""); }
                    kurulu[p] = d;
                }
                catch (Exception ex) { hata[p] = ex.Message.StartsWith("Bilgisayara") ? "kapalı" : ex.Message; }
            });
            yuklendi = true;
            try { BeginInvoke((Action)(() => { liste.Invalidate(); if (liste.SelectedIndex < 0) durum.Text = "Kurmak ya da güncellemek istediğiniz programı seçin."; else Guncelle(); })); } catch { }
        }

        int KuruluSayisi(string id) => kurulu.Count(kv => kv.Value.ContainsKey(id));

        void CizOge(object s, DrawItemEventArgs e)
        {
            if (e.Index < 0) return;
            var k = Katalog[e.Index];
            var g = e.Graphics; Tema.Kaliteli(g);
            bool sec = (e.State & DrawItemState.Selected) != 0;
            using (var b = new SolidBrush(Renk.Yuzey)) g.FillRectangle(b, e.Bounds);
            var r = new RectangleF(e.Bounds.X + Tema.S(4), e.Bounds.Y + Tema.S(2), e.Bounds.Width - Tema.S(8), e.Bounds.Height - Tema.S(4));
            if (sec) using (var p = Tema.Yuvarlak(r, Tema.S(8))) using (var b = new SolidBrush(Color.FromArgb(226, 243, 251))) g.FillPath(b, p);
            Tema.IkonCiz(g, Ik.Uygulamalar, 13f, Renk.Mavi, new RectangleF(r.X + Tema.S(6), r.Y, Tema.S(32), r.Height));
            Tema.YaziCiz(g, k.Ad, Tema.YaziKalin(10f), Renk.Yazi, new RectangleF(r.X + Tema.S(46), r.Y + Tema.S(4), r.Width - Tema.S(200), r.Height / 2));
            Tema.YaziCiz(g, k.Aciklama, Tema.Yazi(8.5f), Renk.Yazi3, new RectangleF(r.X + Tema.S(46), r.Y + r.Height / 2 - Tema.S(2), r.Width - Tema.S(200), r.Height / 2));
            if (yuklendi)
            {
                int n = KuruluSayisi(k.Id), top = kurulu.Count;
                var t = n == 0 ? "kurulu değil" : hedefler.Count == 1 ? "kurulu  " + kurulu.Values.First()[k.Id].surum : n == top ? "hepsinde kurulu" : n + "/" + top + " kurulu";
                Tema.YaziCiz(g, t, Tema.Yazi(8.75f), n > 0 ? Renk.YesilKoyu : Renk.Yazi3, new RectangleF(r.Right - Tema.S(170), r.Y, Tema.S(160), r.Height), StringAlignment.Far);
            }
        }

        void Guncelle()
        {
            if (liste.SelectedIndex < 0 || !yuklendi) return;
            var k = Katalog[liste.SelectedIndex];
            var var_ = kurulu.Where(kv => kv.Value.ContainsKey(k.Id)).Select(kv => kv.Key).ToList();
            var yok = kurulu.Where(kv => !kv.Value.ContainsKey(k.Id) && !hata.ContainsKey(kv.Key)).Select(kv => kv.Key).ToList();
            var sb = new StringBuilder();
            if (var_.Count > 0) sb.Append("Zaten kurulu: " + string.Join(", ", var_.Select(p => p.Baslik + (kurulu[p][k.Id].surum != "" ? " (" + kurulu[p][k.Id].surum + ")" : ""))) + ". Bunlara yeniden kurulmaz, Güncelle ile en yeni sürüme geçer.\n");
            if (yok.Count > 0) sb.Append("Kurulacak: " + string.Join(", ", yok.Select(p => p.Baslik)) + "\n");
            var sorun = hata.Where(kv => kv.Value != "").Select(kv => kv.Key.Baslik + " (" + kv.Value + ")").ToList();
            if (sorun.Count > 0) sb.Append("Atlanacak: " + string.Join(", ", sorun));
            durum.Text = sb.ToString().Trim();
            durum.ForeColor = var_.Count > 0 ? Renk.Turuncu : Renk.Yazi2;
            kur.Enabled = yok.Count > 0; kur.Text = yok.Count == 0 && var_.Count > 0 ? "Hepsinde kurulu" : "Kur"; kur.BoyutAyarla(); kur.Width = Math.Max(kur.Width, Tema.S(110));
            kur.Left = Icerik.Width - kur.Width; guncelle.Left = kur.Left - guncelle.Width - Tema.S(8);
            guncelle.Enabled = var_.Count > 0 && var_.Any(p => !hata.ContainsKey(p));
            kur.Invalidate(); guncelle.Invalidate();
        }

        void Bitir(bool guncelle_)
        {
            if (liste.SelectedIndex < 0) return;
            var k = Katalog[liste.SelectedIndex];
            var h = kurulu.Where(kv => kv.Value.ContainsKey(k.Id) == guncelle_ && !hata.ContainsKey(kv.Key)).Select(kv => kv.Key).ToList();
            Sonuc = (k.Id, k.Ad, guncelle_, h, false);
            Close();
        }
    }
}

namespace Sem.Yonetici
{
    /// <summary>
    /// Masaüstü düzeni: seçilen bilgisayarların masaüstleri taranır, ne kalacağı / toplanacağı / silineceği önizlenir;
    /// "Uygula" basılı tutulunca işaretli olanlar taşınır. Tarama sonuçları %LocalAppData%\Sem\masaustu\ içine de yazılır.
    /// </summary>
    class MasaustuForm : ModernForm
    {
        public const int Gun = 2;

        class Oge { public string Kod, Yol, Neden, Hedef, Satir; public long Boyut; public IsaretKutusu Kutu; }
        class Bolum { public Pc Pc; public List<Oge> Ogeler = new List<Oge>(); public string Hata; public string Sonuc; }

        readonly List<Pc> pcler;
        readonly List<Bolum> bolumler = new List<Bolum>();
        readonly Panel govde, kutu;
        readonly Label durum;
        readonly Buton uygula, tara;
        bool calisiyor;

        public MasaustuForm(List<Pc> pcler) : base("Masaüstü düzeni", 760, 720)
        {
            this.pcler = pcler;
            SabitBoyut = true;
            int g = Icerik.Width;
            var acik = Yazi.Etiket("Öğretmen klasörleri, kısayollar ve son " + Gun + " günde açılan dosyalar kalır. Eski dosyalar masaüstündeki \"Materyaller\" klasörüne türlerine göre toplanır. Gereksizler C:\\Sem Silinenler klasörüne taşınır, 30 gün sonra kendiliğinden silinir. İstemediğiniz satırın işaretini kaldırın.", 9.5f, false, Renk.Yazi2, false);
            acik.SetBounds(0, 0, g, Tema.S(58));
            govde = new Panel { AutoScroll = true, BackColor = Renk.Zemin };
            kutu = InceKaydirma.Sar(govde); kutu.SetBounds(0, Tema.S(66), g, Icerik.Height - Tema.S(66 + 56));
            durum = Yazi.Etiket("", 9.5f, false, Renk.Yazi2, false);
            durum.TextAlign = ContentAlignment.MiddleLeft;
            uygula = new Buton("Uygula", Ik.Tik, ButonTuru.Birincil) { BasiliTut = true, Enabled = false };
            uygula.Width = Math.Max(uygula.Width, Tema.S(130));
            tara = new Buton("Yeniden tara", Ik.Yenile, ButonTuru.Ikincil);
            uygula.Location = new Point(g - uygula.Width, Icerik.Height - uygula.Height);
            tara.Location = new Point(uygula.Left - tara.Width - Tema.S(8), uygula.Top);
            durum.SetBounds(0, uygula.Top, Math.Max(0, tara.Left - Tema.S(12)), uygula.Height);
            uygula.Onaylandi += (s, e) => Uygula();
            uygula.KisaBasildi += (s, e) => durum.Text = "Yanlışlıkla olmasın diye \"Uygula\"yı dolana kadar basılı tutun.";
            tara.Click += (s, e) => Tara();
            Icerik.Controls.AddRange(new Control[] { acik, kutu, durum, tara, uygula });
            Shown += (s, e) => Tara();
        }

        void Tara()
        {
            if (calisiyor) return;
            calisiyor = true; uygula.Enabled = false; tara.Enabled = false;
            bolumler.Clear();
            foreach (var p in pcler) bolumler.Add(new Bolum { Pc = p });
            durum.Text = "Masaüstleri taranıyor…"; durum.ForeColor = Renk.Yazi2;
            Ciz();
            Task.Run(() =>
            {
                Parallel.ForEach(bolumler, b =>
                {
                    try
                    {
                        var m = new Mesaj("MASAUSTU_TARA"); m["gun"] = Gun.ToString();
                        var c = Baglanti.Gonder(b.Pc.Ip, m, 60000);
                        if (!c.Tamam) { b.Hata = c["aciklama"] == "" ? "Bu bilgisayardaki Sem Ajan eski, önce güncellenmeli" : c["aciklama"]; return; }
                        foreach (var satir in c["plan"].Split('\n'))
                        {
                            var a = satir.Split('|');
                            if (a.Length < 5) continue;
                            long boy; long.TryParse(a[2], out boy);
                            b.Ogeler.Add(new Oge { Kod = a[0], Yol = a[1], Boyut = boy, Neden = a[3], Hedef = a[4], Satir = satir });
                        }
                        try
                        {
                            var k = Path.Combine(Depo.Klasor, "masaustu"); Directory.CreateDirectory(k);
                            File.WriteAllText(Path.Combine(k, b.Pc.Ad + ".txt"), DateTime.Now.ToString("dd.MM.yyyy HH:mm") + "\r\n" + c["plan"].Replace("\n", "\r\n"), new UTF8Encoding(true));
                        }
                        catch { }
                    }
                    catch (Exception e) { b.Hata = e.Message; }
                });
                try
                {
                    BeginInvoke((Action)(() =>
                    {
                        calisiyor = false; tara.Enabled = true;
                        int is_ = bolumler.Sum(b => b.Ogeler.Count(o => o.Kod != "K"));
                        uygula.Enabled = is_ > 0;
                        durum.Text = is_ > 0 ? "Hazır. Uygulamak için \"Uygula\"yı basılı tutun." : "Düzenlenecek bir şey yok.";
                        Ciz();
                    }));
                }
                catch { }
            });
        }

        static string Boyut(long b) => b <= 0 ? "" : b < 1024 * 1024 ? Math.Max(1, b / 1024) + " KB" : (b / 1048576.0).ToString("0.0") + " MB";

        void Ciz()
        {
            govde.SuspendLayout();
            foreach (Control c in govde.Controls.Cast<Control>().ToList()) c.Dispose();
            govde.Controls.Clear();
            int g = kutu.ClientSize.Width - Tema.S(26), y = 0;
            foreach (var b in bolumler)
            {
                var ad = Yazi.Etiket(b.Pc.Baslik, 12f, true, Renk.Yazi, false); ad.SetBounds(0, y, g / 2, Tema.S(28));
                int kal = b.Ogeler.Count(o => o.Kod == "K"), top = b.Ogeler.Count(o => o.Kod == "T"), sil = b.Ogeler.Count(o => o.Kod == "S");
                var ozet = Yazi.Etiket(b.Sonuc ?? (b.Hata != null ? b.Hata : calisiyor && b.Ogeler.Count == 0 ? "Taranıyor…" : kal + " kalır  ·  " + top + " toplanır  ·  " + sil + " silinir"),
                    9.5f, false, b.Hata != null ? Renk.PembeKoyu : b.Sonuc != null ? Renk.YesilKoyu : Renk.Yazi2, false);
                ozet.SetBounds(g / 2, y + Tema.S(4), g / 2, Tema.S(24)); ozet.TextAlign = ContentAlignment.TopRight; ozet.AutoEllipsis = true;
                govde.Controls.Add(ad); govde.Controls.Add(ozet);
                y += Tema.S(36);
                foreach (var (kod, baslik, renk) in new[] { ("S", "Silinecek", Renk.PembeKoyu), ("T", "Materyaller klasörüne toplanacak", Renk.Mavi) })
                {
                    var l = b.Ogeler.Where(o => o.Kod == kod).ToList();
                    if (l.Count == 0) continue;
                    var ub = Yazi.UstBaslik(baslik + " (" + l.Count + ")", renk); ub.Location = new Point(0, y); govde.Controls.Add(ub);
                    y += Tema.S(22);
                    foreach (var o in l)
                    {
                        if (o.Kutu == null) o.Kutu = new IsaretKutusu { Isaretli = true };
                        else o.Kutu = new IsaretKutusu { Isaretli = o.Kutu.Isaretli };
                        o.Kutu.Location = new Point(-Tema.S(8), y);
                        var dad = Yazi.Etiket(Path.GetFileName(o.Yol), 9.5f, false, Renk.Yazi, false); dad.SetBounds(Tema.S(34), y + Tema.S(5), g - Tema.S(34 + 260), Tema.S(22)); dad.AutoEllipsis = true;
                        var ne = Yazi.Etiket(kod == "T" ? o.Hedef.Replace("\\", " › ") : o.Neden, 8.5f, false, Renk.Yazi3, false); ne.SetBounds(g - Tema.S(256), y + Tema.S(6), Tema.S(190), Tema.S(20)); ne.AutoEllipsis = true;
                        var by = Yazi.Etiket(Boyut(o.Boyut), 8.5f, false, Renk.Yazi3, false); by.SetBounds(g - Tema.S(64), y + Tema.S(6), Tema.S(64), Tema.S(20)); by.TextAlign = ContentAlignment.TopRight;
                        var kk = o.Kutu; dad.Click += (s, e) => kk.Isaretli = !kk.Isaretli; dad.Cursor = Cursors.Hand;
                        govde.Controls.AddRange(new Control[] { o.Kutu, dad, ne, by });
                        y += Tema.S(30);
                    }
                    y += Tema.S(8);
                }
                var kalan = b.Ogeler.Where(o => o.Kod == "K").ToList();
                if (kalan.Count > 0)
                {
                    var ub = Yazi.UstBaslik("Kalacak (" + kalan.Count + ")", Renk.Yazi3); ub.Location = new Point(0, y); govde.Controls.Add(ub);
                    y += Tema.S(22);
                    var metin = string.Join("  ·  ", kalan.Select(o => Path.GetFileName(o.Yol)));
                    var kl = Yazi.Etiket(metin, 9f, false, Renk.Yazi2, false);
                    int h; using (var gr = CreateGraphics()) h = (int)Math.Ceiling(gr.MeasureString(metin, kl.Font, g).Height) + Tema.S(4);
                    kl.SetBounds(0, y, g, h); govde.Controls.Add(kl);
                    y += h + Tema.S(6);
                }
                y += Tema.S(22);
            }
            govde.ResumeLayout();
        }

        void Uygula()
        {
            if (calisiyor) return;
            calisiyor = true; uygula.Enabled = false; tara.Enabled = false;
            durum.Text = "Uygulanıyor…"; durum.ForeColor = Renk.Yazi2;
            var isler = bolumler.Where(b => b.Hata == null).Select(b => (b, plan: string.Join("\n", b.Ogeler.Where(o => o.Kod != "K" && o.Kutu != null && o.Kutu.Isaretli).Select(o => o.Satir)))).Where(x => x.plan != "").ToList();
            Task.Run(() =>
            {
                Parallel.ForEach(isler, x =>
                {
                    try
                    {
                        var m = new Mesaj("MASAUSTU_DUZENLE"); m["plan"] = x.plan;
                        var c = Baglanti.Gonder(x.b.Pc.Ip, m, 300000);
                        x.b.Sonuc = c.Tamam ? "✓ " + c["aciklama"] : null;
                        if (!c.Tamam) x.b.Hata = c["aciklama"];
                    }
                    catch (Exception e) { x.b.Hata = e.Message; }
                });
                try
                {
                    BeginInvoke((Action)(() =>
                    {
                        calisiyor = false; tara.Enabled = true;
                        durum.Text = "Bitti. Silinenler C:\\Sem Silinenler klasöründe 30 gün bekler.";
                        durum.ForeColor = Renk.YesilKoyu;
                        foreach (var b in bolumler) b.Ogeler.RemoveAll(o => o.Kod != "K" && o.Kutu != null && o.Kutu.Isaretli && b.Sonuc != null);
                        Ciz();
                    }));
                }
                catch { }
            });
        }
    }
}
