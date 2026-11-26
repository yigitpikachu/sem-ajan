// Dosya paylaşımı (1.4):
//  • Sem bilgisayarları arasında: şifreli TCP kanalı (PAYLAS_SOR / PAYLAS_DOSYA / PAYLAS_BITTI).
//    Yönetici bilgisayarından gelenler sorulmadan kabul edilir, diğerlerinde ekranda Kabul/Reddet çıkar.
//  • Telefonlarla: açık LocalSend protokolü (v2, düz HTTP). Telefondaki LocalSend uygulaması her bilgisayarı
//    bir LocalSend cihazı olarak görür. Yönetici bilgisayarı telefonlarda "Sem Yazıcı" olarak görünür:
//    ona gönderilen fotoğraf/PDF yazıcıdan çıkar.
//  • Ortak klasör: yönetici bilgisayarındaki masaüstü "Sem Ortak" klasörü diğer bilgisayarlara tek yönlü kopyalanır.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using Microsoft.Win32;
using Sem.Ortak;

namespace Sem.Ajan
{
    /// <summary>Masaüstündeki "Sem Dosyalar" klasörü: Gelen (gönderene göre), Gönderilen, Ortak.</summary>
    public static class Klasorler
    {
        static string Masaustu => Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory);
        public static string Kok => Path.Combine(Masaustu, "Sem Dosyalar");
        public static string OrtakKaynak => Path.Combine(Masaustu, "Sem Ortak");
        public static string Ortak => Path.Combine(Kok, "Ortak");
        public static string GelenKok => Path.Combine(Kok, "Gelen");
        public static string Gelen(string kimden) { var k = Path.Combine(GelenKok, TemizAd(kimden)); Directory.CreateDirectory(k); return k; }
        public static string Gonderilen(string kime) { var k = Path.Combine(Kok, "Gönderilen", TemizAd(kime)); Directory.CreateDirectory(k); return k; }

        public static string TemizAd(string s)
        {
            s = (s ?? "").Trim();
            foreach (var c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
            s = s.Trim('.', ' ');
            if (s.Length > 80) s = s.Substring(0, 80);
            return s == "" ? "Bilinmeyen" : s;
        }

        public static string Benzersiz(string klasor, string ad)
        {
            ad = TemizAd(Path.GetFileName(ad ?? ""));
            var yol = Path.Combine(klasor, ad);
            for (int i = 2; File.Exists(yol); i++) yol = Path.Combine(klasor, Path.GetFileNameWithoutExtension(ad) + " (" + i + ")" + Path.GetExtension(ad));
            return yol;
        }

        /// <summary>Klasörü oluşturur; ortak hesap yönetici izni istemeden dosyaları silip düzenleyebilsin diye herkese değiştirme izni verir.</summary>
        public static void HerkeseAc(string klasor)
        {
            Directory.CreateDirectory(klasor);
            try
            {
                var di = new DirectoryInfo(klasor);
                var g = di.GetAccessControl();
                g.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null), FileSystemRights.Modify,
                    InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
                di.SetAccessControl(g);
            }
            catch { }
        }

        public static void Hazirla()
        {
            HerkeseAc(Kok);
            Directory.CreateDirectory(GelenKok);
            Directory.CreateDirectory(Path.Combine(Kok, "Gönderilen"));
            // 1.3'teki "Sem Gelen Dosyalar" klasörü yeni yerine taşınır.
            var eski = Path.Combine(Masaustu, "Sem Gelen Dosyalar");
            if (Directory.Exists(eski))
            {
                var hedef = Gelen("Yönetici");
                foreach (var f in Directory.GetFiles(eski, "*", SearchOption.AllDirectories))
                    try { File.Move(f, Benzersiz(hedef, Path.GetFileName(f))); } catch { }
                try { Directory.Delete(eski, true); } catch { }
            }
        }
    }

    public class Paylasim
    {
        readonly AjanServis servis;
        AjanAyar Ayar => servis.Ayar;
        volatile bool calisiyor;

        public Paylasim(AjanServis s) { servis = s; }

        public void Baslat()
        {
            calisiyor = true;
            try { Klasorler.Hazirla(); } catch (Exception e) { Gunluk.Yaz("Sem Dosyalar: " + e.Message); }
            if (Ayar.Parmakizi == "") servis.AyarDegistir(a => a.Parmakizi = Guid.NewGuid().ToString("N"));
            new Thread(() => { try { LocalSendKaldir(); } catch (Exception e) { Gunluk.Yaz("LocalSend kaldırma: " + e.Message); } }) { IsBackground = true }.Start();
            new Thread(HttpDongu) { IsBackground = true }.Start();
            new Thread(CokluYayinDongu) { IsBackground = true }.Start();
            new Thread(PipeDongu) { IsBackground = true }.Start();
        }

        public void Durdur()
        {
            calisiyor = false;
            try { http?.Stop(); } catch { }
            try { coklu?.Close(); } catch { }
        }

        // ================================================================== Ekranda soru (Kabul / Reddet)
        class Soru { public readonly ManualResetEventSlim Olay = new ManualResetEventSlim(); public int Cevap = -1; }
        readonly Dictionary<string, Soru> sorular = new Dictionary<string, Soru>();

        /// <summary>Ekranda soru sorar. 0 = reddet, 1 = kabul, 2 = her zaman izin ver, -1 = cevap yok / ekranda oturum yok.</summary>
        int Sor(string tur, string baslik, string metin, int sn = 90)
        {
            var id = Guid.NewGuid().ToString("N");
            var s = new Soru();
            lock (sorular) sorular[id] = s;
            try
            {
                if (!servis.EkranaGonder("SOR", id, tur, baslik, metin, sn.ToString())) return -1;
                s.Olay.Wait(sn * 1000 + 2000);
                return s.Cevap;
            }
            finally { lock (sorular) sorular.Remove(id); s.Olay.Dispose(); }
        }

        public void CevapGeldi(string id, string cevap)
        {
            Soru s; lock (sorular) sorular.TryGetValue(id ?? "", out s);
            if (s == null) return;
            int c; int.TryParse(cevap, out c); s.Cevap = c; s.Olay.Set();
        }

        static string Boyut(long b) =>
            b >= 1024L * 1024 * 1024 ? (b / 1024.0 / 1024 / 1024).ToString("0.0") + " GB" :
            b >= 1024 * 1024 ? (b / 1024.0 / 1024).ToString("0.0") + " MB" : Math.Max(1, b / 1024) + " KB";

        static string DosyaOzeti(IList<(string ad, long boyut)> d)
        {
            var sb = new StringBuilder();
            foreach (var x in d.Take(4)) sb.Append("• ").Append(x.ad).Append("  (").Append(Boyut(x.boyut)).Append(")\n");
            if (d.Count > 4) sb.Append("… ve ").Append(d.Count - 4).Append(" dosya daha\n");
            sb.Append("Toplam ").Append(Boyut(d.Sum(x => x.boyut)));
            return sb.ToString();
        }

        // ================================================================== Gelen oturumlar
        class Oturum
        {
            public string Id = Guid.NewGuid().ToString("N"), Kimden = "", Klasor = "";
            public bool Yazdir;
            public DateTime Bitis = DateTime.Now.AddHours(2);
            public Dictionary<string, (string ad, long boyut, string jeton)> Dosyalar = new Dictionary<string, (string, long, string)>();
            public List<string> Gelenler = new List<string>();
            public int Kalan;
        }
        readonly Dictionary<string, Oturum> oturumlar = new Dictionary<string, Oturum>();

        Oturum OturumAc(string kimden, bool yazdir)
        {
            var o = new Oturum { Kimden = kimden, Yazdir = yazdir, Klasor = Klasorler.Gelen(kimden) };
            lock (oturumlar)
            {
                foreach (var eski in oturumlar.Where(x => x.Value.Bitis < DateTime.Now).Select(x => x.Key).ToList()) oturumlar.Remove(eski);
                oturumlar[o.Id] = o;
            }
            return o;
        }

        Oturum OturumBul(string id) { Oturum o; lock (oturumlar) oturumlar.TryGetValue(id ?? "", out o); return o; }

        void DosyaBitti(Oturum o, string yol)
        {
            bool son;
            lock (o) { o.Gelenler.Add(yol); o.Kalan--; son = o.Kalan <= 0; }
            if (!son) return;
            lock (oturumlar) oturumlar.Remove(o.Id);
            if (o.Yazdir)
                foreach (var f in o.Gelenler) servis.EkranaGonder("YAZDIR", f, o.Kimden);
            else
                servis.EkranaGonder("GELDI", o.Kimden, o.Gelenler.Count.ToString(), o.Klasor, o.Gelenler.Count == 1 ? Path.GetFileName(o.Gelenler[0]) : "");
            Gunluk.Yaz((o.Yazdir ? "Yazdırma: " : "Dosya geldi: ") + o.Kimden + ", " + o.Gelenler.Count + " dosya");
        }

        // ================================================================== Sem bilgisayarları arası (şifreli TCP)
        public Mesaj Isle(Mesaj m)
        {
            switch (m.Komut)
            {
                case "PAYLAS_SOR":
                {
                    if (!Ayar.Paylasim) return Mesaj.Hata("Bu bilgisayarda paylaşım kapalı");
                    var kimden = m["gonderen"] == "" ? "Bilinmeyen" : m["gonderen"];
                    bool yoneticiden = m["yonetici"] == "1";
                    var dosyalar = (m["dosyalar"] ?? "").Split('\n').Select(x => x.Split('|')).Where(x => x.Length == 2 && x[0] != "")
                        .Select(x => { long b; long.TryParse(x[1], out b); return (ad: x[0], boyut: b); }).ToList();
                    if (m["metin"] != "") servis.EkranaGonder("METIN", kimden, m["metin"]);
                    if (dosyalar.Count == 0) return Mesaj.Basari("Mesaj iletildi");
                    if (!yoneticiden)
                    {
                        int c = Sor("dosya", kimden + " dosya gönderiyor", DosyaOzeti(dosyalar));
                        if (c == -1) return Mesaj.Hata(servis.EkranVar ? "Cevap verilmedi" : "Ekranda açık oturum yok");
                        if (c == 0) return Mesaj.Hata("Reddedildi");
                    }
                    var o = OturumAc(kimden, false);
                    o.Kalan = dosyalar.Count;
                    var r = Mesaj.Basari(); r["oturum"] = o.Id;
                    return r;
                }
                case "PAYLAS_DOSYA":
                {
                    var o = OturumBul(m["oturum"]);
                    if (o == null) return Mesaj.Hata("Oturum bulunamadı");
                    var yol = Klasorler.Benzersiz(o.Klasor, m["ad"]);
                    File.WriteAllBytes(yol, m.Veri);
                    DateTime t; if (DateTime.TryParse(m["degisme"], null, System.Globalization.DateTimeStyles.RoundtripKind, out t)) try { File.SetLastWriteTime(yol, t); } catch { }
                    DosyaBitti(o, yol);
                    return Mesaj.Basari();
                }
                case "ORTAK_LISTE":
                {
                    if (!Ayar.Yonetici || !Directory.Exists(Klasorler.OrtakKaynak)) return Mesaj.Hata("Ortak klasör yok");
                    var kok = Klasorler.OrtakKaynak;
                    var sb = new StringBuilder();
                    foreach (var f in new DirectoryInfo(kok).GetFiles("*", SearchOption.AllDirectories))
                    {
                        if ((f.Attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0 || f.Name.StartsWith("~$")) continue;
                        sb.Append(f.FullName.Substring(kok.Length).TrimStart('\\', '/')).Append('|').Append(f.Length).Append('|').Append(f.LastWriteTimeUtc.Ticks).Append('\n');
                    }
                    var r = Mesaj.Basari(); r["liste"] = sb.ToString(); return r;
                }
                case "ORTAK_AL":
                {
                    if (!Ayar.Yonetici) return Mesaj.Hata("Ortak klasör yok");
                    var kok = Path.GetFullPath(Klasorler.OrtakKaynak) + "\\";
                    var yol = Path.GetFullPath(Path.Combine(kok, m["yol"]));
                    if (!yol.StartsWith(kok, StringComparison.OrdinalIgnoreCase) || !File.Exists(yol)) return Mesaj.Hata("Dosya bulunamadı");
                    var fi = new FileInfo(yol);
                    if (fi.Length > Sabitler.MaxCerceve - 1024 * 1024) return Mesaj.Hata("Dosya çok büyük");
                    var r = Mesaj.Basari();
                    using (var fs = new FileStream(yol, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete)) r.Veri = Ag.TamOku(fs, (int)fi.Length);
                    return r;
                }
            }
            return null;
        }

        // ================================================================== Ortak klasör eşitleme (yönetici → diğerleri)
        int ortakSuruyor;
        DateTime sonrakiOrtak = DateTime.Now.AddSeconds(40);

        public static void OrtakKaynakHazirla()
        {
            Klasorler.HerkeseAc(Klasorler.OrtakKaynak);
            var beni = Path.Combine(Klasorler.OrtakKaynak, "Beni oku.txt");
            if (!File.Exists(beni))
                File.WriteAllText(beni, "Bu klasöre koyduğunuz dosyalar birkaç dakika içinde tüm bilgisayarlarda\r\nMasaüstü > Sem Dosyalar > Ortak klasöründe görünür. Buradan silinen dosya orada da silinir.\r\n", Encoding.UTF8);
        }

        public void OrtakKontrol()
        {
            if (Ayar.Yonetici || !Ayar.Paylasim || Ayar.Anahtar == null || DateTime.Now < sonrakiOrtak) return;
            if (Interlocked.Exchange(ref ortakSuruyor, 1) == 1) return;
            sonrakiOrtak = DateTime.Now.AddMinutes(3);
            new Thread(() =>
            {
                try { OrtakEsitle(); }
                catch (Exception e) { Gunluk.Yaz("Ortak klasör: " + e.Message); }
                finally { Interlocked.Exchange(ref ortakSuruyor, 0); }
            }) { IsBackground = true }.Start();
        }

        void OrtakEsitle()
        {
            var yon = Kesif.Sor(1500).FirstOrDefault(k => k.Yonetici && !k.Ad.Equals(Environment.MachineName, StringComparison.OrdinalIgnoreCase));
            if (yon == null) return;
            var cevap = Ag.Gonder(yon.Ip, Ayar.Anahtar, new Mesaj("ORTAK_LISTE"), 15000);
            if (!cevap.Tamam) return;
            var kaynak = cevap["liste"].Split('\n').Select(x => x.Split('|')).Where(x => x.Length == 3 && x[0] != "")
                .ToDictionary(x => x[0], x => (boyut: long.Parse(x[1]), zaman: new DateTime(long.Parse(x[2]), DateTimeKind.Utc)), StringComparer.OrdinalIgnoreCase);
            var kok = Klasorler.Ortak;
            Directory.CreateDirectory(kok);
            var tamKok = Path.GetFullPath(kok) + "\\";
            int alinan = 0, silinen = 0;
            foreach (var kv in kaynak)
            {
                var yol = Path.GetFullPath(Path.Combine(kok, kv.Key));
                if (!yol.StartsWith(tamKok, StringComparison.OrdinalIgnoreCase)) continue;
                var fi = new FileInfo(yol);
                if (fi.Exists && fi.Length == kv.Value.boyut && Math.Abs((fi.LastWriteTimeUtc - kv.Value.zaman).TotalSeconds) < 2) continue;
                var m = new Mesaj("ORTAK_AL"); m["yol"] = kv.Key;
                var d = Ag.Gonder(yon.Ip, Ayar.Anahtar, m, 120000);
                if (!d.Tamam) continue;
                Directory.CreateDirectory(Path.GetDirectoryName(yol));
                File.WriteAllBytes(yol, d.Veri);
                File.SetLastWriteTimeUtc(yol, kv.Value.zaman);
                alinan++;
            }
            foreach (var f in Directory.GetFiles(kok, "*", SearchOption.AllDirectories))
            {
                var rel = f.Substring(tamKok.Length);
                if (!kaynak.ContainsKey(rel)) { try { File.Delete(f); silinen++; } catch { } }
            }
            foreach (var d in Directory.GetDirectories(kok, "*", SearchOption.AllDirectories).OrderByDescending(x => x.Length))
                try { if (!Directory.EnumerateFileSystemEntries(d).Any()) Directory.Delete(d); } catch { }
            if (alinan + silinen > 0) Gunluk.Yaz("Ortak klasör eşitlendi: " + alinan + " yeni/değişen, " + silinen + " silinen");
        }

        // ================================================================== LocalSend: bilgi
        const int LsPort = 53317;
        static readonly IPAddress LsGrup = IPAddress.Parse("224.0.0.167");
        string Parmak => "sem-" + Ayar.Parmakizi;
        string LsAd => Ayar.Yonetici ? "Sem Yazıcı" : Ayar.Isim;

        Dictionary<string, object> Bilgi(bool tam = true)
        {
            var d = new Dictionary<string, object>
            {
                ["alias"] = LsAd, ["version"] = "2.0", ["deviceModel"] = Ayar.Yonetici ? "Sem Yazıcı" : "Sem Ajan",
                ["deviceType"] = Ayar.Yonetici ? "server" : "desktop", ["fingerprint"] = Parmak, ["download"] = false,
            };
            if (tam) { d["port"] = LsPort; d["protocol"] = "http"; }
            return d;
        }

        /// <summary>Dışarıya gönderirken (bilgisayardan telefona) görünen bilgi: yöneticide de bilgisayarın kendi adı.</summary>
        Dictionary<string, object> GonderenBilgi()
        {
            var d = Bilgi(); d["alias"] = Ayar.Isim; return d;
        }

        static readonly JavaScriptSerializer Json = new JavaScriptSerializer { MaxJsonLength = 64 * 1024 * 1024 };

        // Ağda görülen LocalSend cihazları (telefonlar)
        public class LsCihaz { public string Parmak = "", Ad = "", Model = "", Tur = "", Ip = "", Protokol = "https"; public int Port = LsPort; public DateTime Goruldu; }
        readonly Dictionary<string, LsCihaz> cihazlar = new Dictionary<string, LsCihaz>();

        void CihazKaydet(Dictionary<string, object> d, string ip)
        {
            var parmak = Str(d, "fingerprint");
            if (parmak == "" || parmak == Parmak || parmak.StartsWith("sem-")) return;
            var c = new LsCihaz
            {
                Parmak = parmak, Ad = Str(d, "alias"), Model = Str(d, "deviceModel"), Tur = Str(d, "deviceType"), Ip = ip,
                Protokol = Str(d, "protocol") == "http" ? "http" : "https", Goruldu = DateTime.Now,
            };
            int p; if (int.TryParse(Str(d, "port"), out p) && p > 0) c.Port = p;
            lock (cihazlar) cihazlar[parmak] = c;
        }

        static string Str(Dictionary<string, object> d, string k) { object v; return d != null && d.TryGetValue(k, out v) && v != null ? Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture) : ""; }

        // ================================================================== LocalSend: çoklu yayın (keşif)
        UdpClient coklu;

        void CokluYayinDongu()
        {
            while (calisiyor)
            {
                try
                {
                    coklu = new UdpClient();
                    coklu.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                    coklu.Client.Bind(new IPEndPoint(IPAddress.Any, LsPort));
                    foreach (var ip in YerelIpler()) try { coklu.JoinMulticastGroup(LsGrup, ip); } catch { }
                    Duyur(true);
                    while (calisiyor)
                    {
                        var uzak = new IPEndPoint(IPAddress.Any, 0);
                        var b = coklu.Receive(ref uzak);
                        if (!Ayar.Paylasim) continue;
                        Dictionary<string, object> d;
                        try { d = Json.DeserializeObject(Encoding.UTF8.GetString(b)) as Dictionary<string, object>; } catch { continue; }
                        if (d == null || Str(d, "fingerprint") == Parmak) continue;
                        var ip = uzak.Address.ToString();
                        CihazKaydet(d, ip);
                        if (Str(d, "announce").ToLowerInvariant() == "true" || Str(d, "announcement").ToLowerInvariant() == "true")
                        {
                            var hedef = d;
                            ThreadPool.QueueUserWorkItem(_ => KayitCevabi(hedef, ip));
                        }
                    }
                }
                catch (Exception e) { if (calisiyor) { Gunluk.Yaz("LocalSend keşif: " + e.Message); Thread.Sleep(5000); } }
                finally { try { coklu?.Close(); } catch { } }
            }
        }

        static List<IPAddress> YerelIpler()
        {
            var l = new List<IPAddress>();
            try
            {
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up || ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                    foreach (var u in ni.GetIPProperties().UnicastAddresses)
                        if (u.Address.AddressFamily == AddressFamily.InterNetwork) l.Add(u.Address);
                }
            }
            catch { }
            return l;
        }

        /// <summary>Duyuru cevabı: önce HTTP ile kayıt, olmazsa çoklu yayınla "announce=false".</summary>
        void KayitCevabi(Dictionary<string, object> d, string ip)
        {
            var proto = Str(d, "protocol") == "http" ? "http" : "https";
            int port; if (!int.TryParse(Str(d, "port"), out port) || port <= 0) port = LsPort;
            try
            {
                var cevap = HttpIstek("POST", proto + "://" + ip + ":" + port + "/api/localsend/v2/register", Json.Serialize(Bilgi()), 4000);
                if (cevap != null) return;
            }
            catch { }
            Duyur(false);
        }

        void Duyur(bool duyuru)
        {
            if (!Ayar.Paylasim || !Ayar.Telefon) return;
            var d = Bilgi(); d["announce"] = duyuru; d["announcement"] = duyuru;
            var b = Encoding.UTF8.GetBytes(Json.Serialize(d));
            foreach (var ip in YerelIpler())
            {
                try
                {
                    using (var u = new UdpClient(new IPEndPoint(ip, 0)))
                    {
                        u.Client.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastTimeToLive, 2);
                        u.Send(b, b.Length, new IPEndPoint(LsGrup, LsPort));
                    }
                }
                catch { }
            }
        }

        // ================================================================== LocalSend: HTTP sunucusu (telefondan gelen)
        HttpListener http;

        void HttpDongu()
        {
            while (calisiyor)
            {
                try
                {
                    http = new HttpListener();
                    http.Prefixes.Add("http://+:" + LsPort + "/");
                    http.Start();
                    while (calisiyor)
                    {
                        var ctx = http.GetContext();
                        ThreadPool.QueueUserWorkItem(_ => HttpIsle(ctx));
                    }
                }
                catch (Exception e)
                {
                    if (!calisiyor) break;
                    Gunluk.Yaz("LocalSend sunucu: " + e.Message);
                    LocalSendKapat(); // kapıyı tutan eski LocalSend açıksa kapat
                    Thread.Sleep(30000);
                }
                finally { try { http?.Close(); } catch { } }
            }
        }

        void HttpIsle(HttpListenerContext ctx)
        {
            var q = ctx.Request; var r = ctx.Response;
            try
            {
                var yol = q.Url.AbsolutePath.TrimEnd('/');
                var ip = q.RemoteEndPoint.Address.IsIPv4MappedToIPv6 ? q.RemoteEndPoint.Address.MapToIPv4().ToString() : q.RemoteEndPoint.Address.ToString();
                if (!Ayar.Paylasim || !Ayar.Telefon) { Yanit(r, 403, null); return; }
                var v = yol.Replace("/api/localsend/v1/", "/api/localsend/v2/");
                switch (v)
                {
                    case "/api/localsend/v2/info":
                        Yanit(r, 200, Bilgi(false)); return;
                    case "/api/localsend/v2/register":
                    {
                        var d = JsonOku(q);
                        if (d != null) CihazKaydet(d, ip);
                        Yanit(r, 200, Bilgi(false)); return;
                    }
                    case "/api/localsend/v2/prepare-upload":
                    case "/api/localsend/v2/send-request":
                        HazirlikIsle(q, r, ip); return;
                    case "/api/localsend/v2/upload":
                    case "/api/localsend/v2/send":
                        YuklemeIsle(q, r, ip); return;
                    case "/api/localsend/v2/cancel":
                    {
                        var o = OturumBul(q.QueryString["sessionId"]);
                        if (o != null) lock (oturumlar) oturumlar.Remove(o.Id);
                        Yanit(r, 200, null); return;
                    }
                    default: Yanit(r, 404, null); return;
                }
            }
            catch (Exception e) { Gunluk.Yaz("LocalSend istek: " + e.Message); try { Yanit(r, 500, null); } catch { } }
        }

        static Dictionary<string, object> JsonOku(HttpListenerRequest q)
        {
            using (var sr = new StreamReader(q.InputStream, Encoding.UTF8))
            {
                var s = sr.ReadToEnd();
                try { return Json.DeserializeObject(s) as Dictionary<string, object>; } catch { return null; }
            }
        }

        static void Yanit(HttpListenerResponse r, int kod, object govde)
        {
            r.StatusCode = kod;
            if (govde != null)
            {
                var b = Encoding.UTF8.GetBytes(Json.Serialize(govde));
                r.ContentType = "application/json";
                r.ContentLength64 = b.Length;
                r.OutputStream.Write(b, 0, b.Length);
            }
            r.Close();
        }

        static readonly string[] Resimler = { ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tif", ".tiff" };

        void HazirlikIsle(HttpListenerRequest q, HttpListenerResponse r, string ip)
        {
            var d = JsonOku(q);
            if (d == null) { Yanit(r, 400, null); return; }
            var info = d.ContainsKey("info") ? d["info"] as Dictionary<string, object> : null;
            var files = d.ContainsKey("files") ? d["files"] as Dictionary<string, object> : null;
            if (info == null || files == null) { Yanit(r, 400, null); return; }
            CihazKaydet(info, ip);
            var kimden = Str(info, "alias"); if (kimden == "") kimden = "Telefon";
            var parmak = Str(info, "fingerprint");

            var liste = new List<(string id, string ad, long boyut, string tur, string onizleme)>();
            foreach (var kv in files)
            {
                var f = kv.Value as Dictionary<string, object>; if (f == null) continue;
                long b; long.TryParse(Str(f, "size"), out b);
                liste.Add((Str(f, "id") != "" ? Str(f, "id") : kv.Key, Str(f, "fileName"), b, Str(f, "fileType"), Str(f, "preview")));
            }
            if (liste.Count == 0) { Yanit(r, 204, null); return; }

            // Yazı mesajı: dosya aktarımı gerekmez, ekranda gösterilir.
            if (liste.All(x => x.tur.StartsWith("text/") && x.onizleme != ""))
            {
                foreach (var x in liste) servis.EkranaGonder("METIN", kimden, x.onizleme);
                Yanit(r, 204, null); return;
            }
            var dosyalar = liste.Where(x => !(x.tur.StartsWith("text/") && x.onizleme != "")).ToList();
            var ozet = DosyaOzeti(dosyalar.Select(x => (x.ad, x.boyut)).ToList());

            bool yazdir = Ayar.Yonetici;
            if (yazdir)
            {
                bool izinli = parmak != "" && Ayar.YaziciIzinli.Split(';').Contains(parmak);
                if (!izinli)
                {
                    int c = Sor("yazdir", kimden + " yazdırmak istiyor", ozet, 120);
                    if (c <= 0) { Yanit(r, 403, null); return; }
                    if (c == 2 && parmak != "") servis.AyarDegistir(a => a.YaziciIzinli = string.Join(";", a.YaziciIzinli.Split(';').Where(x => x != "").Concat(new[] { parmak }).Distinct().Skip(0)));
                }
            }
            else
            {
                int c = Sor("dosya", kimden + " dosya gönderiyor", ozet, 90);
                if (c <= 0) { Yanit(r, 403, null); return; }
            }

            var o = OturumAc(kimden, yazdir);
            var jetonlar = new Dictionary<string, object>();
            foreach (var x in dosyalar)
            {
                var j = Guid.NewGuid().ToString("N");
                o.Dosyalar[x.id] = (x.ad, x.boyut, j);
                jetonlar[x.id] = j;
            }
            o.Kalan = dosyalar.Count;
            Yanit(r, 200, new Dictionary<string, object> { ["sessionId"] = o.Id, ["files"] = jetonlar });
        }

        void YuklemeIsle(HttpListenerRequest q, HttpListenerResponse r, string ip)
        {
            var o = OturumBul(q.QueryString["sessionId"]);
            (string ad, long boyut, string jeton) f;
            if (o == null || !o.Dosyalar.TryGetValue(q.QueryString["fileId"] ?? "", out f) || f.jeton != q.QueryString["token"]) { Yanit(r, 403, null); return; }
            lock (o) o.Dosyalar.Remove(q.QueryString["fileId"]); // aynı jetonla ikinci kez yazılamasın
            var yol = Klasorler.Benzersiz(o.Klasor, f.ad);
            try
            {
                using (var fs = File.Create(yol)) q.InputStream.CopyTo(fs, 1 << 16);
            }
            catch { try { File.Delete(yol); } catch { } throw; }
            Yanit(r, 200, null);
            DosyaBitti(o, yol);
        }

        // ================================================================== Gönderme (yerel "Sem Paylaş" penceresinden)
        void PipeDongu()
        {
            while (calisiyor)
            {
                NamedPipeServerStream p = null;
                try
                {
                    var g = new PipeSecurity();
                    g.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null), PipeAccessRights.ReadWrite, AccessControlType.Allow));
                    g.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), PipeAccessRights.FullControl, AccessControlType.Allow));
                    p = new NamedPipeServerStream(Sabitler.PaylasPipe, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 4096, 4096, g);
                    p.WaitForConnection();
                    var baglanti = p; p = null;
                    new Thread(() => PipeIstemci(baglanti)) { IsBackground = true }.Start();
                }
                catch (Exception e) { Gunluk.Yaz("Paylaş kanalı: " + e.Message); Thread.Sleep(2000); }
                finally { p?.Dispose(); }
            }
        }

        void PipeIstemci(NamedPipeServerStream p)
        {
            using (p)
            {
                try
                {
                    var oku = new StreamReader(p, new UTF8Encoding(false));
                    var yaz = new StreamWriter(p, new UTF8Encoding(false)) { AutoFlush = true };
                    var yazKilit = new object();
                    Action<string[]> gonder = x => { lock (yazKilit) { try { yaz.WriteLine(EkranHatti.Paketle(x)); } catch { } } };
                    string satir;
                    while ((satir = oku.ReadLine()) != null)
                    {
                        var a = EkranHatti.Coz(satir);
                        if (a.Length == 0) continue;
                        if (a[0] == "CIHAZLAR") CihazlariYolla(gonder);
                        else if (a[0] == "GONDER" && a.Length >= 4)
                        {
                            var hedef = a[1]; var dosyalar = a[2].Split('\n').Where(x => x != "").ToList(); var metin = a[3];
                            new Thread(() => Gonder(hedef, dosyalar, metin, gonder)) { IsBackground = true }.Start();
                        }
                    }
                }
                catch { }
            }
        }

        void CihazlariYolla(Action<string[]> gonder)
        {
            Duyur(true); // telefonlar duyuruya kayıtla cevap verir
            var sem = new List<KesifSonucu>();
            try { sem = Kesif.Sor(1500); } catch { }
            gonder(new[] { "AYAR", Ayar.Paylasim ? "1" : "0", Ayar.Yonetici ? "1" : "0" });
            foreach (var k in sem.Where(k => !k.Ad.Equals(Environment.MachineName, StringComparison.OrdinalIgnoreCase)).OrderBy(k => k.Grup).ThenBy(k => k.Baslik))
                gonder(new[] { "CIHAZ", "sem:" + k.Ip, k.Baslik, k.Grup, k.Simge, k.Yonetici ? "yonetici" : "pc" });
            lock (cihazlar)
                foreach (var c in cihazlar.Values.Where(c => (DateTime.Now - c.Goruldu).TotalMinutes < 10).OrderBy(c => c.Ad))
                    gonder(new[] { "CIHAZ", "ls:" + c.Parmak, c.Ad, c.Model, "", c.Tur == "mobile" ? "telefon" : "diger" });
            gonder(new[] { "SON" });
        }

        void Gonder(string hedef, List<string> dosyalar, string metin, Action<string[]> gonder)
        {
            string ad = hedef;
            try
            {
                var fl = dosyalar.Select(f => new FileInfo(f)).Where(f => f.Exists).ToList();
                if (fl.Count == 0 && metin == "") throw new Exception("Gönderilecek dosya yok");
                if (hedef.StartsWith("sem:")) ad = SemGonder(hedef.Substring(4), fl, metin, (y) => gonder(new[] { "ILERLEME", hedef, y.ToString() }));
                else if (hedef.StartsWith("ls:")) ad = TelefonaGonder(hedef.Substring(3), fl, metin, (y) => gonder(new[] { "ILERLEME", hedef, y.ToString() }));
                else throw new Exception("Bilinmeyen hedef");
                // Gönderilenlerin bir kopyası (çok büyükler hariç)
                if (fl.Count > 0)
                {
                    var k = Klasorler.Gonderilen(ad);
                    foreach (var f in fl.Where(x => x.Length < 200L * 1024 * 1024)) try { File.Copy(f.FullName, Klasorler.Benzersiz(k, f.Name)); } catch { }
                }
                gonder(new[] { "BITTI", hedef, "1", fl.Count == 0 ? "Mesaj iletildi" : fl.Count + " dosya gönderildi" });
                Gunluk.Yaz("Gönderildi: " + ad + ", " + fl.Count + " dosya");
            }
            catch (Exception e) { gonder(new[] { "BITTI", hedef, "0", e.Message }); }
        }

        string SemGonder(string ip, List<FileInfo> fl, string metin, Action<int> ilerleme)
        {
            if (Ayar.Anahtar == null) throw new Exception("Sem Ajan kurulu değil");
            var buyuk = fl.FirstOrDefault(f => f.Length > Sabitler.MaxCerceve - 2 * 1024 * 1024);
            if (buyuk != null) throw new Exception(buyuk.Name + " çok büyük (en fazla 400 MB)");
            var ad = ip;
            try { var k = Kesif.Sor(800).FirstOrDefault(x => x.Ip == ip); if (k != null) ad = k.Baslik; } catch { }
            var sor = new Mesaj("PAYLAS_SOR");
            sor["gonderen"] = Ayar.Isim; sor["yonetici"] = Ayar.Yonetici ? "1" : "0"; sor["metin"] = metin;
            sor["dosyalar"] = string.Join("\n", fl.Select(f => f.Name.Replace("|", "_") + "|" + f.Length));
            ilerleme(0);
            var c = Ag.Gonder(ip, Ayar.Anahtar, sor, 100000);
            if (!c.Tamam) throw new Exception(c["aciklama"]);
            if (fl.Count == 0) return ad;
            long toplam = Math.Max(1, fl.Sum(f => f.Length)), giden = 0;
            foreach (var f in fl)
            {
                var m = new Mesaj("PAYLAS_DOSYA"); m["oturum"] = c["oturum"]; m["ad"] = f.Name; m["degisme"] = f.LastWriteTime.ToString("o");
                using (var fs = new FileStream(f.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete)) m.Veri = Ag.TamOku(fs, (int)f.Length);
                var d = Ag.Gonder(ip, Ayar.Anahtar, m, 300000);
                if (!d.Tamam) throw new Exception(d["aciklama"]);
                giden += f.Length;
                ilerleme((int)(giden * 100 / toplam));
            }
            return ad;
        }

        static string Mime(string ad)
        {
            switch (Path.GetExtension(ad).ToLowerInvariant())
            {
                case ".jpg": case ".jpeg": return "image/jpeg";
                case ".png": return "image/png";
                case ".gif": return "image/gif";
                case ".pdf": return "application/pdf";
                case ".mp4": return "video/mp4";
                case ".mp3": return "audio/mpeg";
                case ".txt": return "text/plain";
                case ".docx": return "application/vnd.openxmlformats-officedocument.wordprocessingml.document";
                case ".xlsx": return "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
                case ".pptx": return "application/vnd.openxmlformats-officedocument.presentationml.presentation";
                default: return "application/octet-stream";
            }
        }

        string TelefonaGonder(string parmak, List<FileInfo> fl, string metin, Action<int> ilerleme)
        {
            LsCihaz c; lock (cihazlar) cihazlar.TryGetValue(parmak, out c);
            if (c == null) throw new Exception("Telefon artık görünmüyor, LocalSend açık mı?");
            var taban = c.Protokol + "://" + c.Ip + ":" + c.Port + "/api/localsend/v2/";
            var files = new Dictionary<string, object>();
            var idler = new Dictionary<string, FileInfo>();
            foreach (var f in fl)
            {
                var id = Guid.NewGuid().ToString("N");
                idler[id] = f;
                files[id] = new Dictionary<string, object> { ["id"] = id, ["fileName"] = f.Name, ["size"] = f.Length, ["fileType"] = Mime(f.Name), ["sha256"] = null, ["preview"] = null };
            }
            if (metin != "")
            {
                var id = Guid.NewGuid().ToString("N");
                files[id] = new Dictionary<string, object> { ["id"] = id, ["fileName"] = id + ".txt", ["size"] = Encoding.UTF8.GetByteCount(metin), ["fileType"] = "text/plain", ["sha256"] = null, ["preview"] = metin };
            }
            ilerleme(0);
            var govde = Json.Serialize(new Dictionary<string, object> { ["info"] = GonderenBilgi(), ["files"] = files });
            int kod; string cevap;
            try { cevap = HttpIstek("POST", taban + "prepare-upload", govde, 120000, out kod); }
            catch (WebException e) when (e.Response is HttpWebResponse hr)
            {
                kod = (int)hr.StatusCode;
                throw new Exception(kod == 403 ? "Telefonda reddedildi" : kod == 401 ? "Telefonda PIN açık, kapatın" : kod == 409 ? "Telefon başka bir aktarımla meşgul" : "Telefon hata verdi (" + kod + ")");
            }
            if (kod == 204 || idler.Count == 0) return c.Ad;
            var d = Json.DeserializeObject(cevap) as Dictionary<string, object>;
            var oturum = Str(d, "sessionId");
            var jetonlar = d != null && d.ContainsKey("files") ? d["files"] as Dictionary<string, object> : null;
            if (jetonlar == null) throw new Exception("Telefon beklenmeyen cevap verdi");
            long toplam = Math.Max(1, fl.Sum(f => f.Length)), giden = 0;
            foreach (var kv in idler)
            {
                if (!jetonlar.ContainsKey(kv.Key)) continue; // telefon bu dosyayı istemedi
                var url = taban + "upload?sessionId=" + Uri.EscapeDataString(oturum) + "&fileId=" + kv.Key + "&token=" + Uri.EscapeDataString(Str(jetonlar, kv.Key));
                var istek = Istek("POST", url, 30 * 60 * 1000);
                istek.ContentType = "application/octet-stream";
                istek.ContentLength = kv.Value.Length;
                istek.AllowWriteStreamBuffering = false;
                using (var fs = new FileStream(kv.Value.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (var s = istek.GetRequestStream())
                {
                    var buf = new byte[1 << 16]; int n; long simdiki = giden; int son = -1;
                    while ((n = fs.Read(buf, 0, buf.Length)) > 0)
                    {
                        s.Write(buf, 0, n); simdiki += n;
                        int y = (int)(simdiki * 100 / toplam);
                        if (y != son) { son = y; ilerleme(y); }
                    }
                }
                using (var yr = (HttpWebResponse)istek.GetResponse()) { }
                giden += kv.Value.Length;
            }
            return c.Ad;
        }

        static HttpWebRequest Istek(string yontem, string url, int zamanAsimi)
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            var r = (HttpWebRequest)WebRequest.Create(url);
            r.Method = yontem; r.Timeout = zamanAsimi; r.ReadWriteTimeout = zamanAsimi; r.Proxy = null; r.KeepAlive = false;
            // Telefonlardaki LocalSend kendi imzaladığı sertifikayı kullanır; yerel ağda olduğu için kabul edilir.
            r.ServerCertificateValidationCallback = (a, b, c, d) => true;
            return r;
        }

        static string HttpIstek(string yontem, string url, string json, int zamanAsimi) { int _; return HttpIstek(yontem, url, json, zamanAsimi, out _); }

        static string HttpIstek(string yontem, string url, string json, int zamanAsimi, out int kod)
        {
            var r = Istek(yontem, url, zamanAsimi);
            if (json != null)
            {
                var b = Encoding.UTF8.GetBytes(json);
                r.ContentType = "application/json"; r.ContentLength = b.Length;
                using (var s = r.GetRequestStream()) s.Write(b, 0, b.Length);
            }
            using (var yr = (HttpWebResponse)r.GetResponse())
            using (var sr = new StreamReader(yr.GetResponseStream(), Encoding.UTF8))
            {
                kod = (int)yr.StatusCode;
                return sr.ReadToEnd();
            }
        }

        // ================================================================== Eski LocalSend programını kaldırma (Sem onun yerini alır)
        static void LocalSendKapat()
        {
            foreach (var ad in new[] { "localsend_app", "LocalSend", "localsend" })
                foreach (var p in Process.GetProcessesByName(ad)) try { p.Kill(); p.WaitForExit(3000); } catch { }
        }

        void LocalSendKaldir()
        {
            if (Ayar.LocalSendKaldirildi) return;
            LocalSendKapat();
            int bulunan = 0;
            var anahtarlar = new List<RegistryKey>();
            foreach (var gorunum in new[] { RegistryView.Registry64, RegistryView.Registry32 })
                try { anahtarlar.Add(RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, gorunum).OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall")); } catch { }
            try
            {
                foreach (var sid in Registry.Users.GetSubKeyNames().Where(x => x.StartsWith("S-1-5-21-") && !x.EndsWith("_Classes")))
                    try { anahtarlar.Add(Registry.Users.OpenSubKey(sid + @"\Software\Microsoft\Windows\CurrentVersion\Uninstall")); } catch { }
            }
            catch { }
            foreach (var kok in anahtarlar.Where(x => x != null))
            {
                foreach (var alt in kok.GetSubKeyNames())
                {
                    try
                    {
                        using (var k = kok.OpenSubKey(alt))
                        {
                            var ad = k?.GetValue("DisplayName") as string ?? "";
                            if (ad.IndexOf("LocalSend", StringComparison.OrdinalIgnoreCase) < 0) continue;
                            var kaldir = (k.GetValue("QuietUninstallString") as string) ?? (k.GetValue("UninstallString") as string) ?? "";
                            if (kaldir == "") continue;
                            bulunan++;
                            string exe, arg;
                            KomutAyir(kaldir, out exe, out arg);
                            if (exe.EndsWith("msiexec.exe", StringComparison.OrdinalIgnoreCase)) arg = arg.Replace("/I", "/X").Replace("/i", "/x") + " /qn /norestart";
                            else if (!arg.ToUpperInvariant().Contains("SILENT")) arg += " /VERYSILENT /SUPPRESSMSGBOXES /NORESTART";
                            using (var p = Process.Start(new ProcessStartInfo(exe, arg) { UseShellExecute = false, CreateNoWindow = true })) p.WaitForExit(120000);
                        }
                    }
                    catch (Exception e) { Gunluk.Yaz("LocalSend kaldırma: " + e.Message); }
                }
                kok.Dispose();
            }
            // Microsoft Store sürümü
            try
            {
                using (var p = Process.Start(new ProcessStartInfo("powershell.exe",
                    "-NoProfile -ExecutionPolicy Bypass -Command \"Get-AppxPackage -AllUsers *localsend* | Remove-AppxPackage -AllUsers -ErrorAction SilentlyContinue\"")
                    { UseShellExecute = false, CreateNoWindow = true })) p.WaitForExit(120000);
            }
            catch { }
            LocalSendKapat();
            servis.AyarDegistir(a => a.LocalSendKaldirildi = true);
            Gunluk.Yaz(bulunan > 0 ? "Eski LocalSend programı kaldırıldı (yerini Sem aldı)" : "LocalSend kontrolü tamam");
        }

        static void KomutAyir(string s, out string exe, out string arg)
        {
            s = s.Trim();
            if (s.StartsWith("\"")) { int i = s.IndexOf('"', 1); exe = s.Substring(1, i - 1); arg = s.Substring(i + 1).Trim(); return; }
            int e = s.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
            if (e > 0) { exe = s.Substring(0, e + 4); arg = s.Substring(e + 4).Trim(); return; }
            exe = s; arg = "";
        }
    }
}
