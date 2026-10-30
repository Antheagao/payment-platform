#!/bin/sh
# Waits until every service of `docker compose up` answers, checks that the API and the worker started
# without a warning, then prints where to open each one. Exits non-zero if anything is still down after
# the timeout or a start logged a warning. CI runs it after starting the stack exactly the way the
# README says; run it locally the same way: `sh scripts/check-stack.sh`.
#
# Honors the same port variables as docker-compose.yml (DASHBOARD_PORT, API_PORT, and the rest),
# from the environment or from .env. CHECK_STACK_TIMEOUT sets the wait in seconds (default 180).

set -eu

if [ -f .env ]; then
  # Only the port variables; the file may hold anything else.
  eval "$(tr -d '\r' < .env | grep -E '^(DASHBOARD_PORT|API_PORT|WORKER_PORT|ASPIRE_DASHBOARD_PORT|WEBHOOK_RECEIVER_PORT)=[0-9]+$' || true)"
fi

dashboard="http://localhost:${DASHBOARD_PORT:-3080}"
api="http://localhost:${API_PORT:-5080}"
worker="http://localhost:${WORKER_PORT:-5081}"
aspire="http://localhost:${ASPIRE_DASHBOARD_PORT:-18888}"
receiver="http://localhost:${WEBHOOK_RECEIVER_PORT:-8090}"
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

# startup_warnings SERVICE: the Warning, Error, and Critical lines SERVICE logged in its current start,
# up to "Application started". Later lines (an outage you caused on purpose) are not a start's fault.
startup_warnings() {
  id=$(docker compose ps -q "$1" 2>/dev/null)
  [ -n "$id" ] || return 0
  since=$(docker inspect -f '{{.State.StartedAt}}' "$id")
  docker compose logs --no-log-prefix --since "$since" "$1" 2>/dev/null |
    awk '/Application started/ { exit } { print }' |
    grep -E '"LogLevel":"(Warning|Error|Critical)"' || true
}

echo "Waiting up to ${timeout}s for the stack:"
failed=0
wait_for "API ready" "$api/health/ready" 200 || failed=1
wait_for "Worker ready" "$worker/health/ready" 200 || failed=1
wait_for "OpenAPI document" "$api/openapi/v1.json" 200 '"openapi"' || failed=1
# The dashboard's pages need a session. Merchants are never shown the API's readiness (ADR 011); the operator
# area's sign-in page is the one page without a session that reports it.
wait_for "Dashboard" "$dashboard/login" 200 'Sign in' || failed=1
wait_for "Operator area" "$dashboard/operator/login" 200 'API ready' || failed=1
# The Aspire dashboard redirects / to its first page.
wait_for "Aspire dashboard" "$aspire/" 302 || failed=1
wait_for "Webhook receiver" "$receiver/health" 200 || failed=1

for service in api worker; do
  warnings=$(startup_warnings "$service")
  if [ -n "$warnings" ]; then
    printf '  FAIL  %-18s logged warnings while starting:
' "$service start"
    printf '%s
' "$warnings" | cut -c1-400 | sed 's/^/          /'
    failed=1
  else
    printf '  ok    %-18s %s
' "$service start" "no warnings"
  fi
done

if [ "$failed" -ne 0 ]; then
  echo "Something is down: \`docker compose ps -a\` and \`docker compose logs <service>\` say why."
  exit 1
fi

cat <<EOF

Everything is up:
  Dashboard          $dashboard
  API (OpenAPI)      $api/openapi/v1.json
  Aspire dashboard   $aspire   (traces, metrics, structured logs)
  Webhook receiver   $receiver/events   (what the demo merchant's endpoint received)
EOF
