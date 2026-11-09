@echo off
setlocal
cd /d "%~dp0"

if not exist "benim-dosyalarim\EsyaAyar.java" (
  echo Hata: benim-dosyalarim klasoru eksik.
  exit /b 1
)

copy /Y "benim-dosyalarim\EsyaAyar.java" "src\main\java\com\tekesya\EsyaAyar.java" >nul
copy /Y "benim-dosyalarim\kilic.png" "src\main\resources\assets\tekesya\textures\item\kilic.png" >nul
copy /Y "benim-dosyalarim\tr_tr.json" "src\main\resources\assets\tekesya\lang\tr_tr.json" >nul
copy /Y "benim-dosyalarim\en_us.json" "src\main\resources\assets\tekesya\lang\en_us.json" >nul

echo benim-dosyalarim -^> projeye kopyalandi.

call gradlew.bat build --quiet
if errorlevel 1 exit /b 1
copy /Y "build\libs\tekesya-1.0.0.jar" "tekesya-1.0.0.jar" >nul
echo.
echo Tamam: %CD%\tekesya-1.0.0.jar
echo Bunu mods klasorune kopyala.
