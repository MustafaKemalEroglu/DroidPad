#!/usr/bin/env bash
# Cihaz tarafı girdi yardımcısını (Helper/DroidPadInput.java) derleyip Helper/droidpad-input.dex üretir.
# Gerekenler: JDK 17+ (javac), internet (ilk çalıştırmada Android android.jar ve R8/D8 indirilir; ~80 MB, önbelleğe alınır).
# Derlenmiş .dex depoya dahildir; yalnızca Java kaynağını değiştirirseniz çalıştırmanız gerekir.
set -euo pipefail

cd "$(dirname "$0")/.."
CACHE="${XDG_CACHE_HOME:-$HOME/.cache}/droidpad-build"
R8="$CACHE/r8-8.1.72.jar"
ANDROID_JAR="$CACHE/android-13/android.jar"
mkdir -p "$CACHE"

if [ -x "$(command -v javac)" ]; then
  JAVAC=(javac)
else
  JAVAC=(java -m jdk.compiler/com.sun.tools.javac.Main) # yalnızca JRE kuruluysa jdk.compiler modülü
fi

[ -f "$R8" ] || curl -fsSL -o "$R8" https://dl.google.com/android/maven2/com/android/tools/r8/8.1.72/r8-8.1.72.jar
if [ ! -f "$ANDROID_JAR" ]; then
  curl -fsSL -o "$CACHE/platform-33.zip" https://dl.google.com/android/repository/platform-33_r02.zip
  (cd "$CACHE" && python3 -I -c "import zipfile;zipfile.ZipFile('platform-33.zip').extract('android-13/android.jar')")
  rm -f "$CACHE/platform-33.zip"
fi

WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT
"${JAVAC[@]}" -source 8 -target 8 -Xlint:-options -cp "$ANDROID_JAR" -d "$WORK/classes" Helper/DroidPadInput.java
java -cp "$R8" com.android.tools.r8.D8 --release --min-api 26 --lib "$ANDROID_JAR" --output "$WORK" "$WORK"/classes/*.class
cp "$WORK/classes.dex" Helper/droidpad-input.dex
echo "✓ Helper/droidpad-input.dex ($(stat -c%s Helper/droidpad-input.dex) bayt)"
