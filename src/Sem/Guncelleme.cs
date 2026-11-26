// Online güncelleme: GitHub'daki son sürümü bulur, indirir ve imzasını doğrular.
// Sürümler GitHub Actions tarafından gizli bir anahtarla imzalanır; imzası tutmayan dosya asla kurulmaz.
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
        const string AcikAnahtar = "<RSAKeyValue><Modulus>vHCBRECiuxbDP4O0es31nK2UmQTOGtgFj3om650AFCPUsZEFTtG/4wUbnsLyq1bau1YoYRqv/pRUv2Plbdddjx5e7E6qG++5lpS0gEcPLIze8v+8rN5jvk60kDddCJOhsKR6+argV63sJzldRqjHbeN6tlOAof2dmcXr9Cyy6dzvs3HSEkjN27zLiIVQcMxDxVDLTyc188rOUl5Bw27Ci67tFXC7+E3LjY8Ij1wrsqaSDIOkJXkBjxA1b/qlh1w6Qf/BZfeVmBN+DzOrDr4aM0ARad+CndKS/kLwE22YG0Xjx5ytxjaNmz3x0IutahCQ/jpKMyZGHztHPjnb0oE/+sL1avKBb0ylzOjZ5e4DtvkC6S4Y2DyKWlYADZJ+t9qt4IuPaH7Y8V4dmboP5LC921fBpMQdpS7uhKcko37BgN4/dRarEkVcHuY9O9Zm4dBTT1AupITIKIWv3M3M40Dv2orx7P+jB3fLJAmePJyINQd5HPqJr0OlAaIU0fVdNGkH</Modulus><Exponent>AQAB</Exponent></RSAKeyValue>";

        public class Surum
        {
            public string Etiket = "", Notlar = "", ExeAdres = "", ImzaAdres = "";
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
                    var ad = a["name"] as string; var url = a["browser_download_url"] as string;
                    if (ad == "Sem.exe") s.ExeAdres = url;
                    else if (ad == "Sem.exe.sig") s.ImzaAdres = url;
                }
                if (s.ExeAdres == "" || s.ImzaAdres == "") throw new Exception("Sürüm dosyaları eksik");
                return s;
            }
        }

        /// <summary>Sürümü indirir; imza doğru değilse hata verir.</summary>
        public static byte[] Indir(Surum s)
        {
            using (var w = Istemci())
            {
                var exe = w.DownloadData(s.ExeAdres);
                var imza = Convert.FromBase64String(Encoding.ASCII.GetString(w.DownloadData(s.ImzaAdres)).Trim());
                if (!ImzaDogruMu(exe, imza)) throw new CryptographicException("İndirilen dosyanın imzası geçersiz, güncelleme yapılmadı");
                return exe;
            }
        }

        public static bool ImzaDogruMu(byte[] veri, byte[] imza)
        {
            using (var rsa = new RSACryptoServiceProvider())
            {
                rsa.PersistKeyInCsp = false;
                rsa.FromXmlString(AcikAnahtar);
                return rsa.VerifyData(veri, CryptoConfig.MapNameToOID("SHA256"), imza);
            }
        }

        /// <summary>Exe içindeki sürüm numarası.</summary>
        public static string DosyaSurumu(byte[] exe)
        {
            var tmp = Path.Combine(Path.GetTempPath(), "sem_surum_" + Guid.NewGuid().ToString("N") + ".exe");
            try { File.WriteAllBytes(tmp, exe); return FileVersionInfo.GetVersionInfo(tmp).ProductVersion; }
            finally { try { File.Delete(tmp); } catch { } }
        }
    }
}
