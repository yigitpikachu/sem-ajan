// Masaüstü düzeni: öğretmen klasörleri ve kısayollar kalır, son günlerde açılan dosyalar kalır,
// eski dosyalar masaüstündeki "Materyaller" klasörüne türlerine göre toplanır, çöp dosyalar
// "C:\Sem Silinenler" klasörüne taşınır (30 gün bekler, sonra kendiliğinden silinir).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Sem.Ortak;

namespace Sem.Ajan
{
    public static class MasaustuDuzeni
    {
        public const string Toplama = "Materyaller";
        public static string Silinenler => Path.Combine(Path.GetPathRoot(Environment.SystemDirectory), "Sem Silinenler");

        static readonly Dictionary<string, string> Turler = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        static MasaustuDuzeni()
        {
            void T(string kat, string uzantilar) { foreach (var u in uzantilar.Split(' ')) Turler["." + u] = kat; }
            T("Belgeler", "doc docx docm odt rtf txt dot dotx");
            T("PDF", "pdf");
            T("Sunumlar", "ppt pptx pps ppsx pptm odp key");
            T("Tablolar", "xls xlsx xlsm csv ods");
            T("Resimler", "jpg jpeg jfif png gif bmp webp heic heif tif tiff svg ico");
            T("Web sayfaları", "htm html mhtml mht");
            T("Videolar", "mp4 avi mov mkv wmv m4v 3gp webm mpg mpeg");
            T("Sesler", "mp3 wav m4a ogg wma aac flac opus");
            T("Arşivler", "zip rar 7z tar gz");
        }

        static readonly Regex Kurulum = new Regex(@"setup|install|kurulum|yükle|yukle|_x64|_x86|driver|sürücü|surucu|pcl6", RegexOptions.IgnoreCase);
        static readonly Regex SurucuKlasoru = new Regex(@"driver|sürücü|surucu|drİver|pcl6", RegexOptions.IgnoreCase);
        static readonly string[] Cop = { ".tmp", ".crdownload", ".part", ".partial", ".download", ".bak", ".old" };

        /// <summary>Bilgisayardaki kullanıcı masaüstleri (ortak masaüstü hariç).</summary>
        public static List<string> Masaustleri()
        {
            var l = new List<string>();
            var kok = Path.Combine(Path.GetPathRoot(Environment.SystemDirectory), "Users");
            if (!Directory.Exists(kok)) return l;
            foreach (var k in Directory.GetDirectories(kok))
            {
                var ad = Path.GetFileName(k);
                if (ad == "Public" || ad == "Default" || ad == "Default User" || ad == "All Users" || ad.StartsWith("defaultuser", StringComparison.OrdinalIgnoreCase)) continue;
                foreach (var alt in new[] { "Desktop", Path.Combine("OneDrive", "Desktop"), Path.Combine("OneDrive", "Masaüstü") })
                {
                    var d = Path.Combine(k, alt);
                    if (Directory.Exists(d)) l.Add(d);
                }
            }
            return l;
        }

        /// <summary>Plan satırları: kod|yol|boyut|neden|hedef (kod: K kalır, T toplanır, S silinir).</summary>
        public static string Planla(int gun)
        {
            var sb = new StringBuilder();
            var sinir = DateTime.Now.AddDays(-Math.Max(0, gun));
            foreach (var m in Masaustleri())
            {
                sb.Append("M|" + m + "\n");
                DirectoryInfo di = new DirectoryInfo(m);
                FileSystemInfo[] ogeler;
                try { ogeler = di.GetFileSystemInfos(); } catch { continue; }
                ogeler = ogeler.Where(x => (x.Attributes & (FileAttributes.Hidden | FileAttributes.System)) == 0).ToArray();
                var son = SonAcilanlar(m, sinir);
                var kopyalar = Kopyalar(ogeler.OfType<FileInfo>());
                foreach (var o in ogeler.OrderBy(x => x is FileInfo ? 1 : 0).ThenBy(x => x.Name))
                {
                    string kod, neden, hedef = ""; long boyut = 0;
                    try
                    {
                        string asil;
                        if (o is FileInfo kf && kopyalar.TryGetValue(kf.FullName, out asil)) { kod = "S"; neden = "Aynısının kopyası: " + asil; boyut = kf.Length; }
                        else Karar(o, ogeler, son, sinir, out kod, out neden, out hedef, out boyut);
                    }
                    catch { kod = "K"; neden = "Okunamadı"; }
                    sb.Append(kod + "|" + o.FullName + "|" + boyut + "|" + neden + "|" + hedef + "\n");
                }
            }
            return sb.ToString();
        }

        /// <summary>Kullanıcının "son kullanılanlar" listesine göre son günlerde açılmış dosya adları.</summary>
        static HashSet<string> SonAcilanlar(string masaustu, DateTime sinir)
        {
            var l = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var profil = Directory.GetParent(masaustu).FullName;
                if (Path.GetFileName(profil).Equals("OneDrive", StringComparison.OrdinalIgnoreCase)) profil = Directory.GetParent(profil).FullName;
                var son = Path.Combine(profil, @"AppData\Roaming\Microsoft\Windows\Recent");
                if (Directory.Exists(son))
                    foreach (var f in new DirectoryInfo(son).GetFiles("*.lnk"))
                        if (f.LastWriteTime >= sinir) l.Add(Path.GetFileNameWithoutExtension(f.Name));
            }
            catch { }
            return l;
        }

        /// <summary>İçeriği birebir aynı dosyalar: en kısa adlı olan kalır, diğerleri kopya sayılır (yol → asıl ad).</summary>
        static Dictionary<string, string> Kopyalar(IEnumerable<FileInfo> dosyalar)
        {
            var sonuc = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var g in dosyalar.Where(f => f.Length > 0 && f.Length < 500L * 1024 * 1024 && !f.Name.StartsWith("~$")).GroupBy(f => f.Length + "|" + f.Extension.ToLowerInvariant()).Where(g => g.Count() > 1))
            {
                var ozetli = new List<(FileInfo f, string o)>();
                foreach (var f in g) { try { ozetli.Add((f, Ozet(f.FullName))); } catch { } }
                foreach (var ayni in ozetli.GroupBy(x => x.o).Where(x => x.Count() > 1))
                {
                    var l = ayni.Select(x => x.f).OrderBy(f => f.Name.Length).ThenBy(f => f.LastWriteTime).ToList();
                    foreach (var f in l.Skip(1)) sonuc[f.FullName] = l[0].Name;
                }
            }
            return sonuc;
        }

        static void Karar(FileSystemInfo o, FileSystemInfo[] kardesler, HashSet<string> son, DateTime sinir, out string kod, out string neden, out string hedef, out long boyut)
        {
            hedef = ""; boyut = 0;
            if (o is DirectoryInfo d)
            {
                if (d.Name.Equals(Toplama, StringComparison.OrdinalIgnoreCase)) { kod = "K"; neden = "Toplama klasörü"; return; }
                bool bos = !d.EnumerateFiles("*", SearchOption.AllDirectories).Any();
                if (bos && d.LastWriteTime < sinir) { kod = "S"; neden = "Boş klasör"; return; }
                if (SurucuKlasoru.IsMatch(d.Name) && d.LastWriteTime < sinir) { kod = "S"; neden = "Sürücü paketi"; return; }
                // Kaydedilmiş web sayfasının "_files" klasörü sayfasıyla birlikte toplanır
                if (d.Name.EndsWith("_files", StringComparison.OrdinalIgnoreCase))
                {
                    var sayfa = d.Name.Substring(0, d.Name.Length - 6);
                    var html = kardesler.OfType<FileInfo>().FirstOrDefault(f => Path.GetFileNameWithoutExtension(f.Name).Equals(sayfa, StringComparison.OrdinalIgnoreCase) && Turler.TryGetValue(f.Extension, out var k) && k == "Web sayfaları");
                    if (html != null && !Yeni(html, son, sinir)) { kod = "T"; neden = "Web sayfası dosyaları"; hedef = Toplama + "\\Web sayfaları"; return; }
                }
                kod = "K"; neden = "Klasör"; return;
            }
            var f = (FileInfo)o;
            boyut = f.Length;
            var uz = f.Extension.ToLowerInvariant();
            var adsiz = Path.GetFileNameWithoutExtension(f.Name);

            if (uz == ".lnk")
            {
                if (KisayolBozuk(f.FullName)) { kod = "S"; neden = "Bozuk kısayol"; return; }
                kod = "K"; neden = "Kısayol"; return;
            }
            if (uz == ".url" || uz == ".website" || uz == ".appref-ms") { kod = "K"; neden = "Kısayol"; return; }
            if (f.Name.StartsWith("~$")) { kod = "S"; neden = "Office geçici dosyası"; return; }
            if (Cop.Contains(uz)) { kod = "S"; neden = "Geçici dosya"; return; }
            if (f.Length == 0) { kod = "S"; neden = "Boş dosya"; return; }
            if (uz == ".bat" || uz == ".cmd" || uz == ".vbs" || uz == ".ps1") { kod = "S"; neden = "Komut dosyası"; return; }

            bool yeni = Yeni(f, son, sinir);
            if (uz == ".msi" || (uz == ".exe" && Kurulum.IsMatch(adsiz)))
            {
                if (yeni) { kod = "K"; neden = "Yeni kurulum dosyası"; return; }
                kod = "S"; neden = "Kurulum dosyası"; return;
            }
            if (uz == ".exe") { kod = "K"; neden = "Program"; return; }
            if (yeni) { kod = "K"; neden = "Son günlerde kullanıldı"; return; }

            string kat;
            if (!Turler.TryGetValue(uz, out kat)) kat = "Diğer";
            kod = "T"; neden = "Eski dosya"; hedef = Toplama + "\\" + kat;
        }

        /// <summary>Son günlerde değiştirildi ya da açıldı mı (açılma: kullanıcının son kullanılanlar listesi; erişim tarihine güvenilmez).</summary>
        static bool Yeni(FileInfo f, HashSet<string> son, DateTime sinir) => f.LastWriteTime >= sinir || son.Contains(f.Name);

        static string Ozet(string yol)
        {
            using (var s = new FileStream(yol, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var h = SHA1.Create()) return Convert.ToBase64String(h.ComputeHash(s));
        }

        static bool KisayolBozuk(string yol)
        {
            try
            {
                var tur = Type.GetTypeFromProgID("WScript.Shell");
                if (tur == null) return false;
                dynamic sh = Activator.CreateInstance(tur);
                try
                {
                    dynamic k = sh.CreateShortcut(yol);
                    string hedef = k.TargetPath;
                    if (string.IsNullOrWhiteSpace(hedef) || hedef.StartsWith("\\\\")) return false;
                    var kok = Path.GetPathRoot(hedef);
                    if (string.IsNullOrEmpty(kok)) return false;
                    var dr = new DriveInfo(kok);
                    if (!dr.IsReady || dr.DriveType != DriveType.Fixed) return false;
                    return !File.Exists(hedef) && !Directory.Exists(hedef);
                }
                finally { System.Runtime.InteropServices.Marshal.FinalReleaseComObject(sh); }
            }
            catch { return false; }
        }

        /// <summary>Onaylanan planı uygular. Satırlar: kod|yol|...; yalnızca masaüstünün en üstündeki öğeler taşınır.</summary>
        public static string Uygula(string plan, out int toplanan, out int silinen, out int hata)
        {
            toplanan = silinen = hata = 0;
            var masaustleri = Masaustleri();
            var gun = DateTime.Now.ToString("yyyy-MM-dd");
            var silKlasor = Path.Combine(Silinenler, gun);
            var liste = new StringBuilder();
            var hatalar = new List<string>();
            foreach (var satir in (plan ?? "").Split('\n'))
            {
                var a = satir.Split('|');
                if (a.Length < 5 || (a[0] != "T" && a[0] != "S")) continue;
                var yol = a[1];
                var ust = Path.GetDirectoryName(yol);
                var masa = masaustleri.FirstOrDefault(m => string.Equals(m.TrimEnd('\\'), ust?.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase));
                if (masa == null) continue;
                bool klasor = Directory.Exists(yol);
                if (!klasor && !File.Exists(yol)) continue;
                try
                {
                    string hedefKlasor;
                    if (a[0] == "T")
                    {
                        var alt = a[4].Replace('/', '\\');
                        if (!alt.StartsWith(Toplama + "\\", StringComparison.OrdinalIgnoreCase) || alt.Contains("..")) continue;
                        hedefKlasor = Path.Combine(masa, alt);
                        Directory.CreateDirectory(hedefKlasor);
                    }
                    else
                    {
                        if (!Directory.Exists(Silinenler)) Klasorler.HerkeseAc(Silinenler);
                        hedefKlasor = silKlasor;
                        Directory.CreateDirectory(hedefKlasor);
                    }
                    var hedef = Klasorler.Benzersiz(hedefKlasor, Path.GetFileName(yol));
                    while (Directory.Exists(hedef)) hedef = Klasorler.Benzersiz(hedefKlasor, Path.GetFileNameWithoutExtension(hedef) + "_" + Path.GetExtension(hedef));
                    if (klasor) Directory.Move(yol, hedef); else File.Move(yol, hedef);
                    if (a[0] == "T") toplanan++;
                    else { silinen++; liste.Append(yol + "  ->  " + hedef + "\r\n"); }
                }
                catch (Exception e) { hata++; if (hatalar.Count < 5) hatalar.Add(Path.GetFileName(yol) + ": " + e.Message); }
            }
            if (liste.Length > 0) try { File.AppendAllText(Path.Combine(silKlasor, "nereden.txt"), liste.ToString(), Encoding.UTF8); } catch { }
            return string.Join("\n", hatalar);
        }

        static DateTime sonTemizlik = DateTime.MinValue;

        /// <summary>30 günden eski "Sem Silinenler" günlerini siler (günde bir kez).</summary>
        public static void EskileriTemizle()
        {
            if ((DateTime.Now - sonTemizlik).TotalHours < 12) return;
            sonTemizlik = DateTime.Now;
            if (!Directory.Exists(Silinenler)) return;
            foreach (var d in Directory.GetDirectories(Silinenler))
            {
                DateTime t;
                if (DateTime.TryParseExact(Path.GetFileName(d), "yyyy-MM-dd", null, System.Globalization.DateTimeStyles.None, out t) && (DateTime.Today - t).TotalDays > 30)
                    try { Directory.Delete(d, true); Gunluk.Yaz("Sem Silinenler: " + Path.GetFileName(d) + " temizlendi"); } catch { }
            }
        }
    }
}
