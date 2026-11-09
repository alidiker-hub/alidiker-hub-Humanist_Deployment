#!/usr/bin/env bash
# Texture veya EsyaAyar.java değiştirdikten sonra çalıştır.
set -euo pipefail
cd "$(dirname "$0")"
./gradlew build --quiet
cp -f build/libs/tekesya-1.0.0.jar ./tekesya-1.0.0.jar
echo ""
echo "Tamam: $(pwd)/tekesya-1.0.0.jar"
echo "Bunu mods klasörüne kopyala."
