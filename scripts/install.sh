#!/usr/bin/env bash
# DroidPad kurulum betiği: bağımlılıkları denetler, yayınlar ve uygulama menüsüne ekler.
set -euo pipefail

cd "$(dirname "$0")/.."
BIN_DIR="${HOME}/.local/bin"
APP_DIR="${HOME}/.local/share/droidpad"
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
dotnet publish DroidPad.csproj -c Release -r linux-x64 --self-contained -o "$APP_DIR" >/dev/null

mkdir -p "$BIN_DIR" "$DESKTOP_DIR"
ln -sf "$APP_DIR/DroidPad" "$BIN_DIR/droidpad"

cat > "$DESKTOP_DIR/droidpad.desktop" <<DESKTOP
[Desktop Entry]
Type=Application
Name=DroidPad
Comment=Waydroid için tuş eşleme ve makro aracı
Exec=$APP_DIR/DroidPad
Terminal=false
Categories=Utility;Game;
DESKTOP

echo "✓ Kuruldu. Çalıştırmak için: droidpad  (veya uygulama menüsünden DroidPad)"
