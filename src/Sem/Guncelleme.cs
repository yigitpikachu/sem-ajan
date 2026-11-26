// Online güncelleme: GitHub'daki son sürümü bulur, indirir ve doğrular.
// Yalnızca GitHub Actions'ın depodaki koddan derleyip yüklediği Sem.exe kabul edilir; indirilen dosyanın
// SHA-256 özeti GitHub'ın bildirdiği özetle, ürün adı ve sürüm numarası da sürüm etiketiyle tutmalıdır.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

namespace Sem.Ortak
{
    public static class Guncelleme
    {
        public const string Depo = "yigitpikachu/sem-ajan";

        public class Surum
        {
            public string Etiket = "", Notlar = "", ExeAdres = "", Ozet = "";
            public Version Numara;
            public bool YeniMi => Numara != null && Numara > Version.Parse(Sabitler.Surum);
        }

        static WebClient Istemci()
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            var w = new WebClient();
            w.Headers[HttpRequestHeader.UserAgent] = "Sem-Guncelleme/" + Sabitler.Surum;
            w.Encoding = Encoding.UTF8;
            return w;
        }

        /// <summary>GitHub'daki en son yayınlanmış sürümü döndürür.</summary>
        public static Surum SonSurum()
        {
            using (var w = Istemci())
            {
                w.Headers[HttpRequestHeader.Accept] = "application/vnd.github+json";
                var json = w.DownloadString("https://api.github.com/repos/" + Depo + "/releases/latest");
                var d = (Dictionary<string, object>)new JavaScriptSerializer().DeserializeObject(json);
                var s = new Surum { Etiket = d["tag_name"] as string ?? "", Notlar = d.ContainsKey("body") ? d["body"] as string ?? "" : "" };
                Version v;
                if (Version.TryParse(s.Etiket.TrimStart('v', 'V'), out v)) s.Numara = v;
                foreach (Dictionary<string, object> a in (object[])d["assets"])
                {
                    if (a["name"] as string != "Sem.exe") continue;
                    var yukleyen = a.ContainsKey("uploader") ? a["uploader"] as Dictionary<string, object> : null;
                    if (yukleyen == null || yukleyen["login"] as string != "github-actions[bot]") continue; // elle yüklenen dosya kabul edilmez
                    s.ExeAdres = a["browser_download_url"] as string ?? "";
                    var oz = a.ContainsKey("digest") ? a["digest"] as string ?? "" : "";
                    if (oz.StartsWith("sha256:")) s.Ozet = oz.Substring(7).ToLowerInvariant();
                }
                if (s.ExeAdres == "" || s.Numara == null) throw new Exception("Sürüm dosyaları eksik");
                return s;
            }
        }

        /// <summary>Sürümü indirir; doğrulanamazsa hata verir.</summary>
        public static byte[] Indir(Surum s)
        {
            byte[] exe;
            using (var w = Istemci()) exe = w.DownloadData(s.ExeAdres);
            if (s.Ozet != "")
            {
                string oz;
                using (var h = SHA256.Create()) oz = BitConverter.ToString(h.ComputeHash(exe)).Replace("-", "").ToLowerInvariant();
                if (oz != s.Ozet) throw new CryptographicException("İndirilen dosya bozuk, güncelleme yapılmadı");
            }
            var tmp = Path.Combine(Gecici(), "sem_surum_" + Guid.NewGuid().ToString("N") + ".exe");
            try
            {
                File.WriteAllBytes(tmp, exe);
                var b = FileVersionInfo.GetVersionInfo(tmp);
                Version v;
                if (b.ProductName != "Sem Ajan" || !Version.TryParse(b.FileVersion, out v) || v.Major != s.Numara.Major || v.Minor != s.Numara.Minor || Math.Max(v.Build, 0) != Math.Max(s.Numara.Build, 0))
                    throw new CryptographicException("İndirilen dosya Sem Ajan'a ait değil, güncelleme yapılmadı");
            }
            finally { try { File.Delete(tmp); } catch { } }
            return exe;
        }

        /// <summary>Geçici dosyalar Defender istisnası olan ProgramData\Sem altında; yazılamıyorsa sistemin geçici klasöründe.</summary>
        static string Gecici()
        {
            try
            {
                var k = Path.Combine(Sem.Ajan.AjanAyar.Klasor, "gecici");
                Directory.CreateDirectory(k);
                var t = Path.Combine(k, Guid.NewGuid().ToString("N"));
                File.WriteAllText(t, ""); File.Delete(t);
                return k;
            }
            catch { return Path.GetTempPath(); }
        }

        /// <summary>Exe içindeki sürüm numarası.</summary>
        public static string DosyaSurumu(byte[] exe)
        {
            var tmp = Path.Combine(Gecici(), "sem_surum_" + Guid.NewGuid().ToString("N") + ".exe");
            try { File.WriteAllBytes(tmp, exe); return FileVersionInfo.GetVersionInfo(tmp).ProductVersion; }
            finally { try { File.Delete(tmp); } catch { } }
        }
    }
}
