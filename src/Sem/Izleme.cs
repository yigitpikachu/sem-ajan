// Baskı defteri, yazıcı sayacı/toner, internet karnesi, ağ radarı ve sorun bildirimleri (ajan tarafı).
// Ağır işler yalnızca yönetici bilgisayarında ve seyrek çalışır: dakikada bir ping, 5 dakikada bir ağ taraması,
// 3 saatte bir yazıcı sayacı, günde bir hız testi.
using System;
using System.Collections.Generic;
using System.Diagnostics.Eventing.Reader;
using System.IO;
using System.Linq;
using System.Management;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Sem.Ortak;

namespace Sem.Ajan
{
    static class Satir
    {
        /// <summary>Alanı satır biçimine uygun hale getirir (| ve satır sonu girmesin).</summary>
        public static string T(string s) => (s ?? "").Replace("|", "/").Replace("\r", " ").Replace("\n", " ").Trim();
        public static string Zaman(DateTime d) => d.ToString("yyyy-MM-dd HH:mm");
        public static bool ZamanOku(string s, out DateTime d) => DateTime.TryParseExact(s, "yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out d);
    }

    // ================================================================== Baskı defteri
    static class BaskiDefteri
    {
        const string Kayit = "Microsoft-Windows-PrintService/Operational";
        static bool denendi;

        /// <summary>Windows'un yazdırma günlüğünü açar (varsayılan kapalıdır). Hizmet başlarken bir kez.</summary>
        public static void Etkinlestir()
        {
            if (denendi) return; denendi = true;
            try
            {
                using (var c = new EventLogConfiguration(Kayit))
                {
                    bool degisti = false;
                    if (!c.IsEnabled) { c.IsEnabled = true; degisti = true; }
                    if (c.MaximumSizeInBytes < 16L * 1024 * 1024) { c.MaximumSizeInBytes = 16L * 1024 * 1024; degisti = true; }
                    if (degisti) { c.SaveChanges(); Gunluk.Yaz("Yazdırma günlüğü açıldı"); }
                }
            }
            catch (Exception e) { Gunluk.Yaz("Yazdırma günlüğü: " + e.Message); }
        }

        /// <summary>Son günlerde yazdırılan belgeler: zaman|kullanıcı|belge|yazıcı|sayfa</summary>
        public static Mesaj Oku(int gun)
        {
            Etkinlestir();
            var l = new List<string>();
            long ms = Math.Max(1, Math.Min(400, gun)) * 86400000L;
            var q = new EventLogQuery(Kayit, PathType.LogName, "*[System[(EventID=307) and TimeCreated[timediff(@SystemTime) <= " + ms + "]]]");
            try
            {
                using (var r = new EventLogReader(q))
                    for (EventRecord e = r.ReadEvent(); e != null; e = r.ReadEvent())
                        using (e)
                        {
                            try
                            {
                                var x = e.ToXml();
                                string P(int i) { var m = Regex.Match(x, "<Param" + i + ">(.*?)</Param" + i + ">", RegexOptions.Singleline); return m.Success ? WebUtility.HtmlDecode(m.Groups[1].Value) : ""; }
                                int sayfa; int.TryParse(P(8), out sayfa);
                                l.Add(Satir.Zaman(e.TimeCreated ?? DateTime.Now) + "|" + Satir.T(P(3)) + "|" + Satir.T(P(2)) + "|" + Satir.T(P(5)) + "|" + sayfa);
                                if (l.Count > 5000) l.RemoveAt(0);
                            }
                            catch { }
                        }
            }
            catch (EventLogNotFoundException) { }
            catch (Exception e) { return Mesaj.Hata("Yazdırma günlüğü okunamadı: " + e.Message); }
            var c = Mesaj.Basari(); c["adet"] = l.Count.ToString();
            c.Veri = Encoding.UTF8.GetBytes(string.Join("\n", l));
            return c;
        }
    }

    // ================================================================== SNMP v1 (yalnızca okuma)
    static class Snmp
    {
        static byte[] Tlv(byte t, byte[] v)
        {
            var ms = new MemoryStream(); ms.WriteByte(t);
            if (v.Length < 128) ms.WriteByte((byte)v.Length);
            else if (v.Length < 256) { ms.WriteByte(0x81); ms.WriteByte((byte)v.Length); }
            else { ms.WriteByte(0x82); ms.WriteByte((byte)(v.Length >> 8)); ms.WriteByte((byte)v.Length); }
            ms.Write(v, 0, v.Length); return ms.ToArray();
        }
        static byte[] Int(long n)
        {
            var b = new List<byte>();
            do { b.Insert(0, (byte)(n & 0xFF)); n >>= 8; } while (n != 0 && n != -1);
            if (n == 0 && (b[0] & 0x80) != 0) b.Insert(0, 0);
            if (n == -1 && (b[0] & 0x80) == 0) b.Insert(0, 0xFF);
            return Tlv(0x02, b.ToArray());
        }
        static byte[] Oid(string s)
        {
            var p = s.Trim('.').Split('.').Select(uint.Parse).ToArray();
            var ms = new MemoryStream(); ms.WriteByte((byte)(p[0] * 40 + p[1]));
            for (int i = 2; i < p.Length; i++)
            {
                uint v = p[i]; var t = new List<byte> { (byte)(v & 0x7F) }; v >>= 7;
                while (v > 0) { t.Insert(0, (byte)(0x80 | (v & 0x7F))); v >>= 7; }
                ms.Write(t.ToArray(), 0, t.Count);
            }
            return Tlv(0x06, ms.ToArray());
        }
        static byte[] Birlestir(params byte[][] a) { var ms = new MemoryStream(); foreach (var x in a) ms.Write(x, 0, x.Length); return ms.ToArray(); }

        static byte[] Oku(byte[] b, ref int i, out byte tur)
        {
            tur = b[i++]; int n = b[i++];
            if ((n & 0x80) != 0) { int k = n & 0x7F; n = 0; for (int j = 0; j < k; j++) n = (n << 8) | b[i++]; }
            var v = new byte[n]; Buffer.BlockCopy(b, i, v, 0, n); i += n; return v;
        }
        static string OidMetin(byte[] v)
        {
            var sb = new StringBuilder(); sb.Append(v[0] / 40).Append('.').Append(v[0] % 40);
            uint x = 0;
            for (int i = 1; i < v.Length; i++) { x = (x << 7) | (uint)(v[i] & 0x7F); if ((v[i] & 0x80) == 0) { sb.Append('.').Append(x); x = 0; } }
            return sb.ToString();
        }
        static string Metin(byte[] v)
        {
            try { return new UTF8Encoding(false, true).GetString(v).TrimEnd('\0').Trim(); }
            catch { try { return Encoding.GetEncoding(1254).GetString(v).TrimEnd('\0').Trim(); } catch { return ""; } }
        }

        /// <summary>Verilen OID'leri okur. Cevap gelmezse boş sözlük; olmayan değer listede yer almaz.</summary>
        public static Dictionary<string, string> Al(string ip, string topluluk, int sure, params string[] oidler)
        {
            var sonuc = new Dictionary<string, string>();
            int id = new Random().Next(1, int.MaxValue);
            var vb = Tlv(0x30, Birlestir(oidler.Select(o => Tlv(0x30, Birlestir(Oid(o), new byte[] { 0x05, 0x00 }))).ToArray()));
            var pdu = Tlv(0xA0, Birlestir(Int(id), Int(0), Int(0), vb));
            var paket = Tlv(0x30, Birlestir(Int(0), Tlv(0x04, Encoding.ASCII.GetBytes(topluluk)), pdu));
            using (var u = new UdpClient())
            {
                u.Client.ReceiveTimeout = sure;
                u.Send(paket, paket.Length, ip, 161);
                var uzak = new IPEndPoint(IPAddress.Any, 0);
                byte[] b;
                try { b = u.Receive(ref uzak); } catch { return sonuc; }
                int i = 0; byte t;
                var dis = Oku(b, ref i, out t); i = 0;
                Oku(dis, ref i, out t); Oku(dis, ref i, out t); // sürüm, topluluk
                var cevap = Oku(dis, ref i, out t); if (t != 0xA2) return sonuc;
                i = 0; Oku(cevap, ref i, out t); Oku(cevap, ref i, out t); Oku(cevap, ref i, out t);
                var liste = Oku(cevap, ref i, out t);
                i = 0;
                while (i < liste.Length)
                {
                    var bag = Oku(liste, ref i, out t);
                    int j = 0; var o = OidMetin(Oku(bag, ref j, out t)); var v = Oku(bag, ref j, out t);
                    string d = null;
                    if (t == 0x02) { long n = (v.Length > 0 && (v[0] & 0x80) != 0) ? -1 : 0; foreach (var x in v) n = (n << 8) | x; d = n.ToString(); }
                    else if (t == 0x41 || t == 0x42 || t == 0x43) { ulong n = 0; foreach (var x in v) n = (n << 8) | x; d = n.ToString(); }
                    else if (t == 0x04) d = Metin(v);
                    if (d != null) sonuc[o] = d;
                }
            }
            return sonuc;
        }
    }

    // ================================================================== Yazıcı sayacı ve toner tahmini
    static class TonerIzleme
    {
        public const string Sayac = "1.3.6.1.2.1.43.10.2.1.4.1.1", Seviye = "1.3.6.1.2.1.43.11.1.1.9.1.1", Kapasite = "1.3.6.1.2.1.43.11.1.1.8.1.1",
            SarfAd = "1.3.6.1.2.1.43.11.1.1.6.1.1", Ekran = "1.3.6.1.2.1.43.16.5.1.2.1.1", Durum = "1.3.6.1.2.1.25.3.5.1.1.1", Model = "1.3.6.1.2.1.25.3.2.1.3.1";
        static string Dosya => Path.Combine(AjanAyar.Klasor, "toner.txt");
        static readonly object k = new object();
        static DateTime son = DateTime.MinValue;
        static int calisiyor;
        public static string SonEkran = "", SonModel = "", SonSarf = "";
        public static long SonSayac = -1; public static int SonYuzde = -1;

        /// <summary>3 saatte bir sayaç okunur ve kaydedilir (yalnızca yönetici bilgisayarı).</summary>
        public static void Denetle(string ip)
        {
            if ((DateTime.Now - son).TotalHours < 3 || Interlocked.Exchange(ref calisiyor, 1) == 1) return;
            son = DateTime.Now;
            ThreadPool.QueueUserWorkItem(_ => { try { Oku(ip, true); } catch (Exception e) { Gunluk.Yaz("Toner: " + e.Message); } finally { calisiyor = 0; } });
        }

        public static bool Oku(string ip, bool kaydet)
        {
            var d = Snmp.Al(ip, "public", 2500, Sayac, Seviye, Kapasite, SarfAd, Ekran, Model);
            long sayac; if (!d.ContainsKey(Sayac) || !long.TryParse(d[Sayac], out sayac)) return false;
            int sev = -2, kap = 0; if (d.ContainsKey(Seviye)) int.TryParse(d[Seviye], out sev); if (d.ContainsKey(Kapasite)) int.TryParse(d[Kapasite], out kap);
            SonSayac = sayac; SonYuzde = sev >= 0 && kap > 0 ? (int)Math.Round(sev * 100.0 / kap) : -1;
            string v; SonEkran = d.TryGetValue(Ekran, out v) ? v : ""; SonModel = d.TryGetValue(Model, out v) ? v : ""; SonSarf = d.TryGetValue(SarfAd, out v) ? v : "";
            if (kaydet) lock (k) { Directory.CreateDirectory(AjanAyar.Klasor); File.AppendAllText(Dosya, "O|" + Satir.Zaman(DateTime.Now) + "|" + sayac + "|" + SonYuzde + "\n"); }
            return true;
        }

        public static Mesaj Degisti(string ip)
        {
            if (!Oku(ip, true)) return Mesaj.Hata("Yazıcıya ulaşılamadı (" + ip + ")");
            lock (k) File.AppendAllText(Dosya, "D|" + Satir.Zaman(DateTime.Now) + "|" + SonSayac + "\n");
            return Mesaj.Basari("Toner değişimi kaydedildi");
        }

        static List<(char Tur, DateTime Zaman, long Sayac, int Yuzde)> Kayitlar()
        {
            var l = new List<(char, DateTime, long, int)>();
            lock (k)
            {
                if (!File.Exists(Dosya)) return l;
                foreach (var s in File.ReadAllLines(Dosya))
                {
                    var p = s.Split('|'); DateTime z; long n; int y = -1;
                    if (p.Length < 3 || !Satir.ZamanOku(p[1], out z) || !long.TryParse(p[2], out n)) continue;
                    if (p.Length > 3) int.TryParse(p[3], out y);
                    l.Add((p[0][0], z, n, y));
                }
            }
            return l;
        }

        /// <summary>Uyarı metni (boşsa uyarı yok). DURUM'a eklenir.</summary>
        public static string Uyari { get; private set; } = "";

        public static Mesaj Ozet(string ip)
        {
            bool ulasti = Oku(ip, false);
            var r = Mesaj.Basari();
            var l = Kayitlar();
            var olcum = l.Where(x => x.Tur == 'O').OrderBy(x => x.Zaman).ToList();
            var degisim = l.Where(x => x.Tur == 'D').OrderBy(x => x.Zaman).ToList();
            long sayac = ulasti ? SonSayac : olcum.Count > 0 ? olcum.Last().Sayac : -1;
            r["ulasti"] = ulasti ? "1" : "0"; r["sayac"] = sayac.ToString(); r["yuzde"] = SonYuzde.ToString();
            r["ekran"] = SonEkran; r["model"] = SonModel; r["sarf"] = SonSarf;
            // Günlük ortalama: son 30 günün ölçümleri (en az bir günlük veri)
            double gunluk = 0;
            var once = olcum.Where(x => x.Zaman >= DateTime.Now.AddDays(-30)).ToList();
            if (once.Count >= 2 && (once.Last().Zaman - once.First().Zaman).TotalDays >= 1)
                gunluk = (once.Last().Sayac - once.First().Sayac) / Math.Max(1, (once.Last().Zaman - once.First().Zaman).TotalDays);
            r["gunluk"] = gunluk.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);
            // Bir kartuşun ortalama sayfası: önceki değişimler arasından öğrenilir
            var verimler = new List<long>();
            for (int i = 1; i < degisim.Count; i++) { long f = degisim[i].Sayac - degisim[i - 1].Sayac; if (f > 200) verimler.Add(f); }
            long verim = verimler.Count > 0 ? (long)verimler.Average() : (SonSarf.Contains("T06") ? 20500 : 3000);
            r["verim"] = verim.ToString(); r["verim_ogrenildi"] = verimler.Count > 0 ? "1" : "0";
            string uyari = "";
            if (degisim.Count > 0 && sayac >= 0)
            {
                var sd = degisim.Last();
                long basilan = Math.Max(0, sayac - sd.Sayac), kalan = Math.Max(0, verim - basilan);
                r["degisim"] = Satir.Zaman(sd.Zaman); r["basilan"] = basilan.ToString(); r["kalan_sayfa"] = kalan.ToString();
                if (gunluk > 0)
                {
                    int gun = (int)Math.Floor(kalan / gunluk); r["kalan_gun"] = gun.ToString();
                    if (gun <= 10) uyari = gun <= 0 ? "Toner bitmek üzere olabilir" : "Toner yaklaşık " + gun + " gün içinde bitebilir";
                }
            }
            if (SonYuzde >= 0 && SonYuzde <= 10) uyari = "Toner %" + SonYuzde + " kaldı";
            Uyari = uyari; r["uyari"] = uyari;
            return r;
        }

        static DateTime sonOzet = DateTime.MinValue;
        /// <summary>Uyarıyı günde birkaç kez tazeler (DURUM için, ağır değil).</summary>
        public static void UyariTazele(string ip)
        {
            if ((DateTime.Now - sonOzet).TotalHours < 3) return;
            sonOzet = DateTime.Now;
            ThreadPool.QueueUserWorkItem(_ => { try { Ozet(ip); } catch { } });
        }
    }

    // ================================================================== İnternet karnesi
    static class InternetKarnesi
    {
        static string Dosya => Path.Combine(AjanAyar.Klasor, "internet.txt");
        static string GunDosya => Path.Combine(AjanAyar.Klasor, "internet_gun.txt");
        static readonly object k = new object();
        static readonly Dictionary<string, int[]> gunler = new Dictionary<string, int[]>(); // tarih -> {ölçüm, başarılı}
        static DateTime sonTik = DateTime.MinValue, kesintiBas = DateTime.MinValue, sonKayit = DateTime.MinValue;
        static int ardisik, calisiyor, testDeneme; static bool yuklendi, gatewayVar = true;
        static string testGunu = "", denemeGunu = "";
        public static bool Acik = true;
        public static DateTime KesikSince = DateTime.MinValue;

        static void Yukle()
        {
            if (yuklendi) return; yuklendi = true;
            try
            {
                if (File.Exists(GunDosya))
                    foreach (var s in File.ReadAllLines(GunDosya)) { var p = s.Split('|'); int a, b; if (p.Length == 3 && int.TryParse(p[1], out a) && int.TryParse(p[2], out b)) gunler[p[0]] = new[] { a, b }; }
                if (File.Exists(Dosya))
                {
                    var bugun = DateTime.Today.ToString("yyyy-MM-dd");
                    if (File.ReadLines(Dosya).Any(x => x.StartsWith("H|" + bugun))) testGunu = bugun;
                }
            }
            catch { }
        }

        static bool Ping(string h, int sure) { try { using (var p = new Ping()) return p.Send(h, sure).Status == IPStatus.Success; } catch { return false; } }

        static string Gateway()
        {
            try
            {
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces().Where(n => n.OperationalStatus == OperationalStatus.Up))
                    foreach (var g in ni.GetIPProperties().GatewayAddresses)
                        if (g.Address.AddressFamily == AddressFamily.InterNetwork && !g.Address.Equals(IPAddress.Any)) return g.Address.ToString();
            }
            catch { }
            return "";
        }

        /// <summary>Ajanın 5 saniyelik zamanlayıcısından çağrılır; dakikada bir ölçer.</summary>
        public static void Tik()
        {
            if ((DateTime.Now - sonTik).TotalSeconds < 60 || Interlocked.Exchange(ref calisiyor, 1) == 1) return;
            var onceki = sonTik; sonTik = DateTime.Now;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    lock (k) Yukle();
                    // Bilgisayar uyuduysa ya da kapandıysa aradaki süre kesinti sayılmaz
                    if (onceki != DateTime.MinValue && (DateTime.Now - onceki).TotalMinutes > 3) { ardisik = 0; kesintiBas = DateTime.MinValue; }
                    bool ok = Ping("1.1.1.1", 2000) || Ping("8.8.8.8", 2000) || Ping("208.67.222.222", 2000);
                    var gun = DateTime.Today.ToString("yyyy-MM-dd");
                    lock (k)
                    {
                        int[] g; if (!gunler.TryGetValue(gun, out g)) gunler[gun] = g = new int[2];
                        g[0]++; if (ok) g[1]++;
                    }
                    if (ok)
                    {
                        if (kesintiBas != DateTime.MinValue && ardisik >= 2)
                            lock (k) File.AppendAllText(Dosya, "K|" + Satir.Zaman(kesintiBas) + "|" + Satir.Zaman(DateTime.Now) + "|" + (gatewayVar ? "internet" : "ag") + "\n");
                        ardisik = 0; kesintiBas = DateTime.MinValue; Acik = true; KesikSince = DateTime.MinValue;
                    }
                    else
                    {
                        if (ardisik == 0) { kesintiBas = DateTime.Now; var gw = Gateway(); gatewayVar = gw == "" || Ping(gw, 1500); }
                        ardisik++;
                        if (ardisik >= 2) { Acik = false; KesikSince = kesintiBas; }
                    }
                    if ((DateTime.Now - sonKayit).TotalMinutes >= 10)
                    {
                        sonKayit = DateTime.Now;
                        lock (k)
                        {
                            var sinir = DateTime.Today.AddDays(-400).ToString("yyyy-MM-dd");
                            foreach (var eski in gunler.Keys.Where(x => string.CompareOrdinal(x, sinir) < 0).ToList()) gunler.Remove(eski);
                            Directory.CreateDirectory(AjanAyar.Klasor);
                            File.WriteAllLines(GunDosya, gunler.OrderBy(x => x.Key).Select(x => x.Key + "|" + x.Value[0] + "|" + x.Value[1]));
                        }
                    }
                    // Günde bir hız testi: 09:00'dan sonra, internet açıkken (başarısızsa yarım saat arayla 3 deneme)
                    if (denemeGunu != gun) { denemeGunu = gun; testDeneme = 0; }
                    if (ok && testGunu != gun && testDeneme < 3 && DateTime.Now.Hour >= 9 && DateTime.Now.Hour < 18 && (DateTime.Now - sonTest).TotalMinutes >= 30) HizTesti(gun);
                }
                catch (Exception e) { Gunluk.Yaz("İnternet: " + e.Message); }
                finally { calisiyor = 0; }
            });
        }

        static DateTime sonTest = DateTime.MinValue;

        static void HizTesti(string gun)
        {
            sonTest = DateTime.Now; testDeneme++;
            try
            {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                // Gecikme
                var pl = new List<long>();
                using (var p = new Ping()) for (int i = 0; i < 4; i++) { try { var c = p.Send("1.1.1.1", 2000); if (c.Status == IPStatus.Success) pl.Add(c.RoundtripTime); } catch { } }
                double indir = Olc(true, 25_000_000), yukle = Olc(false, 6_000_000);
                if (indir <= 0) return;
                lock (k) File.AppendAllText(Dosya, "H|" + Satir.Zaman(DateTime.Now) + "|" + indir.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + "|" +
                    yukle.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + "|" + (pl.Count > 0 ? (long)pl.Average() : 0) + "\n");
                testGunu = gun; testDeneme = 0;
            }
            catch (Exception e) { Gunluk.Yaz("Hız testi: " + e.Message); }
        }

        /// <summary>Mbit/sn. İndirme en fazla 15 sn, yükleme en fazla 15 sn sürer.</summary>
        static double Olc(bool indir, int bayt)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew(); long aktarilan = 0;
            try
            {
                var istek = (HttpWebRequest)WebRequest.Create(indir ? "https://speed.cloudflare.com/__down?bytes=" + bayt : "https://speed.cloudflare.com/__up");
                istek.UserAgent = "SemAjan/" + Sabitler.Surum; istek.Timeout = 20000; istek.ReadWriteTimeout = 20000; istek.Proxy = null;
                if (!indir)
                {
                    istek.Method = "POST"; istek.ContentType = "application/octet-stream"; istek.ContentLength = bayt; istek.AllowWriteStreamBuffering = false;
                    var parca = new byte[64 * 1024]; new Random().NextBytes(parca);
                    sw.Restart();
                    using (var s = istek.GetRequestStream())
                        while (aktarilan < bayt && sw.Elapsed.TotalSeconds < 15) { int n = (int)Math.Min(parca.Length, bayt - aktarilan); s.Write(parca, 0, n); aktarilan += n; }
                    if (aktarilan < bayt) { istek.Abort(); }
                    else using (var c = istek.GetResponse()) { }
                }
                else
                {
                    using (var c = istek.GetResponse())
                    using (var s = c.GetResponseStream())
                    {
                        sw.Restart(); var tampon = new byte[64 * 1024]; int n;
                        while ((n = s.Read(tampon, 0, tampon.Length)) > 0) { aktarilan += n; if (sw.Elapsed.TotalSeconds > 15) break; }
                    }
                }
            }
            catch { if (aktarilan < bayt / 10) return 0; }
            var sn = Math.Max(0.2, sw.Elapsed.TotalSeconds);
            return aktarilan * 8 / sn / 1_000_000;
        }

        /// <summary>Kayıtlar: K (kesinti), H (hız testi), G (günlük ölçüm). ay = yyyy-MM, boşsa son 3 ay.</summary>
        public static Mesaj Karne(string ay)
        {
            var sb = new StringBuilder();
            Func<string, bool> uygun = t => ay == "" ? string.CompareOrdinal(t, DateTime.Today.AddMonths(-3).ToString("yyyy-MM")) >= 0 : t.StartsWith(ay);
            lock (k)
            {
                Yukle();
                if (File.Exists(Dosya)) foreach (var s in File.ReadAllLines(Dosya)) { var p = s.Split('|'); if (p.Length > 2 && uygun(p[1])) sb.Append(s).Append('\n'); }
                foreach (var g in gunler.OrderBy(x => x.Key)) if (uygun(g.Key)) sb.Append("G|" + g.Key + "|" + g.Value[0] + "|" + g.Value[1]).Append('\n');
            }
            if (!Acik) sb.Append("S|" + Satir.Zaman(KesikSince) + "\n");
            var r = Mesaj.Basari(); r.Veri = Encoding.UTF8.GetBytes(sb.ToString()); return r;
        }
    }

    // ================================================================== Ağ radarı
    static class AgRadari
    {
        static string Dosya => Path.Combine(AjanAyar.Klasor, "radar.txt");
        static readonly object k = new object();
        static DateTime son = DateTime.MinValue;
        static int calisiyor;

        public class Cihaz { public string Mac = "", Ip = "", Ad = "", Host = "", Durum = "b"; public DateTime Ilk, Son; }
        // Durum: b = ilk taramada vardı, y = yeni (görülmedi), t = tanıdık (onaylandı)

        static List<Cihaz> Oku()
        {
            var l = new List<Cihaz>();
            try
            {
                if (File.Exists(Dosya))
                    foreach (var s in File.ReadAllLines(Dosya))
                    {
                        var p = s.Split('|'); if (p.Length < 7) continue;
                        DateTime a, b; Satir.ZamanOku(p[4], out a); Satir.ZamanOku(p[5], out b);
                        l.Add(new Cihaz { Mac = p[0], Ip = p[1], Ad = p[2], Host = p[3], Ilk = a, Son = b, Durum = p[6] });
                    }
            }
            catch { }
            return l;
        }
        static void Yaz(List<Cihaz> l)
        {
            Directory.CreateDirectory(AjanAyar.Klasor);
            File.WriteAllLines(Dosya, l.Select(c => string.Join("|", c.Mac, c.Ip, Satir.T(c.Ad), Satir.T(c.Host), Satir.Zaman(c.Ilk), Satir.Zaman(c.Son), c.Durum)));
        }

        [DllImport("iphlpapi.dll")] static extern int GetIpNetTable(IntPtr tablo, ref int boyut, bool sirala);

        static Dictionary<string, string> ArpTablosu()
        {
            var d = new Dictionary<string, string>(); // ip -> mac
            int boyut = 0; GetIpNetTable(IntPtr.Zero, ref boyut, false);
            if (boyut <= 0) return d;
            var buf = Marshal.AllocHGlobal(boyut);
            try
            {
                if (GetIpNetTable(buf, ref boyut, false) != 0) return d;
                int n = Marshal.ReadInt32(buf);
                for (int i = 0; i < n; i++)
                {
                    var row = buf + 4 + i * 24;
                    int len = Marshal.ReadInt32(row + 4), tur = Marshal.ReadInt32(row + 20);
                    if (tur != 3 && tur != 4 || len != 6) continue;
                    var mac = new byte[6]; Marshal.Copy(row + 8, mac, 0, 6);
                    if (mac.All(x => x == 0) || mac.All(x => x == 0xFF) || (mac[0] & 1) == 1) continue;
                    var ip = new IPAddress(BitConverter.GetBytes(Marshal.ReadInt32(row + 16))).ToString();
                    d[ip] = string.Join("-", mac.Select(x => x.ToString("X2")));
                }
            }
            finally { Marshal.FreeHGlobal(buf); }
            return d;
        }

        /// <summary>Bu bilgisayarın ağ geçidi olan IPv4 adresi, MAC'i ve /24 öneki.</summary>
        static (string ip, string mac, string onek) Yerel()
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces().Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback))
            {
                var ipp = ni.GetIPProperties();
                if (!ipp.GatewayAddresses.Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork && !g.Address.Equals(IPAddress.Any))) continue;
                var u = ipp.UnicastAddresses.FirstOrDefault(a => a.Address.AddressFamily == AddressFamily.InterNetwork);
                if (u == null) continue;
                var b = u.Address.GetAddressBytes();
                return (u.Address.ToString(), string.Join("-", ni.GetPhysicalAddress().GetAddressBytes().Select(x => x.ToString("X2"))), b[0] + "." + b[1] + "." + b[2] + ".");
            }
            return ("", "", "");
        }

        /// <summary>5 dakikada bir ağı tarar (yalnızca yönetici bilgisayarı).</summary>
        public static void Tik()
        {
            if ((DateTime.Now - son).TotalMinutes < 5 || Interlocked.Exchange(ref calisiyor, 1) == 1) return;
            son = DateTime.Now;
            new Thread(() => { try { Tara(); } catch (Exception e) { Gunluk.Yaz("Ağ radarı: " + e.Message); } finally { calisiyor = 0; } }) { IsBackground = true, Priority = ThreadPriority.BelowNormal }.Start();
        }

        static void Tara()
        {
            var (benIp, benMac, onek) = Yerel();
            if (onek == "") return;
            // Ping cevap vermeyen cihazlar da (yazıcı gibi) ARP tablosuna düşer
            var gorevler = new List<Task>();
            for (int i = 1; i < 255; i++)
            {
                var p = new Ping(); var hedef = onek + i;
                gorevler.Add(p.SendPingAsync(hedef, 600).ContinueWith(t => p.Dispose()));
            }
            try { Task.WaitAll(gorevler.ToArray(), 8000); } catch { }
            Thread.Sleep(500);
            var arp = ArpTablosu().Where(x => x.Key.StartsWith(onek)).ToList();
            if (benIp != "" && benMac != "") arp.Add(new KeyValuePair<string, string>(benIp, benMac));
            List<Cihaz> l; bool ilk;
            lock (k) { ilk = !File.Exists(Dosya); l = Oku(); }
            var yeniler = new List<Cihaz>();
            foreach (var kv in arp)
            {
                var c = l.FirstOrDefault(x => x.Mac == kv.Value);
                if (c == null) { c = new Cihaz { Mac = kv.Value, Ilk = DateTime.Now, Durum = ilk ? "b" : "y" }; l.Add(c); if (!ilk) yeniler.Add(c); }
                c.Ip = kv.Key; c.Son = DateTime.Now;
                if (c.Host == "" && (ilk || yeniler.Contains(c))) yeniler.Add(c);
            }
            lock (k) Yaz(l);
            // Yeni cihazların bilgisayar adını bulmaya çalış (birkaç saniye sürebilir)
            foreach (var c in yeniler.Distinct().Take(40))
            {
                try
                {
                    var t = Dns.GetHostEntryAsync(c.Ip);
                    if (t.Wait(2500) && t.Result.HostName != c.Ip) c.Host = t.Result.HostName.Split('.')[0];
                }
                catch { }
                if (c.Host == "") c.Host = "-";
            }
            if (yeniler.Count > 0) lock (k)
                {
                    var g = Oku();
                    foreach (var c in yeniler) { var x = g.FirstOrDefault(y => y.Mac == c.Mac); if (x != null) x.Host = c.Host; }
                    Yaz(g);
                }
        }

        public static string YeniOzet()
        {
            lock (k) return string.Join(",", Oku().Where(c => c.Durum == "y").Select(c => c.Mac));
        }

        public static Mesaj Liste()
        {
            List<Cihaz> l; lock (k) l = Oku();
            var r = Mesaj.Basari();
            r.Veri = Encoding.UTF8.GetBytes(string.Join("\n", l.Select(c => string.Join("|", c.Mac, c.Ip, c.Ad, c.Host == "-" ? "" : c.Host, Satir.Zaman(c.Ilk), Satir.Zaman(c.Son), c.Durum))));
            return r;
        }

        /// <summary>islem: ad (adı verir, tanıdık yapar), tamam (tanıdık yapar; mac "hepsi" olabilir), sil.</summary>
        public static Mesaj Degistir(string islem, string mac, string ad)
        {
            lock (k)
            {
                var l = Oku();
                foreach (var c in l.Where(x => x.Mac == mac || (mac == "hepsi" && x.Durum == "y")).ToList())
                {
                    if (islem == "sil") l.Remove(c);
                    else { c.Durum = "t"; if (islem == "ad") c.Ad = Satir.T(ad); }
                }
                Yaz(l);
            }
            return Mesaj.Basari();
        }
    }

    // ================================================================== Sorun bildirimleri
    static class SorunBildir
    {
        static string Klasor => Path.Combine(AjanAyar.Klasor, "sorun");

        /// <summary>Ekrandan gelen bildirimi kaydeder; yönetim paneli DURUM'da görüp alır.</summary>
        public static void Kaydet(string not, string resim, string kullanici, string yaziciIp)
        {
            Directory.CreateDirectory(Klasor);
            var id = DateTime.Now.ToString("yyyyMMddHHmmss") + new Random().Next(10, 99);
            var sb = new StringBuilder();
            sb.AppendLine("zaman=" + Satir.Zaman(DateTime.Now));
            sb.AppendLine("kullanici=" + Satir.T(kullanici));
            sb.AppendLine("not=" + Convert.ToBase64String(Encoding.UTF8.GetBytes(not ?? "")));
            sb.AppendLine("yazici=" + Satir.T(YaziciDurumu(yaziciIp)));
            sb.AppendLine("saglik=" + Satir.T(Saglik.Ozet().Replace(";", " · ")));
            File.WriteAllText(Path.Combine(Klasor, id + ".txt"), sb.ToString());
            if (!string.IsNullOrEmpty(resim)) try { File.WriteAllBytes(Path.Combine(Klasor, id + ".jpg"), Convert.FromBase64String(resim)); } catch { }
            Gunluk.Yaz("Sorun bildirildi: " + id);
        }

        static string YaziciDurumu(string ip)
        {
            var l = new List<string>();
            try
            {
                using (var s = new ManagementObjectSearcher("SELECT Name, PrinterStatus, WorkOffline, Default FROM Win32_Printer"))
                    foreach (ManagementObject o in s.Get())
                    {
                        var ad = Convert.ToString(o["Name"]);
                        if (ad.StartsWith("Microsoft ") || ad.Contains("OneNote") || ad.StartsWith("Fax") || ad.Contains("PDF")) continue;
                        int st = Convert.ToInt32(o["PrinterStatus"] ?? 0); bool cevrimdisi = Convert.ToBoolean(o["WorkOffline"] ?? false);
                        int is_ = 0;
                        try { using (var j = new ManagementObjectSearcher("SELECT JobId FROM Win32_PrintJob WHERE Name LIKE '" + ad.Replace("'", "''").Replace("\\", "\\\\") + ",%'")) is_ = j.Get().Count; } catch { }
                        var d = cevrimdisi ? "çevrimdışı" : st == 3 ? "hazır" : st == 4 ? "yazdırıyor" : st == 7 ? "kapalı" : st == 1 || st == 2 ? "bilinmiyor" : "durum " + st;
                        l.Add(ad + ": " + d + (is_ > 0 ? ", kuyrukta " + is_ + " belge" : "") + (Convert.ToBoolean(o["Default"] ?? false) ? " (varsayılan)" : ""));
                    }
            }
            catch { }
            try
            {
                if (!string.IsNullOrEmpty(ip))
                {
                    var d = Snmp.Al(ip, "public", 1500, TonerIzleme.Ekran);
                    string e; l.Add(d.TryGetValue(TonerIzleme.Ekran, out e) ? "Yazıcı ekranı: " + e : "Yazıcıya ağdan ulaşılamadı (" + ip + ")");
                }
            }
            catch { }
            return string.Join(" · ", l);
        }

        public static string Bekleyenler()
        {
            try { return Directory.Exists(Klasor) ? string.Join(";", Directory.GetFiles(Klasor, "*.txt").Select(Path.GetFileNameWithoutExtension).OrderBy(x => x)) : ""; }
            catch { return ""; }
        }

        public static Mesaj Al(string id)
        {
            if (!Regex.IsMatch(id ?? "", "^[0-9]{10,20}$")) return Mesaj.Hata("Geçersiz");
            var f = Path.Combine(Klasor, id + ".txt");
            if (!File.Exists(f)) return Mesaj.Hata("Bulunamadı");
            var r = Mesaj.Basari();
            foreach (var s in File.ReadAllLines(f)) { int i = s.IndexOf('='); if (i > 0) r[s.Substring(0, i)] = s.Substring(i + 1); }
            try { r["not"] = Encoding.UTF8.GetString(Convert.FromBase64String(r["not"])); } catch { }
            var j = Path.Combine(Klasor, id + ".jpg"); if (File.Exists(j)) r.Veri = File.ReadAllBytes(j);
            return r;
        }

        public static Mesaj Sil(string id)
        {
            if (!Regex.IsMatch(id ?? "", "^[0-9]{10,20}$")) return Mesaj.Hata("Geçersiz");
            try { File.Delete(Path.Combine(Klasor, id + ".txt")); File.Delete(Path.Combine(Klasor, id + ".jpg")); } catch { }
            return Mesaj.Basari();
        }
    }
}
