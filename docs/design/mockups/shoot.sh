#!/usr/bin/env bash
# Re-captures the screenshots in docs/design/mockups/shots/ with headless Chrome at 2x.
# Usage: docs/design/mockups/shoot.sh   (macOS only: uses sips to crop; needs Google Chrome,
# override its path with CHROME=...; fonts load from Google Fonts)
# Chrome's headless window will not go below 500px wide, so every capture goes through
# frame.html, which frames the page at the exact width, and is then cropped.
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
out="$here/shots"
mkdir -p "$out"
chrome="${CHROME:-/Applications/Google Chrome.app/Contents/MacOS/Google Chrome}"
[ "$(uname)" = Darwin ] || { echo "shoot.sh needs macOS (sips)" >&2; exit 1; }
[ -x "$chrome" ] || { echo "Chrome not found at $chrome; set CHROME=" >&2; exit 1; }

shot() { # name page query width height
  local w=$4 h=$5 win_w=$4
  [ "$win_w" -lt 500 ] && win_w=500
  "$chrome" --headless=new --disable-gpu --hide-scrollbars --force-device-scale-factor=2 \
    --virtual-time-budget=8000 --window-size="$win_w,$h" \
    --screenshot="$out/$1.png" "file://$here/frame.html?src=$2&w=$w&h=$h&q=$(printf %s "$3" | sed 's/&/%26/g')" >/dev/null 2>&1
  sips -c $((h * 2)) $((w * 2)) "$out/$1.png" >/dev/null 2>&1
  echo "$1"
}

for theme in light dark; do
  shot "landing-phone-$theme"       landing.html    "theme=$theme"              390 1000
  shot "landing-tablet-$theme"      landing.html    "theme=$theme"              820 1100
  shot "landing-desktop-$theme"     landing.html    "theme=$theme"              1440 1000
  shot "calculator-phone-$theme"    calculator.html "theme=$theme&state=fresh"  390 1500
  shot "calculator-tablet-$theme"   calculator.html "theme=$theme&state=fresh"  820 1300
  shot "calculator-desktop-$theme"  calculator.html "theme=$theme&state=fresh"  1440 1200
done
for state in saved modal 404 422 503; do
  shot "calculator-phone-$state"    calculator.html "theme=light&state=$state"  390 1500
done
