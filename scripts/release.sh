#!/usr/bin/env bash
# Kullanım: scripts/release.sh <sürüm>   (örn. 0.2.0 veya 0.2.0-beta.1)
# csproj'daki <Version> değerini günceller, commit eder, v<sürüm> etiketini atar ve push eder.
# Etiket push'u GitHub Actions'taki Release workflow'unu tetikler.
set -euo pipefail
cd "$(dirname "$0")/.."

v="${1:-}"; v="${v#v}"
[[ "$v" =~ ^[0-9]+\.[0-9]+\.[0-9]+(-[0-9A-Za-z.]+)?$ ]] || { echo "Geçersiz sürüm: '$v' (örn. 0.2.0)"; exit 1; }
[[ -z "$(git status --porcelain)" ]] || { echo "Çalışma ağacı temiz değil; önce commit edin."; exit 1; }
git rev-parse "v$v" >/dev/null 2>&1 && { echo "v$v etiketi zaten var."; exit 1; }

sed -i -E "s|<Version>[^<]*</Version>|<Version>$v</Version>|" DroidPad.csproj
git diff --quiet || git commit -qam "chore: sürüm $v"  # sürüm zaten güncelse commit atlanır
git tag -a "v$v" -m "v$v"
git push origin HEAD "v$v"
echo "v$v gönderildi; Actions sekmesinden derlemeyi izleyin."
