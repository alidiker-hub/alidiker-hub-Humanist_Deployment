# Mod çalışmıyorsa

## "Mod olarak tanımıyor" (bırakma / mods klasörü)

Forge sadece **`.jar`** dosyasını mod sayar. `tek-esya-mod` klasörünü, kaynak kodu veya `build.gradle` atma.

- Doğru dosya: **`tekesya-1.0.0.jar`**
- Profil: **Forge 1.20.1** (vanilla, Fabric, 1.21 profili ile jar yüklenmez)
- Klasör: **`.minecraft/mods/`** (resourcepacks / saves değil)

Mods ekranında görünmüyorsa önce Forge profilini seçip oyunu yeniden başlat.

## Gerekli sürüm

- **Minecraft 1.20.1** (1.20.2 / 1.21 olmaz)
- **Forge** (NeoForge / Fabric değil), önerilen: **47.4.x**

Ana menü → Mods listesinde **Tek Esya** (`tekesya`) yeşil / yüklü görünmeli.

## Jar nereye?

Sadece şu dosya:

`tekesya-1.0.0.jar` → `.minecraft/mods/`

Eski `tekesya` jar varsa sil; tek jar kalsın.

## Kılıcı nerede bulursun?

1. **Yaratıcı** → arama: `kilic` veya sekmelerde **Kilic** / **Tek Esya**
2. **Savaş (Combat)** sekmesinde de listelenir
3. **Survival** → üretim tarifi (demir kılıç gibi):
   - Üstte demir, ortada demir, altta çubuk

## Hâlâ yok / çöküyorsa

`logs/latest.log` içinde `tekesya` veya `error` ara.

Çok yüksek hasar/hız bazen tek oyuncuda lag yapabilir; yine de mod yüklenmeli.
