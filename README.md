# Sem Ajan

Sem Özel Eğitim ve Rehabilitasyon Merkezi'ndeki bilgisayarları tek yerden yönetmek için hazırlanmış Windows programı.

**Tek dosya: `Sem.exe`.** Aynı dosya hem yönetici bilgisayarına hem de diğer bilgisayarlara kurulur.

## Neler yapar?

**Bilgisayarlar**
- Ağdaki bilgisayarları kendiliğinden bulur; kartta canlı ekran önizlemesi, açık/kapalı durumu, IP, açık kalma süresi, kapanma saati
- Bilgisayarlara ad ve simge verme, oda gruplarına ayırma (Sınıflar, Terapi odaları, Ofis), kart ya da liste görünümü
- Kilitleme, kapatma ve yeniden başlatma (basılı tutarak onay, ekranda 1 dakikalık geri sayım), ağdan açma (Wake-on-LAN)
- Sabah belirlenen saatte bilgisayarları ağdan açma

**Kapanma**
- Gün gün ayarlanabilen otomatik kapanma, önceden geri sayımlı uyarı, sınırlı erteleme, yönetici şifresiyle o gün için iptal

**Duyuru ve dosya**
- Ekranın ortasında duyuru; kimin "Tamam"a bastığı Duyurular sayfasında görünür
- Dosya gönderme (masaüstündeki "Sem Gelen Dosyalar" klasörüne) ve bilgisayardan dosya alma

**Bakım**
- Kurum logolu duvar kâğıdı (değiştirilirse geri gelir)
- Yazıcı kurma (yönetici bilgisayarındaki Canon sürücüsü diğerlerine aynen kurulur) ve yazıcı onarma, sessiz program kurma
- GitHub üzerinden doğrulamalı otomatik güncelleme

Yönetici bilgisayarında program saatin yanında da çalışır: tek tıkla duyuru, "bugün kapanmasın", hepsini kilitle.

## Kurulum

1. [Releases](../../releases/latest) sayfasından **Sem.exe**'yi indirin.
2. Her bilgisayarda Sem.exe'yi çalıştırın, yönetici şifresini girin (tüm bilgisayarlarda aynı) ve **Kur**'a basın. Yönetici bilgisayarında "Yönetici bilgisayarı" seçeneğini açın.
3. Yönetici bilgisayarında masaüstündeki **Sem Ajan** kısayolundan paneli açın.

Kurulumdan sonra program Windows ile birlikte arka planda kendiliğinden başlar.

## Güvenlik

- Bilgisayarlar yalnızca kurulumda belirlenen yönetici şifresinden türetilen anahtarla gelen komutları kabul eder. Programı başka biri indirip kursa bile şifreyi bilmeden bu bilgisayarlara erişemez.
- Şifre hiçbir yerde açık metin olarak saklanmaz; değiştirildiğinde o an kapalı olan bilgisayarlar açılınca yeni şifreye geçer.
- Güncellemelerde yalnızca bu deponun GitHub Actions iş akışının derleyip yüklediği, SHA-256 özeti ve sürüm numarası tutan Sem.exe kabul edilir. Depoya yazma yetkisi güncelleme yetkisi demektir: GitHub hesabında iki adımlı doğrulama açık olmalı.

## Teknik

- C# / .NET Framework 4.8 (Windows 10 ve 11'de hazır gelir), WinForms
- Arka planda `SemAjan` Windows hizmeti; kullanıcı ekranındaki pencereler `Sem.exe /ekran`, yönetim paneli `Sem.exe /yonetim` ile açılır
- İletişim: TCP 47800 (AES-256 + HMAC-SHA256, anahtar şifreden PBKDF2 ile), keşif: UDP 47802
- Derleme: `dotnet build src/Sem/Sem.csproj -c Release`
- İkonlar: [Fluent UI System Icons](https://github.com/microsoft/fluentui-system-icons) (MIT)

## Yeni sürüm yayınlama

1. `src/Sem/Sem.csproj` içindeki `<Version>` değerini artırın, notları `docs/surum-notlari/vX.Y.Z.md` dosyasına yazın.
2. `main` dalına gönderin. Bu sürüm daha önce yayınlanmadıysa GitHub Actions derleyip Release olarak yayınlar. Bilgisayarlar birkaç saat içinde kendiliğinden güncellenir; Sem Ajan → Güncellemeler'den hemen de dağıtılabilir.
