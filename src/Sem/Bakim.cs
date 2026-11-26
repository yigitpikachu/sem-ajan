// Sağlık uyarıları, envanter ve program (winget) işleri: ajan tarafı.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Text;
using System.Threading;
using Microsoft.Win32;
using Sem.Ortak;

namespace Sem.Ajan
{
    /// <summary>Disk doluluğu ve disk arızası. 15 dakikada bir, arka planda ve kısa sürede denetlenir.</summary>
    static class Saglik
    {
        static string ozet = "";
        static DateTime son = DateTime.MinValue;
        static int calisiyor;

        public static string Ozet() { Denetle(); return ozet; }

        public static void Denetle()
        {
            if ((DateTime.Now - son).TotalMinutes < 15 || Interlocked.Exchange(ref calisiyor, 1) == 1) return;
            son = DateTime.Now;
            new Thread(() =>
            {
                try
                {
                    var l = new List<string>();
                    try
                    {
                        var c = new DriveInfo(Path.GetPathRoot(Environment.SystemDirectory));
                        double gb = c.AvailableFreeSpace / 1073741824.0, oran = c.AvailableFreeSpace * 100.0 / Math.Max(1, c.TotalSize);
                        if (gb < 10 || oran < 8) l.Add("disk|C: diskinde " + gb.ToString("0.#") + " GB boş yer kaldı");
                    }
                    catch { }
                    foreach (var d in Envanter.Diskler())
                        if (d.Saglik != "" && d.Saglik != "Sağlam") l.Add("ariza|" + d.Ad + " diski sorun bildiriyor (" + d.Saglik + "), yedek alın");
                    ozet = string.Join(";", l);
                }
                catch (Exception e) { Gunluk.Yaz("Sağlık: " + e.Message); }
                finally { calisiyor = 0; }
            }) { IsBackground = true, Priority = ThreadPriority.BelowNormal }.Start();
        }
    }

    static class Envanter
    {
        public class Disk { public string Ad = "", Tur = "", Boyut = "", Saglik = ""; }

        static IEnumerable<ManagementObject> Wmi(string kapsam, string sorgu)
        {
            var l = new List<ManagementObject>();
            try { using (var s = new ManagementObjectSearcher(kapsam, sorgu)) foreach (ManagementObject o in s.Get()) l.Add(o); } catch { }
            return l;
        }

        static string S(ManagementObject o, string ad) { try { return Convert.ToString(o[ad])?.Trim() ?? ""; } catch { return ""; } }

        public static List<Disk> Diskler()
        {
            var l = new List<Disk>();
            foreach (var o in Wmi(@"root\Microsoft\Windows\Storage", "SELECT FriendlyName, MediaType, Size, HealthStatus FROM MSFT_PhysicalDisk"))
            {
                var mt = S(o, "MediaType"); var hs = S(o, "HealthStatus");
                long b; long.TryParse(S(o, "Size"), out b);
                l.Add(new Disk
                {
                    Ad = S(o, "FriendlyName"), Tur = mt == "4" ? "SSD" : mt == "3" ? "HDD" : "",
                    Boyut = b > 0 ? Math.Round(b / 1e9) + " GB" : "",
                    Saglik = hs == "0" ? "Sağlam" : hs == "1" ? "Uyarı" : hs == "2" ? "Arızalı" : ""
                });
            }
            if (l.Count == 0)
                foreach (var o in Wmi(@"root\cimv2", "SELECT Model, Size, Status FROM Win32_DiskDrive"))
                {
                    long b; long.TryParse(S(o, "Size"), out b);
                    var st = S(o, "Status");
                    l.Add(new Disk { Ad = S(o, "Model"), Boyut = b > 0 ? Math.Round(b / 1e9) + " GB" : "", Saglik = st == "OK" ? "Sağlam" : st == "Pred Fail" ? "Arızalanıyor" : st });
                }
            return l;
        }

        public static Mesaj Topla()
        {
            var r = Mesaj.Basari();
            try
            {
                using (var k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
                {
                    var ad = Convert.ToString(k?.GetValue("ProductName")) ?? "Windows";
                    int yapi; int.TryParse(Convert.ToString(k?.GetValue("CurrentBuild")), out yapi);
                    if (yapi >= 22000) ad = ad.Replace("Windows 10", "Windows 11");
                    r["windows"] = ad + " " + (Convert.ToString(k?.GetValue("DisplayVersion")) ?? Convert.ToString(k?.GetValue("ReleaseId")) ?? "") + " (" + yapi + ")";
                }
            }
            catch { }
            foreach (var o in Wmi(@"root\cimv2", "SELECT LicenseStatus FROM SoftwareLicensingProduct WHERE ApplicationID='55c92734-d682-4d71-983e-d6ec3f16059f' AND PartialProductKey IS NOT NULL"))
                if (S(o, "LicenseStatus") == "1") { r["lisans"] = "Etkin"; break; } else r["lisans"] = "Etkin değil";
            foreach (var o in Wmi(@"root\cimv2", "SELECT Name FROM Win32_Processor")) { r["islemci"] = S(o, "Name"); break; }
            foreach (var o in Wmi(@"root\cimv2", "SELECT Manufacturer, Model, TotalPhysicalMemory FROM Win32_ComputerSystem"))
            {
                long b; long.TryParse(S(o, "TotalPhysicalMemory"), out b);
                r["ram"] = Math.Round(b / 1073741824.0) + " GB";
                r["model"] = (S(o, "Manufacturer") + " " + S(o, "Model")).Trim();
                break;
            }
            foreach (var o in Wmi(@"root\cimv2", "SELECT SerialNumber FROM Win32_BIOS")) { r["seri"] = S(o, "SerialNumber"); break; }
            r["diskler"] = string.Join("\n", Diskler().Select(d => d.Ad + "|" + d.Tur + "|" + d.Boyut + "|" + d.Saglik));
            try
            {
                var c = new DriveInfo(Path.GetPathRoot(Environment.SystemDirectory));
                r["c_bos"] = (c.AvailableFreeSpace / 1073741824.0).ToString("0") + " / " + (c.TotalSize / 1073741824.0).ToString("0") + " GB boş";
            }
            catch { }
            r["programlar"] = string.Join("\n", Programlar.Kurulu().Select(p => p.ad + "|" + p.surum).OrderBy(x => x, StringComparer.CurrentCultureIgnoreCase));
            return r;
        }
    }

    static class Programlar
    {
        /// <summary>Kurulu programlar (makine geneli ve kullanıcı başına), sistem bileşenleri hariç.</summary>
        public static List<(string ad, string surum)> Kurulu()
        {
            var l = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            void Tara(RegistryKey kok, string yol)
            {
                try
                {
                    using (var k = kok.OpenSubKey(yol))
                        if (k != null)
                            foreach (var a in k.GetSubKeyNames())
                                using (var p = k.OpenSubKey(a))
                                {
                                    var ad = Convert.ToString(p?.GetValue("DisplayName"));
                                    if (string.IsNullOrWhiteSpace(ad)) continue;
                                    if (Convert.ToString(p.GetValue("SystemComponent")) == "1" || p.GetValue("ParentKeyName") != null) continue;
                                    if (ad.StartsWith("Microsoft Visual C++") || ad.StartsWith("Update for") || ad.Contains("(KB")) continue;
                                    l[ad.Trim()] = Convert.ToString(p.GetValue("DisplayVersion")) ?? "";
                                }
                }
                catch { }
            }
            const string u = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";
            foreach (var g in new[] { RegistryView.Registry64, RegistryView.Registry32 })
                try { using (var b = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, g)) Tara(b, u); } catch { }
            try { foreach (var sid in Registry.Users.GetSubKeyNames().Where(x => x.StartsWith("S-1-5-21") && !x.EndsWith("_Classes"))) Tara(Registry.Users, sid + "\\" + u); } catch { }
            return l.Select(kv => (kv.Key, kv.Value)).ToList();
        }

        /// <summary>adlar: "anahtar=desen" satırları. Kurulu olanlar "anahtar|ad|sürüm" olarak döner.</summary>
        public static Mesaj Durum(string adlar)
        {
            var kurulu = Kurulu();
            var sb = new StringBuilder();
            foreach (var satir in (adlar ?? "").Split('\n'))
            {
                var i = satir.IndexOf('='); if (i <= 0) continue;
                var desen = satir.Substring(i + 1).Trim(); if (desen == "") continue;
                var b = kurulu.FirstOrDefault(p => p.ad.IndexOf(desen, StringComparison.OrdinalIgnoreCase) >= 0);
                if (b.ad != null) sb.Append(satir.Substring(0, i) + "|" + b.ad + "|" + b.surum + "\n");
            }
            var r = Mesaj.Basari(); r["kurulu"] = sb.ToString(); r["winget"] = Winget() != null ? "1" : "0";
            return r;
        }

        public static string Winget()
        {
            try
            {
                var kok = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "WindowsApps");
                var aday = Directory.GetDirectories(kok, "Microsoft.DesktopAppInstaller_*_x64__8wekyb3d8bbwe")
                    .Select(d => Path.Combine(d, "winget.exe")).Where(File.Exists).OrderByDescending(x => x).FirstOrDefault();
                if (aday != null) return aday;
            }
            catch { }
            var yerel = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Microsoft\WindowsApps\winget.exe");
            return File.Exists(yerel) ? yerel : null;
        }

        public static string WingetCalistir(string islem, string id)
        {
            var exe = Winget();
            if (exe == null) return "HATA: winget yok";
            string Calis(string ek)
            {
                var psi = new ProcessStartInfo(exe, islem + " --id " + id + " -e --silent --accept-package-agreements --accept-source-agreements --disable-interactivity" + ek)
                { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8 };
                using (var p = Process.Start(psi))
                {
                    var cikti = p.StandardOutput.ReadToEndAsync(); p.StandardError.ReadToEnd();
                    if (!p.WaitForExit(30 * 60 * 1000)) { try { p.Kill(); } catch { } return "HATA: zaman aşımı"; }
                    var k = p.ExitCode;
                    if (k == 0) return "tamamlandı";
                    if (k == -1978335189) return "zaten güncel";
                    if (k == -1978335135) return "zaten kurulu";
                    if (k == -1978335212) return "HATA: paket bulunamadı";
                    var son = (cikti.Result ?? "").Split('\n').Select(x => x.Trim()).Where(x => x.Length > 3).LastOrDefault() ?? "";
                    return "HATA: kod " + k + (son != "" ? " (" + son + ")" : "");
                }
            }
            var r = Calis(" --scope machine");
            if (r.StartsWith("HATA") && !r.Contains("bulunamadı")) r = Calis("");
            return r;
        }
    }
}
