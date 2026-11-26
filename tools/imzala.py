"""Sem.exe'yi SEM_IMZA_ANAHTARI ortam değişkenindeki özel anahtarla imzalar.
Anahtar PEM ya da Windows'ta üretilmiş .NET XML (<RSAKeyValue>) biçiminde olabilir.
Kullanım: python3 tools/imzala.py Sem.exe Sem.exe.sig"""
import base64, os, sys, xml.etree.ElementTree as ET
from cryptography.hazmat.primitives import hashes, serialization
from cryptography.hazmat.primitives.asymmetric import padding, rsa

def anahtar_yukle(metin):
    metin = metin.strip()
    if metin.startswith("-----BEGIN"):
        return serialization.load_pem_private_key(metin.encode(), password=None)
    k = ET.fromstring(metin)
    sayi = lambda ad: int.from_bytes(base64.b64decode(k.find(ad).text.strip()), "big")
    acik = rsa.RSAPublicNumbers(sayi("Exponent"), sayi("Modulus"))
    return rsa.RSAPrivateNumbers(sayi("P"), sayi("Q"), sayi("D"), sayi("DP"), sayi("DQ"), sayi("InverseQ"), acik).private_key()

metin = os.environ.get("SEM_IMZA_ANAHTARI", "")
if not metin.strip():
    sys.exit("SEM_IMZA_ANAHTARI gizli değeri tanımlı değil")
anahtar = anahtar_yukle(metin)
veri = open(sys.argv[1], "rb").read()
imza = anahtar.sign(veri, padding.PKCS1v15(), hashes.SHA256())
open(sys.argv[2], "w").write(base64.b64encode(imza).decode())
print("İmzalandı,", anahtar.key_size, "bit")
