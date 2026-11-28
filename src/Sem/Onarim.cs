// Sağlık kontrolleri (Teşhis açılınca okunur, hiçbir şeyi değiştirmez) ve onarım işlemleri.
// Kurallar: hiçbir onarım kendiliğinden çalışmaz; ayar değiştirenler eski hali saklar ("Geri al");
// sistem dosyalarına dokunanlardan önce geri yükleme noktası alınır; kullanıcı dosyası silinmez;
// yeniden başlatma gerektirenler bilgisayarı kendisi başlatmaz.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Management;
using System.Net;
using System.Net.NetworkInformation;
using System.ServiceProcess;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using Sem.Ortak;

namespace Sem.Ajan
{
    static partial class Performans
    {
        static readonly HashSet<string> Uzun = new HashSet<string> { "disk", "temp", "sfc", "dism", "defender", "wuonar", "aghafif", "checkup", "aygit", "sicaklik", "tamvirus" };

        // ================================================================ Kontroller (salt okuma)
        static IEnumerable<EventRecord> Olaylar(string kayit, string sorgu, int en = 200)
        {
            EventLogReader rd = null;
            try { rd = new EventLogReader(new EventLogQuery(kayit, PathType.LogName, sorgu) { ReverseDirection = true }); } catch { }
            if (rd == null) yield break;
            using (rd)
                for (int n = 0; n < en; n++)
                {
                    EventRecord e = null;
                    try { e = rd.ReadEvent(); } catch { }
                    if (e == null) yield break;
                    yield return e;
                }
        }

        static string Ozellik(EventRecord e, int i) { try { return Convert.ToString(e.Properties[i].Value) ?? ""; } catch { return ""; } }

        static string XmlVeri(EventRecord e, string ad)
        {
            try { var m = Regex.Match(e.ToXml(), "<Data Name=['\"]" + Regex.Escape(ad) + "['\"]>([^<]*)</Data>"); return m.Success ? m.Groups[1].Value : ""; } catch { return ""; }
        }

        static string Zaman(long gun) => " and TimeCreated[timediff(@SystemTime) <= " + gun * 86400000L + "]";

        static IEnumerable<ManagementObject> W(string kapsam, string sorgu)
        {
            var l = new List<ManagementObject>();
            try { using (var s = new ManagementObjectSearcher(kapsam, sorgu)) foreach (ManagementObject o in s.Get()) l.Add(o); } catch { }
            return l;
        }
        static string V(ManagementObject o, string ad) { try { return Convert.ToString(o[ad])?.Trim() ?? ""; } catch { return ""; } }

        static void Kontrol(Mesaj r)
        {
            void Dene(Action a) { try { a(); } catch { } }

            // ---- Çökmeler
            Dene(() =>
            {
                int n = 0; string kod = "";
                foreach (var e in Olaylar("System", "*[System[Provider[@Name='Microsoft-Windows-WER-SystemErrorReporting'] and EventID=1001" + Zaman(30) + "]]"))
                    using (e) { n++; if (kod == "") { kod = Ozellik(e, 0).Split(' ')[0]; } }
                r["mavi_ekran"] = n.ToString(); r["mavi_ekran_kod"] = kod;
            });
            Dene(() => r["beklenmedik"] = Olaylar("System", "*[System[Provider[@Name='EventLog'] and EventID=6008" + Zaman(7) + "]]").Count(e => { e.Dispose(); return true; }).ToString());
            Dene(() =>
            {
                var say = new Dictionary<string, (int c, int h)>(StringComparer.OrdinalIgnoreCase);
                foreach (var e in Olaylar("Application", "*[System[((Provider[@Name='Application Error'] and EventID=1000) or (Provider[@Name='Application Hang'] and EventID=1002))" + Zaman(7) + "]]", 300))
                    using (e)
                    {
                        var ad = Ozellik(e, 0); if (ad == "") continue;
                        say.TryGetValue(ad, out var v);
                        say[ad] = e.Id == 1000 ? (v.c + 1, v.h) : (v.c, v.h + 1);
                    }
                r["program_cokme"] = string.Join(";", say.OrderByDescending(x => x.Value.c + x.Value.h).Take(4).Select(x => Satir.T(x.Key).Replace(";", " ").Replace(":", " ") + ":" + x.Value.c + ":" + x.Value.h));
            });
            Dene(() => r["ekran_surucu"] = Olaylar("System", "*[System[Provider[@Name='Display'] and EventID=4101" + Zaman(7) + "]]").Count(e => { e.Dispose(); return true; }).ToString());

            // ---- Windows
            Dene(() =>
            {
                bool bekliyor = false;
                using (var k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending")) bekliyor |= k != null;
                using (var k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired")) bekliyor |= k != null;
                r["yeniden_bekliyor"] = bekliyor ? "1" : "0";
            });
            Dene(() =>
            {
                using (var k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
                {
                    int build; int.TryParse(Convert.ToString(k?.GetValue("CurrentBuildNumber")), out build);
                    var ad = Convert.ToString(k?.GetValue("ProductName")) ?? "";
                    if (build >= 22000) ad = ad.Replace("Windows 10", "Windows 11");
                    r["isletim"] = (ad + " " + (Convert.ToString(k?.GetValue("DisplayVersion")) ?? "")).Trim(); r["isletim_build"] = build.ToString();
                }
            });
            Dene(() =>
            {
                DateTime son = DateTime.MinValue;
                foreach (var o in W(@"root\cimv2", "SELECT InstalledOn FROM Win32_QuickFixEngineering"))
                {
                    DateTime d; var t = V(o, "InstalledOn");
                    if (DateTime.TryParseExact(t, new[] { "M/d/yyyy", "MM/dd/yyyy", "d.M.yyyy", "dd.MM.yyyy", "yyyyMMdd" }, CultureInfo.InvariantCulture, DateTimeStyles.None, out d) && d > son && d <= DateTime.Now.AddDays(1)) son = d;
                }
                if (son > DateTime.MinValue) r["son_guncelleme"] = son.ToString("yyyy-MM-dd");
            });
            Dene(() => r["wu_hata"] = Olaylar("System", "*[System[Provider[@Name='Microsoft-Windows-WindowsUpdateClient'] and EventID=20" + Zaman(14) + "]]").Count(e => { e.Dispose(); return true; }).ToString());
            Dene(() => r["aygit_sorun"] = string.Join(";", SorunluAygitlar().Take(5)));
            r["saat_utc"] = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            Dene(() => r["sayfa_dosyasi"] = W(@"root\cimv2", "SELECT Name FROM Win32_PageFileUsage").Count().ToString());

            // ---- Güvenlik
            Dene(() =>
            {
                foreach (var o in W(@"root\Microsoft\Windows\Defender", "SELECT AntivirusEnabled, RealTimeProtectionEnabled, AntivirusSignatureAge, QuickScanAge FROM MSFT_MpComputerStatus"))
                {
                    r["def_etkin"] = V(o, "AntivirusEnabled") == "True" ? "1" : "0";
                    r["def_anlik"] = V(o, "RealTimeProtectionEnabled") == "True" ? "1" : "0";
                    r["def_imza_gun"] = V(o, "AntivirusSignatureAge"); r["def_tarama_gun"] = V(o, "QuickScanAge");
                }
            });
            Dene(() =>
            {
                var adlar = new List<string>(); int n = 0;
                foreach (var e in Olaylar("Microsoft-Windows-Windows Defender/Operational", "*[System[EventID=1116" + Zaman(7) + "]]", 100))
                    using (e) { n++; var a = XmlVeri(e, "Threat Name"); if (a != "" && !adlar.Contains(a) && adlar.Count < 3) adlar.Add(a); }
                r["tehdit"] = n.ToString(); r["tehdit_ad"] = string.Join(", ", adlar);
            });
            Dene(() =>
            {
                var l = new List<string>();
                foreach (var o in W(@"root\SecurityCenter2", "SELECT displayName, productState FROM AntiVirusProduct"))
                {
                    int st; int.TryParse(V(o, "productState"), out st);
                    l.Add(V(o, "displayName").Replace(";", " ").Replace(":", " ") + ":" + (((st >> 12) & 0xF) == 1 ? "1" : "0"));
                }
                r["antivirus"] = string.Join(";", l);
            });

            // ---- Ağ
            Dene(() => Ag(r));

            // ---- Açılış
            Dene(() => { var l = AcilisListesi(); r["acilis_liste"] = string.Join(";", l.Select(x => x.ad.Replace(";", " ")).Distinct().Take(30)); r["acilis_adet"] = l.Count.ToString(); });
            Dene(() =>
            {
                var sureler = new List<int>();
                foreach (var e in Olaylar("Microsoft-Windows-Diagnostics-Performance/Operational", "*[System[EventID=100]]", 5))
                    using (e) { int ms; if (int.TryParse(XmlVeri(e, "BootTime"), out ms) && ms > 0) sureler.Add(ms); }
                if (sureler.Count > 0) r["acilis_sn"] = ((int)(sureler.Average() / 1000)).ToString();
            });

            // ---- Arka plan servisleri (HDD'de diski meşgul edenler)
            Dene(() => r["sysmain"] = ServisBaslangic("SysMain").ToString());
            Dene(() => r["wsearch"] = ServisBaslangic("WSearch").ToString());

            // ---- Bellek testi sonucu (en son yapılan)
            Dene(() =>
            {
                foreach (var e in Olaylar("System", "*[System[Provider[@Name='Microsoft-Windows-MemoryDiagnostics-Results'] and (EventID=1201 or EventID=1202 or EventID=1101 or EventID=1102)]]", 1))
                    using (e) r["bellek_test"] = (e.TimeCreated ?? DateTime.Now).ToString("yyyy-MM-dd") + "," + (e.Id == 1201 || e.Id == 1101 ? "ok" : "hata");
            });
            Dene(() => { var o = Calistir("bcdedit.exe", "/enum {bootmgr}", 15000); r["bellek_bekliyor"] = o.IndexOf("bootsequence", StringComparison.OrdinalIgnoreCase) >= 0 ? "1" : "0"; });
        }

        static List<string> SorunluAygitlar()
        {
            var l = new List<string>();
            // 22 devre dışı bırakılmış, 45 takılı değil: sorun sayılmaz
            foreach (var o in W(@"root\cimv2", "SELECT Name, ConfigManagerErrorCode FROM Win32_PnPEntity WHERE ConfigManagerErrorCode <> 0 AND ConfigManagerErrorCode <> 22 AND ConfigManagerErrorCode <> 45"))
                l.Add((V(o, "Name") == "" ? "Bilinmeyen aygıt" : V(o, "Name")).Replace(";", " ").Replace(":", " ") + ":" + V(o, "ConfigManagerErrorCode"));
            return l;
        }

        static int ServisBaslangic(string ad)
        {
            using (var k = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\" + ad)) return k?.GetValue("Start") is int n ? n : -1;
        }

        static NetworkInterface AnaAg() =>
            NetworkInterface.GetAllNetworkInterfaces().Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback && n.NetworkInterfaceType != NetworkInterfaceType.Tunnel)
                .Where(n => { try { return n.GetIPProperties().GatewayAddresses.Any(g => g.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork); } catch { return false; } })
                .OrderByDescending(n => n.Speed).FirstOrDefault();

        static (int kayip, int ms) Ping(IPAddress hedef, int adet)
        {
            int ok = 0; long top = 0;
            using (var p = new Ping())
                for (int i = 0; i < adet; i++)
                    try { var c = p.Send(hedef, 500); if (c.Status == IPStatus.Success) { ok++; top += c.RoundtripTime; } } catch { }
            return ((adet - ok) * 100 / adet, ok > 0 ? (int)(top / ok) : -1);
        }

        static void Ag(Mesaj r)
        {
            var n = AnaAg(); if (n == null) { r["ag_ad"] = ""; return; }
            var ip = n.GetIPProperties();
            r["ag_ad"] = n.Description; r["ag_hiz"] = (n.Speed / 1000000).ToString(); r["ag_kablosuz"] = n.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 ? "1" : "0";
            try { r["ag_dhcp"] = ip.GetIPv4Properties().IsDhcpEnabled ? "1" : "0"; } catch { }
            var gw = ip.GatewayAddresses.Select(g => g.Address).FirstOrDefault(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork);
            if (gw != null) { var p = Ping(gw, 10); r["ag_gecit"] = gw.ToString(); r["ag_kayip"] = p.kayip.ToString(); r["ag_ms"] = p.ms.ToString(); }
            var sw = Stopwatch.StartNew();
            try { Dns.GetHostAddresses("www.msftconnecttest.com"); r["dns_ms"] = sw.ElapsedMilliseconds.ToString(); } catch { r["dns_ms"] = "-1"; }
            r["ip_cakisma"] = Olaylar("System", "*[System[Provider[@Name='Tcpip'] and EventID=4199" + Zaman(7) + "]]").Count(e => { e.Dispose(); return true; }).ToString();
            if (r["ag_kablosuz"] == "1")
            {
                var m = Regex.Match(Calistir("netsh.exe", "wlan show interfaces", 10000), @"(Signal|Sinyal)\s*:\s*(\d+)\s*%", RegexOptions.IgnoreCase);
                if (m.Success) r["wifi_sinyal"] = m.Groups[2].Value;
            }
        }

        // ---- Açılış programları: Run anahtarları ve Başlangıç klasörleri (Görev Yöneticisi'nde kapatılmış olanlar hariç)
        class AcilisOge { public string ad, kok, yol, deger, dosya, onay; }

        static bool OnayliKapali(RegistryKey kok, string onayYolu, string ad)
        {
            try { using (var k = kok.OpenSubKey(onayYolu)) return k?.GetValue(ad) is byte[] b && b.Length > 0 && (b[0] & 1) == 1; } catch { return false; }
        }

        static List<AcilisOge> AcilisListesi()
        {
            var l = new List<AcilisOge>();
            const string onay = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\";
            void Anahtar(RegistryKey kok, string kokAd, string yol, string onayAlt)
            {
                try
                {
                    using (var k = kok.OpenSubKey(yol))
                        if (k != null)
                            foreach (var v in k.GetValueNames().Where(x => x != ""))
                                if (!OnayliKapali(kok, onay + onayAlt, v)) l.Add(new AcilisOge { ad = v, kok = kokAd, yol = yol, deger = Convert.ToString(k.GetValue(v)) });
                }
                catch { }
            }
            Anahtar(Registry.LocalMachine, "HKLM", @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", "Run");
            Anahtar(Registry.LocalMachine, "HKLM", @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run", "Run32");
            try
            {
                foreach (var sid in Registry.Users.GetSubKeyNames().Where(x => x.StartsWith("S-1-5-21-") && !x.EndsWith("_Classes")))
                    using (var u = Registry.Users.OpenSubKey(sid))
                        if (u != null) Anahtar(u, "HKU\\" + sid, @"Software\Microsoft\Windows\CurrentVersion\Run", "Run");
            }
            catch { }
            var klasorler = new List<string> { Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup) };
            try { foreach (var u in Directory.GetDirectories(@"C:\Users")) klasorler.Add(Path.Combine(u, @"AppData\Roaming\Microsoft\Windows\Start Menu\Programs\Startup")); } catch { }
            foreach (var kl in klasorler.Where(Directory.Exists))
                try
                {
                    foreach (var f in Directory.GetFiles(kl).Where(x => !x.EndsWith("desktop.ini", StringComparison.OrdinalIgnoreCase)))
                        l.Add(new AcilisOge { ad = Path.GetFileNameWithoutExtension(f), dosya = f });
                }
                catch { }
            // Sem'in kendisi listede gösterilmez
            return l.Where(x => x.ad.IndexOf("Sem", StringComparison.OrdinalIgnoreCase) < 0 || x.ad.Length > 12).Where(x => !(x.deger ?? x.dosya ?? "").EndsWith("Sem.exe", StringComparison.OrdinalIgnoreCase) && (x.deger ?? "").IndexOf("Sem.exe", StringComparison.OrdinalIgnoreCase) < 0).ToList();
        }

        // ================================================================ Geri al kaydı
        static string GeriKlasor => Path.Combine(AjanAyar.Klasor, "geri");

        static void GeriYaz(string id, Dictionary<string, string> d)
        {
            try { Directory.CreateDirectory(GeriKlasor); File.WriteAllLines(Path.Combine(GeriKlasor, id + ".txt"), d.Select(x => x.Key + "=" + x.Value), Encoding.UTF8); } catch { }
        }

        static Dictionary<string, string> GeriOku(string id)
        {
            var d = new Dictionary<string, string>();
            try { foreach (var x in File.ReadAllLines(Path.Combine(GeriKlasor, id + ".txt"), Encoding.UTF8)) { int i = x.IndexOf('='); if (i > 0) d[x.Substring(0, i)] = x.Substring(i + 1); } } catch { }
            return d;
        }

        static Mesaj GeriliBasari(string metin, bool geri) { var m = Mesaj.Basari(metin); if (geri) m["ek"] = "geri"; return m; }

        // ================================================================ Kısa işlemler
        static Mesaj KisaYeni(string islem, string ad, Kayit kayit)
        {
            switch (islem)
            {
                case "saat":
                {
                    Calistir("sc.exe", "config w32time start= auto", 15000);
                    Calistir("net.exe", "start w32time", 30000);
                    var o = Calistir("w32tm.exe", "/resync /force", 30000);
                    bool ok = o.IndexOf("success", StringComparison.OrdinalIgnoreCase) >= 0 || o.IndexOf("başar", StringComparison.OrdinalIgnoreCase) >= 0 || o.IndexOf("tamamland", StringComparison.OrdinalIgnoreCase) >= 0;
                    var m = ok ? Mesaj.Basari("Saat internetten eşitlendi.") : Mesaj.Hata("Saat eşitlenemedi: " + o.Split('\n').LastOrDefault(x => x.Trim() != ""));
                    m["saat_utc"] = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture); return m;
                }
                case "acilis":
                {
                    var o = AcilisListesi().FirstOrDefault(x => x.ad == ad);
                    if (o == null) return Mesaj.Basari(ad + " zaten açılışta başlamıyor");
                    var d = new Dictionary<string, string> { ["tur"] = "acilis", ["ad"] = ad };
                    if (o.dosya != null)
                    {
                        var hedef = Path.Combine(GeriKlasor, kayit.Id + "_" + Path.GetFileName(o.dosya));
                        Directory.CreateDirectory(GeriKlasor); File.Move(o.dosya, hedef);
                        d["dosya"] = o.dosya; d["yedek"] = hedef;
                    }
                    else
                    {
                        var kok = o.kok == "HKLM" ? Registry.LocalMachine : Registry.Users.OpenSubKey(o.kok.Substring(4), true);
                        using (var k = (o.kok == "HKLM" ? kok : kok).OpenSubKey(o.yol, true)) k.DeleteValue(o.ad);
                        d["kok"] = o.kok; d["yol"] = o.yol; d["deger"] = o.deger;
                    }
                    GeriYaz(kayit.Id, d);
                    return GeriliBasari(ad + " artık açılışta başlamayacak (program kaldırılmadı, istenirse elle açılabilir). Sonraki açılışta geçerli.", true);
                }
                case "sysmain":
                {
                    int eski = ServisBaslangic("SysMain");
                    if (eski == 4) return Mesaj.Basari("SysMain zaten kapalı");
                    GeriYaz(kayit.Id, new Dictionary<string, string> { ["tur"] = "sysmain", ["start"] = eski.ToString() });
                    using (var k = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\SysMain", true)) k.SetValue("Start", 4, RegistryValueKind.DWord);
                    try { using (var s = new ServiceController("SysMain")) if (s.Status == ServiceControllerStatus.Running) { s.Stop(); s.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(30)); } } catch { }
                    return GeriliBasari("SysMain kapatıldı; klasik diskte arka planda sürekli okuma azalır. Hemen geçerli.", true);
                }
                case "bellek":
                {
                    var o = Calistir("bcdedit.exe", "/bootsequence {memdiag}", 30000);
                    if (Calistir("bcdedit.exe", "/enum {bootmgr}", 15000).IndexOf("bootsequence", StringComparison.OrdinalIgnoreCase) < 0) return Mesaj.Hata("Bellek testi ayarlanamadı: " + o);
                    GeriYaz(kayit.Id, new Dictionary<string, string> { ["tur"] = "bellek" });
                    return GeriliBasari("Bellek testi sonraki açılışa ayarlandı. O açılış 15-20 dakika sürer (mavi ekranda ilerler). Sonuç Teşhis'te görünür.", true);
                }
                case "agtam":
                {
                    var n = AnaAg();
                    bool sabit = NetworkInterface.GetAllNetworkInterfaces().Where(x => x.OperationalStatus == OperationalStatus.Up && x.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                        .Any(x => { try { return x.GetIPProperties().UnicastAddresses.Any(u => u.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork) && !x.GetIPProperties().GetIPv4Properties().IsDhcpEnabled; } catch { return false; } });
                    if (sabit) return Mesaj.Hata("Bu bilgisayarda elle girilmiş (sabit) IP var; tam sıfırlama bu ayarı sileceği için yapılmadı. Hafif onarımı deneyin.");
                    var nokta = GeriYuklemeNoktasi(kayit, "Ağ sıfırlama");
                    Calistir("netsh.exe", "winsock reset", 60000);
                    Calistir("netsh.exe", "int ip reset", 60000);
                    Calistir("ipconfig.exe", "/flushdns", 15000);
                    return Mesaj.Basari("Ağ ayarları sıfırlandı. Bilgisayar yeniden başlatılınca geçerli olur." + nokta);
                }
                case "geri": return GeriAl(ad, kayit);
            }
            return Mesaj.Hata("Bilinmeyen işlem");
        }

        static Mesaj GeriAl(string id, Kayit kayit)
        {
            var d = GeriOku(id);
            if (!d.ContainsKey("tur")) return Mesaj.Hata("Geri alma bilgisi bulunamadı");
            Mesaj m;
            switch (d["tur"])
            {
                case "guc":
                    Calistir("powercfg.exe", "/setactive " + d["guid"]);
                    m = Mesaj.Basari("Güç ayarı eski haline döndü"); break;
                case "hizlibaslat":
                    using (var r = Registry.LocalMachine.CreateSubKey(@"SYSTEM\CurrentControlSet\Control\Session Manager\Power")) r.SetValue("HiberbootEnabled", 1, RegistryValueKind.DWord);
                    m = Mesaj.Basari("Hızlı başlatma yeniden açıldı"); break;
                case "acilis":
                    if (d.ContainsKey("yedek")) File.Move(d["yedek"], d["dosya"]);
                    else
                    {
                        var kok = d["kok"] == "HKLM" ? Registry.LocalMachine : Registry.Users.OpenSubKey(d["kok"].Substring(4), true);
                        using (var k = kok.CreateSubKey(d["yol"])) k.SetValue(d["ad"], d["deger"]);
                    }
                    m = Mesaj.Basari(d["ad"] + " yeniden açılışta başlayacak"); break;
                case "sysmain":
                {
                    int st; int.TryParse(d["start"], out st); if (st < 2) st = 2;
                    using (var k = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\SysMain", true)) k.SetValue("Start", st, RegistryValueKind.DWord);
                    if (st == 2) try { using (var s = new ServiceController("SysMain")) s.Start(); } catch { }
                    m = Mesaj.Basari("SysMain yeniden açıldı"); break;
                }
                case "sicaklik":
                {
                    var k = Sensor.SurucuKaldir();
                    if (!k.ok) return Mesaj.Hata(k.metin);
                    m = Mesaj.Basari(k.metin); break;
                }
                case "bellek":
                    Calistir("bcdedit.exe", "/deletevalue {bootmgr} bootsequence", 15000);
                    m = Mesaj.Basari("Bellek testi iptal edildi"); break;
                default: return Mesaj.Hata("Bu işlem geri alınamaz");
            }
            // Asıl kaydı "geri alındı" olarak işaretle
            lock (ik)
            {
                var asil = KayitlariOku().FirstOrDefault(x => x.Id == id);
                if (asil != null) { asil.Ek = "gerialindi"; Yaz(asil); }
            }
            try { File.Delete(Path.Combine(GeriKlasor, id + ".txt")); } catch { }
            kayit.Ad = IslemAdiKisa(KayitlariOku().FirstOrDefault(x => x.Id == id)?.Islem ?? "");
            return m;
        }

        static string IslemAdiKisa(string islem) => islem;

        // ================================================================ Uzun işlemler (arka planda, ilerlemeli)
        static void UzunCalistir(Kayit r)
        {
            (bool ok, string sonuc, string ek) x;
            switch (r.Islem)
            {
                case "disk": x = DiskTaraIc(r, ""); break;
                case "temp": TempTemizle(r); return;
                case "sfc": { var n = GeriYuklemeNoktasi(r, "Sistem dosyası onarımı"); x = Sfc(r, ""); x.sonuc += n; break; }
                case "dism": { var n = GeriYuklemeNoktasi(r, "Windows onarımı"); x = Dism(r, ""); x.sonuc += n; break; }
                case "defender": x = Defender(r, ""); break;
                case "tamvirus": x = Defender(r, "", true); break;
                case "wuonar": { var n = GeriYuklemeNoktasi(r, "Windows Update onarımı"); x = WuOnar(r); x.sonuc += n; break; }
                case "aghafif": x = AgHafif(r); break;
                case "aygit": x = AygitTara(r); break;
                case "checkup": x = Checkup(r); break;
                case "sicaklik":
                {
                    r.Asama = "Sürücü indiriliyor ve kuruluyor"; r.Ilerleme = "30"; Yaz(r);
                    var k = Sensor.SurucuKur();
                    if (k.ok)
                    {
                        GeriYaz(r.Id, new Dictionary<string, string> { ["tur"] = "sicaklik" });
                        r.Asama = "Sıcaklık okunuyor"; r.Ilerleme = "80"; Yaz(r);
                        var d = Sensor.Oku();
                        string t; d.TryGetValue("cpu_sicaklik", out t);
                        x = t != null ? (true, k.metin + ". İşlemci şu an " + t + "°" + (d.TryGetValue("fanlar", out var f) && f != "" ? ", fanlar: " + f.Replace(";", ", ").Replace(":", " ") + " rpm" : "") + ".", "geri")
                                      : (true, k.metin + " ama sıcaklık okunamadı" + (d.TryGetValue("hata", out var h) ? " (" + h + ")" : "") + ". Geri alabilirsiniz.", "geri");
                    }
                    else x = (false, k.metin, "");
                    break;
                }
                default: x = (false, "Bilinmeyen işlem", ""); break;
            }
            r.Asama = ""; Bitir(r, x.ok, x.sonuc, x.ek);
        }

        /// <summary>Komutu çalıştırır, çıktısını satır satır (\r dahil) verir; çıkış kodu ve tüm metni döner.</summary>
        static (int kod, string metin) Izle(string exe, string arg, Encoding enc, Action<string> satir, int dk)
        {
            var psi = new ProcessStartInfo(exe, arg) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true, StandardOutputEncoding = enc };
            var tum = new StringBuilder(); var s = new StringBuilder();
            using (var x = Process.Start(psi))
            {
                try { x.PriorityClass = ProcessPriorityClass.BelowNormal; } catch { }
                x.ErrorDataReceived += (a, b) => { if (b.Data != null) lock (tum) tum.AppendLine(b.Data); };
                x.BeginErrorReadLine();
                int ch;
                while ((ch = x.StandardOutput.Read()) >= 0)
                {
                    if (ch == 0) continue;
                    if (ch != '\r' && ch != '\n') { s.Append((char)ch); continue; }
                    var t = s.ToString().Trim(); s.Clear();
                    if (t == "") continue;
                    lock (tum) tum.AppendLine(t);
                    try { satir?.Invoke(t); } catch { }
                }
                if (s.Length > 0) lock (tum) tum.AppendLine(s.ToString());
                x.WaitForExit(dk * 60 * 1000);
                return (x.HasExited ? x.ExitCode : -1, tum.ToString());
            }
        }

        static Encoding Oem() { try { return Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage); } catch { return Encoding.Default; } }

        static string GeriYuklemeNoktasi(Kayit r, string neden)
        {
            r.AsamaAd = "Geri yükleme noktası oluşturuluyor"; Ilerle(r, "0"); Yaz(r);
            const string ps = "try { Enable-ComputerRestore -Drive 'C:\\' -ErrorAction SilentlyContinue; " +
                              "New-ItemProperty -Path 'HKLM:\\SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion\\SystemRestore' -Name SystemRestorePointCreationFrequency -Value 0 -PropertyType DWord -Force | Out-Null; " +
                              "Checkpoint-Computer -Description 'Sem: {0}' -RestorePointType MODIFY_SETTINGS -ErrorAction Stop; 'TAMAM' } catch { 'HATA ' + $_.Exception.Message }";
            var o = Calistir("powershell.exe", "-NoProfile -ExecutionPolicy Bypass -Command \"" + ps.Replace("{0}", neden.Replace("'", "")) + "\"", 10 * 60 * 1000);
            return o.Contains("TAMAM") ? " Öncesinde geri yükleme noktası alındı." : " (Geri yükleme noktası alınamadı.)";
        }

        static bool YuzdeVarMi(string t) => Yuzde.IsMatch(t);

        /// <summary>Aşamanın kendi yüzdesini işler; genel yüzde (check-up'ta tüm işin yüzdesi) ana çubuğa, aşamanınki yazıya.</summary>
        static void Ilerle(Kayit r, string y)
        {
            int p; if (!int.TryParse(y, out p)) return;
            p = Math.Max(0, Math.Min(100, p));
            r.Ilerleme = (r.Taban + p * r.Agirlik / 100).ToString();
            r.Asama = r.AsamaAd + (r.Agirlik < 100 ? " · %" + p : "");
        }

        // ---- Ayrıntılı rapor: ProgramData\Sem\islem\<id>.txt ("# başlık", "- madde", düz satır)
        static string DetayKlasor => Path.Combine(AjanAyar.Klasor, "islem");
        static void DetayEkle(Kayit r, string baslik, IEnumerable<string> satirlar)
        {
            try
            {
                Directory.CreateDirectory(DetayKlasor);
                File.AppendAllText(Path.Combine(DetayKlasor, r.Id + ".txt"), "# " + baslik + "\n" + string.Join("\n", satirlar.Where(x => x != null)) + "\n\n", Encoding.UTF8);
            }
            catch { }
        }

        /// <summary>Bir işlemin ayrıntılı raporu. Eski kayıtlar için (dosya yoksa) Windows kayıtlarından sonradan çıkarılır.</summary>
        public static Mesaj IslemDetay(string id)
        {
            var r = Mesaj.Basari("");
            var f = Path.Combine(DetayKlasor, id + ".txt");
            try
            {
                if (File.Exists(f)) { r.Veri = File.ReadAllBytes(f); return r; }
                var k = KayitlariOku().FirstOrDefault(x => x.Id == id);
                if (k == null) return Mesaj.Hata("Kayıt bulunamadı");
                DateTime bas, bit;
                DateTime.TryParseExact(k.Bas, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out bas);
                if (!DateTime.TryParseExact(k.Bit, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out bit)) bit = DateTime.Now;
                var sb = new StringBuilder();
                sb.Append("# Sonuç\n" + k.Sonuc.Replace(" / ", "\n").Replace(" | ", "\n") + "\n\n");
                if (k.Islem == "defender" || k.Islem == "checkup" || k.Islem == "tamvirus")
                    sb.Append("# Virüs taraması\n" + string.Join("\n", TehditSatirlari(bas.AddMinutes(-1), bit.AddMinutes(1))) + "\n\n");
                if (k.Islem == "sfc" || k.Islem == "checkup")
                    sb.Append("# Sistem dosyaları\n" + string.Join("\n", SfcSatirlari(bas, bit)) + "\n\n");
                if (k.Islem == "disk" || k.Islem == "checkup")
                    sb.Append("# Disk taraması\n(Bu sürümden önce yapılan taramanın ayrıntısı saklanmadı; sonuç yukarıda.)\n\n");
                r.Veri = Encoding.UTF8.GetBytes(sb.ToString());
            }
            catch (Exception e) { return Mesaj.Hata(e.Message); }
            return r;
        }

        // ---- Defender: bulunan tehditler (adı, dosyası, ne yapıldığı)
        static List<string> TehditSatirlari(DateTime bas, DateTime bit)
        {
            var l = new List<string>();
            var adlar = new Dictionary<string, (string ad, string seviye)>();
            foreach (var o in W(@"root\Microsoft\Windows\Defender", "SELECT ThreatID, ThreatName, SeverityID FROM MSFT_MpThreat"))
                adlar[V(o, "ThreatID")] = (V(o, "ThreatName"), V(o, "SeverityID"));
            foreach (var o in W(@"root\Microsoft\Windows\Defender", "SELECT ThreatID, InitialDetectionTime, Resources, ActionSuccess, CleaningActionID, ThreatStatusID FROM MSFT_MpThreatDetection"))
            {
                DateTime z = DateTime.MinValue;
                try { z = ManagementDateTimeConverter.ToDateTime(V(o, "InitialDetectionTime")); } catch { }
                if (z < bas || z > bit) continue;
                adlar.TryGetValue(V(o, "ThreatID"), out var t);
                string kaynak = "";
                try { var rs = o["Resources"] as string[]; if (rs != null) kaynak = string.Join(", ", rs.Select(x => x.Contains("_") && x.IndexOf(":_") > 0 ? x.Substring(x.IndexOf(":_") + 2) : x).Take(3)); } catch { }
                string seviye = t.seviye == "5" ? "ciddi" : t.seviye == "4" ? "yüksek" : t.seviye == "2" ? "orta" : t.seviye == "1" ? "düşük" : "";
                string durum = V(o, "ThreatStatusID");
                string ne = durum == "3" || durum == "4" ? "karantinaya alındı" : durum == "5" || durum == "6" ? "silindi" : durum == "102" ? "izin verildi" : durum == "1" ? "bulundu, temizlenmedi" : V(o, "ActionSuccess") == "True" ? "temizlendi" : "durum " + durum;
                l.Add("- " + z.ToString("d MMM HH:mm", new CultureInfo("tr-TR")) + " · " + (t.ad ?? "Bilinmeyen tehdit") + (seviye != "" ? " (" + seviye + ")" : "") + " · " + ne + (kaynak != "" ? " · " + kaynak : ""));
            }
            if (l.Count == 0)
            {
                // WMI boşsa olay kaydı
                foreach (var e in Olaylar("Microsoft-Windows-Windows Defender/Operational", "*[System[(EventID=1116 or EventID=1117)]]", 60))
                    using (e)
                    {
                        var z = e.TimeCreated ?? DateTime.MinValue; if (z < bas || z > bit) continue;
                        l.Add("- " + z.ToString("d MMM HH:mm", new CultureInfo("tr-TR")) + " · " + XmlVeri(e, "Threat Name") + " · " + (e.Id == 1117 ? "işlem yapıldı: " + XmlVeri(e, "Action Name") : "bulundu") + " · " + XmlVeri(e, "Path"));
                    }
            }
            if (l.Count == 0) l.Add("Bu aralıkta kayıtlı tehdit yok.");
            return l;
        }

        // ---- SFC: Windows onarım kaydından (CBS.log) bozuk bulunup onarılan dosyalar
        static List<string> SfcSatirlari(DateTime bas, DateTime bit)
        {
            var onarilan = new List<string>(); var onarilamayan = new List<string>();
            var dosya = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), @"Logs\CBS\CBS.log");
            var rx = new Regex(@"^(\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}).*\[SR\] (Repairing corrupted file|Cannot repair member file|Repaired file) (.+?)( from store|$)");
            try
            {
                using (var fs = new FileStream(dosya, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                {
                    if (fs.Length > 40L * 1024 * 1024) fs.Seek(-40L * 1024 * 1024, SeekOrigin.End);
                    using (var sr = new StreamReader(fs, Encoding.UTF8))
                    {
                        string x;
                        while ((x = sr.ReadLine()) != null)
                        {
                            if (x.IndexOf("[SR]", StringComparison.Ordinal) < 0) continue;
                            var m = rx.Match(x); if (!m.Success) continue;
                            DateTime z; if (!DateTime.TryParseExact(m.Groups[1].Value, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out z) || z < bas.AddMinutes(-1) || z > bit.AddMinutes(1)) continue;
                            var yol = Regex.Replace(m.Groups[3].Value, @"^\\\?\?\\", "").Trim();
                            var ad = DosyaAdi(yol);
                            if (m.Groups[2].Value.StartsWith("Cannot")) { if (!onarilamayan.Contains(ad)) onarilamayan.Add(ad); }
                            else if (!onarilan.Contains(ad)) onarilan.Add(ad);
                        }
                    }
                }
            }
            catch (Exception e) { return new List<string> { "Windows onarım kaydı okunamadı: " + e.Message }; }
            var l = new List<string>();
            if (onarilan.Count > 0) { l.Add("Onarılan " + onarilan.Count + " dosya:"); l.AddRange(onarilan.Take(40).Select(x => "- " + x)); }
            if (onarilamayan.Count > 0) { l.Add("Onarılamayan " + onarilamayan.Count + " dosya (Windows onarımı (DISM) gerekir):"); l.AddRange(onarilamayan.Take(40).Select(x => "- " + x)); }
            if (l.Count == 0) l.Add("Onarım kaydında bu aralıkta dosya yok (bozuk dosya bulunmadı ya da kayıt silinmiş).");
            return l;
        }

        /// <summary>Dosya yolunu düz Türkçe açıklamayla verir.</summary>
        static string DosyaAdi(string yol)
        {
            var ad = Path.GetFileName(yol); var k = yol.ToLowerInvariant(); var uz = Path.GetExtension(ad).ToLowerInvariant();
            string ne;
            if (k.Contains(@"\drivers\") || uz == ".sys") ne = "donanım sürücüsü";
            else if (k.Contains(@"\winsxs\")) ne = "Windows bileşen deposu";
            else if (k.Contains(@"\fonts\")) ne = "yazı tipi";
            else if (k.Contains(@"\systemapps\") || k.Contains(@"\windowsapps\")) ne = "Windows uygulaması";
            else if (uz == ".dll") ne = "Windows kitaplığı";
            else if (uz == ".exe") ne = "Windows programı";
            else if (uz == ".mui") ne = "dil dosyası";
            else if (uz == ".xml" || uz == ".manifest" || uz == ".cat") ne = "ayar/imza dosyası";
            else if (uz == ".png" || uz == ".jpg" || uz == ".ico") ne = "görsel";
            else ne = "sistem dosyası";
            return ad + " (" + ne + ") · " + yol;
        }

        static readonly Regex Yuzde = new Regex(@"(\d{1,3})(?:[.,]\d+)?\s*%|%\s*(\d{1,3})");
        static string YuzdeAl(string t) { var m = Yuzde.Match(t); return m.Success ? (m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value) : null; }

        static (bool, string, string) Sfc(Kayit r, string onek)
        {
            r.AsamaAd = onek + "Sistem dosyaları"; Ilerle(r, "0"); r.Asama = r.AsamaAd + " · hazırlanıyor (birkaç dakika yüzde vermez)"; Yaz(r);
            var son = DateTime.MinValue; var bas = DateTime.Now;
            // sfc çıktısı yönlendirilince UTF-16 yazar
            var x = Izle("sfc.exe", "/scannow", Encoding.Unicode, t =>
            {
                var y = YuzdeAl(t); if (y == null) return;
                Ilerle(r, y); if ((DateTime.Now - son).TotalSeconds > 3) { son = DateTime.Now; Yaz(r); }
            }, 120);
            DetayEkle(r, "Sistem dosyaları (sfc /scannow)", SfcSatirlari(bas, DateTime.Now));
            var m = x.metin.ToLowerInvariant();
            if (m.Contains("did not find any integrity") || (m.Contains("ihlal") && (m.Contains("bulamad") || m.Contains("bulunamad")))) return (true, "Sistem dosyaları sağlam, bozuk dosya yok.", "");
            if (m.Contains("successfully repaired") || m.Contains("başarıyla onar")) return (true, "Bozuk sistem dosyaları bulundu ve onarıldı. Yeniden başlatınca tam geçerli olur.", "");
            if (m.Contains("unable to fix") || m.Contains("onaramad") || m.Contains("düzeltemed") || m.Contains("onarılamad")) return (false, "Bozuk sistem dosyaları var ama bir kısmı onarılamadı. Windows onarımı (DISM) gerekiyor.", "dism");
            if (m.Contains("pending") || m.Contains("bekleyen")) return (false, "Bekleyen bir Windows işlemi var; bilgisayar yeniden başlatıldıktan sonra tekrar deneyin.", "");
            var sonSatir = x.metin.Split('\n').Select(y => y.Trim()).Where(y => y != "" && YuzdeAl(y) == null).LastOrDefault() ?? "";
            return (x.kod == 0, (x.kod == 0 ? "Denetim tamamlandı. " : "Denetim tamamlanamadı. ") + sonSatir, "");
        }

        static (bool, string, string) Dism(Kayit r, string onek)
        {
            r.AsamaAd = onek + "Windows bileşenleri onarılıyor (internet gerekir)"; Ilerle(r, "0"); Yaz(r);
            var son = DateTime.MinValue;
            var x = Izle("dism.exe", "/Online /Cleanup-Image /RestoreHealth", Oem(), t =>
            {
                var y = YuzdeAl(t); if (y == null) return;
                Ilerle(r, y); if ((DateTime.Now - son).TotalSeconds > 3) { son = DateTime.Now; Yaz(r); }
            }, 180);
            DetayEkle(r, "Windows onarımı (DISM)", new[] { "Sonuç kodu " + x.kod + (x.kod == 0 ? " (başarılı)" : "") }.Concat(x.metin.Split('\n').Select(y => y.Trim()).Where(y => y != "" && YuzdeAl(y) == null).Skip(0).Reverse().Take(12).Reverse().Select(y => "- " + y)));
            if (x.kod == 0) return (true, "Windows bileşenleri onarıldı ya da zaten sağlamdı. Ardından sistem dosyası onarımı yapılabilir.", "sfc");
            var sonSatir = x.metin.Split('\n').Select(y => y.Trim()).Where(y => y != "" && YuzdeAl(y) == null).LastOrDefault() ?? "";
            return (false, "Windows onarımı tamamlanamadı (kod " + x.kod + "). " + sonSatir, "");
        }

        static (bool, string, string) Defender(Kayit r, string onek, bool tam = false)
        {
            var mp = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"Windows Defender\MpCmdRun.exe");
            if (!File.Exists(mp)) return (false, "Windows Defender bulunamadı.", "");
            bool etkin = W(@"root\Microsoft\Windows\Defender", "SELECT AntivirusEnabled FROM MSFT_MpComputerStatus").Any(o => V(o, "AntivirusEnabled") == "True");
            if (!etkin) return (false, "Defender kapalı; bu bilgisayarda başka bir antivirüs kullanılıyor olabilir. Taramayı o programla yapın.", "");
            r.AsamaAd = onek + "Virüs tanımları güncelleniyor"; Ilerle(r, "0"); Yaz(r);
            var g = Izle(mp, "-SignatureUpdate", Oem(), null, 20);
            var bas = DateTime.Now;
            r.AsamaAd = onek + (tam ? "Tam virüs taraması (1-3 saat sürebilir)" : "Hızlı virüs taraması"); Ilerle(r, "10"); Yaz(r);
            var t = Izle(mp, "-Scan -ScanType " + (tam ? "2" : "1"), Oem(), null, tam ? 360 : 120);
            var tehdit = TehditSatirlari(bas.AddMinutes(-1), DateTime.Now.AddMinutes(1));
            bool bulundu = !tehdit[0].StartsWith("Bu aralıkta");
            string imza = g.kod == 0 ? "Tanımlar güncellendi. " : "Tanımlar güncellenemedi (internet?). ";
            DetayEkle(r, tam ? "Tam virüs taraması" : "Hızlı virüs taraması", new[] { imza.Trim(), "Tarama sonuç kodu " + t.kod + (t.kod == 0 ? " (temiz)" : t.kod == 2 ? " (tehdit bulundu)" : "") }.Concat(tehdit));
            var adlar = tehdit.Where(x => x.StartsWith("- ")).Select(x => x.Split('·').Skip(1).FirstOrDefault()?.Trim()).Where(x => !string.IsNullOrEmpty(x)).Distinct().Take(3).ToList();
            if (bulundu || t.kod == 2)
                return (false, imza + "Tehdit bulundu: " + (adlar.Count > 0 ? string.Join(", ", adlar) : "adı okunamadı") + ". Ayrıntılar'da dosyası ve Defender'ın ne yaptığı yazar." + (tam ? "" : " Tam tarama önerilir."), tam ? "" : "tamvirus");
            if (t.kod == 0) return (true, imza + (tam ? "Tam tarama" : "Hızlı tarama") + " temiz, tehdit bulunmadı.", "");
            return (false, imza + "Tarama tamamlanamadı (kod " + t.kod + ").", "");
        }

        static (bool, string, string) WuOnar(Kayit r)
        {
            r.Asama = "Güncelleme servisleri durduruluyor"; r.Ilerleme = "10"; Yaz(r);
            foreach (var s in new[] { "wuauserv", "bits", "cryptsvc", "UsoSvc" }) Calistir("net.exe", "stop " + s + " /y", 90000);
            r.Asama = "Güncelleme önbelleği yenileniyor"; r.Ilerleme = "40"; Yaz(r);
            var win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            string Tasi(string yol)
            {
                var yedek = yol + ".sem-yedek";
                try { if (Directory.Exists(yedek)) Directory.Delete(yedek, true); } catch { }
                try { if (Directory.Exists(yol)) { Directory.Move(yol, yedek); return "ok"; } return "yok"; } catch (Exception e) { return e.Message; }
            }
            var a = Tasi(Path.Combine(win, "SoftwareDistribution"));
            Tasi(Path.Combine(win, @"System32\catroot2"));
            r.Asama = "Servisler yeniden başlatılıyor"; r.Ilerleme = "70"; Yaz(r);
            foreach (var s in new[] { "cryptsvc", "bits", "wuauserv", "UsoSvc" }) Calistir("net.exe", "start " + s, 90000);
            r.Asama = "Güncellemeler aranıyor"; r.Ilerleme = "90"; Yaz(r);
            try { Calistir(Path.Combine(win, @"System32\UsoClient.exe"), "StartScan", 60000); } catch { }
            return a == "ok" || a == "yok" ? (true, "Windows Update sıfırlandı; güncellemeler yeniden aranıyor. Eski önbellek SoftwareDistribution.sem-yedek olarak saklandı.", "")
                                          : (false, "Güncelleme önbelleği taşınamadı: " + a, "");
        }

        static (bool, string, string) AgHafif(Kayit r)
        {
            r.Asama = "DNS önbelleği temizleniyor"; r.Ilerleme = "20"; Yaz(r);
            Calistir("ipconfig.exe", "/flushdns", 15000);
            Calistir("arp.exe", "-d *", 15000);
            r.Asama = "IP adresi yenileniyor"; r.Ilerleme = "50"; Yaz(r);
            Calistir("ipconfig.exe", "/renew", 60000);
            r.Asama = "Bağlantı ölçülüyor"; r.Ilerleme = "80"; Yaz(r);
            var n = AnaAg();
            var gw = n?.GetIPProperties().GatewayAddresses.Select(g => g.Address).FirstOrDefault(x => x.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork);
            if (gw == null) return (false, "Onarım yapıldı ama modeme bağlantı yok; kabloyu kontrol edin.", "");
            var p = Ping(gw, 10);
            return (p.kayip < 20, "DNS temizlendi, IP yenilendi. Modeme ping: kayıp %" + p.kayip + (p.ms >= 0 ? ", " + p.ms + " ms" : "") + ".", "");
        }

        static (bool, string, string) AygitTara(Kayit r)
        {
            int once = SorunluAygitlar().Count;
            r.Asama = "Aygıtlar yeniden taranıyor"; r.Ilerleme = "30"; Yaz(r);
            Calistir("pnputil.exe", "/scan-devices", 5 * 60 * 1000);
            System.Threading.Thread.Sleep(5000);
            var sonra = SorunluAygitlar();
            if (sonra.Count == 0) return (true, once > 0 ? "Aygıtlar yeniden tanındı; sorunlu aygıt kalmadı." : "Sorunlu aygıt yok.", "");
            return (false, sonra.Count + " aygıtta sorun sürüyor: " + string.Join(", ", sonra.Select(x => x.Split(':')[0]).Take(3)) + ". Bu aygıtın sürücüsünü üreticinin sitesinden kurmak gerekir.", "");
        }

        static (bool, string, string) Checkup(Kayit r)
        {
            var sonuc = new List<string>(); bool hepsi = true; string ek = "";
            void Bitti(string ad, (bool ok, string metin, string ek) x)
            {
                // Biten aşamanın sonucu hemen kayda geçer (Teşhis'te check-up sürerken görünür)
                sonuc.Add(ad + ": " + x.metin); hepsi &= x.ok; if (x.ek != "" && ek == "") ek = x.ek;
                r.Sonuc = string.Join(" | ", sonuc); Yaz(r);
            }
            r.Taban = 0; r.Agirlik = 5;
            var nokta = GeriYuklemeNoktasi(r, "Check-up");
            r.Taban = 5; r.Agirlik = 30; Bitti("Virüs", Defender(r, "1/3 · "));
            r.Taban = 35; r.Agirlik = 25; var k = DiskTaraIc(r, "2/3 · "); Bitti("Disk", (k.ok, k.sonuc, k.ek));
            r.Taban = 60; r.Agirlik = 40; var s = Sfc(r, "3/3 · "); Bitti("Sistem dosyaları", s);
            if (s.Item3 == "dism")
            {
                r.Taban = 60; r.Agirlik = 40;
                var m = Dism(r, "Ek · "); Bitti("Windows onarımı", m);
                if (m.Item1) Bitti("Tekrar sistem dosyaları", Sfc(r, "Ek · "));
            }
            r.Taban = 0; r.Agirlik = 100;
            return (hepsi, string.Join(" | ", sonuc) + nokta, ek);
        }
    }
}
