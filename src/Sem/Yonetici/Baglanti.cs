using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using Sem.Ortak;

namespace Sem.Yonetici
{
    public class Pc
    {
        public string Ad = "", Ip = "", Mac = "";
        public bool Acik;
        public string Hata = "";
        public Mesaj SonDurum;
        public DateTime SonGorulme = DateTime.MinValue;
    }

    /// <summary>Bilinen bilgisayarlar listesi ve anahtarlar (%LocalAppData%\Sem).</summary>
    static class Depo
    {
        static string Klasor { get { var k = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Sem"); Directory.CreateDirectory(k); return k; } }
        static string ListeDosya => Path.Combine(Klasor, "bilgisayarlar.txt");
        static string EskiDosya => Path.Combine(Klasor, "eski_anahtarlar.txt");

        public static List<Pc> ListeOku()
        {
            var l = new List<Pc>();
            if (!File.Exists(ListeDosya)) return l;
            foreach (var s in File.ReadAllLines(ListeDosya, Encoding.UTF8))
            {
                var p = s.Split('|');
                if (p.Length >= 2 && p[0] != "") l.Add(new Pc { Ad = p[0], Ip = p[1], Mac = p.Length > 2 ? p[2] : "" });
            }
            return l;
        }

        public static void ListeYaz(IEnumerable<Pc> l) =>
            File.WriteAllLines(ListeDosya, l.Select(p => p.Ad + "|" + p.Ip + "|" + p.Mac), Encoding.UTF8);

        public static List<byte[]> EskiAnahtarlar() =>
            File.Exists(EskiDosya) ? File.ReadAllLines(EskiDosya).Where(x => x.Trim() != "").Select(Convert.FromBase64String).ToList() : new List<byte[]>();

        public static void EskiAnahtarEkle(byte[] k)
        {
            var l = EskiAnahtarlar();
            if (!l.Any(x => Sifreleme.Esit(x, k))) l.Add(k);
            File.WriteAllLines(EskiDosya, l.Skip(Math.Max(0, l.Count - 5)).Select(Convert.ToBase64String));
        }
    }

    static class Baglanti
    {
        public static byte[] Anahtar;

        /// <summary>Komut gönderir. Bilgisayar eski bir şifrede kaldıysa (o sırada kapalıydı) önce şifresini günceller.</summary>
        public static Mesaj Gonder(string ip, Mesaj m, int zamanAsimiMs = 5000)
        {
            try { return Ag.Gonder(ip, Anahtar, m, zamanAsimiMs); }
            catch (CryptographicException)
            {
                foreach (var eski in Depo.EskiAnahtarlar())
                {
                    try
                    {
                        var s = new Mesaj("SIFRE") { Veri = Anahtar };
                        if (Ag.Gonder(ip, eski, s, zamanAsimiMs).Tamam) return Ag.Gonder(ip, Anahtar, m, zamanAsimiMs);
                    }
                    catch (CryptographicException) { }
                }
                throw new Exception("Şifre uyuşmuyor (bu bilgisayar farklı bir şifreyle kurulmuş)");
            }
        }

        /// <summary>Ağa "kim var?" diye sorar, cevap veren Sem bilgisayarlarını döndürür (IP → ad).</summary>
        public static Dictionary<string, string> Kesfet(int beklemeMs = 1200)
        {
            var sonuc = new Dictionary<string, string>();
            using (var u = new UdpClient(0))
            {
                u.EnableBroadcast = true;
                var soru = Encoding.UTF8.GetBytes(Sabitler.KesifSoru);
                foreach (var b in YayinAdresleri())
                    try { u.Send(soru, soru.Length, new IPEndPoint(b, Sabitler.KesifPort)); } catch { }
                var bitis = DateTime.Now.AddMilliseconds(beklemeMs);
                while (DateTime.Now < bitis)
                {
                    u.Client.ReceiveTimeout = Math.Max(50, (int)(bitis - DateTime.Now).TotalMilliseconds);
                    try
                    {
                        var uzak = new IPEndPoint(IPAddress.Any, 0);
                        var p = Encoding.UTF8.GetString(u.Receive(ref uzak)).Split('|');
                        if (p.Length >= 2 && p[0] == Sabitler.KesifCevap)
                        {
                            var ip = uzak.Address.IsIPv4MappedToIPv6 ? uzak.Address.MapToIPv4() : uzak.Address;
                            sonuc[ip.ToString()] = p[1];
                        }
                    }
                    catch (SocketException) { break; }
                }
            }
            return sonuc;
        }

        static IEnumerable<IPAddress> YayinAdresleri()
        {
            var l = new List<IPAddress> { IPAddress.Broadcast };
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up || ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                foreach (var u in ni.GetIPProperties().UnicastAddresses)
                {
                    if (u.Address.AddressFamily != AddressFamily.InterNetwork || u.IPv4Mask == null) continue;
                    var ip = u.Address.GetAddressBytes(); var m = u.IPv4Mask.GetAddressBytes();
                    var b = new byte[4];
                    for (int i = 0; i < 4; i++) b[i] = (byte)(ip[i] | ~m[i]);
                    l.Add(new IPAddress(b));
                }
            }
            return l.Distinct();
        }

        /// <summary>Kapalı bilgisayarı ağdan açmayı dener (BIOS'ta Wake-on-LAN açık olmalı).</summary>
        public static void Uyandir(string mac)
        {
            var m = mac.Split('-', ':').Select(x => Convert.ToByte(x, 16)).ToArray();
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
    }
}
