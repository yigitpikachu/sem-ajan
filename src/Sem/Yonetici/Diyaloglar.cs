using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using System.Windows.Forms;
using Sem.Ajan;
using Sem.Arayuz;
using Sem.Ortak;

namespace Sem.Yonetici
{
    static class Diyalog
    {
        public static string Metin(IWin32Window sahip, string baslik, string aciklama, string varsayilan, string tamam = "Tamam")
        {
            using (var f = new ModernForm(baslik, 460, 250))
            {
                var a = Yazi.Etiket(aciklama, 10f, false, Renk.Yazi2, false); a.SetBounds(0, 0, f.Icerik.Width, Tema.S(48));
                var g = new Giris { Text = varsayilan, Location = new Point(0, Tema.S(56)), Width = f.Icerik.Width };
                f.Icerik.Controls.Add(a); f.Icerik.Controls.Add(g);
                var ok = AltDugmeler(f, tamam);
                string sonuc = null;
                ok.Click += (s, e) => { sonuc = g.Text; f.DialogResult = DialogResult.OK; f.Close(); };
                g.Kutu.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; sonuc = g.Text; f.DialogResult = DialogResult.OK; f.Close(); } };
                f.Shown += (s, e) => g.Focus();
                return f.ShowDialog(sahip) == DialogResult.OK ? sonuc : null;
            }
        }

        public static Buton AltDugmeler(ModernForm f, string tamamMetni, ButonTuru tur = ButonTuru.Birincil, string ikon = "")
        {
            var tamam = new Buton(tamamMetni, ikon, tur) { Anchor = AnchorStyles.Bottom | AnchorStyles.Right };
            tamam.Width = Math.Max(tamam.Width, Tema.S(120));
            var vazgec = new Buton("Vazgeç", "", ButonTuru.Sade) { Anchor = AnchorStyles.Bottom | AnchorStyles.Right };
            tamam.Location = new Point(f.Icerik.Width - tamam.Width, f.Icerik.Height - tamam.Height);
            vazgec.Location = new Point(tamam.Left - vazgec.Width - Tema.S(8), tamam.Top);
            vazgec.Click += (s, e) => { f.DialogResult = DialogResult.Cancel; f.Close(); };
            f.Icerik.Controls.Add(tamam); f.Icerik.Controls.Add(vazgec);
            return tamam;
        }

        public static Label Baslik(Control kap, string metin, int y)
        {
            var l = Yazi.Etiket(metin, 9.5f, true, Renk.Yazi2); l.Location = new Point(0, y); kap.Controls.Add(l); return l;
        }
    }

    /// <summary>Yönetim ekranına giriş.</summary>
    class GirisFormu : ModernForm
    {
        public string Sifre => sifre.Text;
        readonly Giris sifre;

        public GirisFormu(Func<string, bool> dogrula) : base(null, 420, 400)
        {
            Text = "Sem Ajan"; ShowInTaskbar = true; StartPosition = FormStartPosition.CenterScreen; TopMost = false;
            int g = Icerik.Width;
            var logo = new PictureBox { Image = Tema.Logo(), SizeMode = PictureBoxSizeMode.Zoom, Size = Tema.S(104, 104), BackColor = Renk.Zemin };
            logo.Location = new Point((g - logo.Width) / 2, Tema.S(8));
            var b = Yazi.Etiket("Sem Ajan", 17f, true, Renk.Yazi, false); b.TextAlign = ContentAlignment.MiddleCenter; b.SetBounds(0, Tema.S(122), g, Tema.S(36));
            var a = Yazi.Etiket("Devam etmek için yönetici şifrenizi girin.", 10f, false, Renk.Yazi2, false); a.TextAlign = ContentAlignment.MiddleCenter; a.SetBounds(0, Tema.S(160), g, Tema.S(24));
            sifre = new Giris(true) { Location = new Point(0, Tema.S(212)), Width = g, Ipucu = "Yönetici şifresi" };
            var hata = Yazi.Etiket("", 9f, false, Renk.Pembe, false); hata.TextAlign = ContentAlignment.MiddleCenter; hata.SetBounds(0, Tema.S(258), g, Tema.S(22));
            var gir = new Buton("Giriş yap", Ik.Sag, ButonTuru.Birincil) { Location = new Point(0, Tema.S(290)), Width = g, Height = Tema.S(48) };
            Icerik.Controls.AddRange(new Control[] { logo, b, a, sifre, hata, gir });
            int deneme = 0;
            Action dene = () =>
            {
                if (sifre.Text == "") return;
                if (dogrula(sifre.Text)) { DialogResult = DialogResult.OK; Close(); return; }
                deneme++; hata.Text = deneme >= 3 ? "Çok fazla hatalı deneme." : "Şifre yanlış, tekrar deneyin.";
                sifre.Text = ""; sifre.Focus();
                if (deneme >= 3) { DialogResult = DialogResult.Cancel; Close(); }
            };
            gir.Click += (s, e) => dene();
            sifre.Kutu.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; dene(); } };
            Shown += (s, e) => { Activate(); sifre.Focus(); };
        }
    }

    class DuyuruYazForm : ModernForm
    {
        readonly Giris bas, metin;
        public string Baslik => bas.Text.Trim();
        public string Metin => metin.Text.Trim();

        public DuyuruYazForm(int kisi) : base("Duyuru gönder", 540, 520)
        {
            int g = Icerik.Width;
            var a = Yazi.Etiket(kisi + " bilgisayarın ekranının ortasında görünecek ve \"Tamam\"a basılana kadar kapanmayacak.", 10f, false, Renk.Yazi2, false);
            a.SetBounds(0, 0, g, Tema.S(40));
            Icerik.Controls.Add(a);
            Diyalog.Baslik(Icerik, "Başlık", Tema.S(48));
            bas = new Giris { Text = "Duyuru", Location = new Point(0, Tema.S(72)), Width = g };
            Diyalog.Baslik(Icerik, "Mesaj", Tema.S(128));
            metin = new Giris(false, true) { Location = new Point(0, Tema.S(152)), Width = g, Height = Tema.S(120) };
            Icerik.Controls.Add(bas); Icerik.Controls.Add(metin);

            Diyalog.Baslik(Icerik, "Hazır mesajlar", Tema.S(284));
            var hazir = new FlowLayoutPanel { Location = new Point(-Tema.S(4), Tema.S(304)), Size = new Size(g + Tema.S(8), Tema.S(44)), BackColor = Renk.Zemin };
            foreach (var (b, m) in new[] {
                ("Toplantı", "Öğretmenler odasında 10 dakika sonra kısa bir toplantı yapılacaktır."),
                ("Ders bitiyor", "Ders süresi bitmek üzere. Lütfen çalışmalarınızı kaydedin."),
                ("Mesai bitiyor", "Mesai bitiyor. Bilgisayarlar birazdan kapanacak, lütfen işlerinizi kaydedin."),
                ("Yemek", "Yemek hazır, afiyet olsun!") })
            {
                var btn = new Buton(b, "", ButonTuru.Ikincil) { Font = Tema.Yazi(9f), Height = Tema.S(34) };
                btn.BoyutAyarla();
                btn.Click += (s, e) => { bas.Text = b == "Yemek" ? "Duyuru" : b; metin.Text = m; metin.Focus(); };
                hazir.Controls.Add(btn);
            }
            Icerik.Controls.Add(hazir);

            var ok = Diyalog.AltDugmeler(this, "Gönder", ButonTuru.Birincil, Ik.Gonder);
            ok.Click += (s, e) => { if (Metin == "") { metin.Focus(); return; } DialogResult = DialogResult.OK; Close(); };
            Shown += (s, e) => metin.Focus();
        }
    }

    class KapanmaForm : ModernForm
    {
        readonly Anahtar otomatik;
        readonly Anahtar[] gun = new Anahtar[7];
        readonly Giris[] saat = new Giris[7];
        readonly Adim uyari, erteleDk, erteleHak;
        public Mesaj Sonuc;

        public KapanmaForm(Mesaj ornek, int kisi) : base("Kapanma saatleri", 560, 690)
        {
            int g = Icerik.Width;
            var a = Yazi.Etiket("Seçili " + kisi + " bilgisayara uygulanacak.", 10f, false, Renk.Yazi2); a.Location = new Point(0, 0);
            Icerik.Controls.Add(a);
            otomatik = new Anahtar("Otomatik kapanma", "Kapalıysa bu bilgisayarlar hiç otomatik kapanmaz (ör. yönetici bilgisayarı).") { Location = new Point(0, Tema.S(34)), Width = g, Acik = ornek["otomatik"] != "0" };
            Icerik.Controls.Add(otomatik);

            var kart = new KartPanel { Location = new Point(0, Tema.S(100)), Size = new Size(g, Tema.S(7 * 40 + 24)), BackColor = Renk.Zemin, Golge = false };
            Icerik.Controls.Add(kart);
            int y = Tema.S(10);
            foreach (int d in new[] { 1, 2, 3, 4, 5, 6, 0 })
            {
                var deger = ornek.Alan.ContainsKey("kapanma_" + AjanAyar.GunKod[d]) ? ornek["kapanma_" + AjanAyar.GunKod[d]] : (d == 6 ? "17:00" : "18:00");
                TimeSpan t; bool var_ = TimeSpan.TryParse(deger, out t);
                saat[d] = new Giris { Text = var_ ? t.ToString(@"hh\:mm") : (d == 6 ? "17:00" : "18:00"), Width = Tema.S(92), Location = new Point(g - Tema.S(110), y), Height = Tema.S(36), Enabled = var_ };
                saat[d].Kutu.TextAlign = HorizontalAlignment.Center; saat[d].Kutu.MaxLength = 5;
                gun[d] = new Anahtar(AjanAyar.GunAd[d], "") { Location = new Point(Tema.S(18), y + Tema.S(2)), Width = g - Tema.S(150), Acik = var_, BackColor = Renk.Yuzey };
                int dd = d;
                gun[d].Degisti += (s, e) => saat[dd].Enabled = gun[dd].Acik;
                kart.Controls.Add(gun[d]); kart.Controls.Add(saat[d]);
                y += Tema.S(40);
            }
            var not = Yazi.Etiket("Kapalı günlerde bilgisayar otomatik kapanmaz.", 9f, false, Renk.Yazi3); not.Location = new Point(0, kart.Bottom + Tema.S(6));
            Icerik.Controls.Add(not);

            y = kart.Bottom + Tema.S(34);
            uyari = Satir("Kaç dakika önce uyarı çıksın", ornek["uyari_dk"], 5, 1, 30, "dk", ref y);
            erteleDk = Satir("Ertele düğmesi kaç dakika ertelesin", ornek["ertele_dk"], 15, 1, 120, "dk", ref y);
            erteleHak = Satir("Günde en fazla kaç kez ertelenebilsin", ornek["ertele_hak"], 2, 0, 10, "kez", ref y);

            var ok = Diyalog.AltDugmeler(this, "Kaydet", ButonTuru.Birincil, Ik.Tik);
            ok.Click += (s, e) =>
            {
                var m = new Mesaj("AYAR");
                for (int d = 0; d < 7; d++)
                {
                    TimeSpan t;
                    if (gun[d].Acik && !TimeSpan.TryParse(saat[d].Text, out t)) { MesajKutusu.Bilgi(this, "Saat hatalı", AjanAyar.GunAd[d] + " için saati 18:00 biçiminde yazın.", true); return; }
                    m["kapanma_" + AjanAyar.GunKod[d]] = gun[d].Acik ? TimeSpan.Parse(saat[d].Text).ToString(@"hh\:mm") : "";
                }
                m["uyari_dk"] = uyari.Deger.ToString(); m["ertele_dk"] = erteleDk.Deger.ToString(); m["ertele_hak"] = erteleHak.Deger.ToString();
                m["otomatik"] = otomatik.Acik ? "1" : "0";
                Sonuc = m; DialogResult = DialogResult.OK; Close();
            };
        }

        Adim Satir(string etiket, string deger, int vars, int min, int max, string birim, ref int y)
        {
            int v; if (!int.TryParse(deger, out v)) v = vars;
            var l = Yazi.Etiket(etiket, 10f); l.Location = new Point(0, y + Tema.S(8));
            var a = new Adim(min, max, v, birim) { Location = new Point(Icerik.Width - Tema.S(140), y) };
            Icerik.Controls.Add(l); Icerik.Controls.Add(a);
            y += Tema.S(42);
            return a;
        }
    }

    class YaziciForm : ModernForm
    {
        /// <summary>Merkezdeki Canon i-SENSYS X 1643P'nin adresi.</summary>
        public const string VarsayilanIp = "192.168.1.50";
        readonly Giris klasor, ip, ad, surucu;
        public string Klasor => klasor.Text.Trim();
        public string Ip => ip.Text.Trim();
        public string Ad => ad.Text.Trim();
        public string Surucu => surucu.Text.Trim();

        public YaziciForm() : base("Yazıcı kur", 560, 560)
        {
            int g = Icerik.Width;
            var a = Yazi.Etiket("Canon'un sitesinden indirdiğiniz sürücü dosyasını çift tıklayıp bir klasöre açın, sonra o klasörü seçin (içinde .inf dosyası olan klasör).", 10f, false, Renk.Yazi2, false);
            a.SetBounds(0, 0, g, Tema.S(48)); Icerik.Controls.Add(a);

            Diyalog.Baslik(Icerik, "Sürücü klasörü", Tema.S(60));
            klasor = new Giris { Location = new Point(0, Tema.S(84)), Width = g - Tema.S(112), Ipucu = "Klasör seçin" };
            var sec = new Buton("Seç…", Ik.Klasor, ButonTuru.Ikincil) { Location = new Point(g - Tema.S(104), Tema.S(84)), Width = Tema.S(104), Height = Tema.S(42) };
            sec.Click += (s, e) => { using (var d = new FolderBrowserDialog { Description = "Sürücü klasörünü seçin" }) if (d.ShowDialog(this) == DialogResult.OK) klasor.Text = d.SelectedPath; };
            Diyalog.Baslik(Icerik, "Yazıcının IP adresi", Tema.S(140));
            ip = new Giris { Text = VarsayilanIp, Location = new Point(0, Tema.S(164)), Width = Tema.S(220), Ipucu = "ör. 192.168.1.50" };
            var ipNot = Yazi.Etiket("Yazıcının ekranında Menü → Ağ ayarları\nbölümünde yazar.", 9f, false, Renk.Yazi3, false); ipNot.SetBounds(Tema.S(236), Tema.S(166), g - Tema.S(236), Tema.S(40));
            Diyalog.Baslik(Icerik, "Bilgisayarlarda görünecek ad", Tema.S(220));
            ad = new Giris { Text = "Canon i-SENSYS X 1643P", Location = new Point(0, Tema.S(244)), Width = g };
            Diyalog.Baslik(Icerik, "Sürücü adı (isteğe bağlı)", Tema.S(300));
            surucu = new Giris { Location = new Point(0, Tema.S(324)), Width = g, Ipucu = "Boş bırakın, otomatik bulunur" };
            Icerik.Controls.AddRange(new Control[] { klasor, sec, ip, ipNot, ad, surucu });

            var ok = Diyalog.AltDugmeler(this, "Kur", ButonTuru.Birincil, Ik.Yazici);
            ok.Click += (s, e) =>
            {
                if (!Directory.Exists(Klasor) || Directory.GetFiles(Klasor, "*.inf", SearchOption.AllDirectories).Length == 0)
                { MesajKutusu.Bilgi(this, "Sürücü bulunamadı", "Seçilen klasörde .inf dosyası yok. Sürücüyü önce bir klasöre açmanız gerekiyor.", true); return; }
                System.Net.IPAddress x;
                if (!System.Net.IPAddress.TryParse(Ip, out x)) { MesajKutusu.Bilgi(this, "IP adresi hatalı", "Yazıcının IP adresini 192.168.1.50 biçiminde yazın.", true); return; }
                DialogResult = DialogResult.OK; Close();
            };
        }
    }

    class SifreDegistirForm : ModernForm
    {
        readonly Giris eski, yeni1, yeni2;
        public string YeniSifre => yeni1.Text;

        public SifreDegistirForm(byte[] mevcutAnahtar) : base("Şifre değiştir", 460, 470)
        {
            int g = Icerik.Width;
            var a = Yazi.Etiket("Tüm bilgisayarların şifresi birlikte değişir. O an kapalı olanlar açıldıklarında kendiliğinden yeni şifreye geçer.", 10f, false, Renk.Yazi2, false);
            a.SetBounds(0, 0, g, Tema.S(48)); Icerik.Controls.Add(a);
            Diyalog.Baslik(Icerik, "Şu anki şifre", Tema.S(60));
            eski = new Giris(true) { Location = new Point(0, Tema.S(84)), Width = g };
            Diyalog.Baslik(Icerik, "Yeni şifre", Tema.S(140));
            yeni1 = new Giris(true) { Location = new Point(0, Tema.S(164)), Width = g, Ipucu = "En az 4 karakter" };
            Diyalog.Baslik(Icerik, "Yeni şifre tekrar", Tema.S(220));
            yeni2 = new Giris(true) { Location = new Point(0, Tema.S(244)), Width = g };
            var hata = Yazi.Etiket("", 9f, false, Renk.Pembe); hata.Location = new Point(0, Tema.S(294));
            Icerik.Controls.AddRange(new Control[] { eski, yeni1, yeni2, hata });
            var ok = Diyalog.AltDugmeler(this, "Değiştir", ButonTuru.Birincil, Ik.Anahtar);
            ok.Click += (s, e) =>
            {
                if (!Sifreleme.Esit(Sifreleme.AnahtarUret(eski.Text), mevcutAnahtar)) { hata.Text = "Şu anki şifre yanlış."; return; }
                if (yeni1.Text.Length < 4) { hata.Text = "Yeni şifre en az 4 karakter olmalı."; return; }
                if (yeni1.Text != yeni2.Text) { hata.Text = "Yeni şifreler aynı değil."; return; }
                DialogResult = DialogResult.OK; Close();
            };
            Shown += (s, e) => eski.Focus();
        }
    }

    /// <summary>Uzaktaki bilgisayarın klasörlerinde gezip dosya indirmeye yarar.</summary>
    class DosyaTarayici : ModernForm
    {
        readonly Pc pc;
        readonly Label yol, durum;
        readonly DosyaListesi l;
        string mevcut = "";

        public DosyaTarayici(Pc pc) : base("Dosya al  ·  " + pc.Ad, 720, 560)
        {
            this.pc = pc;
            int g = Icerik.Width;
            var geri = new Buton("", Ik.Sol, ButonTuru.Ikincil) { Size = Tema.S(40, 40), Location = new Point(0, 0) };
            var bas = new Buton("", Ik.Ev, ButonTuru.Ikincil) { Size = Tema.S(40, 40), Location = new Point(Tema.S(48), 0) };
            yol = Yazi.Etiket("", 10f, false, Renk.Yazi2, false); yol.SetBounds(Tema.S(100), 0, g - Tema.S(100), Tema.S(40)); yol.TextAlign = ContentAlignment.MiddleLeft; yol.AutoEllipsis = true;
            var kart = new KartPanel { Location = new Point(0, Tema.S(52)), Size = new Size(g, Icerik.Height - Tema.S(112)), Padding = Tema.Pd(6, 8, 6, 12), BackColor = Renk.Zemin, Golge = false, Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right };
            l = new DosyaListesi { Dock = DockStyle.Fill };
            kart.Controls.Add(InceKaydirma.Sar(l));
            durum = Yazi.Etiket("", 9.5f, false, Renk.Yazi2, false); durum.SetBounds(0, Icerik.Height - Tema.S(40), g - Tema.S(200), Tema.S(40)); durum.TextAlign = ContentAlignment.MiddleLeft;
            var indir = new Buton("Seçilenleri indir", Ik.Indir, ButonTuru.Birincil);
            indir.Location = new Point(g - indir.Width, Icerik.Height - indir.Height);
            Icerik.Controls.AddRange(new Control[] { geri, bas, yol, kart, durum, indir });

            geri.Click += (s, e) => { var p = mevcut == "" ? null : Path.GetDirectoryName(mevcut.TrimEnd('\\')); Git(string.IsNullOrEmpty(p) || mevcut.TrimEnd('\\').Length <= 2 ? "" : p); };
            bas.Click += (s, e) => Git("");
            l.DoubleClick += (s, e) =>
            {
                if (!(l.SelectedItem is DosyaListesi.Oge o)) return;
                if (o.Klasor) Git(o.Yol); else Indir();
            };
            indir.Click += (s, e) => Indir();
            Shown += (s, e) => Git("");
        }

        void Git(string p)
        {
            durum.Text = "Yükleniyor…"; UseWaitCursor = true;
            Task.Run(() =>
            {
                try
                {
                    var m = new Mesaj("DOSYA_LISTE"); m["yol"] = p;
                    var c = Baglanti.Gonder(pc.Ip, m, 10000);
                    BeginInvoke((Action)(() =>
                    {
                        UseWaitCursor = false;
                        if (!c.Tamam) { durum.Text = c["aciklama"]; return; }
                        mevcut = p; yol.Text = p == "" ? "Kısayollar" : p;
                        l.BeginUpdate(); l.Items.Clear();
                        foreach (var s in c["liste"].Split('\n').Where(x => x.Length > 2))
                        {
                            var a = s.Split('|');
                            l.Items.Add(new DosyaListesi.Oge
                            {
                                Klasor = a[0] == "D", Yol = a[1],
                                Ad = p == "" ? KisaAd(a[1]) : Path.GetFileName(a[1]),
                                Boyut = a[0] == "F" && a.Length > 2 ? Boyut(long.Parse(a[2])) : "",
                                Tarih = a.Length > 3 ? a[3] : ""
                            });
                        }
                        l.EndUpdate();
                        durum.Text = l.Items.Count + " öğe";
                    }));
                }
                catch (Exception e) { BeginInvoke((Action)(() => { UseWaitCursor = false; durum.Text = "Hata: " + e.Message; })); }
            });
        }

        static string KisaAd(string yol)
        {
            var parca = yol.TrimEnd('\\').Split('\\');
            if (parca.Length <= 1) return yol + "  (disk)";
            if (yol.IndexOf("Public", StringComparison.OrdinalIgnoreCase) >= 0) return "Ortak masaüstü";
            var son = parca[parca.Length - 1];
            var tr = son == "Desktop" || son == "Masaüstü" ? "Masaüstü" : son == "Documents" ? "Belgeler" : son == "Downloads" ? "İndirilenler" : son;
            return tr + "  (" + parca[2] + ")";
        }

        void Indir()
        {
            var dosyalar = l.SelectedItems.Cast<DosyaListesi.Oge>().Where(o => !o.Klasor).Select(o => o.Yol).ToList();
            if (dosyalar.Count == 0) { durum.Text = "İndirmek için dosya seçin."; return; }
            string hedef;
            using (var d = new FolderBrowserDialog { Description = "İndirilen dosyalar nereye kaydedilsin?", SelectedPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop) })
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                hedef = d.SelectedPath;
            }
            durum.Text = "İndiriliyor…"; UseWaitCursor = true;
            Task.Run(() =>
            {
                int ok = 0; string hata = "";
                foreach (var f in dosyalar)
                {
                    try
                    {
                        var m = new Mesaj("DOSYA_AL"); m["yol"] = f;
                        var c = Baglanti.Gonder(pc.Ip, m, 180000);
                        if (!c.Tamam) { hata = c["aciklama"]; continue; }
                        var yer = Path.Combine(hedef, c["ad"]);
                        for (int i = 2; File.Exists(yer); i++) yer = Path.Combine(hedef, Path.GetFileNameWithoutExtension(c["ad"]) + " (" + i + ")" + Path.GetExtension(c["ad"]));
                        File.WriteAllBytes(yer, c.Veri); ok++;
                    }
                    catch (Exception e) { hata = e.Message; }
                }
                BeginInvoke((Action)(() => { UseWaitCursor = false; durum.Text = "✓ " + ok + " dosya indirildi" + (hata != "" ? "  ·  Hata: " + hata : "") + "  →  " + hedef; }));
            });
        }

        static string Boyut(long b) => b < 1024 ? b + " B" : b < 1048576 ? (b / 1024) + " KB" : (b / 1048576.0).ToString("0.0") + " MB";
    }

    class DosyaListesi : ListBox
    {
        public class Oge { public bool Klasor; public string Yol, Ad, Boyut, Tarih; public override string ToString() => Ad; }

        public DosyaListesi()
        {
            DrawMode = DrawMode.OwnerDrawFixed; ItemHeight = Tema.S(44); BorderStyle = BorderStyle.None;
            BackColor = Renk.Yuzey; IntegralHeight = false; SelectionMode = SelectionMode.MultiExtended;
        }

        protected override void OnDrawItem(DrawItemEventArgs e)
        {
            if (e.Index < 0) return;
            var o = (Oge)Items[e.Index];
            var g = e.Graphics; Tema.Kaliteli(g);
            bool sec = (e.State & DrawItemState.Selected) != 0;
            using (var b = new SolidBrush(Renk.Yuzey)) g.FillRectangle(b, e.Bounds);
            var r = new RectangleF(e.Bounds.X + Tema.S(4), e.Bounds.Y + Tema.S(2), e.Bounds.Width - Tema.S(8), e.Bounds.Height - Tema.S(4));
            if (sec) using (var p = Tema.Yuvarlak(r, Tema.S(8))) using (var b = new SolidBrush(Color.FromArgb(226, 243, 251))) g.FillPath(b, p);
            Tema.IkonCiz(g, o.Klasor ? Ik.Klasor : Ik.Belge, 13f, o.Klasor ? Renk.Turuncu : Renk.Mavi, new RectangleF(r.X + Tema.S(6), r.Y, Tema.S(32), r.Height));
            Tema.YaziCiz(g, o.Ad, Tema.Yazi(10f), Renk.Yazi, new RectangleF(r.X + Tema.S(46), r.Y, r.Width - Tema.S(280), r.Height));
            Tema.YaziCiz(g, o.Boyut, Tema.Yazi(9f), Renk.Yazi2, new RectangleF(r.Right - Tema.S(230), r.Y, Tema.S(80), r.Height), StringAlignment.Far);
            Tema.YaziCiz(g, o.Tarih, Tema.Yazi(9f), Renk.Yazi3, new RectangleF(r.Right - Tema.S(140), r.Y, Tema.S(130), r.Height), StringAlignment.Far);
        }
    }
}

namespace Sem.Yonetici
{
    /// <summary>GitHub'dan yeni sürüm denetimi ve tüm bilgisayarlara dağıtım.</summary>
    class GuncellemeForm : ModernForm
    {
        public byte[] Exe; public string YeniSurum; public bool DosyadanSec; public bool? OtoGuncelle;
        readonly Label durum, notlar;
        readonly Buton dagit;
        readonly Panel durumIkon;
        string ikon = Ik.Esitle; Color renk = Renk.Mavi;

        readonly Anahtar oto;
        readonly Buton dosya, kapat;

        public GuncellemeForm(bool otoAcik) : base("Güncellemeler", 460, 420)
        {
            int g = Icerik.Width;
            var kart = new KartPanel { Location = new Point(0, 0), Size = new Size(g, Tema.S(80)), BackColor = Renk.Zemin, Golge = false };
            durumIkon = new Panel { Size = Tema.S(40, 40), Location = Tema.P(16, 20), BackColor = Renk.Yuzey };
            durumIkon.Paint += (s, e) => { Tema.Kaliteli(e.Graphics); using (var b = new SolidBrush(Renk.Acik(renk, 30))) e.Graphics.FillEllipse(b, 0, 0, durumIkon.Width - 1, durumIkon.Height - 1); Tema.IkonCiz(e.Graphics, ikon, 13f, renk, new RectangleF(0, 0, durumIkon.Width, durumIkon.Height)); };
            durum = Yazi.Etiket("Yeni sürüm denetleniyor…", 11f, true); durum.BackColor = Renk.Yuzey; durum.Location = Tema.P(68, 18);
            var kurulu = Yazi.Etiket("Kurulu sürüm: " + Sabitler.Surum, 9f, false, Renk.Yazi2); kurulu.BackColor = Renk.Yuzey; kurulu.Location = Tema.P(69, 44);
            kart.Controls.AddRange(new Control[] { durumIkon, durum, kurulu });
            Icerik.Controls.Add(kart);

            notlar = Yazi.Etiket("", 9.5f, false, Renk.Yazi2, false);
            Icerik.Controls.Add(notlar);

            dagit = new Buton("İndir ve tüm bilgisayarlara dağıt", Ik.Indir, ButonTuru.Birincil) { Width = g, Height = Tema.S(42), Visible = false };
            dagit.Click += (s, e) => Dagit();
            Icerik.Controls.Add(dagit);

            oto = new Anahtar("Otomatik güncelleme", "Bilgisayarlar yeni sürümü kendileri denetleyip kurar.") { Width = g, Acik = otoAcik };
            oto.Degisti += (s, e) => OtoGuncelle = oto.Acik;
            Icerik.Controls.Add(oto);

            dosya = new Buton("Dosyadan güncelle…", Ik.Klasor, ButonTuru.Sade) { Font = Tema.Yazi(9.5f) };
            dosya.BoyutAyarla();
            dosya.Click += (s, e) => { DosyadanSec = true; DialogResult = DialogResult.OK; Close(); };
            kapat = new Buton("Kapat", "", ButonTuru.Ikincil);
            kapat.Width = Tema.S(100);
            kapat.Click += (s, e) => { DialogResult = OtoGuncelle != null ? DialogResult.OK : DialogResult.Cancel; Close(); };
            Icerik.Controls.Add(dosya); Icerik.Controls.Add(kapat);
            Diz();

            Shown += (s, e) => Denetle();
        }

        /// <summary>Görünen parçaları alt alta dizer, pencereyi içeriği kadar boyutlandırır.</summary>
        void Diz()
        {
            int g = Icerik.Width, y = Tema.S(92);
            int nh = 0;
            if (notlar.Text != "")
            {
                using (var gr = notlar.CreateGraphics()) nh = Math.Min(Tema.S(140), (int)Math.Ceiling(gr.MeasureString(notlar.Text, notlar.Font, g).Height) + Tema.S(2));
            }
            notlar.SetBounds(0, y, g, nh); notlar.Visible = nh > 0; if (nh > 0) y += nh + Tema.S(12);
            if (dagit.Visible) { dagit.Location = new Point(0, y); y += dagit.Height + Tema.S(16); }
            oto.Location = new Point(0, y); y += oto.Height + Tema.S(18);
            dosya.Location = new Point(0, y + (kapat.Height - dosya.Height) / 2);
            kapat.Location = new Point(g - kapat.Width, y);
            y += kapat.Height;
            foreach (Control c in Icerik.Controls) c.Anchor = AnchorStyles.Top | AnchorStyles.Left;
            ClientSize = new Size(ClientSize.Width, Icerik.Top + y + Tema.S(20));
        }

        void Durum(string metin, string ik, Color r) { durum.Text = metin; ikon = ik; renk = r; durumIkon.Invalidate(); }

        Guncelleme.Surum bulunan;

        void Denetle()
        {
            Task.Run(() =>
            {
                try
                {
                    var s = Guncelleme.SonSurum();
                    BeginInvoke((Action)(() =>
                    {
                        bulunan = s;
                        if (s.YeniMi)
                        {
                            Durum("Yeni sürüm hazır: " + s.Numara, Ik.Zil, Renk.Turuncu);
                            notlar.Text = string.IsNullOrWhiteSpace(s.Notlar) ? "Bu sürümle ilgili not yok." : s.Notlar.Trim();
                            dagit.Visible = true;
                        }
                        else { Durum("Sem Ajan güncel", Ik.TikDaire, Renk.Yesil); notlar.Text = "En son sürüm (" + s.Etiket + ") zaten kurulu."; }
                        Diz();
                    }));
                }
                catch (Exception e)
                {
                    var yok = e is WebException we && we.Response is HttpWebResponse r && r.StatusCode == HttpStatusCode.NotFound;
                    BeginInvoke((Action)(() =>
                    {
                        if (yok) { Durum("Henüz yayınlanmış sürüm yok", Ik.Bilgi, Renk.Mavi); notlar.Text = "Yeni bir sürüm yayınlanınca burada görünecek."; }
                        else { Durum("Denetlenemedi", Ik.Uyari, Renk.Pembe); notlar.Text = "İnternet bağlantısını kontrol edin. (" + e.Message + ")"; }
                        Diz();
                    }));
                }
            });
        }

        void Dagit()
        {
            dagit.Enabled = false; dagit.Text = "İndiriliyor…";
            Task.Run(() =>
            {
                try
                {
                    var exe = Guncelleme.Indir(bulunan); // özet ve sürüm burada doğrulanır
                    BeginInvoke((Action)(() => { Exe = exe; YeniSurum = bulunan.Numara.ToString(); DialogResult = DialogResult.OK; Close(); }));
                }
                catch (Exception e)
                {
                    BeginInvoke((Action)(() => { dagit.Enabled = true; dagit.Text = "Tekrar dene"; Durum("İndirilemedi", Ik.Uyari, Renk.Pembe); notlar.Text = e.Message; Diz(); }));
                }
            });
        }
    }
}

namespace Sem.Yonetici
{
    /// <summary>Yan yana hap şeklinde seçenekler (ör. grup seçimi).</summary>
    class HapSecici : Control
    {
        readonly (string Metin, Color Renk)[] secenekler;
        readonly List<RectangleF> alanlar = new List<RectangleF>();
        int secili, uzerinde = -1;
        public event EventHandler Degisti;
        public int Secili { get => secili; set { secili = value; Invalidate(); Degisti?.Invoke(this, EventArgs.Empty); } }

        public HapSecici(params (string, Color)[] secenekler)
        {
            this.secenekler = secenekler;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent; Height = Tema.S(36); Cursor = Cursors.Hand;
        }

        protected override void OnMouseMove(MouseEventArgs e) { int i = alanlar.FindIndex(a => a.Contains(e.Location)); if (i != uzerinde) { uzerinde = i; Invalidate(); } }
        protected override void OnMouseLeave(EventArgs e) { uzerinde = -1; Invalidate(); }
        protected override void OnMouseUp(MouseEventArgs e) { int i = alanlar.FindIndex(a => a.Contains(e.Location)); if (i >= 0) Secili = i; }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; Tema.Kaliteli(g);
            alanlar.Clear();
            float x = 0;
            using (var f = Tema.Yazi(9.5f))
                for (int i = 0; i < secenekler.Length; i++)
                {
                    var (metin, renk) = secenekler[i];
                    bool s = i == secili;
                    float w = g.MeasureString(metin, f).Width + Tema.S(renk == Color.Empty ? 24 : 40);
                    var r = new RectangleF(x, 0.5f, w, Height - 1.5f);
                    alanlar.Add(r);
                    using (var yol = Tema.Yuvarlak(r, r.Height / 2))
                    {
                        using (var b = new SolidBrush(s ? Renk.Acik(renk == Color.Empty ? Renk.Mavi : renk, 34) : i == uzerinde ? Renk.Hover : Renk.Yuzey)) g.FillPath(b, yol);
                        using (var pen = new Pen(s ? (renk == Color.Empty ? Renk.Mavi : renk) : Renk.CizgiKoyu, s ? Tema.S(1.5f) : 1)) g.DrawPath(pen, yol);
                    }
                    float tx = r.X + Tema.S(12);
                    if (renk != Color.Empty)
                    {
                        using (var b = new SolidBrush(renk)) g.FillEllipse(b, tx + Tema.S(2), r.Y + r.Height / 2 - Tema.S(4), Tema.S(8), Tema.S(8));
                        tx += Tema.S(16);
                    }
                    Tema.YaziCiz(g, metin, f, Renk.Yazi, new RectangleF(tx, r.Y, w, r.Height));
                    x += w + Tema.S(8);
                }
        }
    }

    /// <summary>Bilgisayara görünen ad, grup ve simge verme.</summary>
    class PcDuzenleForm : ModernForm
    {
        readonly Giris ad;
        readonly HapSecici grup;
        readonly List<Buton> simgeler = new List<Buton>();
        public string GorunenAd => ad.Text.Trim();
        public string Grup => Gruplar.Liste[grup.Secili].Kod;
        public string Simge { get; private set; }

        public PcDuzenleForm(Pc pc) : base("Bilgisayarı düzenle", 520, 470)
        {
            int g = Icerik.Width;
            Simge = pc.Simge;
            var a = Yazi.Etiket(pc.Ad + "  ·  " + pc.Ip, 9.5f, false, Renk.Yazi3); a.Location = new Point(0, 0);
            Diyalog.Baslik(Icerik, "Görünen ad", Tema.S(30));
            ad = new Giris { Text = pc.GorunenAd, Location = new Point(0, Tema.S(54)), Width = g, Ipucu = "ör. Duyu Bütünleme Odası" };
            Diyalog.Baslik(Icerik, "Grup", Tema.S(110));
            grup = new HapSecici(Gruplar.Liste.Select(x => (x.Ad, x.Renk)).ToArray()) { Location = new Point(0, Tema.S(134)), Width = g };
            grup.Secili = Gruplar.Sira(pc.Grup);
            Diyalog.Baslik(Icerik, "Simge", Tema.S(188));
            var akis = new FlowLayoutPanel { Location = new Point(-Tema.S(3), Tema.S(212)), Size = new Size(g + Tema.S(6), Tema.S(104)), BackColor = Renk.Zemin };
            foreach (var (ikon, ipucu) in Simgeler.Liste)
            {
                var b = new Buton("", ikon, ikon == pc.Ikon ? ButonTuru.Birincil : ButonTuru.Ikincil) { Size = Tema.S(46, 44), Margin = Tema.Pd(3, 3, 3, 3), Tag = ikon };
                new ToolTip().SetToolTip(b, ipucu);
                b.Click += (s, e) => { Simge = (string)b.Tag; foreach (var x in simgeler) { x.Tur = x == b ? ButonTuru.Birincil : ButonTuru.Ikincil; x.Invalidate(); } };
                simgeler.Add(b); akis.Controls.Add(b);
            }
            Icerik.Controls.AddRange(new Control[] { a, ad, grup, akis });
            var ok = Diyalog.AltDugmeler(this, "Kaydet", ButonTuru.Birincil, Ik.Tik);
            ok.Click += (s, e) => { DialogResult = DialogResult.OK; Close(); };
            ad.Kutu.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; DialogResult = DialogResult.OK; Close(); } };
            Shown += (s, e) => ad.Focus();
        }
    }

    /// <summary>Özellikleri aç/kapa ve sabah açılma saati.</summary>
    class AyarlarForm : ModernForm
    {
        public readonly YonetimAyar Ayar;
        public string SabahSaat = "", SabahGunler = "";
        public bool Paylasim, Telefon;
        readonly Anahtar onizleme, dosya, yazici, program, duvar, duyuru, sabah, paylasim, telefon;
        readonly Giris saat;
        readonly Buton[] gunler = new Buton[7];
        readonly Label sabahNot;

        public AyarlarForm(YonetimAyar mevcut, string sabahSaat, string sabahGunler, bool paylasimAcik = true, bool telefonAcik = true) : base("Ayarlar", 520, 900)
        {
            int g = Icerik.Width;
            Ayar = mevcut;
            var ust = Yazi.UstBaslik("Görünen özellikler", Renk.Yazi3); ust.Location = new Point(0, Tema.S(2));
            Icerik.Controls.Add(ust);
            int y = Tema.S(26);
            Func<string, bool, Anahtar> ekle = (metin, acik) =>
            {
                var a = new Anahtar(metin) { Location = new Point(0, y), Width = g, Acik = acik };
                Icerik.Controls.Add(a); y += Tema.S(36); return a;
            };
            onizleme = ekle("Kartlarda canlı ekran önizlemesi", mevcut.Onizleme);
            dosya = ekle("Dosya gönder ve al", mevcut.Dosya);
            yazici = ekle("Yazıcı kur ve onar", mevcut.Yazici);
            program = ekle("Program kur", mevcut.Program);
            duvar = ekle("Duvar kâğıdı", mevcut.Duvar);
            duyuru = ekle("Duyuru geçmişi (kim okudu)", mevcut.Duyurular);
            var not = Yazi.Etiket("Kapatılan özellikler menülerden kalkar.", 9f, false, Renk.Yazi3); not.Location = new Point(0, y + Tema.S(2));
            Icerik.Controls.Add(not);

            y += Tema.S(40);
            var ust2 = Yazi.UstBaslik("Sabah otomatik açılma", Renk.Yazi3); ust2.Location = new Point(0, y);
            Icerik.Controls.Add(ust2);
            y += Tema.S(24);
            sabah = new Anahtar("Bilgisayarlar sabah kendiliğinden açılsın", "") { Location = new Point(0, y), Width = g, Acik = mevcut.Sabah && sabahSaat != "" };
            Icerik.Controls.Add(sabah);
            y += Tema.S(44);
            saat = new Giris { Text = sabahSaat != "" ? sabahSaat : "08:30", Location = new Point(0, y), Size = new Size(Tema.S(86), Tema.S(38)) };
            saat.Kutu.TextAlign = HorizontalAlignment.Center; saat.Kutu.MaxLength = 5;
            Icerik.Controls.Add(saat);
            var secili = (sabahGunler == "" ? "2,3,4,5,6" : sabahGunler).Split(',');
            int gx = Tema.S(98), gw = (g - gx + Tema.S(4)) / 7 - Tema.S(4);
            var kisa = new[] { "Paz", "Pzt", "Sal", "Çar", "Per", "Cum", "Cmt" };
            foreach (int d in new[] { 1, 2, 3, 4, 5, 6, 0 })
            {
                var b = new Buton(kisa[d], "", secili.Contains(d.ToString()) ? ButonTuru.Birincil : ButonTuru.Ikincil)
                { Font = Tema.Yazi(9f), Size = new Size(gw, Tema.S(38)), Location = new Point(gx, y), Tag = d };
                b.Click += (s, e) => { b.Tur = b.Tur == ButonTuru.Birincil ? ButonTuru.Ikincil : ButonTuru.Birincil; b.Invalidate(); };
                gunler[d] = b; Icerik.Controls.Add(b);
                gx += b.Width + Tema.S(4);
            }
            y += Tema.S(48);
            sabahNot = Yazi.Etiket("Bu saatte açık olan Sem bilgisayarı (genelde yönetici bilgisayarı) diğerlerine ağdan açılma sinyali gönderir. " +
                "Yönetici bilgisayarı gece kapatılmamalı. Bilgisayarların BIOS ayarında \"Wake on LAN\" açık olmalı; denemek için kapalı bir bilgisayarda Diğer → Uyandır'ı kullanın.",
                9f, false, Renk.Yazi2, false);
            sabahNot.SetBounds(0, y, g, Tema.S(68));
            Icerik.Controls.Add(sabahNot);
            sabah.Degisti += (s, e) => SabahEtkin();
            SabahEtkin();

            y += Tema.S(80);
            var ust3 = Yazi.UstBaslik("Paylaşım", Renk.Yazi3); ust3.Location = new Point(0, y);
            Icerik.Controls.Add(ust3);
            y += Tema.S(24);
            paylasim = new Anahtar("Bilgisayarlar arası dosya paylaşımı", "") { Location = new Point(0, y), Width = g, Acik = paylasimAcik };
            Icerik.Controls.Add(paylasim); y += Tema.S(36);
            telefon = new Anahtar("Telefonlardan dosya kabul et (LocalSend)", "") { Location = new Point(0, y), Width = g, Acik = telefonAcik };
            Icerik.Controls.Add(telefon); y += Tema.S(40);
            var pnot = Yazi.Etiket("Telefondaki LocalSend uygulaması bilgisayarları adlarıyla görür. Bu bilgisayar telefonlarda \"Sem Yazıcı\" olarak görünür: gönderilen fotoğraf ve PDF'ler yazıcıdan çıkar.", 9f, false, Renk.Yazi2, false);
            pnot.SetBounds(0, y, g, Tema.S(52));
            Icerik.Controls.Add(pnot);
            paylasim.Degisti += (s, e) => { telefon.Enabled = paylasim.Acik; telefon.Invalidate(); };
            telefon.Enabled = paylasim.Acik;

            var ok = Diyalog.AltDugmeler(this, "Kaydet", ButonTuru.Birincil, Ik.Tik);
            ok.Click += (s, e) =>
            {
                TimeSpan t;
                if (sabah.Acik && (!TimeSpan.TryParse(saat.Text, out t) || t.TotalHours >= 24)) { MesajKutusu.Bilgi(this, "Saat hatalı", "Sabah açılma saatini 08:30 biçiminde yazın.", true); return; }
                var gs = gunler.Select((b, i) => (b, i)).Where(x => x.b.Tur == ButonTuru.Birincil).Select(x => x.i.ToString()).ToList();
                if (sabah.Acik && gs.Count == 0) { MesajKutusu.Bilgi(this, "Gün seçin", "Sabah açılma için en az bir gün seçin.", true); return; }
                Ayar.Onizleme = onizleme.Acik; Ayar.Dosya = dosya.Acik; Ayar.Yazici = yazici.Acik; Ayar.Program = program.Acik;
                Ayar.Duvar = duvar.Acik; Ayar.Duyurular = duyuru.Acik; Ayar.Sabah = sabah.Acik;
                SabahSaat = sabah.Acik ? TimeSpan.Parse(saat.Text).ToString(@"hh\:mm") : "";
                SabahGunler = string.Join(",", gs);
                Paylasim = paylasim.Acik; Telefon = telefon.Acik;
                DialogResult = DialogResult.OK; Close();
            };
        }

        void SabahEtkin()
        {
            saat.Enabled = sabah.Acik; saat.Invalidate();
            foreach (var b in gunler) b.Enabled = sabah.Acik;
            sabahNot.ForeColor = sabah.Acik ? Renk.Yazi2 : Renk.Yazi3;
        }
    }

    /// <summary>Bir bilgisayarın ekranını büyük ve birkaç saniyede bir yenilenerek gösterir.</summary>
    class EkranGorForm : ModernForm
    {
        readonly Pc pc;
        readonly PictureBox resim;
        readonly Label durum;
        readonly Timer zaman;
        volatile bool istekte;
        Image son;

        public EkranGorForm(Pc pc) : base(pc.Baslik + "  ·  ekran", 960, 640)
        {
            this.pc = pc; SabitBoyut = true;
            var alan = Screen.PrimaryScreen.WorkingArea;
            int w = Math.Min(Tema.S(1100), (int)(alan.Width * 0.8)), h = (int)((w - Tema.S(48)) * 9 / 16.0) + Tema.S(64 + 20 + 56);
            if (h > alan.Height * 0.92) { h = (int)(alan.Height * 0.92); }
            ClientSize = new Size(w, h);
            Icerik.Size = new Size(ClientSize.Width - Tema.S(48), ClientSize.Height - Icerik.Top - Tema.S(20));
            int g = Icerik.Width;
            var kart = new KartPanel { Location = new Point(0, 0), Size = new Size(g, Icerik.Height - Tema.S(56)), BackColor = Renk.Zemin, Golge = false, Dolgu = Color.FromArgb(28, 30, 34), Padding = Tema.Pd(1, 1, 1, 2) };
            resim = new PictureBox { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.FromArgb(28, 30, 34) };
            kart.Controls.Add(resim);
            durum = Yazi.Etiket("Görüntü alınıyor…", 9.5f, false, Renk.Yazi2, false);
            durum.SetBounds(0, Icerik.Height - Tema.S(42), g - Tema.S(260), Tema.S(42)); durum.TextAlign = ContentAlignment.MiddleLeft;
            var kaydet = new Buton("Kaydet", Ik.Indir, ButonTuru.Ikincil);
            var kapat = new Buton("Kapat", "", ButonTuru.Birincil) { Width = Tema.S(110) };
            kapat.Location = new Point(g - kapat.Width, Icerik.Height - kapat.Height);
            kaydet.Location = new Point(kapat.Left - kaydet.Width - Tema.S(8), kapat.Top);
            kapat.Click += (s, e) => Close();
            kaydet.Click += (s, e) => Kaydet();
            Icerik.Controls.AddRange(new Control[] { kart, durum, kaydet, kapat });
            if (pc.Onizleme != null) resim.Image = (Image)pc.Onizleme.Clone();

            zaman = new Timer { Interval = 2500 };
            zaman.Tick += (s, e) => Iste();
            Shown += (s, e) => { Iste(); zaman.Start(); };
            FormClosed += (s, e) => { zaman.Dispose(); son?.Dispose(); };
        }

        void Iste()
        {
            if (istekte) return;
            istekte = true;
            Task.Run(() =>
            {
                Image img = null; string hata = null;
                try
                {
                    var m = new Mesaj("EKRAN_RESMI"); m["genislik"] = "1280";
                    var c = Baglanti.Gonder(pc.Ip, m, 10000);
                    if (c.Tamam) using (var ms = new MemoryStream(c.Veri)) img = new Bitmap(Image.FromStream(ms)); else hata = c["aciklama"];
                }
                catch (Exception e) { hata = e.Message; }
                istekte = false;
                if (IsDisposed) { img?.Dispose(); return; }
                try
                {
                    BeginInvoke((Action)(() =>
                    {
                        if (img != null) { var eski = son; son = img; resim.Image = img; eski?.Dispose(); durum.Text = "Canlı  ·  " + DateTime.Now.ToString("HH:mm:ss"); durum.ForeColor = Renk.YesilKoyu; }
                        else { durum.Text = hata; durum.ForeColor = Renk.Pembe; }
                    }));
                }
                catch { img?.Dispose(); }
            });
        }

        void Kaydet()
        {
            if (resim.Image == null) return;
            using (var d = new SaveFileDialog { Filter = "PNG resmi|*.png", FileName = pc.Ad + " " + DateTime.Now.ToString("yyyy-MM-dd HH.mm") + ".png", InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Desktop) })
                if (d.ShowDialog(this) == DialogResult.OK) resim.Image.Save(d.FileName, System.Drawing.Imaging.ImageFormat.Png);
        }
    }

    /// <summary>Bir duyurunun ayrıntısı: kimlere ulaştı, kim ne zaman okudu.</summary>
    class DuyuruDetayForm : ModernForm
    {
        public bool TekrarGonder;

        public DuyuruDetayForm(DuyuruKaydi k, Func<string, string> gorunenAd) : base(k.Baslik == "" ? "Duyuru" : k.Baslik, 500, 560)
        {
            int g = Icerik.Width;
            var zaman = Yazi.Etiket(k.Zaman.ToString("dd.MM.yyyy HH:mm"), 9f, false, Renk.Yazi3); zaman.Location = new Point(0, 0);
            var metin = Yazi.Etiket(k.Metin, 10f, false, Renk.Yazi, false);
            int mh;
            using (var gr = metin.CreateGraphics()) mh = Math.Min(Tema.S(120), (int)Math.Ceiling(gr.MeasureString(k.Metin, metin.Font, g).Height) + Tema.S(4));
            metin.SetBounds(0, Tema.S(24), g, mh);
            Icerik.Controls.Add(zaman); Icerik.Controls.Add(metin);
            int y = Tema.S(24) + mh + Tema.S(16);
            var ust = Yazi.UstBaslik("Okuma durumu", Renk.Yazi3); ust.Location = new Point(0, y); Icerik.Controls.Add(ust);
            y += Tema.S(24);
            var satirlar = k.Ulasan.OrderBy(a => k.Okuyan.ContainsKey(a) ? 0 : 1).ThenBy(gorunenAd).ToList();
            var kart = new KartPanel { Location = new Point(0, y), Size = new Size(g, Math.Max(1, satirlar.Count) * Tema.S(40) + Tema.S(12)), BackColor = Renk.Zemin, Golge = false };
            int sy = Tema.S(6);
            if (satirlar.Count == 0)
            {
                var l = Yazi.Etiket("Duyuru hiçbir bilgisayara ulaşmadı.", 9.5f, false, Renk.Yazi2); l.BackColor = Renk.Yuzey; l.Location = new Point(Tema.S(16), Tema.S(16)); kart.Controls.Add(l);
            }
            foreach (var a in satirlar)
            {
                DateTime z; bool okudu = k.Okuyan.TryGetValue(a, out z);
                var satir = new Panel { Location = new Point(Tema.S(8), sy), Size = new Size(g - Tema.S(16), Tema.S(40)), BackColor = Renk.Yuzey };
                string ad = gorunenAd(a);
                satir.Paint += (s, e) =>
                {
                    var gg = e.Graphics; Tema.Kaliteli(gg);
                    Tema.IkonCiz(gg, okudu ? Ik.TikDaire : Ik.Saat, 11f, okudu ? Renk.Yesil : Renk.Yazi3, new RectangleF(0, 0, Tema.S(32), satir.Height));
                    Tema.YaziCiz(gg, ad, Tema.Yazi(10f), Renk.Yazi, new RectangleF(Tema.S(36), 0, satir.Width - Tema.S(200), satir.Height));
                    Tema.YaziCiz(gg, okudu ? "Okudu  ·  " + (z.Date == k.Zaman.Date ? z.ToString("HH:mm") : z.ToString("dd.MM HH:mm")) : "Henüz okumadı", Tema.Yazi(9f), okudu ? Renk.YesilKoyu : Renk.Yazi3,
                        new RectangleF(satir.Width - Tema.S(170), 0, Tema.S(160), satir.Height), StringAlignment.Far);
                };
                kart.Controls.Add(satir); sy += Tema.S(40);
            }
            Icerik.Controls.Add(kart);
            y += kart.Height + Tema.S(20);

            bool okumayanVar = k.Ulasan.Any(a => !k.Okuyan.ContainsKey(a));
            var kapat = new Buton("Kapat", "", ButonTuru.Birincil) { Width = Tema.S(110) };
            kapat.Location = new Point(g - kapat.Width, y);
            kapat.Click += (s, e) => Close();
            Icerik.Controls.Add(kapat);
            if (okumayanVar)
            {
                var tekrar = new Buton("Okumayanlara tekrar gönder", Ik.Gonder, ButonTuru.Sade) { Font = Tema.Yazi(9.5f) };
                tekrar.BoyutAyarla(); tekrar.Location = new Point(0, y + (kapat.Height - tekrar.Height) / 2);
                tekrar.Click += (s, e) => { TekrarGonder = true; DialogResult = DialogResult.OK; Close(); };
                Icerik.Controls.Add(tekrar);
            }
            foreach (Control c in Icerik.Controls) c.Anchor = AnchorStyles.Top | AnchorStyles.Left;
            SabitBoyut = true;
            ClientSize = new Size(ClientSize.Width, Icerik.Top + y + kapat.Height + Tema.S(22));
        }
    }

    /// <summary>Saatin yanındaki simgeye tıklanınca açılan küçük hızlı panel.</summary>
    class TepsiPanel : Form
    {
        public TepsiPanel(int acik, int toplam, string kapanma, Action duyuru, Action bugunKapanmasin, Action kilitle, Action paneliAc)
        {
            FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; StartPosition = FormStartPosition.Manual; TopMost = true;
            BackColor = Renk.Zemin; DoubleBuffered = true; KeyPreview = true; Font = Tema.Yazi(10f);
            ClientSize = Tema.S(320, 356);
            Deactivate += (s, e) => Close();
            KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) Close(); };
            int g = ClientSize.Width - Tema.S(36), x0 = Tema.S(18);

            var logo = new PictureBox { Image = Tema.Logo(), SizeMode = PictureBoxSizeMode.Zoom, Size = Tema.S(34, 34), Location = new Point(x0, Tema.S(16)), BackColor = Renk.Zemin };
            var ad = Yazi.Etiket("Sem Ajan", 12f, true); ad.Location = new Point(x0 + Tema.S(44), Tema.S(14));
            var ad2 = Yazi.Etiket(DateTime.Now.ToString("d MMMM dddd", new System.Globalization.CultureInfo("tr-TR")), 8.5f, false, Renk.Yazi2); ad2.Location = new Point(x0 + Tema.S(45), Tema.S(36));
            Controls.AddRange(new Control[] { logo, ad, ad2 });

            var ozet = new KartPanel { Location = new Point(x0, Tema.S(66)), Size = new Size(g, Tema.S(76)), BackColor = Renk.Zemin, Golge = false };
            ozet.Paint += (s, e) =>
            {
                var gg = e.Graphics; Tema.Kaliteli(gg);
                float y = Tema.S(10), yarim = ozet.Width / 2f;
                Tema.YaziCiz(gg, acik + " / " + toplam, Tema.YaziKalin(17f), Renk.Yazi, new RectangleF(Tema.S(16), y, yarim, Tema.S(32)));
                Tema.YaziCiz(gg, "bilgisayar açık", Tema.Yazi(8.5f), Renk.Yazi2, new RectangleF(Tema.S(17), y + Tema.S(32), yarim, Tema.S(20)));
                Tema.YaziCiz(gg, kapanma, Tema.YaziKalin(17f), Renk.Yazi, new RectangleF(yarim + Tema.S(8), y, yarim - Tema.S(16), Tema.S(32)));
                Tema.YaziCiz(gg, "bugünkü kapanma", Tema.Yazi(8.5f), Renk.Yazi2, new RectangleF(yarim + Tema.S(9), y + Tema.S(32), yarim, Tema.S(20)));
                using (var pen = new Pen(Renk.Cizgi)) gg.DrawLine(pen, yarim - Tema.S(4), Tema.S(14), yarim - Tema.S(4), ozet.Height - Tema.S(16));
            };
            Controls.Add(ozet);

            int y0 = Tema.S(156);
            Func<string, string, ButonTuru, Buton> dugme = (metin, ikon, tur) =>
            {
                var b = new Buton(metin, ikon, tur) { Location = new Point(x0, y0), Width = g, Height = Tema.S(40) };
                Controls.Add(b); y0 += Tema.S(46); return b;
            };
            var d1 = dugme("Duyuru gönder", Ik.Megafon, ButonTuru.Ikincil);
            var d2 = dugme("Bugün kapanmasın", Ik.Duraklat, ButonTuru.Ikincil);
            var d3 = dugme("Hepsini kilitle  (basılı tutun)", Ik.Kilit, ButonTuru.Ikincil);
            d3.BasiliTut = true; d3.ForeColorOzel = Renk.Pembe;
            var d4 = dugme("Paneli aç", Ik.Ac, ButonTuru.Birincil);
            d1.Click += (s, e) => { Close(); duyuru(); };
            d2.Click += (s, e) => { Close(); bugunKapanmasin(); };
            d3.Onaylandi += (s, e) => { Close(); kilitle(); };
            d4.Click += (s, e) => { Close(); paneliAc(); };
            ClientSize = new Size(ClientSize.Width, y0 + Tema.S(10));

            var alan = Screen.PrimaryScreen.WorkingArea;
            Location = new Point(alan.Right - Width - Tema.S(12), alan.Bottom - Height - Tema.S(12));
        }

        protected override CreateParams CreateParams { get { var c = base.CreateParams; c.ClassStyle |= 0x20000; return c; } }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            if (!Tema.KoseleriYuvarla(Handle)) using (var p = Tema.Yuvarlak(new RectangleF(0, 0, Width, Height), Tema.S(12))) Region = new Region(p);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Tema.Kaliteli(e.Graphics);
            using (var pen = new Pen(Renk.CizgiKoyu)) using (var p = Tema.Yuvarlak(new RectangleF(0, 0, Width - 1, Height - 1), Tema.S(12))) e.Graphics.DrawPath(pen, p);
        }

        protected override void OnShown(EventArgs e) { base.OnShown(e); Activate(); }
    }
}
