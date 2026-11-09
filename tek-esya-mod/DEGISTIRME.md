# Modu senin şekilde değiştirmek

**Tek klasör:** `benim-dosyalarim/`

```
benim-dosyalarim/
  EsyaAyar.java   ← kod (hasar, hız, dayanıklılık)
  kilic.png       ← texture
  tr_tr.json      ← Türkçe isim
  en_us.json      ← İngilizce isim
  OKU.md
```

Başka yere dokunma. Derleyince script bu dosyaları `src/` içine kopyalar ve jar üretir.

## Derleme

| Ortam | Komut |
|--------|--------|
| Linux / Mac | `./yeniden-derle.sh` |
| Windows | `yeniden-derle.bat` |

Çıktı: `tekesya-1.0.0.jar`

## EsyaAyar.java kısa sözlük

| Sabit | Anlamı |
|--------|--------|
| `HASAR` | Saldırı hasarı |
| `SALDIRI_HIZI` | Vuruş hızı |
| `DAYANIKLILIK` | Kırılma öncesi kullanım |
| `ESYA_ID` | `tekesya:kilic` — değiştirme önerilmez |

İsim için JSON:

```json
"item.tekesya.kilic": "Senin Kılıcın"
```
