#!/usr/bin/env bash
# Usage: Tools/CoreTests/test.sh <Board|Collect|Boosters|Economy|Generation|All> [extra dotnet test args]
# Runs Unscrew.Core NUnit tests outside Unity in seconds. Safe to run in parallel for different modules.
set -euo pipefail
module="${1:?module name required}"; shift || true
here="$(cd "$(dirname "$0")" && pwd)"
dotnet test "$here/$module/$module.Tests.csproj" --nologo -v q "$@"
