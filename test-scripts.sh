#!/usr/bin/env bash
set -euo pipefail

test -x ./rsw.sh
bash -n ./rsw.sh

test -f ./rsw.ps1
if command -v pwsh >/dev/null 2>&1; then
  pwsh -NoProfile -Command "\$null = [System.Management.Automation.PSParser]::Tokenize((Get-Content -Raw ./rsw.ps1), [ref]\$null)"
else
  echo "pwsh not found; skipping PowerShell parse check"
fi
