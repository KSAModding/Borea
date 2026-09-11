#!/usr/bin/env bash

set -euo pipefail

dotnet tool restore

tool_version=
found_tool=false
while IFS= read -r line; do
  line=${line%$'\r'}
  if [[ "$line" == *'"cyclonedx"'* ]]; then
    found_tool=true
  elif $found_tool && [[ "$line" == *'"version"'* ]]; then
    tool_version=${line#*:}
    tool_version=${tool_version//[\", ]/}
    break
  fi
done < .config/dotnet-tools.json
if [ -z "$tool_version" ]; then
  echo "The CycloneDX tool is missing from the local tool manifest." >&2
  exit 1
fi
while IFS= read -r line; do
  line=${line%$'\r'}
  global_packages=${line##*: }
done < <(dotnet nuget locals global-packages --list --force-english-output)
if [[ "$global_packages" == [A-Za-z]:\\* ]]; then
  drive=${global_packages:0:1}
  global_packages=${global_packages:2}
  global_packages=${global_packages//\\//}
  global_packages="/${drive,,}$global_packages"
fi
tool_package="${global_packages%/}/cyclonedx/$tool_version/cyclonedx.$tool_version.nupkg"
read -r expected_hash _ < .config/dotnet-tools.sha512
read -r actual_hash _ < <(sha512sum "$tool_package")

if [ "${actual_hash,,}" != "${expected_hash,,}" ]; then
  echo "The CycloneDX package does not match its pinned SHA-512 hash." >&2
  exit 1
fi
