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
    static partial class Performans
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
            // Sıcaklık sürücüsü kuruluysa gerçek işlemci sıcaklığı (LibreHardwareMonitor)
            try { int t; if (Sensor.SurucuVar() && int.TryParse(Sensor.Oku().TryGetValue("cpu_sicaklik", out var v) ? v : "", out t) && t > 5 && t < 120) return t; } catch { }
            // Yoksa ACPI bölgesi (çoğu masaüstü anakart bildirmez)
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

        /// <summary>Sistem diskinin (C:) sağlığı: yalnızca disk sürücüsünün kendi hata olayları (başka kaynakların
        /// aynı numaralı olayları sayılmaz, takılıp çıkarılan USB bellekler hariç), SMART ve Windows güvenilirlik sayaçları.</summary>
        static void DiskSagligi(Mesaj r, string zaman)
        {
            int sistemDisk = -1;
            try
            {
                using (var s = new ManagementObjectSearcher("ASSOCIATORS OF {Win32_LogicalDisk.DeviceID='C:'} WHERE AssocClass=Win32_LogicalDiskToPartition"))
                    foreach (ManagementObject o in s.Get()) { sistemDisk = Convert.ToInt32(o["DiskIndex"]); break; }
            }
            catch { }
            r["sistem_disk"] = sistemDisk.ToString();
            // disk 7 bozuk kesim, 51 sayfalama sırasında hata, 153 işlem yeniden denendi; Ntfs 55 dosya sistemi bozuk;
            // depolama denetleyicisi 129 yanıt vermedi (sıfırlandı)
            var sorgu = "*[System[((Provider[@Name='disk'] and (EventID=7 or EventID=51 or EventID=153)) or (Provider[@Name='Ntfs' or @Name='Microsoft-Windows-Ntfs'] and EventID=55) or " +
                        "(Provider[@Name='storahci' or @Name='stornvme' or @Name='iaStorA' or @Name='iaStorAC' or @Name='iaStorAVC' or @Name='iaStorV' or @Name='amdsata' or @Name='amd_sata'] and EventID=129))" + zaman + "]]";
            int hata = 0, sifirlama = 0; var detay = new List<string>();
            try
            {
                using (var rd = new EventLogReader(new EventLogQuery("System", PathType.LogName, sorgu) { ReverseDirection = true }))
                    for (var e = rd.ReadEvent(); e != null && hata + sifirlama < 500; e = rd.ReadEvent())
                        using (e)
                        {
                            string kaynak = e.ProviderName ?? "";
                            if (kaynak == "disk")
                            {
                                // Olay hangi diske ait: \Device\Harddisk1\DR1 gibi; sistem diski değilse (USB bellek) sayılmaz
                                string aygit = ""; try { aygit = Convert.ToString(e.Properties[0].Value); } catch { }
                                var m = System.Text.RegularExpressions.Regex.Match(aygit, @"Harddisk(\d+)");
                                if (sistemDisk >= 0 && m.Success && m.Groups[1].Value != sistemDisk.ToString()) continue;
                            }
                            bool sifir = e.Id == 129;
                            if (sifir) sifirlama++; else hata++;
                            if (detay.Count < 5)
                                detay.Add((e.TimeCreated ?? DateTime.Now).ToString("yyyy-MM-dd HH:mm") + "," + (sifir ? "129" : kaynak.Contains("tfs") ? "ntfs55" : e.Id.ToString()));
                        }
            }
            catch { }
            r["olay_disk"] = hata.ToString(); r["olay_disk_sifir"] = sifirlama.ToString(); r["olay_disk_detay"] = string.Join(";", detay);
            // SMART: disk kendi arızasını öngörüyor mu
            try
            {
                using (var s = new ManagementObjectSearcher(@"root\WMI", "SELECT PredictFailure FROM MSStorageDriver_FailurePredictStatus"))
                    foreach (ManagementObject o in s.Get()) if (Convert.ToBoolean(o["PredictFailure"])) r["smart_uyari"] = "1";
            }
            catch { }
            // Güvenilirlik sayaçları (sistem diski): çalışma saati, sıcaklık, düzeltilemeyen hata, aşınma
            try
            {
                using (var s = new ManagementObjectSearcher(@"root\Microsoft\Windows\Storage", "SELECT DeviceId, PowerOnHours, Temperature, ReadErrorsUncorrected, WriteErrorsUncorrected, Wear FROM MSFT_StorageReliabilityCounter"))
                    foreach (ManagementObject o in s.Get())
                    {
                        if (sistemDisk >= 0 && Convert.ToString(o["DeviceId"]) != sistemDisk.ToString()) continue;
                        string V(string ad) { try { var v = o[ad]; return v == null ? "" : Convert.ToString(v); } catch { return ""; } }
                        r["disk_saat"] = V("PowerOnHours"); r["disk_isi"] = V("Temperature"); r["disk_asinma"] = V("Wear");
                        long a, b; long.TryParse(V("ReadErrorsUncorrected"), out a); long.TryParse(V("WriteErrorsUncorrected"), out b);
                        if (V("ReadErrorsUncorrected") != "" || V("WriteErrorsUncorrected") != "") r["disk_duzeltilemeyen"] = (a + b).ToString();
                        break;
                    }
            }
            catch { }
        }

        public static Mesaj Veri(bool tam = true)
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
            DiskSagligi(r, zaman);
            r["olay_sicaklik"] = OlayAdet("System", "*[System[Provider[@Name='Microsoft-Windows-Kernel-Power'] and (EventID=86 or EventID=88)" + zaman + "]]").ToString();
            if (tam) try { Kontrol(r); } catch { }
            if (tam) try
            {
                r["sicaklik_surucu"] = Sensor.SurucuVar() ? "1" : "0";
                foreach (var kv in Sensor.Oku()) r["sn_" + kv.Key] = kv.Value;
            }
            catch { }
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

        // ------------------------------------------------------------ İşlem kaydı
        // Çöz ile başlatılan her işlem islemler.txt'ye yazılır; panel sonradan açıldığında da
        // durumu (sürüyor / bitti / hata), ilerlemesi ve sonucu görülür.
        // Satır: id|baslangic|bitis|islem|ad|durum(s,t,h,y)|ilerleme(%)|asama|sonuc|ek
        class Kayit { public string Id, Bas, Bit = "", Islem, Ad = "", Durum = "s", Ilerleme = "", Asama = "", Sonuc = "", Ek = ""; public int Taban, Agirlik = 100; public string AsamaAd = ""; }
        static string IslemDosya => Path.Combine(AjanAyar.Klasor, "islemler.txt");
        static readonly object ik = new object();
        static readonly HashSet<string> suren = new HashSet<string>();
        static string Simdi() => DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

        static List<Kayit> KayitlariOku()
        {
            var l = new List<Kayit>();
            try
            {
                if (File.Exists(IslemDosya))
                    foreach (var x in File.ReadAllLines(IslemDosya, Encoding.UTF8))
                    {
                        var p = x.Split('|'); if (p.Length < 10) continue;
                        l.Add(new Kayit { Id = p[0], Bas = p[1], Bit = p[2], Islem = p[3], Ad = p[4], Durum = p[5], Ilerleme = p[6], Asama = p[7], Sonuc = p[8], Ek = p[9] });
                    }
            }
            catch { }
            return l;
        }

        static void Yaz(Kayit r)
        {
            lock (ik)
            {
                var l = KayitlariOku();
                int i = l.FindIndex(x => x.Id == r.Id);
                if (i >= 0) l[i] = r; else l.Add(r);
                if (l.Count > 40) l = l.Skip(l.Count - 40).ToList();
                try
                {
                    Directory.CreateDirectory(AjanAyar.Klasor);
                    File.WriteAllLines(IslemDosya, l.Select(x => string.Join("|", new[] { x.Id, x.Bas, x.Bit, x.Islem, x.Ad, x.Durum, x.Ilerleme, x.Asama, x.Sonuc, x.Ek }.Select(Satir.T))), Encoding.UTF8);
                }
                catch { }
            }
        }

        static void Bitir(Kayit r, bool ok, string sonuc, string ek = "")
        {
            r.Bit = Simdi(); r.Durum = ok ? "t" : "h"; r.Sonuc = sonuc; r.Ek = ek; if (ok) r.Ilerleme = "100";
            Yaz(r); lock (ik) suren.Remove(r.Id);
            Gunluk.Yaz("İşlem " + r.Islem + ": " + sonuc);
        }

        /// <summary>İşlem listesi (panel Teşhis'te gösterir). Servis yeniden başladıysa yarıda kalanlar işaretlenir.</summary>
        public static Mesaj Islemler()
        {
            var r = Mesaj.Basari("");
            lock (ik)
            {
                foreach (var x in KayitlariOku().Where(x => x.Durum == "s" && !suren.Contains(x.Id)))
                { x.Durum = "y"; x.Bit = Simdi(); x.Sonuc = "Yarıda kaldı (bilgisayar kapandı ya da Sem yeniden başladı)"; Yaz(x); }
                try { r.Veri = File.Exists(IslemDosya) ? File.ReadAllBytes(IslemDosya) : new byte[0]; } catch { }
            }
            return r;
        }

        /// <summary>DURUM için: son biten işlem (panel Etkinlik'e yazar).</summary>
        public static string SonBiten()
        {
            try
            {
                var x = KayitlariOku().Where(k => k.Durum != "s" && k.Bit != "").OrderBy(k => k.Bit).LastOrDefault();
                return x == null ? "" : x.Id + "|" + x.Bit + "|" + x.Islem + "|" + x.Durum + "|" + Satir.T(x.Sonuc);
            }
            catch { return ""; }
        }

        public static Mesaj Coz(string islem, string ad)
        {
            lock (ik)
                if ((Uzun.Contains(islem) || islem == "diskonar") && KayitlariOku().Any(x => (x.Islem == islem || x.Islem == "checkup" || islem == "checkup") && Uzun.Contains(x.Islem) && x.Durum == "s" && suren.Contains(x.Id)))
                    return Mesaj.Hata("Bu işlem (ya da check-up) zaten sürüyor");
            var r = new Kayit { Id = DateTime.Now.Ticks.ToString("x"), Bas = Simdi(), Islem = islem, Ad = ad ?? "" };
            lock (ik) suren.Add(r.Id);
            Yaz(r);
            // Uzun işler arka planda; kısa olanlar hemen sonuçlanır
            if (Uzun.Contains(islem))
            {
                new Thread(() =>
                {
                    try { UzunCalistir(r); }
                    catch (Exception e) { Bitir(r, false, e.Message); }
                }) { IsBackground = true, Priority = ThreadPriority.BelowNormal }.Start();
                var m = Mesaj.Basari("Başladı"); m["id"] = r.Id; return m;
            }
            Mesaj sonuc;
            try { sonuc = Kisa(islem, ad, r); } catch (Exception e) { sonuc = Mesaj.Hata(e.Message); }
            Bitir(r, sonuc.Tamam, sonuc["aciklama"], sonuc["ek"]);
            sonuc["id"] = r.Id;
            return sonuc;
        }

        static Mesaj Kisa(string islem, string ad, Kayit kayit)
        {
            switch (islem)
            {
                case "guc":
                {
                    var eski = System.Text.RegularExpressions.Regex.Match(GucPlani(), "[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}").Value;
                    if (eski != "") GeriYaz(kayit.Id, new Dictionary<string, string> { ["tur"] = "guc", ["guid"] = eski });
                    Calistir("powercfg.exe", "/setactive " + YuksekPerformans);
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
                        ? GeriliBasari("Güç ayarı Yüksek performans yapıldı, uyku kapatıldı. Hemen geçerli.", eski != "") : Mesaj.Hata("Güç ayarı değiştirilemedi: " + g);
                }
                case "hizlibaslat":
                {
                    using (var r = Registry.LocalMachine.CreateSubKey(@"SYSTEM\CurrentControlSet\Control\Session Manager\Power")) r.SetValue("HiberbootEnabled", 0, RegistryValueKind.DWord);
                    bool ok = !HizliBaslatma();
                    if (ok) GeriYaz(kayit.Id, new Dictionary<string, string> { ["tur"] = "hizlibaslat" });
                    return ok ? GeriliBasari("Hızlı başlatma kapatıldı. Bir sonraki kapatıp açışta bilgisayar tam sıfırlanarak açılacak.", true) : Mesaj.Hata("Ayar yazılamadı");
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
                    bool kaldi = Process.GetProcessesByName(ad).Length > 0;
                    if (n == 0 && c == 0) return Mesaj.Basari(ad + " zaten çalışmıyordu");
                    return kaldi ? Mesaj.Hata(ad + " kapatılamadı, hâlâ çalışıyor") : Mesaj.Basari(ad + " kapatıldı" + (c > 0 ? " ve açılışta kendiliğinden başlaması engellendi" : " (açılışta tekrar başlayabilir)"));
                }
                case "diskonar":
                {
                    // Sorunlu kesimleri bir sonraki açılışta onarır (Windows kilitli diski açılışta düzeltir)
                    var o = Calistir("cmd.exe", "/c echo Y| chkdsk C: /spotfix", 120000);
                    return Mesaj.Basari("Onarım bir sonraki açılışa ayarlandı. Bilgisayar yeniden başlayınca birkaç dakika onarım ekranı görünür.");
                }
            }
            return KisaYeni(islem, ad, kayit);
        }

        static void TempTemizle(Kayit r)
        {
            long silinen = 0; int adet = 0;
            var klasorler = new List<string> { Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp") };
            try { foreach (var u in Directory.GetDirectories(@"C:\Users")) klasorler.Add(Path.Combine(u, @"AppData\Local\Temp")); } catch { }
            var dosyalar = new List<string>();
            r.Asama = "Dosyalar listeleniyor"; Yaz(r);
            foreach (var kl in klasorler.Where(Directory.Exists))
                try { dosyalar.AddRange(Directory.EnumerateFiles(kl, "*", SearchOption.AllDirectories)); } catch { }
            r.Asama = dosyalar.Count + " dosya inceleniyor"; Yaz(r);
            var sonYaz = DateTime.Now;
            for (int i = 0; i < dosyalar.Count; i++)
            {
                try { var fi = new FileInfo(dosyalar[i]); if (fi.LastWriteTime < DateTime.Now.AddDays(-2)) { long b = fi.Length; fi.Delete(); silinen += b; adet++; } } catch { }
                if ((DateTime.Now - sonYaz).TotalSeconds > 2) { sonYaz = DateTime.Now; r.Ilerleme = (i * 100 / Math.Max(1, dosyalar.Count)).ToString(); r.Asama = adet + " dosya silindi (" + silinen / 1048576 + " MB)"; Yaz(r); }
            }
            r.Asama = "";
            Bitir(r, true, adet + " dosya silindi, " + (silinen / 1048576) + " MB yer açıldı" + (adet == 0 ? " (temizlenecek eski dosya yoktu)" : ""));
        }

        static void DiskTara(Kayit r) { var x = DiskTaraIc(r, ""); r.Asama = ""; Bitir(r, x.ok, x.sonuc, x.ek); }

        static (bool ok, string sonuc, string ek) DiskTaraIc(Kayit r, string onek)
        {
            // chkdsk /scan: disk kullanılırken salt tarama yapar. İlerlemeyi satır satır okuyup kaydeder.
            Encoding oem; try { oem = Encoding.GetEncoding(System.Globalization.CultureInfo.CurrentCulture.TextInfo.OEMCodePage); } catch { oem = Encoding.Default; }
            var psi = new ProcessStartInfo("chkdsk.exe", "C: /scan") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true, StandardOutputEncoding = oem };
            var asamaRx = new System.Text.RegularExpressions.Regex(@"^\s*(Stage|Aşama|A.ama)\s*(\d)\s*:\s*(.+)$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            var topRx = new System.Text.RegularExpressions.Regex(@"(Total|Toplam)\s*:\s*%?\s*(\d{1,3})", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            var tum = new StringBuilder(); var satir = new StringBuilder();
            var sonYaz = DateTime.MinValue;
            r.AsamaAd = onek + "Disk taraması başlıyor"; r.Asama = r.AsamaAd; Ilerle(r, "0"); Yaz(r);
            using (var x = Process.Start(psi))
            {
                x.PriorityClass = ProcessPriorityClass.BelowNormal;
                int ch;
                while ((ch = x.StandardOutput.Read()) >= 0)
                {
                    if (ch != '\r' && ch != '\n') { satir.Append((char)ch); continue; }
                    var s = satir.ToString().Trim(); satir.Clear();
                    if (s == "") continue;
                    tum.AppendLine(s);
                    var a = asamaRx.Match(s);
                    if (a.Success) { r.AsamaAd = onek + "Disk · bölüm " + a.Groups[2].Value + "/3: " + a.Groups[3].Value.Trim().TrimEnd('.', ' '); r.Asama = r.AsamaAd; }
                    var t = topRx.Match(s);
                    if (t.Success) Ilerle(r, t.Groups[2].Value);
                    if (a.Success || (DateTime.Now - sonYaz).TotalSeconds > 3) { sonYaz = DateTime.Now; Yaz(r); }
                }
                x.WaitForExit(3 * 60 * 60 * 1000);
                // chkdsk çıkış kodu: 0 temiz, 1 hata bulunup düzeltildi, 2 temizlik yapıldı, 3 hata var (onarım gerekli)
                int kod = x.HasExited ? x.ExitCode : -1;
                var son = tum.ToString().Split('\n').Select(y => y.Trim()).Where(y => y != "" && !topRx.IsMatch(y)).ToList();
                var ozet = string.Join(" ", son.Skip(Math.Max(0, son.Count - 2)));
                DetayEkle(r, "Disk taraması (chkdsk C: /scan)", new[] { "Sonuç kodu " + kod + (kod == 0 ? " (temiz)" : kod == 1 ? " (hata bulunup düzeltildi)" : kod == 3 ? " (hata var, onarım gerekli)" : "") }
                    .Concat(son.Where(y => !asamaRx.IsMatch(y) && YuzdeVarMi(y) == false).Skip(Math.Max(0, son.Count - 25)).Select(y => "- " + y)));
                if (kod == 0 || kod == 2) return (true, "Disk temiz, sorun bulunmadı.", "");
                if (kod == 1) return (true, "Hatalar bulundu ve düzeltildi.", "");
                if (kod == 3) return (false, "Diskte hata bulundu, onarım gerekiyor. " + ozet, "diskonar");
                return (false, "Tarama tamamlanamadı. " + ozet, "");
            }
        }
    }
}
