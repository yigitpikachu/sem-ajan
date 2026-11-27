using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Printing;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Media;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Sem.Arayuz;
using Sem.Ortak;

namespace Sem.Ajan
{
    /// <summary>Sağ altta beliren kart taban sınıfı: üst üste gelenler yukarı doğru dizilir.</summary>
    class KoseKarti : ModernForm
    {
        static readonly List<KoseKarti> acik = new List<KoseKarti>();
        protected override bool ShowWithoutActivation => true;

        public KoseKarti(int w, int h) : base(null, w, h, false)
        {
            TopMost = true; StartPosition = FormStartPosition.Manual; ShowInTaskbar = false;
            BackColor = Renk.Yuzey; Icerik.BackColor = Renk.Yuzey;
            Icerik.SetBounds(Tema.S(20), Tema.S(18), Width - Tema.S(40), Height - Tema.S(36));
            SabitBoyut = true;
            Load += (s, e) => { acik.Add(this); Diz(); };
            FormClosed += (s, e) => { acik.Remove(this); Diz(); };
        }

        static void Diz()
        {
            var alan = Screen.PrimaryScreen.WorkingArea;
            int y = alan.Bottom - Tema.S(16);
            foreach (var k in acik.ToList())
            {
                y -= k.Height;
                k.Location = new Point(alan.Right - k.Width - Tema.S(16), y);
                y -= Tema.S(10);
            }
        }

        protected Label Baslik(string metin, string ust)
        {
            var u = Yazi.UstBaslik(ust, Renk.Yazi3); u.Location = new Point(0, 0);
            var b = Yazi.Etiket(metin, 11f, true, Renk.Yazi, false); b.SetBounds(0, Tema.S(18), Icerik.Width, Tema.S(26));
            Icerik.Controls.Add(u); Icerik.Controls.Add(b);
            return b;
        }
    }

    /// <summary>"X dosya gönderiyor / yazdırmak istiyor": Kabul / Reddet (yazdırmada "Her zaman izin ver").</summary>
    class SoruKarti : KoseKarti
    {
        readonly Action<int> cevap;
        bool bitti;
        readonly DateTime son;
        readonly int sure;
        readonly Panel cubuk;

        public SoruKarti(string tur, string baslik, string metin, int sn, Action<int> cevap) : base(400, 36 + 50 + 16 * Satir(metin) + 14 + (tur == "mudur" ? 32 : 0) + 36)
        {
            this.cevap = cevap; sure = Math.Max(10, sn); son = DateTime.Now.AddSeconds(sure);
            Baslik(baslik, "Sem Paylaş");
            var m = Yazi.Etiket(metin, 9f, false, Renk.Yazi2, false); m.SetBounds(0, Tema.S(50), Icerik.Width, Tema.S(16 * Satir(metin) + 4));
            Icerik.Controls.Add(m);

            int y = Icerik.Height - Tema.S(36);
            var kabul = new Buton(tur == "mudur" ? "Kaydet" : "Kabul et", Ik.Tik, ButonTuru.Birincil) { Height = Tema.S(36) };
            var red = new Buton("Reddet", "", ButonTuru.Ikincil) { Height = Tema.S(36) };
            kabul.BoyutAyarla(); red.BoyutAyarla();
            kabul.Location = new Point(Icerik.Width - kabul.Width, y);
            red.Location = new Point(kabul.Left - red.Width - Tema.S(8), y);
            kabul.Click += (s, e) => Bitir(1);
            red.Click += (s, e) => Bitir(0);
            Icerik.Controls.Add(kabul); Icerik.Controls.Add(red);
            if (tur == "mudur")
            {
                var yaz = new Buton("Yazdır", Ik.YaziciIk, ButonTuru.Ikincil) { Height = Tema.S(36) };
                yaz.BoyutAyarla();
                yaz.Location = new Point(red.Left - Tema.S(8) - yaz.Width, y);
                yaz.Click += (s, e) => Bitir(3);
                Icerik.Controls.Add(yaz);
                var her = new Buton("Bu telefona her zaman izin ver", "", ButonTuru.Sade) { Height = Tema.S(28), Font = Tema.Yazi(8.75f) };
                her.BoyutAyarla(); her.Location = new Point(-Tema.S(8), y - Tema.S(34));
                her.Click += (s, e) => Bitir(2);
                Icerik.Controls.Add(her);
            }
            // Kalan süre: alttaki ince çizgi kısalır
            cubuk = new Panel { BackColor = Renk.Acik(Renk.Mavi, 120), Height = Tema.S(2), Location = new Point(0, Height - Tema.S(2)), Width = Width };
            Controls.Add(cubuk); cubuk.BringToFront();
            var t = new System.Windows.Forms.Timer { Interval = 100 };
            t.Tick += (s, e) =>
            {
                var k = (son - DateTime.Now).TotalSeconds;
                cubuk.Width = (int)(Width * Math.Max(0, k / sure));
                if (k <= 0) Bitir(0);
            };
            t.Start();
            FormClosed += (s, e) => { t.Dispose(); Bitir(0); };
            Shown += (s, e) => SystemSounds.Asterisk.Play();
        }

        static int Satir(string m) => Math.Min(8, Math.Max(1, (m ?? "").Split('\n').Length));

        void Bitir(int c)
        {
            if (bitti) return; bitti = true;
            try { cevap(c); } catch { }
            if (!IsDisposed) BeginInvoke((Action)Close);
        }
    }

    /// <summary>Gelen yazı mesajı: kopyalanabilir.</summary>
    class MetinKarti : KoseKarti
    {
        public MetinKarti(string kimden, string metin) : base(400, 36 + 52 + 18 * Satir(metin) + 14 + 36)
        {
            Baslik(kimden, "Mesaj");
            var kutu = new TextBox
            {
                Text = metin.Replace("\r\n", "\n").Replace("\n", "\r\n"), ReadOnly = true, Multiline = true, BorderStyle = BorderStyle.None,
                BackColor = Renk.Yuzey, ForeColor = Renk.Yazi, Font = Tema.Yazi(10f), ScrollBars = Satir(metin) >= 8 ? ScrollBars.Vertical : ScrollBars.None,
            };
            kutu.SetBounds(0, Tema.S(52), Icerik.Width, Icerik.Height - Tema.S(52 + 48));
            var kopyala = new Buton("Kopyala", Ik.Kopyala, ButonTuru.Birincil) { Height = Tema.S(36) };
            var kapat = new Buton("Kapat", "", ButonTuru.Ikincil) { Height = Tema.S(36) };
            kopyala.BoyutAyarla(); kapat.BoyutAyarla();
            kopyala.Location = new Point(Icerik.Width - kopyala.Width, Icerik.Height - Tema.S(36));
            kapat.Location = new Point(kopyala.Left - kapat.Width - Tema.S(8), kopyala.Top);
            kopyala.Click += (s, e) => { try { Clipboard.SetText(metin); kopyala.Text = "Kopyalandı"; kopyala.Ikon = Ik.TikDaire; kopyala.BoyutAyarla(); kopyala.Left = Icerik.Width - kopyala.Width; } catch { } };
            kapat.Click += (s, e) => Close();
            Icerik.Controls.AddRange(new Control[] { kutu, kopyala, kapat });
            Shown += (s, e) => { kutu.Select(0, 0); kopyala.Focus(); SystemSounds.Asterisk.Play(); };
        }

        static int Satir(string m)
        {
            int n = 0;
            foreach (var l in (m ?? "").Split('\n')) n += Math.Max(1, (l.Length + 47) / 48);
            return Math.Min(8, Math.Max(1, n));
        }
    }

    /// <summary>Telefondan gelen dosyaları yazıcıya gönderir (fotoğraf ve PDF; diğerleri Windows'un "yazdır" komutuyla).</summary>
    static class Yazdirici
    {
        public static string YaziciBul()
        {
            var l = PrinterSettings.InstalledPrinters.Cast<string>().ToList();
            return l.FirstOrDefault(x => x.Contains("1643")) ?? l.FirstOrDefault(x => x.IndexOf("Canon", StringComparison.OrdinalIgnoreCase) >= 0)
                ?? new PrinterSettings().PrinterName;
        }

        static readonly string[] Resim = { ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tif", ".tiff" };

        /// <summary>Dosyayı yazdırır; olmazsa hata mesajı döndürür.</summary>
        public static string Yazdir(string yol)
        {
            var uz = Path.GetExtension(yol).ToLowerInvariant();
            var sayfalar = new List<Image>();
            try
            {
                if (Resim.Contains(uz)) sayfalar.Add(Image.FromFile(yol));
                else if (uz == ".pdf")
                {
                    try { sayfalar.AddRange(PdfSayfalari(yol)); }
                    catch (Exception e) { Gunluk.Yaz("PDF çizilemedi: " + e.Message); return KabukYazdir(yol); }
                }
                else return KabukYazdir(yol);
                if (sayfalar.Count == 0) return "Dosyada sayfa yok";
                int i = 0;
                using (var pd = new PrintDocument())
                {
                    pd.PrinterSettings.PrinterName = YaziciBul();
                    pd.DocumentName = "Sem - " + Path.GetFileName(yol);
                    pd.PrintController = new StandardPrintController(); // "yazdırılıyor" penceresi çıkmasın
                    pd.QueryPageSettings += (s, e) => { var im = sayfalar[i]; e.PageSettings.Landscape = im.Width > im.Height * 1.05; };
                    pd.PrintPage += (s, e) =>
                    {
                        var im = sayfalar[i];
                        var m = e.MarginBounds;
                        float o = Math.Min(m.Width / (float)im.Width, m.Height / (float)im.Height);
                        float w = im.Width * o, h = im.Height * o;
                        e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        e.Graphics.DrawImage(im, m.X + (m.Width - w) / 2, m.Y + (m.Height - h) / 2, w, h);
                        i++;
                        e.HasMorePages = i < sayfalar.Count;
                    };
                    pd.Print();
                }
                return null;
            }
            catch (Exception e) { return e.Message; }
            finally { foreach (var s in sayfalar) s.Dispose(); }
        }

        static string KabukYazdir(string yol)
        {
            try
            {
                var p = Process.Start(new ProcessStartInfo(yol) { Verb = "print", UseShellExecute = true, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden });
                return null;
            }
            catch { return "Bu dosya türü yazdırılamıyor (" + Path.GetExtension(yol) + "). PDF ya da fotoğraf gönderin."; }
        }

        // ---------------------------------------------------------------- PDF: Windows'un yerleşik PDF çizicisi (Windows.Data.Pdf)
        const string WinRT = ", ContentType=WindowsRuntime";
        static Type T(string ad, string derleme) => Type.GetType(ad + ", " + derleme + WinRT, true);
        static readonly Type Uzanti = Type.GetType("System.WindowsRuntimeSystemExtensions, System.Runtime.WindowsRuntime, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089", true);
        static readonly Type AkisUzanti = Type.GetType("System.IO.WindowsRuntimeStreamExtensions, System.Runtime.WindowsRuntime, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089", true);

        static object Bekle(object islem, Type donus)
        {
            if (donus.IsGenericType)
            {
                var sonucTur = donus.GetGenericArguments()[0];
                var m = Uzanti.GetMethods().First(x => x.Name == "AsTask" && x.IsGenericMethod && x.GetParameters().Length == 1
                    && x.GetParameters()[0].ParameterType.Name.StartsWith("IAsyncOperation`")).MakeGenericMethod(sonucTur);
                var t = (Task)m.Invoke(null, new[] { islem });
                t.Wait(60000);
                return t.GetType().GetProperty("Result").GetValue(t);
            }
            var ma = Uzanti.GetMethods().First(x => x.Name == "AsTask" && !x.IsGenericMethod && x.GetParameters().Length == 1 && x.GetParameters()[0].ParameterType.Name == "IAsyncAction");
            ((Task)ma.Invoke(null, new[] { islem })).Wait(60000);
            return null;
        }

        static object Cagir(Type tur, object nesne, string ad, params object[] arg)
        {
            var m = tur.GetMethods().First(x => x.Name == ad && x.GetParameters().Length == arg.Length);
            var r = m.Invoke(nesne, arg);
            return m.ReturnType.Name.StartsWith("IAsync") ? Bekle(r, m.ReturnType) : r;
        }

        static List<Image> PdfSayfalari(string yol)
        {
            var tDosya = T("Windows.Storage.StorageFile", "Windows.Storage");
            var tPdf = T("Windows.Data.Pdf.PdfDocument", "Windows.Data.Pdf");
            var tSecenek = T("Windows.Data.Pdf.PdfPageRenderOptions", "Windows.Data.Pdf");
            var tBellek = T("Windows.Storage.Streams.InMemoryRandomAccessStream", "Windows.Storage.Streams");
            var dosya = Cagir(tDosya, null, "GetFileFromPathAsync", Path.GetFullPath(yol));
            var belge = Cagir(tPdf, null, "LoadFromFileAsync", dosya);
            uint adet = (uint)tPdf.GetProperty("PageCount").GetValue(belge);
            var sonuc = new List<Image>();
            for (uint i = 0; i < Math.Min(adet, 50u); i++)
            {
                var sayfa = tPdf.GetMethod("GetPage").Invoke(belge, new object[] { i });
                var tSayfa = sayfa.GetType();
                var boyut = tSayfa.GetProperty("Size").GetValue(sayfa); // Windows.Foundation.Size (DIP)
                double gen = Convert.ToDouble(boyut.GetType().GetProperty("Width").GetValue(boyut));
                double yuk = Convert.ToDouble(boyut.GetType().GetProperty("Height").GetValue(boyut));
                var sec = Activator.CreateInstance(tSecenek);
                double olcek = 2480.0 / Math.Max(gen, yuk); // uzun kenar ~300 dpi A4
                tSecenek.GetProperty("DestinationWidth").SetValue(sec, (uint)Math.Round(gen * olcek));
                tSecenek.GetProperty("DestinationHeight").SetValue(sec, (uint)Math.Round(yuk * olcek));
                var akis = Activator.CreateInstance(tBellek);
                var render = tSayfa.GetMethods().First(x => x.Name == "RenderToStreamAsync" && x.GetParameters().Length == 2);
                Bekle(render.Invoke(sayfa, new[] { akis, sec }), render.ReturnType);
                var asStream = AkisUzanti.GetMethods().First(x => x.Name == "AsStream" && x.GetParameters().Length == 1 && x.GetParameters()[0].ParameterType.Name == "IRandomAccessStream");
                using (var s = (Stream)asStream.Invoke(null, new[] { akis }))
                {
                    s.Position = 0;
                    var ms = new MemoryStream(); s.CopyTo(ms); ms.Position = 0;
                    sonuc.Add(Image.FromStream(ms));
                }
                (sayfa as IDisposable)?.Dispose();
            }
            return sonuc;
        }
    }

    // ==================================================================== Sem Paylaş penceresi (/paylas)

    /// <summary>Hizmetle konuşan yerel kanal (SemAjanPaylas).</summary>
    class PaylasKanali : IDisposable
    {
        NamedPipeClientStream p; StreamWriter yaz;
        public event Action<string[]> Geldi;

        public bool Baglan()
        {
            try
            {
                p = new NamedPipeClientStream(".", Sabitler.PaylasPipe, PipeDirection.InOut, PipeOptions.Asynchronous);
                p.Connect(3000);
                yaz = new StreamWriter(p, new UTF8Encoding(false)) { AutoFlush = true };
                var oku = new StreamReader(p, new UTF8Encoding(false));
                new Thread(() =>
                {
                    try { string s; while ((s = oku.ReadLine()) != null) Geldi?.Invoke(EkranHatti.Coz(s)); } catch { }
                    Geldi?.Invoke(new[] { "KOPTU" });
                }) { IsBackground = true }.Start();
                return true;
            }
            catch { return false; }
        }

        public void Gonder(params string[] a) { try { lock (this) yaz?.WriteLine(EkranHatti.Paketle(a)); } catch { } }
        public void Dispose() { try { p?.Dispose(); } catch { } }
    }

    class PaylasForm : ModernForm
    {
        readonly List<string> dosyalar = new List<string>();
        readonly BirakmaAlani alan;
        readonly Giris mesaj;
        readonly FlowLayoutPanel liste;
        readonly Buton gonder, yenile;
        readonly Label durum;
        readonly PaylasKanali kanal = new PaylasKanali();
        readonly Dictionary<string, CihazKarti> kartlar = new Dictionary<string, CihazKarti>();
        int bekleyen;

        public PaylasForm(IEnumerable<string> ilk) : base("Sem Paylaş", 640, 620)
        {
            SabitBoyut = true; ShowInTaskbar = true; StartPosition = FormStartPosition.CenterScreen; AllowDrop = true;
            int g = Icerik.Width;

            alan = new BirakmaAlani { Location = new Point(0, 0), Size = new Size(g, Tema.S(108)) };
            alan.Click += (s, e) => DosyaSec();
            alan.Temizle += () => { dosyalar.Clear(); alan.Dosyalar = dosyalar; Guncelle(); };
            Icerik.Controls.Add(alan);

            mesaj = new Giris(false) { Location = new Point(0, Tema.S(120)), Width = g, Ipucu = "Mesaj ekle (isteğe bağlı)" };
            mesaj.TextChanged += (s, e) => Guncelle();
            Icerik.Controls.Add(mesaj);

            var bas = Yazi.UstBaslik("Kime gönderilsin?", Renk.Yazi3); bas.Location = new Point(0, Tema.S(178));
            Icerik.Controls.Add(bas);

            liste = new FlowLayoutPanel { Location = new Point(-Tema.S(4), Tema.S(198)), Size = new Size(g + Tema.S(8), Icerik.Height - Tema.S(198 + 60)), AutoScroll = true, BackColor = Renk.Zemin, Padding = new Padding(0) };
            var listeKutu = InceKaydirma.Sar(liste); listeKutu.SetBounds(-Tema.S(4), Tema.S(198), g + Tema.S(8), Icerik.Height - Tema.S(198 + 60));
            Icerik.Controls.Add(listeKutu);

            durum = Yazi.Etiket("Cihazlar aranıyor…", 9f, false, Renk.Yazi2, false);
            durum.SetBounds(0, Icerik.Height - Tema.S(40), g - Tema.S(260), Tema.S(40)); durum.TextAlign = ContentAlignment.MiddleLeft;
            durum.Anchor = AnchorStyles.Left | AnchorStyles.Bottom;
            yenile = new Buton("", Ik.Yenile, ButonTuru.Sade) { Size = Tema.S(40, 40), Anchor = AnchorStyles.Right | AnchorStyles.Bottom };
            gonder = new Buton("Gönder", Ik.GonderOk, ButonTuru.Birincil) { Height = Tema.S(40), Anchor = AnchorStyles.Right | AnchorStyles.Bottom };
            gonder.Width = Tema.S(150);
            gonder.Location = new Point(g - gonder.Width, Icerik.Height - Tema.S(40));
            yenile.Location = new Point(gonder.Left - yenile.Width - Tema.S(6), gonder.Top);
            yenile.Click += (s, e) => Ara();
            gonder.Click += (s, e) => Gonder();
            Icerik.Controls.AddRange(new Control[] { durum, yenile, gonder });

            DragEnter += (s, e) => e.Effect = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
            DragDrop += (s, e) => Ekle((string[])e.Data.GetData(DataFormats.FileDrop));
            foreach (Control c in new Control[] { alan, liste }) { c.AllowDrop = true; c.DragEnter += (s, e) => e.Effect = DragDropEffects.Copy; c.DragDrop += (s, e) => Ekle((string[])e.Data.GetData(DataFormats.FileDrop)); }

            kanal.Geldi += a => { try { BeginInvoke((Action)(() => KanalGeldi(a))); } catch { } };
            Ekle(ilk);
            Shown += (s, e) =>
            {
                if (!kanal.Baglan()) { durum.Text = "Sem Ajan hizmetine ulaşılamadı."; durum.ForeColor = Renk.Pembe; return; }
                Ara();
            };
            FormClosed += (s, e) => kanal.Dispose();
            Guncelle();
        }

        void DosyaSec()
        {
            using (var o = new OpenFileDialog { Multiselect = true, Title = "Gönderilecek dosyaları seçin" })
                if (o.ShowDialog(this) == DialogResult.OK) Ekle(o.FileNames);
        }

        void Ekle(IEnumerable<string> yollar)
        {
            if (yollar == null) return;
            foreach (var y in yollar)
            {
                if (Directory.Exists(y)) { foreach (var f in Directory.GetFiles(y, "*", SearchOption.AllDirectories).Take(500)) if (!dosyalar.Contains(f)) dosyalar.Add(f); }
                else if (File.Exists(y) && !dosyalar.Contains(y)) dosyalar.Add(y);
            }
            alan.Dosyalar = dosyalar;
            Guncelle();
        }

        void Ara()
        {
            foreach (var k in kartlar.Values) k.Dispose();
            kartlar.Clear(); liste.Controls.Clear();
            durum.Text = "Cihazlar aranıyor…"; durum.ForeColor = Renk.Yazi2;
            yenile.Enabled = false;
            kanal.Gonder("CIHAZLAR");
        }

        void KanalGeldi(string[] a)
        {
            switch (a[0])
            {
                case "CIHAZ":
                {
                    if (kartlar.ContainsKey(a[1])) break;
                    var k = new CihazKarti(a[1], a[2], a[3], a[4], a[5], a.Length > 6 ? a[6] : "");
                    k.Click += (s, e) => { if (bekleyen == 0) { k.Secili = !k.Secili; Guncelle(); } };
                    kartlar[a[1]] = k; liste.Controls.Add(k);
                    break;
                }
                case "SON":
                    yenile.Enabled = true;
                    if (kartlar.Count == 0) durum.Text = "Açık bilgisayar ya da telefon bulunamadı. Telefonda LocalSend açıksa burada görünür.";
                    else durum.Text = kartlar.Count + " cihaz bulundu" + (kartlar.Values.Any(k => k.Tur == "telefon") ? "" : " · telefonda LocalSend'i açın");
                    Guncelle();
                    break;
                case "ILERLEME":
                {
                    CihazKarti k; int y;
                    if (kartlar.TryGetValue(a[1], out k) && int.TryParse(a[2], out y)) { k.Ilerleme = y / 100f; k.Durum = 1; k.Invalidate(); }
                    break;
                }
                case "BITTI":
                {
                    CihazKarti k;
                    if (kartlar.TryGetValue(a[1], out k)) { k.Durum = a[2] == "1" ? 2 : 3; k.Not = a[3]; k.Invalidate(); }
                    bekleyen = Math.Max(0, bekleyen - 1);
                    if (bekleyen == 0)
                    {
                        int ok = kartlar.Values.Count(x => x.Durum == 2), hata = kartlar.Values.Count(x => x.Durum == 3);
                        durum.Text = hata == 0 ? "Gönderildi" + (ok > 1 ? " (" + ok + " cihaz)" : "") : ok + " başarılı, " + hata + " başarısız. Kartın altında nedeni yazıyor.";
                        durum.ForeColor = hata == 0 ? Renk.YesilKoyu : Renk.Pembe;
                        gonder.Text = "Tekrar gönder";
                        Guncelle();
                    }
                    break;
                }
                case "KOPTU":
                    if (!IsDisposed && bekleyen > 0) { durum.Text = "Hizmet bağlantısı koptu."; durum.ForeColor = Renk.Pembe; bekleyen = 0; Guncelle(); }
                    break;
            }
        }

        void Guncelle()
        {
            bool icerik = dosyalar.Count > 0 || mesaj.Text.Trim() != "";
            gonder.Enabled = bekleyen == 0 && icerik && kartlar.Values.Any(k => k.Secili);
        }

        void Gonder()
        {
            var hedefler = kartlar.Values.Where(k => k.Secili).ToList();
            if (hedefler.Count == 0) return;
            bekleyen = hedefler.Count;
            foreach (var k in kartlar.Values) { k.Durum = 0; k.Not = ""; k.Ilerleme = 0; k.Invalidate(); }
            durum.Text = "Gönderiliyor… Karşı tarafın onaylaması bekleniyor."; durum.ForeColor = Renk.Yazi2;
            foreach (var k in hedefler) { k.Durum = 1; k.Invalidate(); kanal.Gonder("GONDER", k.Id, string.Join("\n", dosyalar), mesaj.Text.Trim()); }
            Guncelle();
        }

        /// <summary>Kesik çizgili bırakma alanı.</summary>
        class BirakmaAlani : Control
        {
            public event Action Temizle;
            List<string> d = new List<string>();
            bool uzerinde;
            Rectangle temizleAlan;
            public List<string> Dosyalar { set { d = value.ToList(); Invalidate(); } }

            public BirakmaAlani()
            {
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
                BackColor = Renk.Zemin; Cursor = Cursors.Hand;
            }
            protected override void OnMouseEnter(EventArgs e) { uzerinde = true; Invalidate(); base.OnMouseEnter(e); }
            protected override void OnMouseLeave(EventArgs e) { uzerinde = false; Invalidate(); base.OnMouseLeave(e); }
            protected override void OnMouseClick(MouseEventArgs e)
            {
                if (d.Count > 0 && temizleAlan.Contains(e.Location)) { Temizle?.Invoke(); return; }
                base.OnClick(e);
            }
            protected override void OnClick(EventArgs e) { }

            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics; Tema.Kaliteli(g);
                var r = new RectangleF(1, 1, Width - 3, Height - 3);
                using (var p = Tema.Yuvarlak(r, Tema.S(14)))
                {
                    using (var b = new SolidBrush(uzerinde ? Renk.Acik(Renk.Mavi, 14) : Renk.Yuzey)) g.FillPath(b, p);
                    using (var pen = new Pen(uzerinde ? Renk.Acik(Renk.Mavi, 160) : Renk.CizgiKoyu, Tema.S(1.5f)) { DashPattern = new[] { 4f, 3f } }) g.DrawPath(pen, p);
                }
                if (d.Count == 0)
                {
                    Tema.IkonCiz(g, Ik.BulutYukle, 20f, Renk.Mavi, new RectangleF(0, Tema.S(16), Width, Tema.S(36)));
                    Tema.YaziCiz(g, "Dosyaları buraya sürükleyin", Tema.YaziKalin(10.5f), Renk.Yazi, new RectangleF(0, Tema.S(54), Width, Tema.S(22)), StringAlignment.Center);
                    Tema.YaziCiz(g, "ya da seçmek için tıklayın", Tema.Yazi(9f), Renk.Yazi2, new RectangleF(0, Tema.S(76), Width, Tema.S(18)), StringAlignment.Center);
                    return;
                }
                long top = 0; foreach (var f in d) try { top += new FileInfo(f).Length; } catch { }
                float x = Tema.S(20);
                var ik = new RectangleF(x, (Height - Tema.S(44)) / 2f, Tema.S(44), Tema.S(44));
                using (var p = Tema.Yuvarlak(ik, Tema.S(12))) using (var b = new SolidBrush(Renk.Acik(Renk.Mavi, 26))) g.FillPath(b, p);
                Tema.IkonCiz(g, d.Count == 1 ? Ik.Dosya : Ik.Klasor, 14f, Renk.Mavi, ik);
                x += Tema.S(60);
                string baslik = d.Count == 1 ? Path.GetFileName(d[0]) : d.Count + " dosya";
                Tema.YaziCiz(g, baslik, Tema.YaziKalin(10.5f), Renk.Yazi, new RectangleF(x, Height / 2f - Tema.S(22), Width - x - Tema.S(110), Tema.S(22)));
                string alt = Boyut(top) + (d.Count > 1 ? "  ·  " + string.Join(", ", d.Take(3).Select(Path.GetFileName)) + (d.Count > 3 ? "…" : "") : "") + "  ·  eklemek için tıklayın";
                Tema.YaziCiz(g, alt, Tema.Yazi(8.75f), Renk.Yazi2, new RectangleF(x, Height / 2f + Tema.S(1), Width - x - Tema.S(110), Tema.S(20)));
                temizleAlan = new Rectangle(Width - Tema.S(96), (Height - Tema.S(30)) / 2, Tema.S(80), Tema.S(30));
                Tema.YaziCiz(g, "Temizle", Tema.Yazi(9f), Renk.Yazi2, temizleAlan, StringAlignment.Center);
            }

            static string Boyut(long b) => b >= 1024 * 1024 ? (b / 1024.0 / 1024).ToString("0.0") + " MB" : Math.Max(1, b / 1024) + " KB";
        }

        /// <summary>Cihaz kutusu: simge, ad, alt yazı; seçilince ince mavi halka, gönderirken ilerleme.</summary>
        class CihazKarti : Control
        {
            public readonly string Id, Ad, Alt, Simge, Tur;
            public bool Secili;
            public int Durum; // 0 boş, 1 gönderiliyor, 2 tamam, 3 hata
            public float Ilerleme;
            public string Not = "";
            bool uzerinde;
            readonly Color renk;

            public CihazKarti(string id, string ad, string grupYaModel, string simge, string tur, string grupAd = "")
            {
                Id = id; Ad = ad; Tur = tur;
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
                BackColor = Renk.Zemin; Cursor = Cursors.Hand;
                Size = Tema.S(186, 64); Margin = Tema.Pd(4, 4, 4, 4);
                if (tur == "telefon" || tur == "diger") { Simge = Ik.Telefon; Alt = grupYaModel != "" ? grupYaModel : "Telefon"; renk = Renk.Pembe; }
                else
                {
                    Simge = simge != "" ? simge : Ik.Masaustu;
                    Alt = tur == "yonetici" ? "Yönetici" : grupAd != "" ? grupAd : Yonetici.Gruplar.Ad(grupYaModel == "" ? Yonetici.Gruplar.Varsayilan : grupYaModel);
                    renk = grupYaModel == "" ? Renk.Mavi : Yonetici.Gruplar.KodRengi(grupYaModel);
                    if (renk == Renk.Yazi3) renk = Renk.Mavi;
                }
            }

            protected override void OnMouseEnter(EventArgs e) { uzerinde = true; Invalidate(); base.OnMouseEnter(e); }
            protected override void OnMouseLeave(EventArgs e) { uzerinde = false; Invalidate(); base.OnMouseLeave(e); }

            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics; Tema.Kaliteli(g);
                var r = new RectangleF(Tema.S(2), Tema.S(2), Width - Tema.S(4) - 1, Height - Tema.S(5) - 1);
                Tema.YuzenKart(g, r, Tema.S(12), Secili ? Color.FromArgb(246, 251, 254) : Renk.Yuzey, uzerinde ? 0.6f : 0.1f);
                if (Secili) using (var p = Tema.Yuvarlak(r, Tema.S(12))) using (var pen = new Pen(Renk.Mavi, Tema.S(1.5f))) g.DrawPath(pen, p);
                var ik = new RectangleF(r.X + Tema.S(12), r.Y + (r.Height - Tema.S(36)) / 2, Tema.S(36), Tema.S(36));
                using (var b = new SolidBrush(Renk.Acik(renk, 26))) g.FillEllipse(b, ik);
                Tema.IkonCiz(g, Simge, 12f, renk, ik);
                if (Durum == 1)
                {
                    var hr = RectangleF.Inflate(ik, Tema.S(3), Tema.S(3));
                    using (var pen = new Pen(Renk.Acik(Renk.Mavi, 40), Tema.S(2.5f))) g.DrawEllipse(pen, hr);
                    using (var pen = new Pen(Renk.Mavi, Tema.S(2.5f)) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                        g.DrawArc(pen, hr, -90, Math.Max(12, 360 * Ilerleme));
                }
                float x = ik.Right + Tema.S(10), w = r.Right - x - Tema.S(28);
                Tema.YaziCiz(g, Ad, Tema.YaziKalin(9.75f), Renk.Yazi, new RectangleF(x, r.Y + Tema.S(11), w, Tema.S(20)));
                string alt = Durum == 2 ? "Gönderildi" : Durum == 3 ? (Not != "" ? Not : "Gönderilemedi") : Durum == 1 ? (Ilerleme > 0 ? "%" + (int)(Ilerleme * 100) : "Onay bekleniyor…") : Alt;
                Tema.YaziCiz(g, alt, Tema.Yazi(8.5f), Durum == 2 ? Renk.YesilKoyu : Durum == 3 ? Renk.Pembe : Renk.Yazi2, new RectangleF(x, r.Y + Tema.S(31), w + Tema.S(20), Tema.S(18)));
                var kose = new RectangleF(r.Right - Tema.S(26), r.Y + Tema.S(8), Tema.S(18), Tema.S(18));
                if (Durum == 2) Tema.IkonCiz(g, Ik.TikDaire, 11f, Renk.Yesil, kose);
                else if (Durum == 3) Tema.IkonCiz(g, Ik.HataDaire, 11f, Renk.Pembe, kose);
                else if (Secili) { using (var b = new SolidBrush(Renk.Mavi)) g.FillEllipse(b, kose); Tema.IkonCiz(g, Ik.Tik, 7.5f, Color.White, kose); }
            }
        }
    }
}
