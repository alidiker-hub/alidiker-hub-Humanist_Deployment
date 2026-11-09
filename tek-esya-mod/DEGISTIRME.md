# Senin düzenleyeceğin dosyalar

Çoğu değişiklik için **sadece bu üçü** yeterli:

| # | Ne | Dosya |
|---|-----|--------|
| 1 | **Kod / istatistik** | `src/main/java/com/tekesya/EsyaAyar.java` |
| 2 | **Texture (görünüm)** | `src/main/resources/assets/tekesya/textures/item/kilic.png` |
| 3 | **İsim (TR / EN)** | `src/main/resources/assets/tekesya/lang/tr_tr.json` ve `en_us.json` |

Sonra jar:

```bash
cd tek-esya-mod
./yeniden-derle.sh
```

Oyuna gidecek dosya: **`tekesya-1.0.0.jar`** (script bunu günceller).

---

## 1. EsyaAyar.java (kod)

| Sabit | Anlamı |
|--------|--------|
| `ESYA_ID` | `tekesya:kilic` — nadiren değiştir |
| `HASAR` | Saldırı hasarı |
| `SALDIRI_HIZI` | Vuruş hızı (demir ≈ 1.6) |
| `DAYANIKLILIK` | Dayanıklılık |
| `BUYU_SEVIYESI` | Büyü masası |
| `TIER_SEVIYESI` | Malzeme seviyesi |

`ModItems.java` dosyasına dokunmana gerek yok; ayarlar buradan okunur.

`ESYA_ID` değişirse ayrıca: `models/item/<id>.json`, `textures/item/<id>.png`, lang içinde `item.tekesya.<id>`.

---

## 2. kilic.png (texture)

- Klasörde `BURAYI_OKU.txt` var.
- Kendi çizimini **aynı dosya adıyla** kaydet: `kilic.png`.
- Model (`models/item/kilic.json`) aynı kalabilir.

---

## 3. Dil dosyaları

```json
"item.tekesya.kilic": "Buraya isim"
```

`ESYA_ID` farklıysa `kilic` yerine o id’yi kullan.

---

## İleri (nadiren)

| Konu | Dosya |
|------|--------|
| Mod kimliği | `TekEsyaMod.java` → `MOD_ID` |
| Forge meta | `src/main/resources/META-INF/mods.toml` |

Bunları değiştirmeden modu özelleştirebilirsin.
