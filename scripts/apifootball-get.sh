#!/usr/bin/env bash
# Read-only API-Football GET, run from the Oracle VM so it uses the production key without that key
# ever leaving the VM or appearing in output. Prints the raw JSON response body.
#
#   bash scripts/apifootball-get.sh "standings?league=5&season=2026"
#
# The path is validated against a strict character set because it's interpolated into the remote
# shell command - anything that could break out of the quoted URL is rejected.
set -euo pipefail

path="${1:-}"
path="${path#/}"
if [[ ! "$path" =~ ^[a-z/]+(\?[A-Za-z0-9=\&_.,:-]*)?$ ]]; then
    echo "usage: $0 '<endpoint>?<query>'  (e.g. 'fixtures?id=1545601')" >&2
    exit 2
fi

ssh -i ~/.ssh/oracle_fulltime -o ConnectTimeout=15 ubuntu@89.168.59.239 "
    pid=\$(systemctl show -p MainPID --value fulltime-api)
    key=\$(sudo cat /proc/\$pid/environ | tr '\\0' '\\n' | grep '^ApiFootball__ApiKey=' | cut -d= -f2-)
    [ -n \"\$key\" ] || { echo 'ApiFootball__ApiKey not found in fulltime-api environment' >&2; exit 1; }
    curl -sS -m 30 -G -H \"x-apisports-key: \$key\" 'https://v3.football.api-sports.io/$path'
"
