# Sem Ajan

Sem Özel Eğitim ve Rehabilitasyon Merkezi'ndeki bilgisayarları tek yerden yönetmek için hazırlanmış Windows programı.

**Tek dosya: `Sem.exe`.** Aynı dosya hem yönetici bilgisayarına hem de diğer bilgisayarlara kurulur.

## Neler yapar?

- Kurum logolu duvar kâğıdı (değiştirilirse geri gelir)
- Gün gün ayarlanabilen otomatik kapanma, 5 dakika önce geri sayımlı uyarı, sınırlı erteleme, yönetici şifresiyle iptal
- Ekranın ortasında duyuru
- Dosya gönderme ve bilgisayardan dosya alma
- Uzaktan kapatma, yeniden başlatma, kilitleme, ağdan açma (Wake-on-LAN)
- Yazıcı sürücüsü kurma ve yazıcı onarma, program kurma
- GitHub üzerinden doğrulamalı online güncelleme

## İndirme

En son sürüm: [Releases](../../releases/latest) sayfasından **Sem.exe**.

## Teknik

- C# / .NET Framework 4.8 (Windows 10 ve 11'de hazır gelir), WinForms
- Arka planda `SemAjan` Windows hizmeti olarak çalışır; kullanıcı ekranındaki pencereler `Sem.exe /ekran` ile açılır
- Bilgisayarlar arası iletişim: TCP 47800 (AES-256 + HMAC-SHA256, anahtar yönetici şifresinden PBKDF2 ile türetilir), keşif: UDP 47802
- Sürümler `v*` etiketiyle GitHub Actions tarafından derlenir (`.github/workflows/surum.yml`). Programlar yalnızca bu iş akışının yüklediği, SHA-256 özeti, ürün adı ve sürüm numarası tutan Sem.exe'yi kurar. Depoya yazma yetkisi güncelleme yetkisi demektir: GitHub hesabında iki adımlı doğrulama açık olmalı.
- İkonlar: [Fluent UI System Icons](https://github.com/microsoft/fluentui-system-icons) (MIT)

## Yeni sürüm yayınlama

1. `src/Sem/Sem.csproj` içindeki `<Version>` değerini artırın, `docs/surum-notlari/vX.Y.Z.md` dosyasına notları yazın.
2. `main` dalına gönderin. Bu sürüm daha önce yayınlanmadıysa GitHub Actions derler ve Release'i yayınlar. Bilgisayarlar birkaç saat içinde kendiliğinden güncellenir ya da Sem Ajan → Güncellemeler'den hemen dağıtılabilir.
