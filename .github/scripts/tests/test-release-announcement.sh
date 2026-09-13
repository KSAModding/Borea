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
# Records the body and the URL of the request instead of sending it.
while [ "$#" -gt 0 ]; do
  case "$1" in
    -d | --data | --data-binary) printf '%s' "$2" > "$REQUEST_BODY"; shift 2 ;;
    -H | --max-time) shift 2 ;;
    -*) shift ;;
    *) printf '%s' "$1" > "$REQUEST_URL"; shift ;;
  esac
done
EOF
chmod +x "$work/bin/curl"

# run_step <webhook URL> <version> <generated notes>
run_step() {
  rm -f "$work/body.json" "$work/url"
  env PATH="$work/bin:$PATH" REQUEST_BODY="$work/body.json" REQUEST_URL="$work/url" \
    WEBHOOK_URL="$1" VERSION="$2" RELEASE_URL="https://github.com/KSAModding/Borea/releases/tag/v$2" NOTES="$3" \
    bash -e "$work/step.sh" > "$work/output.txt"
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

notes="## What's Changed
* Include the CLI in the desktop App archives by @Maximilian-Nesslauer in https://github.com/KSAModding/Borea/pull/156
* Add an About tab to the settings by @averageksp in https://github.com/KSAModding/Borea/pull/145

## New Contributors
* @averageksp made their first contribution in https://github.com/KSAModding/Borea/pull/145

**Full Changelog**: https://github.com/KSAModding/Borea/compare/v1.1.0...v1.2.0"

run_step "" 1.2.0 "$notes"
check "a missing secret posts nothing" test ! -e "$work/body.json"
check "a missing secret says why the step posts nothing" grep -q "DISCORD_RELEASE_WEBHOOK_URL is not set" "$work/output.txt"

run_step "https://discord.example/api/webhooks/1/token" 1.2.0 "$notes"
content=$(jq -r .content "$work/body.json")
check "the post goes to the webhook" grep -qx "https://discord.example/api/webhooks/1/token" "$work/url"
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
check "the post notifies nobody" jq -e '.allowed_mentions.parse == []' "$work/body.json"

run_step "https://discord.example/api/webhooks/1/token" 1.3.0-beta.1 "$notes"
check "a version with a hyphen is marked as a pre-release" test "$(jq -r '.content | split("\n")[0]' "$work/body.json")" = "**Borea 1.3.0-beta.1** (pre-release)"

run_step "https://discord.example/api/webhooks/1/token" 1.2.0 ""
content=$(jq -r .content "$work/body.json")
check "empty notes still post the release" has_line "https://github.com/KSAModding/Borea/releases/tag/v1.2.0" "$content"
check "empty notes have no change list" not has_text "Changes:" "$content"

title="@everyone $(printf 'Rewrite the release workflow %.0s' {1..10})"
long="## What's Changed"
for number in $(seq 1 300); do
  long+=$'\n'"* $title by @someone in https://github.com/KSAModding/Borea/pull/$number"
done
run_step "https://discord.example/api/webhooks/1/token" 1.2.0 "$long"
content=$(jq -r .content "$work/body.json")
shown=$(grep -c '^- ' <<< "$content")
check "a long change list keeps a margin under the 2000 characters of Discord" jq -e '.content | length <= 1900' "$work/body.json"
check "a long change list shows some entries" test "$shown" -gt 0
check "a long change list names how many entries are left out" has_line "and $((300 - shown)) more" "$content"
check "a long entry is cut" jq -e '.content | split("\n") | map(select(startswith("- "))) | all(length <= 182 and endswith("..."))' "$work/body.json"
check "a long change list still notifies nobody" jq -e '.allowed_mentions.parse == []' "$work/body.json"

check "announce runs only when this run created the release" grep -q "if: needs.release.outputs.created == 'true'" "$workflow"
check "only the step that creates a release sets created" test "$(grep -c 'created=true' "$workflow")" -eq 1
create_step_needs_a_tag_push() {
  grep -A2 -- '- name: Create or update the release' "$workflow" | grep -q "if: github.event_name == 'push'"
}
check "the step that creates a release runs only for a tag push" create_step_needs_a_tag_push
check "the post gives up after 30 seconds and does not retry" grep -qF 'curl -sSf --max-time 30 -H' "$workflow"
check "announce has no token permissions" \
  awk '{ sub(/\r$/, "") } /^  announce:$/ { job = 1; next } job && /^  [a-z]/ { exit } job && /^    permissions: \{\}$/ { found = 1 } END { exit !found }' "$workflow"

if [ "$failures" -ne 0 ]; then
  echo "$failures checks failed." >&2
  exit 1
fi
echo "All checks passed."
