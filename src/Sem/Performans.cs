// Performans izleme (her bilgisayar, 5 dakikada bir, son 2 gün) ve "Çöz" işlemleri.
// Ölçümler performans sayaçlarının 5 dakikalık ortalamasıdır; anlık sıçramalar yanıltmaz.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using System.IO;
using System.Linq;
using System.Management;
using System.Text;
using System.Threading;
using Microsoft.Win32;
using Sem.Ortak;

namespace Sem.Ajan
{
    static class Performans
    {
        static string Dosya => Path.Combine(AjanAyar.Klasor, "performans.txt");
        static readonly object k = new object();
        static DateTime son = DateTime.MinValue;
        static int calisiyor;
        static PerformanceCounter cpu, hiz, disk;
        static Dictionary<int, (string ad, TimeSpan t)> oncekiSure = new Dictionary<int, (string, TimeSpan)>();
        static DateTime oncekiZaman = DateTime.MinValue;
        static int yavasArdisik;
        static bool sayacKuruldu;
        public static string Uyari = "";

        static int maxMhz = -1;
        static int MaxMhz()
        {
            if (maxMhz > 0) return maxMhz;
            try { using (var s = new ManagementObjectSearcher("SELECT MaxClockSpeed FROM Win32_Processor")) foreach (ManagementObject o in s.Get()) maxMhz = Convert.ToInt32(o["MaxClockSpeed"]); } catch { }
            return maxMhz;
        }

        static void Sayaclar()
        {
            if (cpu != null) return;
            try { cpu = new PerformanceCounter("Processor Information", "% Processor Utility", "_Total"); cpu.NextValue(); }
            catch { try { cpu = new PerformanceCounter("Processor", "% Processor Time", "_Total"); cpu.NextValue(); } catch { cpu = null; } }
            try { hiz = new PerformanceCounter("Processor Information", "% Processor Performance", "_Total"); hiz.NextValue(); } catch { hiz = null; }
            try { disk = new PerformanceCounter("PhysicalDisk", "% Idle Time", "_Total"); disk.NextValue(); } catch { disk = null; }
        }

        /// <summary>Ajanın zamanlayıcısından çağrılır; 5 dakikada bir ölçer.</summary>
        public static void Tik()
        {
            // İlk çağrıda sayaçlar kurulur; ilk ölçüm 30 sn sonra (sayaç ortalaması o aralıktan)
            if (!sayacKuruldu) { sayacKuruldu = true; son = DateTime.Now.AddMinutes(-4.5); ThreadPool.QueueUserWorkItem(_ => { try { Sayaclar(); } catch { } }); return; }
            if ((DateTime.Now - son).TotalMinutes < 5 || Interlocked.Exchange(ref calisiyor, 1) == 1) return;
            son = DateTime.Now;
            new Thread(() => { try { Olc(); } catch (Exception e) { Gunluk.Yaz("Performans: " + e.Message); } finally { calisiyor = 0; } }) { IsBackground = true, Priority = ThreadPriority.BelowNormal }.Start();
        }

        static float Oku(PerformanceCounter c) { try { return c == null ? -1 : c.NextValue(); } catch { return -1; } }

        static int Sicaklik()
        {
            // Çoğu masaüstü anakart bildirmez; bildirirse kullanılır
            try
            {
                using (var s = new ManagementObjectSearcher(@"root\WMI", "SELECT CurrentTemperature FROM MSAcpi_ThermalZoneTemperature"))
                    foreach (ManagementObject o in s.Get())
                    {
                        double c = Convert.ToDouble(o["CurrentTemperature"]) / 10.0 - 273.15;
                        if (c > 5 && c < 120) return (int)Math.Round(c);
                    }
            }
            catch { }
            return -1;
        }

        static void Olc()
        {
            Sayaclar();
            float c = Oku(cpu), h = Oku(hiz), d = Oku(disk);
            int mx = MaxMhz();
            int mhz = h >= 0 && mx > 0 ? (int)(mx * h / 100) : -1;
            int ramYuzde = -1; long ramMb = 0, toplamMb = 0;
            try
            {
                using (var s = new ManagementObjectSearcher("SELECT FreePhysicalMemory, TotalVisibleMemorySize FROM Win32_OperatingSystem"))
                    foreach (ManagementObject o in s.Get())
                    {
                        long bos = Convert.ToInt64(o["FreePhysicalMemory"]), top = Convert.ToInt64(o["TotalVisibleMemorySize"]);
                        toplamMb = top / 1024; ramMb = (top - bos) / 1024; ramYuzde = (int)Math.Round((top - bos) * 100.0 / Math.Max(1, top));
                    }
            }
            catch { }
            // En çok işlemci ve RAM kullanan program (son 5 dakika)
            string ustCpu = "", ustRam = ""; double ustCpuY = 0; long ustRamMb = 0;
            var simdi = new Dictionary<int, (string, TimeSpan)>();
            var gecen = (DateTime.Now - oncekiZaman).TotalSeconds;
            var cekirdek = Environment.ProcessorCount;
            var toplamlar = new Dictionary<string, (double cpu, long ram)>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in Process.GetProcesses())
                using (p)
                {
                    try
                    {
                        if (p.Id == 0 || p.Id == 4) continue;
                        var ad = p.ProcessName; long ws = p.WorkingSet64 / 1048576;
                        double y = 0;
                        try
                        {
                            var t = p.TotalProcessorTime; simdi[p.Id] = (ad, t);
                            if (oncekiSure.TryGetValue(p.Id, out var o) && o.ad == ad && gecen > 30) y = (t - o.t).TotalSeconds / gecen / cekirdek * 100;
                        }
                        catch { }
                        toplamlar.TryGetValue(ad, out var v); toplamlar[ad] = (v.cpu + y, v.ram + ws);
                    }
                    catch { }
                }
            foreach (var kv in toplamlar)
            {
                if (kv.Key.Equals("Idle", StringComparison.OrdinalIgnoreCase) || kv.Key.Equals("Memory Compression", StringComparison.OrdinalIgnoreCase)) continue;
                if (kv.Value.cpu > ustCpuY) { ustCpuY = kv.Value.cpu; ustCpu = kv.Key; }
                if (kv.Value.ram > ustRamMb) { ustRamMb = kv.Value.ram; ustRam = kv.Key; }
            }
            bool ilkOlcum = oncekiZaman == DateTime.MinValue;
            oncekiSure = simdi; oncekiZaman = DateTime.Now;
            int sic = Sicaklik();
            var satir = string.Join("|", Satir.Zaman(DateTime.Now), (int)Math.Round(c), h < 0 ? -1 : (int)Math.Round(h), mhz, sic, ramYuzde, ramMb,
                d < 0 ? -1 : (int)Math.Round(100 - Math.Min(100, d)), Satir.T(ustCpu) + ":" + (int)Math.Round(ustCpuY), Satir.T(ustRam) + ":" + ustRamMb);
            if (ilkOlcum && c <= 0) return; // sayaçların ilk değeri anlamsız
            lock (k)
            {
                Directory.CreateDirectory(AjanAyar.Klasor);
                File.AppendAllText(Dosya, satir + "\n");
                var l = File.ReadAllLines(Dosya);
                if (l.Length > 700) File.WriteAllLines(Dosya, l.Skip(l.Length - 576));
            }
            // Yavaşlama uyarısı: işlem yükü varken işlemci hızı düşükse ya da RAM doluysa (art arda 2 ölçüm)
            bool yavas = (c >= 25 && h >= 0 && h < 55) || ramYuzde >= 93 || (sic >= 90);
            yavasArdisik = yavas ? yavasArdisik + 1 : 0;
            if (yavasArdisik >= 2)
            {
                var neden = new List<string>();
                if (c >= 25 && h >= 0 && h < 55) neden.Add("işlemci " + (mhz > 0 ? (mhz / 1000.0).ToString("0.0", System.Globalization.CultureInfo.GetCultureInfo("tr-TR")) + " GHz'e" : "%" + (int)h + " hıza") + " düştü");
                if (sic >= 90) neden.Add("sıcaklık " + sic + "°");
                if (ramYuzde >= 93) neden.Add("RAM %" + ramYuzde + " dolu (en çok: " + ustRam + ")");
                Uyari = Satir.Zaman(DateTime.Now) + "|" + string.Join(", ", neden);
            }
            else if (yavasArdisik == 0) Uyari = "";
        }

        // ------------------------------------------------------------ Teşhis için bilgiler
        static int OlayAdet(string kayit, string sorgu)
        {
            int n = 0;
            try
            {
                using (var r = new EventLogReader(new EventLogQuery(kayit, PathType.LogName, sorgu)))
                    for (var e = r.ReadEvent(); e != null && n < 999; e = r.ReadEvent()) using (e) n++;
            }
            catch { }
            return n;
        }

        public static string GucPlani()
        {
            try
            {
                var p = new ProcessStartInfo("powercfg.exe", "/getactivescheme") { UseShellExecute = false, RedirectStandardOutput = true, CreateNoWindow = true, StandardOutputEncoding = Encoding.Default };
                using (var x = Process.Start(p)) { var o = x.StandardOutput.ReadToEnd(); x.WaitForExit(5000); return o.Trim(); }
            }
            catch { return ""; }
        }

        public static bool HizliBaslatma()
        {
            try { using (var r = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Session Manager\Power")) return (r?.GetValue("HiberbootEnabled") as int? ?? 0) == 1; }
            catch { return false; }
        }

        public static Mesaj Veri()
        {
            var r = Mesaj.Basari();
            long hafta = 7L * 86400000;
            string zaman = " and TimeCreated[timediff(@SystemTime) <= " + hafta + "]";
            try
            {
                using (var s = new ManagementObjectSearcher("SELECT Name, MaxClockSpeed, NumberOfCores FROM Win32_Processor"))
                    foreach (ManagementObject o in s.Get()) { r["islemci"] = Convert.ToString(o["Name"]).Trim(); r["max_mhz"] = Convert.ToString(o["MaxClockSpeed"]); r["cekirdek"] = Convert.ToString(o["NumberOfCores"]); }
            }
            catch { }
            try
            {
                var taraklar = new List<string>(); long toplam = 0;
                using (var s = new ManagementObjectSearcher("SELECT Capacity, Speed, ConfiguredClockSpeed FROM Win32_PhysicalMemory"))
                    foreach (ManagementObject o in s.Get())
                    {
                        long cap = Convert.ToInt64(o["Capacity"]); toplam += cap;
                        var hz = Convert.ToString(o["ConfiguredClockSpeed"] ?? o["Speed"]);
                        taraklar.Add(cap / 1073741824 + " GB" + (hz != "" && hz != "0" ? " " + hz + " MHz" : ""));
                    }
                r["ram_tarak"] = taraklar.Count.ToString(); r["ram_detay"] = string.Join(" + ", taraklar); r["ram_gb"] = (toplam / 1073741824.0).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);
                using (var s = new ManagementObjectSearcher("SELECT TotalVisibleMemorySize, LastBootUpTime FROM Win32_OperatingSystem"))
                    foreach (ManagementObject o in s.Get())
                    {
                        r["ram_gorunen_gb"] = (Convert.ToInt64(o["TotalVisibleMemorySize"]) / 1048576.0).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);
                        var b = ManagementDateTimeConverter.ToDateTime(Convert.ToString(o["LastBootUpTime"]));
                        r["acilis"] = Satir.Zaman(b);
                    }
            }
            catch { }
            r["guc"] = GucPlani();
            r["hizli_baslatma"] = HizliBaslatma() ? "1" : "0";
            try
            {
                var c = new DriveInfo(Path.GetPathRoot(Environment.SystemDirectory));
                r["disk_bos_gb"] = (c.AvailableFreeSpace / 1073741824.0).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);
                r["disk_yuzde"] = ((int)(c.AvailableFreeSpace * 100.0 / Math.Max(1, c.TotalSize))).ToString();
            }
            catch { }
            try { var dl = Envanter.Diskler(); r["disk_tur"] = string.Join(", ", dl.Select(x => x.Tur + (x.Saglik != "" ? " " + x.Saglik : ""))); } catch { }
            // Son 7 günün olayları
            r["olay_kisma"] = OlayAdet("System", "*[System[Provider[@Name='Microsoft-Windows-Kernel-Processor-Power'] and (EventID=37)" + zaman + "]]").ToString();
            r["olay_whea"] = OlayAdet("System", "*[System[Provider[@Name='Microsoft-Windows-WHEA-Logger'] and (Level=1 or Level=2 or Level=3)" + zaman + "]]").ToString();
            r["olay_guc41"] = OlayAdet("System", "*[System[Provider[@Name='Microsoft-Windows-Kernel-Power'] and (EventID=41)" + zaman + "]]").ToString();
            r["olay_disk"] = OlayAdet("System", "*[System[(EventID=7 or EventID=51 or EventID=153 or EventID=129)" + zaman + "]]").ToString();
            r["olay_sicaklik"] = OlayAdet("System", "*[System[Provider[@Name='Microsoft-Windows-Kernel-Power'] and (EventID=86 or EventID=88)" + zaman + "]]").ToString();
            lock (k) r.Veri = File.Exists(Dosya) ? File.ReadAllBytes(Dosya) : new byte[0];
            return r;
        }

        // ------------------------------------------------------------ Çöz
        const string YuksekPerformans = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c";
        static readonly string[] Korunan = { "system", "smss", "csrss", "wininit", "winlogon", "services", "lsass", "svchost", "explorer", "dwm", "sem", "msmpeng", "spoolsv", "fontdrvhost", "registry", "memory compression", "audiodg", "searchindexer", "taskhostw", "sihost", "ctfmon" };

        static string Calistir(string exe, string arg, int ms = 60000)
        {
            var p = new ProcessStartInfo(exe, arg) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true, StandardOutputEncoding = Encoding.Default };
            using (var x = Process.Start(p)) { var o = x.StandardOutput.ReadToEnd() + x.StandardError.ReadToEnd(); x.WaitForExit(ms); return o.Trim(); }
        }

        public static Mesaj Coz(string islem, string ad)
        {
            try
            {
                switch (islem)
                {
                    case "guc":
                    {
                        var o = Calistir("powercfg.exe", "/setactive " + YuksekPerformans);
                        if (!GucPlani().Contains(YuksekPerformans))
                        {
                            // Bazı Windows'larda plan gizlidir: kopyası oluşturulup etkinleştirilir
                            var cikti = Calistir("powercfg.exe", "-duplicatescheme " + YuksekPerformans);
                            var m = System.Text.RegularExpressions.Regex.Match(cikti, "[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}");
                            if (m.Success) Calistir("powercfg.exe", "/setactive " + m.Value);
                        }
                        // Prizdeyken uykuya geçmesin (uykudan dönüşte yavaş kalma sorunlarını da önler)
                        Calistir("powercfg.exe", "/change standby-timeout-ac 0");
                        var g = GucPlani();
                        return g.Contains("8c5e7fda") || g.IndexOf("performans", StringComparison.OrdinalIgnoreCase) >= 0 || g.IndexOf("performance", StringComparison.OrdinalIgnoreCase) >= 0
                            ? Mesaj.Basari("Güç ayarı Yüksek performans yapıldı") : Mesaj.Hata("Güç ayarı değiştirilemedi: " + g);
                    }
                    case "hizlibaslat":
                        using (var r = Registry.LocalMachine.CreateSubKey(@"SYSTEM\CurrentControlSet\Control\Session Manager\Power")) r.SetValue("HiberbootEnabled", 0, RegistryValueKind.DWord);
                        return Mesaj.Basari("Hızlı başlatma kapatıldı: bilgisayar her kapanışta tam sıfırlanacak");
                    case "temp":
                    {
                        long silinen = 0;
                        var klasorler = new List<string> { Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp") };
                        try { foreach (var u in Directory.GetDirectories(@"C:\Users")) klasorler.Add(Path.Combine(u, @"AppData\Local\Temp")); } catch { }
                        foreach (var kl in klasorler.Where(Directory.Exists))
                        {
                            IEnumerable<string> dosyalar; try { dosyalar = Directory.EnumerateFiles(kl, "*", SearchOption.AllDirectories).ToList(); } catch { continue; }
                            foreach (var f in dosyalar)
                                try { var fi = new FileInfo(f); if (fi.LastWriteTime < DateTime.Now.AddDays(-2)) { long b = fi.Length; fi.Delete(); silinen += b; } } catch { }
                        }
                        return Mesaj.Basari("Geçici dosyalar temizlendi: " + (silinen / 1048576) + " MB");
                    }
                    case "program":
                    {
                        if (string.IsNullOrWhiteSpace(ad) || Korunan.Contains(ad.ToLowerInvariant())) return Mesaj.Hata("Bu program kapatılamaz (Windows'un parçası)");
                        int n = 0;
                        foreach (var p in Process.GetProcessesByName(ad)) using (p) try { p.Kill(); n++; } catch { }
                        // Açılışta çalışıyorsa açılıştan çıkar (yalnızca Run anahtarları; kaldırmaz)
                        int c = 0;
                        var anahtarlar = new List<RegistryKey>();
                        try { anahtarlar.Add(Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true)); } catch { }
                        try { anahtarlar.Add(Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run", true)); } catch { }
                        try { foreach (var sid in Registry.Users.GetSubKeyNames().Where(x => x.StartsWith("S-1-5-21-") && !x.EndsWith("_Classes"))) anahtarlar.Add(Registry.Users.OpenSubKey(sid + @"\Software\Microsoft\Windows\CurrentVersion\Run", true)); } catch { }
                        foreach (var a in anahtarlar.Where(x => x != null))
                            using (a)
                                foreach (var v in a.GetValueNames())
                                    if ((Convert.ToString(a.GetValue(v)) ?? "").IndexOf(ad + ".exe", StringComparison.OrdinalIgnoreCase) >= 0) { try { a.DeleteValue(v); c++; } catch { } }
                        return Mesaj.Basari(ad + " kapatıldı (" + n + ")" + (c > 0 ? ", açılıştan çıkarıldı" : ""));
                    }
                    case "disk":
                        new Thread(() =>
                        {
                            try { var o = Calistir("chkdsk.exe", "C: /scan", 60 * 60 * 1000); Gunluk.Yaz("Disk taraması: " + o.Split('\n').LastOrDefault(x => x.Trim() != "")); }
                            catch (Exception e) { Gunluk.Yaz("Disk taraması: " + e.Message); }
                        }) { IsBackground = true, Priority = ThreadPriority.BelowNormal }.Start();
                        return Mesaj.Basari("Disk taraması başladı (arka planda, bilgisayar kullanılabilir)");
                }
            }
            catch (Exception e) { return Mesaj.Hata(e.Message); }
            return Mesaj.Hata("Bilinmeyen işlem");
        }
    }
}
