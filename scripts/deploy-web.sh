#!/usr/bin/env bash
# Deployt das gebaute React-Frontend (web/dist) in den UserData-Ordner des
# Spiels, von wo IpamWebServer es ausliefert:
#   <Spiel>/UserData/gregMod.IPAM/web/
set -euo pipefail
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
GAME_DIR="${DATACENTER_HOME:-$HOME/.local/share/Steam/steamapps/common/Data Center}"
SRC="$HERE/web/dist"
DST="$GAME_DIR/UserData/gregMod.IPAM/web"

if [ ! -d "$SRC" ]; then
  echo "ERROR: $SRC missing. Run 'npm run build' in web/ first."
  exit 1
fi
mkdir -p "$DST"
cp -r "$SRC"/. "$DST"/
echo "Deployed web/dist -> $DST"
ls "$DST"
