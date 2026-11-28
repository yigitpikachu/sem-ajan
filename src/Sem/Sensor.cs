// İşlemci sıcaklığı ve fan devri: açık kaynaklı LibreHardwareMonitor (MPL-2.0) ile okunur.
// Donanıma erişim için imzalı PawnIO sürücüsü gerekir; yalnızca panelden "Sıcaklık ölçümünü aç"
// denince kurulur (Geri al ile kaldırılır). Okuma ayrı bir süreçte yapılır ("Sem.exe /sensor"):
// sürücü tarafında bir sorun olursa servis etkilenmez.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;

namespace Sem.Ajan
{
    static class GomuluKutuphane
    {
        /// <summary>Sem.exe içine gömülü kütüphaneleri (lib.*.dll) gerektiğinde yükler.</summary>
        public static void Kur()
        {
            var yuklu = new Dictionary<string, Assembly>(StringComparer.OrdinalIgnoreCase);
            AppDomain.CurrentDomain.AssemblyResolve += (s, e) =>
            {
                var ad = new AssemblyName(e.Name).Name;
                lock (yuklu)
                {
                    if (yuklu.TryGetValue(ad, out var a)) return a;
                    using (var st = Assembly.GetExecutingAssembly().GetManifestResourceStream("lib." + ad + ".dll"))
                    {
                        if (st == null) return null;
                        var b = new byte[st.Length]; st.Read(b, 0, b.Length);
                        a = Assembly.Load(b); yuklu[ad] = a; return a;
                    }
                }
            };
        }
    }

    static class Sensor
    {
        const string PawnSurum = "2.2.0";
        const string PawnAdres = "https://github.com/namazso/PawnIO.Setup/releases/download/" + PawnSurum + "/PawnIO_setup.exe";
        const string PawnSha256 = "1f519a22e47187f70a1379a48ca604981c4fcf694f4e65b734aaa74a9fba3032";

        public static bool SurucuVar()
        {
            try { using (var k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PawnIO")) return k != null; } catch { return false; }
        }

        /// <summary>PawnIO sürücüsünü indirir (özeti doğrulanır) ve sessizce kurar.</summary>
        public static (bool ok, string metin) SurucuKur()
        {
            if (SurucuVar()) return (true, "Sürücü zaten kurulu");
            if (!Environment.Is64BitOperatingSystem) return (false, "32 bit Windows desteklenmiyor");
            var dosya = Path.Combine(AjanAyar.Klasor, "PawnIO_setup.exe");
            try
            {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                using (var w = new WebClient()) w.DownloadFile(PawnAdres, dosya);
                string ozet; using (var sha = SHA256.Create()) using (var f = File.OpenRead(dosya)) ozet = BitConverter.ToString(sha.ComputeHash(f)).Replace("-", "").ToLowerInvariant();
                if (ozet != PawnSha256) { File.Delete(dosya); return (false, "İndirilen sürücü doğrulanamadı, kurulmadı"); }
                using (var p = Process.Start(new ProcessStartInfo(dosya, "-install -silent") { UseShellExecute = false, CreateNoWindow = true }))
                {
                    p.WaitForExit(120000);
                    if (!SurucuVar()) return (false, "Sürücü kurulamadı (kod " + (p.HasExited ? p.ExitCode.ToString() : "?") + ")");
                }
                return (true, "Sıcaklık sürücüsü (PawnIO " + PawnSurum + ") kuruldu");
            }
            catch (Exception e) { return (false, "Sürücü indirilemedi: " + e.Message); }
            finally { try { File.Delete(dosya); } catch { } }
        }

        public static (bool ok, string metin) SurucuKaldir()
        {
            try
            {
                string kaldir;
                using (var k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PawnIO")) kaldir = Convert.ToString(k?.GetValue("QuietUninstallString")) ?? "";
                if (kaldir == "") return (true, "Sürücü zaten kurulu değil");
                using (var p = Process.Start(new ProcessStartInfo("cmd.exe", "/c \"" + kaldir + "\"") { UseShellExecute = false, CreateNoWindow = true })) p.WaitForExit(120000);
                return SurucuVar() ? (false, "Sürücü kaldırılamadı") : (true, "Sıcaklık sürücüsü kaldırıldı");
            }
            catch (Exception e) { return (false, e.Message); }
        }

        static DateTime sonOkuma = DateTime.MinValue; static Dictionary<string, string> son = new Dictionary<string, string>();
        static readonly object k = new object();

        /// <summary>Ayrı süreçte okur (en fazla dakikada bir; arada son değer kullanılır). Sürücü yoksa boş döner.</summary>
        public static Dictionary<string, string> Oku()
        {
            lock (k)
            {
                if (!SurucuVar()) return new Dictionary<string, string>();
                if ((DateTime.Now - sonOkuma).TotalSeconds < 60) return son;
                sonOkuma = DateTime.Now;
                var d = new Dictionary<string, string>();
                try
                {
                    var psi = new ProcessStartInfo(Process.GetCurrentProcess().MainModule.FileName, "/sensor") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, StandardOutputEncoding = Encoding.UTF8 };
                    using (var p = Process.Start(psi))
                    {
                        try { p.PriorityClass = ProcessPriorityClass.BelowNormal; } catch { }
                        var cikti = p.StandardOutput.ReadToEndAsync();
                        if (!p.WaitForExit(30000)) { try { p.Kill(); } catch { } Gunluk.Yaz("Sensör okuma zaman aşımı"); }
                        else foreach (var x in cikti.Result.Split('\n')) { int i = x.IndexOf('='); if (i > 0) d[x.Substring(0, i).Trim()] = x.Substring(i + 1).Trim(); }
                    }
                }
                catch (Exception e) { Gunluk.Yaz("Sensör: " + e.Message); }
                son = d; return d;
            }
        }

        /// <summary>"/sensor" modunda çalışır: değerleri anahtar=değer satırları olarak yazar.</summary>
        public static int Calistir()
        {
            try { Console.OutputEncoding = Encoding.UTF8; } catch { }
            try { Olc(); } catch (Exception e) { Console.WriteLine("hata=" + e.GetType().Name + ": " + e.Message.Replace("\n", " ")); }
            return 0;
        }

        static void Olc()
        {
            var c = new LibreHardwareMonitor.Hardware.Computer { IsCpuEnabled = true, IsMotherboardEnabled = true };
            c.Open();
            try
            {
                var inv = CultureInfo.InvariantCulture;
                var tum = new List<(LibreHardwareMonitor.Hardware.IHardware h, LibreHardwareMonitor.Hardware.ISensor s)>();
                foreach (var h in c.Hardware)
                {
                    h.Update();
                    foreach (var s in h.Sensors) tum.Add((h, s));
                    foreach (var alt in h.SubHardware) { alt.Update(); foreach (var s in alt.Sensors) tum.Add((alt, s)); }
                }
                var T = LibreHardwareMonitor.Hardware.SensorType.Temperature;
                // İşlemci: AMD'de "Core (Tctl/Tdie)", Intel'de "CPU Package"
                var cpuS = tum.Where(x => x.h.HardwareType == LibreHardwareMonitor.Hardware.HardwareType.Cpu && x.s.SensorType == T && x.s.Value.HasValue && x.s.Value > 0).ToList();
                var ana = cpuS.FirstOrDefault(x => x.s.Name.Contains("Tctl") || x.s.Name.Contains("Tdie") || x.s.Name.Contains("Package")).s ?? cpuS.Select(x => x.s).OrderByDescending(x => x.Value).FirstOrDefault();
                if (ana != null) Console.WriteLine("cpu_sicaklik=" + Math.Round(ana.Value.Value).ToString(inv));
                var anakartS = tum.Where(x => (x.h.HardwareType == LibreHardwareMonitor.Hardware.HardwareType.SuperIO || x.h.HardwareType == LibreHardwareMonitor.Hardware.HardwareType.Motherboard) && x.s.SensorType == T && x.s.Value.HasValue && x.s.Value > 0 && x.s.Value < 120).ToList();
                if (anakartS.Count > 0) Console.WriteLine("anakart_sicaklik=" + Math.Round(anakartS.Max(x => x.s.Value.Value)).ToString(inv));
                // Fanlar: 0 olan da yazılır (takılı ama dönmüyor olabilir); hiç bağlı olmayan başlıklar çoğunlukla hiç görünmez
                var fanlar = tum.Where(x => x.s.SensorType == LibreHardwareMonitor.Hardware.SensorType.Fan && x.s.Value.HasValue).Select(x => x.s.Name.Replace(";", " ").Replace(":", " ") + ":" + Math.Round(x.s.Value.Value).ToString(inv)).ToList();
                Console.WriteLine("fanlar=" + string.Join(";", fanlar));
                var yuk = tum.FirstOrDefault(x => x.h.HardwareType == LibreHardwareMonitor.Hardware.HardwareType.Cpu && x.s.SensorType == LibreHardwareMonitor.Hardware.SensorType.Power && x.s.Name.Contains("Package")).s;
                if (yuk?.Value != null) Console.WriteLine("cpu_guc=" + Math.Round(yuk.Value.Value).ToString(inv));
                Console.WriteLine("anakart=" + (c.SMBios?.Board?.ManufacturerName + " " + c.SMBios?.Board?.ProductName).Trim());
            }
            finally { c.Close(); }
        }
    }
}
