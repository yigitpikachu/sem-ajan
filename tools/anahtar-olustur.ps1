# Sem Ajan imza anahtarı oluşturur. Bir kez, yönetici bilgisayarında çalıştırılır.
# - Gizli anahtar panoya kopyalanır: GitHub'da SEM_IMZA_ANAHTARI gizli değerine yapıştırın. Hiçbir dosyaya yazılmaz.
# - Açık anahtar masaüstüne "sem-acik-anahtar.txt" olarak kaydedilir: bunu programa gömmek için paylaşın (gizli değildir).
$rsa = New-Object System.Security.Cryptography.RSACryptoServiceProvider 3072
$rsa.PersistKeyInCsp = $false
Set-Clipboard -Value $rsa.ToXmlString($true)
$masaustu = [Environment]::GetFolderPath('Desktop')
Set-Content -Path (Join-Path $masaustu 'sem-acik-anahtar.txt') -Value $rsa.ToXmlString($false) -Encoding ASCII
$rsa.Clear()
Start-Process 'https://github.com/yigitpikachu/sem-ajan/settings/secrets/actions/new'
Write-Host ''
Write-Host 'Gizli anahtar panoya kopyalandi.' -ForegroundColor Green
Write-Host 'Acilan GitHub sayfasinda Name: SEM_IMZA_ANAHTARI yazin, Secret kutusuna Ctrl+V ile yapistirip kaydedin.'
Write-Host 'Masaustundeki sem-acik-anahtar.txt dosyasini Claude ile paylasin.'
