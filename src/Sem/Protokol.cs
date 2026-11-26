// Sem Ajan ortak iletişim katmanı.
// Her bağlantıda ajan rastgele bir "nonce" gönderir; tüm çerçeveler AES-256 ile şifrelenir
// ve HMAC-SHA256 ile imzalanır (nonce imzaya dahil olduğu için kayıt edilip tekrar gönderilemez).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace Sem.Ortak
{
    public static class Sabitler
    {
        public const int TcpPort = 47800;
        public const int KesifPort = 47802;
        public const string KesifSoru = "SEM-KIMSIN-1";
        public const string KesifCevap = "SEM-BURADA-1";
        public static readonly string Surum = SurumBul();
        static string SurumBul()
        {
            var a = typeof(Sabitler).Assembly.GetName().Version;
            return a.Major + "." + a.Minor + "." + a.Build;
        }
        public const int MaxCerceve = 400 * 1024 * 1024; // 400 MB
        public const string PipeAdi = "SemAjanEkran";
        public const string PaylasPipe = "SemAjanPaylas";
    }

    /// <summary>Bir komut ya da cevap: anahtar/değer başlıkları + isteğe bağlı ikili veri.</summary>
    public class Mesaj
    {
        public Dictionary<string, string> Alan = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public byte[] Veri = new byte[0];

        public Mesaj() { }
        public Mesaj(string komut) { Alan["komut"] = komut; }

        public string this[string k]
        {
            get { string v; return Alan.TryGetValue(k, out v) ? v : ""; }
            set { Alan[k] = value ?? ""; }
        }
        public string Komut => this["komut"];
        public bool Tamam => this["durum"] == "tamam";

        public static Mesaj Basari(string aciklama = "") { var m = new Mesaj(); m["durum"] = "tamam"; m["aciklama"] = aciklama; return m; }
        public static Mesaj Hata(string aciklama) { var m = new Mesaj(); m["durum"] = "hata"; m["aciklama"] = aciklama; return m; }

        public byte[] Paketle()
        {
            var sb = new StringBuilder();
            foreach (var kv in Alan) sb.Append(Uri.EscapeDataString(kv.Key)).Append('=').Append(Uri.EscapeDataString(kv.Value)).Append('\n');
            var bas = Encoding.UTF8.GetBytes(sb.ToString());
            var ms = new MemoryStream(4 + bas.Length + Veri.Length);
            ms.Write(BitConverter.GetBytes(bas.Length), 0, 4);
            ms.Write(bas, 0, bas.Length);
            ms.Write(Veri, 0, Veri.Length);
            return ms.ToArray();
        }

        public static Mesaj Coz(byte[] b)
        {
            var m = new Mesaj();
            int len = BitConverter.ToInt32(b, 0);
            if (len < 0 || len > b.Length - 4) throw new InvalidDataException("Bozuk mesaj");
            var metin = Encoding.UTF8.GetString(b, 4, len);
            foreach (var satir in metin.Split('\n'))
            {
                int i = satir.IndexOf('=');
                if (i <= 0) continue;
                m.Alan[Uri.UnescapeDataString(satir.Substring(0, i))] = Uri.UnescapeDataString(satir.Substring(i + 1));
            }
            m.Veri = new byte[b.Length - 4 - len];
            Buffer.BlockCopy(b, 4 + len, m.Veri, 0, m.Veri.Length);
            return m;
        }
    }

    public static class Sifreleme
    {
        static readonly byte[] Tuz = Encoding.UTF8.GetBytes("Sem-Ozel-Egitim-Yonetici-v1");

        /// <summary>Yönetici şifresinden 32 baytlık ortak anahtar üretir (tüm bilgisayarlarda aynı).</summary>
        public static byte[] AnahtarUret(string sifre)
        {
            using (var k = new Rfc2898DeriveBytes(Encoding.UTF8.GetBytes(sifre), Tuz, 30000))
                return k.GetBytes(32);
        }

        public static bool Esit(byte[] a, byte[] b)
        {
            if (a == null || b == null || a.Length != b.Length) return false;
            int f = 0;
            for (int i = 0; i < a.Length; i++) f |= a[i] ^ b[i];
            return f == 0;
        }

        static byte[] Turet(byte[] anahtar, string amac)
        {
            using (var h = new HMACSHA256(anahtar)) return h.ComputeHash(Encoding.UTF8.GetBytes(amac));
        }

        public static byte[] Kilitle(byte[] anahtar, byte[] nonce, string yon, byte[] acik)
        {
            byte[] iv, sifreli;
            using (var aes = Aes.Create())
            {
                aes.Key = Turet(anahtar, "sifre"); aes.GenerateIV(); iv = aes.IV;
                using (var enc = aes.CreateEncryptor()) sifreli = enc.TransformFinalBlock(acik, 0, acik.Length);
            }
            byte[] mac = Imza(anahtar, nonce, yon, iv, sifreli);
            var sonuc = new byte[16 + sifreli.Length + 32];
            Buffer.BlockCopy(iv, 0, sonuc, 0, 16);
            Buffer.BlockCopy(sifreli, 0, sonuc, 16, sifreli.Length);
            Buffer.BlockCopy(mac, 0, sonuc, 16 + sifreli.Length, 32);
            return sonuc;
        }

        public static byte[] Ac(byte[] anahtar, byte[] nonce, string yon, byte[] paket)
        {
            if (paket.Length < 16 + 16 + 32) throw new CryptographicException("Kısa paket");
            var iv = new byte[16]; Buffer.BlockCopy(paket, 0, iv, 0, 16);
            var sifreli = new byte[paket.Length - 48]; Buffer.BlockCopy(paket, 16, sifreli, 0, sifreli.Length);
            var mac = new byte[32]; Buffer.BlockCopy(paket, paket.Length - 32, mac, 0, 32);
            if (!Esit(mac, Imza(anahtar, nonce, yon, iv, sifreli))) throw new CryptographicException("Şifre uyuşmuyor");
            using (var aes = Aes.Create())
            {
                aes.Key = Turet(anahtar, "sifre"); aes.IV = iv;
                using (var dec = aes.CreateDecryptor()) return dec.TransformFinalBlock(sifreli, 0, sifreli.Length);
            }
        }

        static byte[] Imza(byte[] anahtar, byte[] nonce, string yon, byte[] iv, byte[] sifreli)
        {
            using (var h = new HMACSHA256(Turet(anahtar, "imza")))
            {
                var y = Encoding.ASCII.GetBytes(yon);
                h.TransformBlock(nonce, 0, nonce.Length, null, 0);
                h.TransformBlock(y, 0, y.Length, null, 0);
                h.TransformBlock(iv, 0, iv.Length, null, 0);
                h.TransformFinalBlock(sifreli, 0, sifreli.Length);
                return h.Hash;
            }
        }
    }

    public static class Ag
    {
        public static void CerceveYaz(Stream s, byte[] b)
        {
            s.Write(BitConverter.GetBytes(b.Length), 0, 4);
            s.Write(b, 0, b.Length);
            s.Flush();
        }

        public static byte[] CerceveOku(Stream s)
        {
            var l = TamOku(s, 4);
            int len = BitConverter.ToInt32(l, 0);
            if (len < 0 || len > Sabitler.MaxCerceve) throw new InvalidDataException("Çok büyük veri");
            return TamOku(s, len);
        }

        public static byte[] TamOku(Stream s, int n)
        {
            var b = new byte[n]; int o = 0;
            while (o < n)
            {
                int r = s.Read(b, o, n - o);
                if (r <= 0) throw new EndOfStreamException("Bağlantı koptu");
                o += r;
            }
            return b;
        }

        /// <summary>Yönetici tarafı: bir ajana komut gönderip cevabı alır.</summary>
        public static Mesaj Gonder(string ip, byte[] anahtar, Mesaj m, int zamanAsimiMs = 4000)
        {
            using (var c = new TcpClient())
            {
                var ar = c.BeginConnect(ip, Sabitler.TcpPort, null, null);
                if (!ar.AsyncWaitHandle.WaitOne(Math.Min(zamanAsimiMs, 2500))) throw new TimeoutException("Bilgisayara ulaşılamadı");
                c.EndConnect(ar);
                c.ReceiveTimeout = zamanAsimiMs; c.SendTimeout = Math.Max(zamanAsimiMs, 60000);
                var s = c.GetStream();
                var nonce = TamOku(s, 16);
                CerceveYaz(s, Sifreleme.Kilitle(anahtar, nonce, "Y", m.Paketle()));
                return Mesaj.Coz(Sifreleme.Ac(anahtar, nonce, "A", CerceveOku(s)));
            }
        }
    }

    /// <summary>Ağdaki Sem bilgisayarları (UDP keşif cevabı).</summary>
    public class KesifSonucu
    {
        public string Ip = "", Ad = "", Surum = "", Isim = "", Grup = "", Simge = "";
        public bool Yonetici;
        public string Baslik => Isim != "" ? Isim : Ad;
    }

    public static class Kesif
    {
        /// <summary>Ağa "kim var?" diye sorar, cevap veren Sem bilgisayarlarını döndürür.</summary>
        public static List<KesifSonucu> Sor(int beklemeMs = 1200)
        {
            var sonuc = new Dictionary<string, KesifSonucu>();
            using (var u = new UdpClient(0))
            {
                u.EnableBroadcast = true;
                var soru = Encoding.UTF8.GetBytes(Sabitler.KesifSoru);
                foreach (var b in Uyandirma.YayinAdresleri())
                    try { u.Send(soru, soru.Length, new IPEndPoint(b, Sabitler.KesifPort)); } catch { }
                var bitis = DateTime.Now.AddMilliseconds(beklemeMs);
                while (DateTime.Now < bitis)
                {
                    u.Client.ReceiveTimeout = Math.Max(50, (int)(bitis - DateTime.Now).TotalMilliseconds);
                    try
                    {
                        var uzak = new IPEndPoint(IPAddress.Any, 0);
                        var p = Encoding.UTF8.GetString(u.Receive(ref uzak)).Split('|');
                        if (p.Length < 2 || p[0] != Sabitler.KesifCevap) continue;
                        var ip = uzak.Address.IsIPv4MappedToIPv6 ? uzak.Address.MapToIPv4() : uzak.Address;
                        Func<int, string> a = i => p.Length > i ? p[i] : "";
                        sonuc[ip.ToString()] = new KesifSonucu { Ip = ip.ToString(), Ad = a(1), Surum = a(2), Isim = a(3), Grup = a(4), Yonetici = a(5) == "1", Simge = a(6) };
                    }
                    catch (SocketException) { break; }
                }
            }
            return sonuc.Values.ToList();
        }

        public static string Cevap(string ad, string isim, string grup, bool yonetici, string simge) =>
            string.Join("|", Sabitler.KesifCevap, ad, Sabitler.Surum, (isim ?? "").Replace("|", " "), grup ?? "", yonetici ? "1" : "0", simge ?? "");
    }

    /// <summary>Wake-on-LAN: kapalı bilgisayarı ağdan açma sinyali.</summary>
    public static class Uyandirma
    {
        public static void Gonder(string mac)
        {
            var m = (mac ?? "").Split('-', ':').Where(x => x != "").Select(x => Convert.ToByte(x, 16)).ToArray();
            if (m.Length != 6) throw new Exception("MAC adresi bilinmiyor");
            var paket = new byte[102];
            for (int i = 0; i < 6; i++) paket[i] = 0xFF;
            for (int i = 1; i <= 16; i++) Buffer.BlockCopy(m, 0, paket, i * 6, 6);
            using (var u = new UdpClient { EnableBroadcast = true })
                foreach (var b in YayinAdresleri())
                {
                    try { u.Send(paket, paket.Length, new IPEndPoint(b, 9)); } catch { }
                    try { u.Send(paket, paket.Length, new IPEndPoint(b, 7)); } catch { }
                }
        }

        public static IEnumerable<IPAddress> YayinAdresleri()
        {
            var l = new List<IPAddress> { IPAddress.Broadcast };
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up || ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                foreach (var u in ni.GetIPProperties().UnicastAddresses)
                {
                    if (u.Address.AddressFamily != AddressFamily.InterNetwork || u.IPv4Mask == null) continue;
                    var ip = u.Address.GetAddressBytes(); var mk = u.IPv4Mask.GetAddressBytes();
                    var b = new byte[4];
                    for (int i = 0; i < 4; i++) b[i] = (byte)(ip[i] | ~mk[i]);
                    l.Add(new IPAddress(b));
                }
            }
            return l.Distinct();
        }
    }
}
