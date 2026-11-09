package com.tekesya;

/**
 * Bu dosyayı düzenle — kılıç istatistikleri ve kayıt adı burada.
 *
 * Texture (PNG, 16×16, şeffaf):
 *   src/main/resources/assets/tekesya/textures/item/kilic.png
 *   → Dosyayı Paint, Aseprite, GIMP vb. ile değiştir; adı {@code kilic.png} kalsın.
 *
 * Oyundaki isim:
 *   src/main/resources/assets/tekesya/lang/tr_tr.json
 *   src/main/resources/assets/tekesya/lang/en_us.json
 *   Anahtar: {@code item.tekesya.<ESYA_ID>}
 *
 * Derleme: proje kökünde {@code ./yeniden-derle.sh}
 */
public final class EsyaAyar {

    /** Oyun içi id: {@code tekesya:<ESYA_ID>}. Değiştirirsen model/lang dosya adlarını da uyumlu yap. */
    public static final String ESYA_ID = "kilic";

    /** Saldırı hasarı (demir kılıç ≈ 6). */
    public static final int HASAR = 6;

    /** Saldırı hızı; demir kılıç ≈ 1.6, el ≈ 4.0. */
    public static final double SALDIRI_HIZI = 1.6;

    /** Kullanım ömrü (kırılma). */
    public static final int DAYANIKLILIK = 250;

    /** Büyü masasında çıkan büyü seviyesi. */
    public static final int BUYU_SEVIYESI = 14;

    /** Tier seviyesi (2 ≈ demir). */
    public static final int TIER_SEVIYESI = 2;

    private EsyaAyar() {
    }
}
