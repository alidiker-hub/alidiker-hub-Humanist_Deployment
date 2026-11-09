# Tek eşya modu — neyi nerede değiştirirsin?

Dağıtımdan önce çoğu zaman **1 → 2 → 4** yeterli. Jar’ı güncellemek için mutlaka **4. adım**.

## 1. Görünüm (texture — kılıç resmi)

| Ne | Dosya |
|----|--------|
| Oyundaki kılıç ikonu | `src/main/resources/assets/tekesya/textures/item/kilic.png` |

- Format: **PNG**, tipik boyut **16×16** (şeffaf arka plan).
- Dosya adını **`kilic.png`** tut; model buna bağlı.
- Model dosyası (`models/item/kilic.json`) normalde dokunmana gerek yok; sadece farklı isim kullanacaksan hem PNG hem modeli birlikte güncelle.

## 2. İstatistikler (hasar, hız, dayanıklılık)

| Ne | Dosya | Sabitler |
|----|--------|----------|
| Kılıç değerleri | `src/main/java/com/tekesya/ModItems.java` | `HASAR`, `HIZ`, `DAYANIKLILIK` |

`HASAR`, `HIZ`, `DAYANIKLILIK` satırlarının hemen üstündeki yorumda texture yolu da yazıyor.

## 3. İsimler (oyunda görünen yazı)

| Dil | Dosya |
|-----|--------|
| Türkçe | `src/main/resources/assets/tekesya/lang/tr_tr.json` → `item.tekesya.kilic` |
| İngilizce | `src/main/resources/assets/tekesya/lang/en_us.json` → `item.tekesya.kilic` |

Yaratıcı sekme adı: `itemGroup.tekesya`.

## 4. Jar’ı yeniden üret (indirenler bunu görür)

```bash
cd tek-esya-mod
./gradlew build
```

Çıkan dosya: `build/libs/tekesya-1.0.0.jar`  
İstersen kökteki `tekesya-1.0.0.jar` dosyasını bununla değiştirerek repoda da güncel tut.

## 5. Mod kimliği (nadiren değişir)

- Mod ID: `src/main/java/com/tekesya/TekEsyaMod.java` → `MOD_ID = "tekesya"`
- Kayıt adı: `ModItems.java` içinde `ITEMS.register("kilic", ...)` → oyun içi id: `tekesya:kilic`

Bunları değiştirirsen klasör adları (`assets/tekesya/...`) ve `mods.toml` ile uyumlu kalmalı.
