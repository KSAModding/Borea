#!/usr/bin/env bash

# Runs the Discord step of release.yml with a fake curl that records the request. Needs bash and jq.

set -euo pipefail

if ! command -v jq > /dev/null; then
  echo "This test needs jq." >&2
  exit 2
fi

here=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
workflow="$here/../../workflows/release.yml"
work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT

# The run block of the step, without the indentation that it has in the workflow.
awk '
  { sub(/\r$/, "") }
  /^ *- name: Post the release to Discord$/ { step = 1; next }
  step && !run && /^ *run: \|$/ { run = 1; indent = -1; next }
  run {
    if ($0 ~ /^ *$/) { print ""; next }
    match($0, /^ */)
    if (indent < 0) indent = RLENGTH
    if (RLENGTH < indent) exit
    print substr($0, indent + 1)
  }
' "$workflow" > "$work/step.sh"
if ! grep -q 'jq -n' "$work/step.sh"; then
  echo "release.yml has no step \"Post the release to Discord\" with a jq payload." >&2
  exit 1
fi

mkdir "$work/bin"
cat > "$work/bin/curl" <<'EOF'
#!/usr/bin/env bash
# Records the request instead of sending it, and prints its arguments so that a check can see them in the log.
echo "fake curl $*"
printf '%s\n' "$@" > "$REQUEST_ARGS"
output=/dev/null
while [ "$#" -gt 0 ]; do
  case "$1" in
    -d | --data | --data-binary) printf '%s' "$2" > "$REQUEST_BODY"; shift 2 ;;
    -H | --header)
      if [ "$2" = @- ]; then cat >> "$REQUEST_HEADERS"; else printf '%s\n' "$2" >> "$REQUEST_HEADERS"; fi
      shift 2 ;;
    -o | --output) output=$2; shift 2 ;;
    --max-time) shift 2 ;;
    -*) shift ;;
    *) printf '%s' "$1" > "$REQUEST_URL"; shift ;;
  esac
done
if [ "$FAKE_CURL_FAILS" = yes ]; then
  printf '{"message": "Missing Permissions", "code": 50013}' > "$output"
  echo "curl: (22) The requested URL returned error: 403" >&2
  exit 22
fi
printf '{"id": "1"}' > "$output"
EOF
chmod +x "$work/bin/curl"

# The configuration of the next run_step.
webhook=""
token=""
channel=""
role=""
fails=no

# run_step <version> <generated notes>. Sets status to the exit code of the step.
run_step() {
  rm -f "$work/body.json" "$work/url" "$work/headers" "$work/args"
  status=0
  env PATH="$work/bin:$PATH" REQUEST_BODY="$work/body.json" REQUEST_URL="$work/url" \
    REQUEST_HEADERS="$work/headers" REQUEST_ARGS="$work/args" FAKE_CURL_FAILS="$fails" \
    GITHUB_SERVER_URL="https://github.com" GITHUB_REPOSITORY="KSAModding/Borea" \
    WEBHOOK_URL="$webhook" BOT_TOKEN="$token" CHANNEL_ID="$channel" ROLE_ID="$role" \
    VERSION="$1" RELEASE_URL="https://github.com/KSAModding/Borea/releases/tag/v$1" NOTES="$2" \
    bash -e "$work/step.sh" > "$work/output.txt" 2>&1 || status=$?
}

failures=0
# check <description> <command> [arguments]
check() {
  local description=$1
  shift
  if "$@" > /dev/null; then
    echo "ok - $description"
  else
    echo "not ok - $description" >&2
    failures=$((failures + 1))
  fi
}
not() { ! "$@"; }
has_line() { grep -qx -- "$1" <<< "$2"; }
has_text() { grep -qF -- "$1" <<< "$2"; }
first_line() { jq -r '.content | split("\n")[0]' "$work/body.json"; }
mentions_are() { jq -e --argjson expected "$1" '.allowed_mentions == $expected' "$work/body.json"; }

notes="## What's Changed
* Include the CLI in the desktop App archives by @Maximilian-Nesslauer in https://github.com/KSAModding/Borea/pull/156
* Add an About tab to the settings by @averageksp in https://github.com/KSAModding/Borea/pull/145

## New Contributors
* @averageksp made their first contribution in https://github.com/KSAModding/Borea/pull/145

**Full Changelog**: https://github.com/KSAModding/Borea/compare/v1.1.0...v1.2.0"

hook="https://discord.example/api/webhooks/1/token"
secret="bot-token-that-must-not-be-printed"
channel_id="987654321"
channel_url="https://discord.com/api/v10/channels/$channel_id/messages"
role_id="123456789012345678"
no_mentions='{"parse": []}'
role_only="{\"parse\": [], \"roles\": [\"$role_id\"]}"

run_step 1.2.0 "$notes"
check "nothing configured posts nothing" test ! -e "$work/body.json"
check "nothing configured passes" test "$status" -eq 0
check "nothing configured says why the step posts nothing" \
  grep -q "Neither DISCORD_RELEASE_BOT_TOKEN nor DISCORD_RELEASE_WEBHOOK_URL is set" "$work/output.txt"

channel=$channel_id
role=$role_id
run_step 1.2.0 "$notes"
check "variables without a secret post nothing and pass" test ! -e "$work/body.json" -a "$status" -eq 0
channel=""
role=""

webhook=$hook
run_step 1.2.0 "$notes"
content=$(jq -r .content "$work/body.json")
check "webhook mode passes" test "$status" -eq 0
check "webhook mode waits for Discord to save the message" grep -qx "$hook?wait=true" "$work/url"
check "webhook mode sends no authorization header" not grep -qi "^Authorization:" "$work/headers"
check "the request names Borea as the user agent" \
  grep -qx "User-Agent: DiscordBot (https://github.com/KSAModding/Borea, 1.2.0)" "$work/headers"
check "a saved post prints no response" not grep -q '"id"' "$work/output.txt"
check "the first line names the version" test "$(head -n 1 <<< "$content")" = "**Borea 1.2.0**"
check "the post links the release page" has_line "https://github.com/KSAModding/Borea/releases/tag/v1.2.0" "$content"
# The backticks are Discord formatting, not command substitution.
# shellcheck disable=SC2016
check "one line names the App archives" has_line 'App with the `borea` command: `Borea-1.2.0-<platform>` for Windows, Linux and macOS' "$content"
# shellcheck disable=SC2016
check "one line names the CLI archives" has_line 'CLI only: `Borea-Cli-1.2.0-<platform>` for the same platforms' "$content"
check "changes are listed with their pull request number" has_line "- Include the CLI in the desktop App archives (#156)" "$content"
check "the second change is listed" has_line "- Add an About tab to the settings (#145)" "$content"
check "a short list has no remainder line" not has_text " more" "$content"
check "new contributors are not listed" not has_text "first contribution" "$content"
check "a release is not marked as a pre-release" not has_text "pre-release" "$content"
check "without a role the post notifies nobody" mentions_are "$no_mentions"

webhook="$hook?thread_id=7"
run_step 1.2.0 "$notes"
check "a webhook URL with a query gets wait as a second parameter" grep -qx "$hook?thread_id=7&wait=true" "$work/url"
webhook=$hook

run_step 1.3.0-beta.1 "$notes"
check "a version with a hyphen is marked as a pre-release" test "$(first_line)" = "**Borea 1.3.0-beta.1** (pre-release)"

run_step 1.2.0 ""
content=$(jq -r .content "$work/body.json")
check "empty notes still post the release" has_line "https://github.com/KSAModding/Borea/releases/tag/v1.2.0" "$content"
check "empty notes have no change list" not has_text "Changes:" "$content"

fails=yes
run_step 1.2.0 "$notes"
check "a webhook post that Discord rejects fails the step" test "$status" -ne 0
check "a rejected post prints the code and message from Discord" \
  grep -qx "Discord answered 50013: Missing Permissions" "$work/output.txt"
fails=no

webhook=""
token=$secret
channel=$channel_id
run_step 1.2.0 "$notes"
check "bot mode passes" test "$status" -eq 0
check "bot mode posts to the channel through API v10" grep -qx "$channel_url" "$work/url"
check "bot mode sends the bot authorization header" grep -qx "Authorization: Bot $secret" "$work/headers"
check "bot mode keeps the token off the curl command line" not grep -qF "$secret" "$work/args"
check "the log shows the curl command line" grep -qF "fake curl " "$work/output.txt"
check "bot mode does not print the token" not grep -qF "$secret" "$work/output.txt"
check "bot mode posts the same message" test "$(first_line)" = "**Borea 1.2.0**"
check "bot mode without a role notifies nobody" mentions_are "$no_mentions"

fails=yes
run_step 1.2.0 "$notes"
check "a bot post that Discord rejects fails the step" test "$status" -ne 0
check "a rejected bot post does not print the token" not grep -qF "$secret" "$work/output.txt"
fails=no

channel=""
run_step 1.2.0 "$notes"
check "a bot token without a channel id fails" test "$status" -ne 0
check "a bot token without a channel id posts nothing" test ! -e "$work/body.json"
check "a bot token without a channel id says why" grep -q "DISCORD_RELEASE_CHANNEL_ID is not a Discord id" "$work/output.txt"

channel="123/../../webhooks/1"
run_step 1.2.0 "$notes"
check "a channel id that is not digits fails and posts nothing" test "$status" -ne 0 -a ! -e "$work/body.json"

webhook=$hook
channel=$channel_id
run_step 1.2.0 "$notes"
check "with both modes configured the bot posts" grep -qx "$channel_url" "$work/url"
check "with both modes configured the run shows a warning" \
  grep -q "^::warning::DISCORD_RELEASE_BOT_TOKEN and DISCORD_RELEASE_WEBHOOK_URL are both set" "$work/output.txt"
check "with both modes configured the token is not printed" not grep -qF "$secret" "$work/output.txt"
webhook=""

mentions="## What's Changed
* Ping @everyone and <@&555> and <@42> by @someone in https://github.com/KSAModding/Borea/pull/9"
role=$role_id
run_step 1.2.0 "$mentions"
check "a bot role ping passes" test "$status" -eq 0
check "a bot role ping starts the message with the role mention" test "$(first_line)" = "<@&$role_id> **Borea 1.2.0**"
check "a bot role ping allows exactly that role and nothing else" mentions_are "$role_only"

run_step 1.3.0-beta.1 "$mentions"
check "a bot pre-release posts to the channel" grep -qx "$channel_url" "$work/url"
check "a bot pre-release does not start with the role mention" test "$(first_line)" = "**Borea 1.3.0-beta.1** (pre-release)"
check "a bot pre-release notifies nobody" mentions_are "$no_mentions"
token=""
channel=""

webhook=$hook
run_step 1.2.0 "$mentions"
check "a webhook role ping passes" test "$status" -eq 0
check "a webhook role ping starts the message with the role mention" test "$(first_line)" = "<@&$role_id> **Borea 1.2.0**"
check "a webhook role ping allows exactly that role and nothing else" mentions_are "$role_only"

run_step 1.3.0-beta.1 "$mentions"
check "a webhook pre-release does not start with the role mention" test "$(first_line)" = "**Borea 1.3.0-beta.1** (pre-release)"
check "a webhook pre-release notifies nobody" mentions_are "$no_mentions"

for bad in "everyone" "<@&1>" "12 34" "1e5" "123456789012345678901"; do
  role=$bad
  run_step 1.2.0 "$notes"
  check "the role id \"$bad\" fails and posts nothing" test "$status" -ne 0 -a ! -e "$work/body.json"
done
role=""

title="@everyone $(printf 'Rewrite the release workflow %.0s' {1..10})"
long="## What's Changed"
for number in $(seq 1 300); do
  long+=$'\n'"* $title by @someone in https://github.com/KSAModding/Borea/pull/$number"
done
for role in "" "$role_id"; do
  run_step 1.2.0 "$long"
  content=$(jq -r .content "$work/body.json")
  shown=$(grep -c '^- ' <<< "$content")
  label=${role:+" with a role ping"}
  check "a long change list$label keeps a margin under the 2000 characters of Discord" jq -e '.content | length <= 1900' "$work/body.json"
  check "a long change list$label shows some entries" test "$shown" -gt 0
  check "a long change list$label names how many entries are left out" has_line "and $((300 - shown)) more" "$content"
  check "a long entry$label is cut" jq -e '.content | split("\n") | map(select(startswith("- "))) | all(length <= 182 and endswith("..."))' "$work/body.json"
done
check "a long change list with a role ping keeps the role mention" test "$(first_line)" = "<@&$role_id> **Borea 1.2.0**"
check "a long change list with a role ping allows only that role" mentions_are "$role_only"
role=""

check "announce runs only when this run created the release" grep -q "if: needs.release.outputs.created == 'true'" "$workflow"
check "only the step that creates a release sets created" test "$(grep -c 'created=true' "$workflow")" -eq 1
create_step_needs_a_tag_push() {
  grep -A2 -- '- name: Create the release' "$workflow" | grep -q "if: github.event_name == 'push'"
}
check "the step that creates a release runs only for a tag push" create_step_needs_a_tag_push
# The dollar sign is part of the text that the check looks for.
# shellcheck disable=SC2016
check "the post gives up after 30 seconds and does not retry" grep -qF 'curl -sS --fail-with-body -o "$response" --max-time 30 ' "$work/step.sh"
check "announce has no token permissions and a timeout" \
  awk '{ sub(/\r$/, "") } /^  announce:$/ { job = 1; next } job && /^  [a-z]/ { exit }
    job && /^    permissions: \{\}$/ { p = 1 } job && /^    timeout-minutes: [0-9]+$/ { t = 1 } END { exit !(p && t) }' "$workflow"

if [ "$failures" -ne 0 ]; then
  echo "$failures checks failed." >&2
  exit 1
fi
echo "All checks passed."
