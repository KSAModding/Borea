#!/usr/bin/env bash

# Adds the CLI publish output to the App publish output, so that the App archive also contains borea.
# Both are self-contained publishes of the same release, so a file that both publishes write must be
# byte-identical. The script checks every shared path first and changes nothing when one is different.

set -euo pipefail

if [ "$#" -ne 2 ]; then
  echo "Usage: $0 <app-publish-directory> <cli-publish-directory>" >&2
  exit 2
fi
app=${1%/}
cli=${2%/}

for dir in "$app" "$cli"; do
  if [ ! -d "$dir" ]; then
    echo "$dir is not a directory." >&2
    exit 1
  fi
done

unexpected=$(cd "$cli" && find . ! -type f ! -type d)
if [ -n "$unexpected" ]; then
  echo "The CLI publish contains entries that are not files or directories:" >&2
  echo "$unexpected" >&2
  exit 1
fi

shared=0
only_cli=0
different=0
while IFS= read -r -d '' file; do
  file=${file#./}
  if [ -e "$app/$file" ] || [ -L "$app/$file" ]; then
    shared=$((shared + 1))
    if [ ! -f "$app/$file" ] || [ -L "$app/$file" ] || ! cmp -s "$app/$file" "$cli/$file"; then
      echo "$file is different in the App and the CLI publish." >&2
      different=$((different + 1))
    fi
  else
    only_cli=$((only_cli + 1))
  fi
done < <(cd "$cli" && find . -type f -print0)

# A CLI directory can only be merged into an App directory, so any other entry at that path fails
# here, before the copy starts.
while IFS= read -r -d '' dir; do
  dir=${dir#./}
  if { [ -e "$app/$dir" ] || [ -L "$app/$dir" ]; } && { [ ! -d "$app/$dir" ] || [ -L "$app/$dir" ]; }; then
    echo "$dir is a directory in the CLI publish but not in the App publish." >&2
    different=$((different + 1))
  fi
done < <(cd "$cli" && find . -mindepth 1 -type d -print0)

# Windows and macOS usually unpack onto a file system that ignores case, so two names that differ
# only in case would overwrite each other there.
names=$(
  (cd "$app" && find . -mindepth 1)
  (cd "$cli" && find . -mindepth 1)
)
collisions=$(sort -u <<< "$names" | tr '[:upper:]' '[:lower:]' | sort | uniq -d)
if [ -n "$collisions" ]; then
  echo "The combined output has names that differ only in case:" >&2
  sort -u <<< "$names" |
    awk 'NR == FNR { clash[$0] = 1; next } tolower($0) in clash' <(printf '%s\n' "$collisions") - >&2
  exit 1
fi

if [ "$different" -ne 0 ]; then
  echo "$different shared paths are different, so the CLI is not added to the App." >&2
  exit 1
fi

cp -a "$cli/." "$app/"
echo "$shared shared files are identical. Added $only_cli files that only the CLI publish writes."
