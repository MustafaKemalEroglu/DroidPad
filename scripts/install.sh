#!/usr/bin/env bash
# DroidPad kurulum betiği: bağımlılıkları denetler, yayınlar ve uygulama menüsüne ekler.
set -euo pipefail

cd "$(dirname "$0")/.."
BIN_DIR="${HOME}/.local/bin"
APP_DIR="${HOME}/.local/share/droidpad"   # eski çok dosyalı kurulum (temizlenir)
DESKTOP_DIR="${HOME}/.local/share/applications"

missing=0
for cmd in dotnet adb waydroid; do
  if ! command -v "$cmd" >/dev/null 2>&1; then
    echo "✗ '$cmd' bulunamadı" >&2
    missing=1
  fi
done
if [ "$missing" -eq 1 ]; then
  echo "Gerekenler: .NET 10 SDK, android-tools (adb), waydroid" >&2
  exit 1
fi

echo "→ Yayınlanıyor..."
dotnet publish DroidPad.csproj -c Release -r linux-x64 >/dev/null

mkdir -p "$BIN_DIR" "$DESKTOP_DIR"
rm -rf "$APP_DIR"
install -m 755 bin/Release/net10.0/linux-x64/publish/DroidPad "$BIN_DIR/droidpad"

cat > "$DESKTOP_DIR/droidpad.desktop" <<DESKTOP
[Desktop Entry]
Type=Application
Name=DroidPad
Comment=Waydroid için tuş eşleme ve makro aracı
Exec=$BIN_DIR/droidpad
Terminal=false
Categories=Utility;Game;
DESKTOP

# GNOME Shell eklentisi: kaplamayı Waydroid'in masaüstüne taşır, yalnızca Waydroid ön plandayken üstte tutar
if command -v gnome-extensions >/dev/null 2>&1; then
  EXT="droidpad-follow@droidpad"
  mkdir -p "${HOME}/.local/share/gnome-shell/extensions/$EXT"
  cp Extension/$EXT/* "${HOME}/.local/share/gnome-shell/extensions/$EXT/"
  gnome-extensions enable "$EXT" 2>/dev/null || true
  echo "→ GNOME eklentisi kuruldu (ilk kurulumda oturumu kapatıp açın)."
fi

echo "✓ Kuruldu. Çalıştırmak için: droidpad  (veya uygulama menüsünden DroidPad)"
