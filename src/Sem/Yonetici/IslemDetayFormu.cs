// Bir bakım işleminin ayrıntılı raporu: SFC'nin onardığı dosyalar, bulunan virüsler, disk taraması özeti.
// Ajan "# Başlık" / "- madde" / düz satır biçiminde metin döndürür; burada okunur ve yazdırılabilir.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Sem.Arayuz;
using Sem.Ortak;

namespace Sem.Yonetici
{
    class IslemDetayForm : ModernForm
    {
        readonly SatirListesi liste = new SatirListesi();
        readonly Label durum;
        readonly Buton yazdir;
        readonly string pcAd, baslik, tarih;
        string metin = "";

        public IslemDetayForm(Pc pc, IslemKaydi x, string baslik) : base("Ayrıntılar", 680, 560)
        {
            SabitBoyut = true; this.baslik = baslik; pcAd = pc.Baslik;
            tarih = x.Bas.ToString("d MMMM yyyy HH:mm", new System.Globalization.CultureInfo("tr-TR"));
            int g = Icerik.Width;
            var t = Yazi.Etiket(baslik + " · " + pcAd, 10.5f, true, Renk.Yazi, false); t.SetBounds(0, 0, g - Tema.S(130), Tema.S(24)); t.AutoEllipsis = true;
            var a = Yazi.Etiket(tarih + (x.Bit > x.Bas ? " · " + IslemKaydi.Sure(x.Bit - x.Bas) + " sürdü" : ""), 9f, false, Renk.Yazi2, false); a.SetBounds(0, Tema.S(24), g - Tema.S(130), Tema.S(20));
            yazdir = new Buton("Yazdır", Ik.Belge, ButonTuru.Ikincil) { Enabled = false }; yazdir.Location = new Point(g - yazdir.Width, 0); yazdir.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            liste.Kutu.SetBounds(0, Tema.S(54), g, Icerik.Height - Tema.S(54) - Tema.S(30));
            liste.Kutu.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            durum = Yazi.Etiket("Yükleniyor…", 9f, false, Renk.Yazi2, false); durum.SetBounds(0, Icerik.Height - Tema.S(26), g, Tema.S(24)); durum.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            Icerik.Controls.AddRange(new Control[] { t, a, yazdir, liste.Kutu, durum });
            yazdir.Click += (s, e) => Yazdir();
            Shown += (s, e) => Task.Run(() =>
            {
                try
                {
                    var r = Baglanti.Gonder(pc.Ip, new Mesaj("ISLEM_DETAY") { ["id"] = x.Id }, 60000);
                    if (!r.Tamam) { Ui(() => durum.Text = r["aciklama"].Contains("Bilinmeyen") ? "Bu bilgisayardaki Sem güncellenmeli." : r["aciklama"]); return; }
                    var m = Encoding.UTF8.GetString(r.Veri ?? new byte[0]);
                    Ui(() => { metin = m; Ciz(); durum.Text = "Yazdır ile tarayıcıda açılır; oradan yazdırabilir ya da PDF kaydedebilirsiniz."; yazdir.Enabled = true; });
                }
                catch (Exception ex) { Ui(() => durum.Text = "Bilgisayara ulaşılamadı: " + ex.Message); }
            });
        }

        void Ui(Action a) { try { if (!IsDisposed) BeginInvoke(a); } catch { } }

        void Ciz()
        {
            liste.Temizle();
            int w = liste.Genislik;
            using (var gr = CreateGraphics())
                foreach (var ham in metin.Replace("\r", "").Split('\n'))
                {
                    var s = ham.TrimEnd();
                    if (s.StartsWith("# ")) { liste.Baslik(s.Substring(2)); continue; }
                    if (s == "") { liste.Bosluk(4); continue; }
                    bool madde = s.StartsWith("- ");
                    var yazi = madde ? "•  " + s.Substring(2) : s;
                    var l = Yazi.Etiket(yazi, 9.5f, false, Renk.Yazi, false);
                    int girinti = madde ? Tema.S(6) : 0;
                    int h = (int)Math.Ceiling(gr.MeasureString(yazi, l.Font, w - girinti).Height) + Tema.S(2);
                    l.Size = new Size(w - girinti, h); l.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
                    var kap = new Panel { Size = new Size(w, h + Tema.S(3)), BackColor = Renk.Zemin, Anchor = l.Anchor };
                    l.Location = new Point(girinti, 0); kap.Controls.Add(l);
                    liste.EklePx(kap);
                }
            liste.Bitir();
        }

        void Yazdir()
        {
            try
            {
                var sb = new StringBuilder();
                sb.Append("<!doctype html><html lang=tr><head><meta charset=utf-8><title>").Append(WebUtility.HtmlEncode(baslik + " - " + pcAd)).Append("</title><style>")
                  .Append("body{font-family:'Segoe UI',sans-serif;max-width:760px;margin:32px auto;color:#1d2330;padding:0 16px}h1{font-size:20px;margin:0}")
                  .Append(".alt{color:#667;margin:4px 0 20px}h2{font-size:13px;text-transform:uppercase;letter-spacing:.06em;color:#556;border-bottom:1px solid #dde;padding-bottom:4px;margin-top:24px}")
                  .Append("p{margin:4px 0;line-height:1.45}li{margin:3px 0;line-height:1.45}@media print{body{margin:0 auto}}</style></head><body>");
                sb.Append("<h1>").Append(WebUtility.HtmlEncode(baslik)).Append("</h1><div class=alt>").Append(WebUtility.HtmlEncode(pcAd + " · " + tarih)).Append("</div>");
                bool ul = false;
                foreach (var ham in metin.Replace("\r", "").Split('\n'))
                {
                    var s = ham.TrimEnd();
                    bool madde = s.StartsWith("- ");
                    if (ul && !madde) { sb.Append("</ul>"); ul = false; }
                    if (s.StartsWith("# ")) sb.Append("<h2>").Append(WebUtility.HtmlEncode(s.Substring(2))).Append("</h2>");
                    else if (madde) { if (!ul) { sb.Append("<ul>"); ul = true; } sb.Append("<li>").Append(WebUtility.HtmlEncode(s.Substring(2))).Append("</li>"); }
                    else if (s != "") sb.Append("<p>").Append(WebUtility.HtmlEncode(s)).Append("</p>");
                }
                if (ul) sb.Append("</ul>");
                sb.Append("<script>window.onload=function(){window.print()}</script></body></html>");
                var yol = Path.Combine(Path.GetTempPath(), "Sem-rapor-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".html");
                File.WriteAllText(yol, sb.ToString(), new UTF8Encoding(true));
                Process.Start(new ProcessStartInfo(yol) { UseShellExecute = true });
            }
            catch (Exception ex) { durum.Text = "Açılamadı: " + ex.Message; }
        }
    }
}
