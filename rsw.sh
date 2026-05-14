#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT="$ROOT_DIR/ConsoleAppCore/ConsoleAppCore.csproj"
SOLUTION="$ROOT_DIR/KoenZomers.Ring.SnapshotDownload.sln"
VERBOSE=false

bold="$(printf '\033[1m')"
dim="$(printf '\033[2m')"
green="$(printf '\033[32m')"
cyan="$(printf '\033[36m')"
yellow="$(printf '\033[33m')"
red="$(printf '\033[31m')"
reset="$(printf '\033[0m')"

usage() {
  cat <<EOF
Usage: ./rsw.sh [--log]

Options:
  --log     Print extra explanations before and after each major command.
  -h, --help
            Show this help.
EOF
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --log)
      VERBOSE=true
      shift
      ;;
    -h|--help)
      usage
      exit 0
      ;;
    *)
      printf 'Unknown option: %s\n' "$1"
      usage
      exit 1
      ;;
  esac
done

banner() {
  cat <<'EOF'
    .aMMMb  dMMMMb  .aMMMb  dMMMMb  dMP dMP dMMMMMMMMb  .aMMMb  dMMMMb dMMMMMMP
   dMP"dMP dMP dMP dMP"dMP dMP dMP dMP.dMP dMP"dMP"dMP dMP"dMP dMP.dMP   dMP
  dMMMMMP dMP dMP dMP dMP dMP dMP  VMMMMP dMP dMP dMP dMP dMP dMMMMK"   dMP
 dMP dMP dMP dMP dMP.aMP dMP dMP dA .dMP dMP dMP dMP dMP.aMP dMP"AMF   dMP
dMP dMP dMP dMP  VMMMP" dMP dMP  VMMMP" dMP dMP dMP  VMMMP" dMP dMP   dMP

                       R I N G   S N A P S H O T   W I Z A R D
EOF
}

say() {
  printf '%b\n' "$*"
}

log() {
  if [[ "$VERBOSE" == true ]]; then
    say "${dim}[log]${reset} $*"
  fi
}

log_command() {
  if [[ "$VERBOSE" == true ]]; then
    say "${dim}[log] Running:${reset} $*"
  fi
}

prompt() {
  local label="$1"
  local default="${2:-}"
  local value

  if [[ -n "$default" ]]; then
    read -r -p "$(printf '%b' "${cyan}?${reset} ${label} ${dim}[${default}]${reset}: ")" value
    printf '%s' "${value:-$default}"
  else
    read -r -p "$(printf '%b' "${cyan}?${reset} ${label}: ")" value
    printf '%s' "$value"
  fi
}

prompt_secret() {
  local label="$1"
  local value

  read -r -s -p "$(printf '%b' "${cyan}?${reset} ${label}: ")" value
  printf '\n' >&2
  printf '%s' "$value"
}

confirm() {
  local label="$1"
  local default="${2:-y}"
  local answer
  local hint

  if [[ "$default" == "y" ]]; then
    hint="Y/n"
  else
    hint="y/N"
  fi

  read -r -p "$(printf '%b' "${cyan}?${reset} ${label} ${dim}[${hint}]${reset}: ")" answer
  answer="${answer:-$default}"

  [[ "$answer" =~ ^[Yy]$ ]]
}

find_dotnet() {
  if command -v dotnet >/dev/null 2>&1; then
    command -v dotnet
    return
  fi

  if [[ -x /tmp/dotnet-ring-snapshot/dotnet ]]; then
    printf '%s\n' "/tmp/dotnet-ring-snapshot/dotnet"
    return
  fi

  return 1
}

run_ring() {
  "$DOTNET" run --project "$PROJECT" --configuration Release -- "$@"
}

describe_ring_output() {
  log "Output below comes from RingSnapshotDownload, the .NET console app."
  log "If Ring requires 2FA, the app will pause and wait for the code in this terminal."
  log "If authentication fails, the app should now print Ring's safe OAuth error text."
}

banner
say "${bold}Welcome.${reset} This wizard will list your Ring devices and download one snapshot."
say "${yellow}Your password is hidden while typing. Do not share Settings.json or refresh tokens.${reset}"
if [[ "$VERBOSE" == true ]]; then
  say "${cyan}Verbose log mode is on.${reset} I will explain each command and what its output means."
fi
say

if ! DOTNET="$(find_dotnet)"; then
  say "${red}Could not find dotnet.${reset}"
  say "Install .NET 8 from https://dotnet.microsoft.com/download/dotnet/8.0 and run this script again."
  exit 1
fi

say "${green}Using dotnet:${reset} $DOTNET"
say "${green}Project:${reset} $PROJECT"
log "Repo folder: $ROOT_DIR"
log "Solution file: $SOLUTION"
log "The script prefers dotnet on PATH, then falls back to /tmp/dotnet-ring-snapshot/dotnet."
say

if confirm "Build the project before running" "y"; then
  say
  say "${bold}Building...${reset}"
  log "This compiles the API library and RingSnapshotDownload app in Release mode."
  log_command "$DOTNET build \"$SOLUTION\" --configuration Release"
  "$DOTNET" build "$SOLUTION" --configuration Release
  log "Build completed. If it says 0 errors, the app is ready to run."
  say
fi

email="$(prompt "Ring email")"
while [[ -z "$email" ]]; do
  say "${red}Email is required.${reset}"
  email="$(prompt "Ring email")"
done

password="$(prompt_secret "Ring password")"
while [[ -z "$password" ]]; do
  say "${red}Password is required.${reset}"
  password="$(prompt_secret "Ring password")"
done

say
say "${bold}Step 1: Listing Ring devices${reset}"
say "${dim}If Ring asks for 2FA, type the code directly into this terminal.${reset}"
say
log "The next command sends your email/password to the local .NET app, which then contacts Ring."
log "For safety, the password is not printed in this log."
log_command "$DOTNET run --project \"$PROJECT\" --configuration Release -- -username \"$email\" -password \"********\" -list"
describe_ring_output
if ! run_ring -username "$email" -password "$password" -list; then
  say
  say "${red}Ring login or device listing failed.${reset}"
  say "Check your email/password, complete any Ring security prompts, and try again."
  log "Failure happened while listing devices, before a device ID was selected."
  exit 1
fi
log "Device listing finished. Use one of the numeric IDs printed above."

say
device_id="$(prompt "Device ID to download from")"
while [[ ! "$device_id" =~ ^[0-9]+$ ]]; do
  say "${red}Enter a numeric device ID from the list above.${reset}"
  device_id="$(prompt "Device ID to download from")"
done

output_dir="$(prompt "Snapshot output folder" "$ROOT_DIR/snapshots")"
mkdir -p "$output_dir"
log "Snapshots will be saved under: $output_dir"

args=(-username "$email" -password "$password" -deviceid "$device_id" -out "$output_dir")

if confirm "Force Ring to capture a fresh snapshot" "y"; then
  args+=(-forceupdate)
fi

if confirm "Validate the downloaded image" "y"; then
  args+=(-validateimage)
fi

max_retries="$(prompt "Maximum retries" "3")"
if [[ "$max_retries" =~ ^[0-9]+$ ]]; then
  args+=(-maxretries "$max_retries")
  log "The app will retry snapshot download up to $max_retries time(s)."
else
  say "${yellow}Ignoring non-numeric retry value; app default will be used.${reset}"
  log "Retry value was not numeric, so the .NET app default applies."
fi

say
say "${bold}Step 2: Downloading snapshot${reset}"
say
log "The next output is the actual snapshot download run."
log_command "$DOTNET run --project \"$PROJECT\" --configuration Release -- -username \"$email\" -password \"********\" -deviceid \"$device_id\" -out \"$output_dir\" ${args[*]:8}"
describe_ring_output
if ! run_ring "${args[@]}"; then
  say
  say "${red}Snapshot download failed.${reset}"
  say "Try again later, or run the wizard without forcing a fresh snapshot."
  log "Failure happened during snapshot download after selecting device ID $device_id."
  exit 1
fi

say
say "${green}Finished.${reset} Check this folder:"
say "$output_dir"
