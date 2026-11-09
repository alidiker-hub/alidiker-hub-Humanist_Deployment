# Tek Esya (Forge 1.20.1)

Tek bir esya ekleyen Forge modu. Kaynak kod burada; jar derlenmis cikti.

## Nereyi duzenlersin

| Dosya | Ne ise yarar |
| --- | --- |
| `src/main/java/com/tekesya/ModItems.java` | Esyanin kendisi. Isim, ozellik, yaratici sekme. |
| `src/main/java/com/tekesya/TekEsyaMod.java` | Modun acilis noktasi. |
| `src/main/resources/assets/tekesya/lang/tr_tr.json` | Oyundaki Turkce isim. |
| `src/main/resources/assets/tekesya/models/item/ornek_esya.json` | Esya modeli. |
| `src/main/resources/assets/tekesya/textures/item/ornek_esya.png` | 16x16 doku. |

Esya id: `tekesya:ornek_esya`. Yaratıcı envanterde **Tek Esya** sekmesi.

## Calistirma

Java 17 gerekir.

```bash
./gradlew runClient
```

Jar:

```bash
./gradlew build
```

Cikti: `build/libs/tekesya-1.0.0.jar` — bunu `mods` klasorune koy. Forge 1.20.1 (47.2+) lazim.
