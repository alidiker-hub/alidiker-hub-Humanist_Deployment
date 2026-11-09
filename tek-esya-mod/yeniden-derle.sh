#!/usr/bin/env bash
# önce benim-dosyalarim/ → src, sonra jar üret
set -euo pipefail
ROOT="$(cd "$(dirname "$0")" && pwd)"
cd "$ROOT"

BENIM="$ROOT/benim-dosyalarim"
if [[ ! -d "$BENIM" ]]; then
  echo "Hata: benim-dosyalarim/ yok." >&2
  exit 1
fi

cp -f "$BENIM/EsyaAyar.java" "$ROOT/src/main/java/com/tekesya/EsyaAyar.java"
cp -f "$BENIM/kilic.png" "$ROOT/src/main/resources/assets/tekesya/textures/item/kilic.png"
cp -f "$BENIM/tr_tr.json" "$ROOT/src/main/resources/assets/tekesya/lang/tr_tr.json"
cp -f "$BENIM/en_us.json" "$ROOT/src/main/resources/assets/tekesya/lang/en_us.json"

echo "benim-dosyalarim → projeye kopyalandi."

./gradlew build --quiet
cp -f build/libs/tekesya-1.0.0.jar ./tekesya-1.0.0.jar
echo ""
echo "Tamam: $ROOT/tekesya-1.0.0.jar"
echo "Bunu mods klasörüne kopyala."
