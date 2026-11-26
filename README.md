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
- GitHub üzerinden imzalı online güncelleme

## İndirme

En son sürüm: [Releases](../../releases/latest) sayfasından **Sem.exe**.

## Teknik

- C# / .NET Framework 4.8 (Windows 10 ve 11'de hazır gelir), WinForms
- Arka planda `SemAjan` Windows hizmeti olarak çalışır; kullanıcı ekranındaki pencereler `Sem.exe /ekran` ile açılır
- Bilgisayarlar arası iletişim: TCP 47800 (AES-256 + HMAC-SHA256, anahtar yönetici şifresinden PBKDF2 ile türetilir), keşif: UDP 47802
- Sürümler `v*` etiketiyle GitHub Actions tarafından derlenir ve RSA-3072 ile imzalanır (`.github/workflows/surum.yml`). Programlar yalnızca bu imzayı taşıyan güncellemeleri kurar.
- İkonlar: [Fluent UI System Icons](https://github.com/microsoft/fluentui-system-icons) (MIT)

## Yeni sürüm yayınlama

1. `src/Sem/Sem.csproj` içindeki `<Version>` değerini artırın, `docs/surum-notlari/vX.Y.Z.md` dosyasına notları yazın.
2. `git tag vX.Y.Z && git push origin vX.Y.Z`
3. GitHub Actions derleyip imzalar ve Release'i yayınlar. Bilgisayarlar birkaç saat içinde kendiliğinden güncellenir ya da Sem Ajan → Güncellemeler'den hemen dağıtılabilir.
