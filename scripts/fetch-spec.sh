#!/usr/bin/env bash
#
# Re-downloads the live v4 specification over the vendored copy.
#
# Run by .github/workflows/spec-drift.yml on a schedule. If the download differs from the
# committed copy, the workflow runs the conformance gates against it and opens a pull
# request — that PR is the early warning that the API changed under us.
#
# Exits 0 whether or not anything changed; the workflow diffs the working tree.
#
# Requires: curl, jq.

set -euo pipefail

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
spec="$here/../openapi/huuray-v4.json"
url="${HUURAY_SPEC_URL:-https://api.huuray.com/swagger/v4/swagger.json}"

tmp="$(mktemp)"
trap 'rm -f "$tmp"' EXIT

if ! curl --fail --silent --show-error --location "$url" --output "$tmp"; then
  echo "Failed to fetch the specification from $url" >&2
  exit 1
fi

version="$(jq -r '.info.version // empty' "$tmp")"
if [ "$version" != "v4" ]; then
  echo "Refusing to write: expected info.version \"v4\", got \"$version\"." >&2
  echo "This SDK targets v4 only. A version change is a deliberate decision, not a sync." >&2
  exit 1
fi

# Two-space indent with a trailing newline, matching the committed formatting exactly, so
# a diff means the API changed rather than the formatter did. jq preserves key order.
jq --indent 2 '.' "$tmp" > "$tmp.formatted"
mv "$tmp.formatted" "$tmp"

if cmp --silent "$tmp" "$spec"; then
  echo "Spec unchanged."
  exit 0
fi

cp "$tmp" "$spec"
echo "Spec CHANGED — run the conformance gates and review the diff."
