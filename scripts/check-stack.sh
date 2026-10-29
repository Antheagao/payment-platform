#!/bin/sh
# Waits until every service of `docker compose up` answers, then prints where to open each one.
# Exits non-zero if anything is still down after the timeout. CI runs it after starting the stack
# exactly the way the README says; run it locally the same way: `sh scripts/check-stack.sh`.
#
# Honors the same port variables as docker-compose.yml (DASHBOARD_PORT, API_PORT, and the rest),
# from the environment or from .env. CHECK_STACK_TIMEOUT sets the wait in seconds (default 180).

set -eu

if [ -f .env ]; then
  # Only the port variables; the file may hold anything else.
  eval "$(tr -d '\r' < .env | grep -E '^(DASHBOARD_PORT|API_PORT|WORKER_PORT|ASPIRE_DASHBOARD_PORT)=[0-9]+$' || true)"
fi

dashboard="http://localhost:${DASHBOARD_PORT:-3080}"
api="http://localhost:${API_PORT:-5080}"
worker="http://localhost:${WORKER_PORT:-5081}"
aspire="http://localhost:${ASPIRE_DASHBOARD_PORT:-18888}"
timeout="${CHECK_STACK_TIMEOUT:-180}"

# status URL: the HTTP status code, or 000 when nothing answers.
status() {
  curl -s -o /dev/null -w '%{http_code}' --max-time 5 "$1" || true
}

# wait_for NAME URL EXPECTED [TEXT]: polls until URL returns EXPECTED (and contains TEXT, if given).
wait_for() {
  name=$1 url=$2 expected=$3 text=${4:-}
  started=$(date +%s)
  while :; do
    code=$(status "$url")
    if [ "$code" = "$expected" ]; then
      if [ -z "$text" ] || curl -s --max-time 10 "$url" | grep -q "$text"; then
        printf '  ok    %-18s %s\n' "$name" "$url"
        return 0
      fi
    fi
    if [ $(( $(date +%s) - started )) -ge "$timeout" ]; then
      printf '  FAIL  %-18s %s (last status %s)\n' "$name" "$url" "$code"
      return 1
    fi
    sleep 2
  done
}

echo "Waiting up to ${timeout}s for the stack:"
failed=0
wait_for "API ready" "$api/health/ready" 200 || failed=1
wait_for "Worker ready" "$worker/health/ready" 200 || failed=1
wait_for "OpenAPI document" "$api/openapi/v1.json" 200 '"openapi"' || failed=1
wait_for "Dashboard" "$dashboard/" 200 'API ready' || failed=1
# The Aspire dashboard redirects / to its first page.
wait_for "Aspire dashboard" "$aspire/" 302 || failed=1

if [ "$failed" -ne 0 ]; then
  echo "Something is down: \`docker compose ps -a\` and \`docker compose logs <service>\` say why."
  exit 1
fi

cat <<EOF

Everything is up:
  Dashboard          $dashboard
  API (OpenAPI)      $api/openapi/v1.json
  Aspire dashboard   $aspire   (traces, metrics, structured logs)
EOF
