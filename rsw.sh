#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT="$ROOT_DIR/ConsoleAppCore/ConsoleAppCore.csproj"
SOLUTION="$ROOT_DIR/KoenZomers.Ring.SnapshotDownload.sln"
VERBOSE=false
DLALL=false
DLALL_DAYS=14
DLALL_DAYS_SET=false
DLALL_EXTRACT=false
DLALL_EXTRACT_INTERVAL=1

bold="$(printf '\033[1m')"
dim="$(printf '\033[2m')"
green="$(printf '\033[32m')"
cyan="$(printf '\033[36m')"
yellow="$(printf '\033[33m')"
red="$(printf '\033[31m')"
reset="$(printf '\033[0m')"

usage() {
  cat <<EOF
Usage: ./rsw.sh [--log] [--dlall] [--dlall-days DAYS] [--dlall-extract] [--dlall-extract-interval SECONDS]

Options:
  --log     Print extra explanations before and after each major command.
  --dlall   Experimental: download historical Ring recordings for local frame extraction.
  --dlall-days DAYS
            Number of days to query with --dlall. Default: prompt, suggested 14. Maximum: 180.
  --dlall-extract
            After --dlall downloads MP4 recordings, extract local JPG frames with ffmpeg.
  --dlall-extract-interval SECONDS
            Extract one JPG frame every N seconds. Default: 1.
            After extraction, the wizard can optionally create a timelapse MP4.
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
    --dlall)
      DLALL=true
      shift
      ;;
    --dlall-extract)
      DLALL=true
      DLALL_EXTRACT=true
      shift
      ;;
    --dlall-extract-interval)
      if [[ $# -lt 2 || ! "$2" =~ ^[0-9]+([.][0-9]+)?$ ]]; then
        printf 'Expected a positive number after --dlall-extract-interval.\n'
        usage
        exit 1
      fi
      DLALL_EXTRACT_INTERVAL="$2"
      shift 2
      ;;
    --dlall-days)
      if [[ $# -lt 2 || ! "$2" =~ ^[0-9]+$ || "$2" -lt 1 || "$2" -gt 180 ]]; then
        printf 'Expected a number from 1 to 180 after --dlall-days.\n'
        usage
        exit 1
      fi
      DLALL_DAYS="$2"
      DLALL_DAYS_SET=true
      shift 2
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

extract_frames() {
  local recordings_dir="$1"
  local frames_dir="$recordings_dir/jpg-frames"
  local clips=()
  local clip
  local clip_name
  local output_pattern

  ensure_ffmpeg

  while IFS= read -r -d '' clip; do
    clips+=("$clip")
  done < <(find "$recordings_dir" -maxdepth 1 -type f -name '*.mp4' -print0 | sort -z)

  if [[ "${#clips[@]}" -eq 0 ]]; then
    say "${yellow}No MP4 files found to extract from:${reset} $recordings_dir"
    return 0
  fi

  mkdir -p "$frames_dir"
  say "${bold}Step 3: Extracting local JPG frames${reset}"
  say "Extracting one frame every ${DLALL_EXTRACT_INTERVAL} second(s) into:"
  say "$frames_dir"
  say

  for clip in "${clips[@]}"; do
    clip_name="$(basename "$clip" .mp4)"
    clip_name="${clip_name// /_}"
    output_pattern="$frames_dir/${clip_name}_frame_%06d.jpg"
    log_command "ffmpeg -hide_banner -loglevel error -i \"$clip\" -vf fps=1/$DLALL_EXTRACT_INTERVAL -q:v 2 \"$output_pattern\""
    ffmpeg -hide_banner -loglevel error -i "$clip" -vf "fps=1/$DLALL_EXTRACT_INTERVAL" -q:v 2 "$output_pattern"
    say "${green}Extracted frames from:${reset} $(basename "$clip")"
  done
}

create_timelapse() {
  local recordings_dir="$1"
  local frames_dir="$recordings_dir/jpg-frames"
  local timelapse_dir="$recordings_dir/timelapse"
  local fps
  local output_name
  local output_path
  local temp_dir
  local frame
  local frame_count=0

  ensure_ffmpeg

  if [[ ! -d "$frames_dir" ]] || ! find "$frames_dir" -maxdepth 1 -type f -name '*.jpg' -print -quit | grep -q .; then
    say "${yellow}No JPG frames found for timelapse:${reset} $frames_dir"
    return 0
  fi

  say
  say "${bold}Step 4: Timelapse video${reset}"
  if ! confirm "Create a timelapse MP4 from the extracted JPG frames" "n"; then
    log "Timelapse creation skipped by user."
    return 0
  fi

  fps="$(prompt "Timelapse frames per second" "24")"
  while [[ ! "$fps" =~ ^[0-9]+([.][0-9]+)?$ || "$fps" == "0" || "$fps" == "0.0" ]]; do
    say "${red}Enter a positive number for frames per second.${reset}"
    fps="$(prompt "Timelapse frames per second" "24")"
  done

  output_name="$(prompt "Timelapse output filename" "timelapse.mp4")"
  while [[ -z "$output_name" || "$output_name" == */* ]]; do
    say "${red}Enter a filename only, without folders or slashes.${reset}"
    output_name="$(prompt "Timelapse output filename" "timelapse.mp4")"
  done
  if [[ "$output_name" != *.mp4 ]]; then
    output_name="${output_name}.mp4"
  fi

  mkdir -p "$timelapse_dir"
  output_path="$timelapse_dir/$output_name"
  if [[ -e "$output_path" ]]; then
    if confirm "Overwrite existing timelapse file" "n"; then
      rm -f "$output_path"
    else
      say "${yellow}Timelapse skipped because output already exists:${reset} $output_path"
      return 0
    fi
  fi

  temp_dir="$(mktemp -d "$recordings_dir/.timelapse-frames.XXXXXX")"
  while IFS= read -r -d '' frame; do
    frame_count=$((frame_count + 1))
    ln -s "$frame" "$temp_dir/frame_$(printf '%08d' "$frame_count").jpg"
  done < <(find "$frames_dir" -maxdepth 1 -type f -name '*.jpg' -print0 | sort -z)

  say "Stitching $frame_count frame(s) at $fps FPS into:"
  say "$output_path"
  log_command "ffmpeg -hide_banner -loglevel error -framerate \"$fps\" -i \"$temp_dir/frame_%08d.jpg\" -c:v libx264 -pix_fmt yuv420p -movflags +faststart \"$output_path\""

  if ffmpeg -hide_banner -loglevel error -framerate "$fps" -i "$temp_dir/frame_%08d.jpg" -c:v libx264 -pix_fmt yuv420p -movflags +faststart "$output_path"; then
    rm -rf "$temp_dir"
    say "${green}Timelapse saved:${reset} $output_path"
    if confirm "Delete JPG frames after successful timelapse to save disk space" "n"; then
      find "$frames_dir" -maxdepth 1 -type f -name '*.jpg' -delete
      say "${green}Deleted extracted JPG frames:${reset} $frames_dir"
    fi
  else
    rm -rf "$temp_dir"
    say "${red}Timelapse creation failed.${reset}"
    return 1
  fi
}

ensure_ffmpeg() {
  if command -v ffmpeg >/dev/null 2>&1; then
    log "ffmpeg found at: $(command -v ffmpeg)"
    return 0
  fi

  say "${yellow}ffmpeg is required for --dlall-extract but is not installed.${reset}"

  if [[ "$(uname -s)" == "Darwin" ]] && command -v brew >/dev/null 2>&1; then
    if confirm "Install ffmpeg now with Homebrew" "y"; then
      log_command "brew install ffmpeg"
      brew install ffmpeg
      if command -v ffmpeg >/dev/null 2>&1; then
        say "${green}ffmpeg installed.${reset}"
        return 0
      fi

      say "${red}Homebrew finished, but ffmpeg is still not on PATH.${reset}"
      return 1
    fi

    say "Install later with: brew install ffmpeg"
    return 1
  fi

  if [[ "$(uname -s)" == "Darwin" ]]; then
    say "Install Homebrew from https://brew.sh, then run: brew install ffmpeg"
  else
    say "Install ffmpeg with your package manager, then rerun this command."
  fi

  return 1
}

describe_ring_output() {
  log "Output below comes from RingSnapshotDownload, the .NET console app."
  log "If Ring requires 2FA, the app will pause and wait for the code in this terminal."
  log "If authentication fails, the app should now print Ring's safe OAuth error text."
}

banner
say "${bold}Welcome.${reset} This wizard will list your Ring devices and download one snapshot."
if [[ "$DLALL" == true ]]; then
  say "${yellow}Experimental --dlall mode is on.${reset} I will download historical Ring recordings that Ring returns for this account."
  if [[ "$DLALL_EXTRACT" == true ]]; then
    say "${yellow}Local frame extraction is on.${reset} I will use ffmpeg to turn downloaded MP4s into JPG frames."
  fi
  if [[ "$DLALL_DAYS_SET" == false ]]; then
    requested_days="$(prompt "How many days back to query (1-180)" "$DLALL_DAYS")"
    while [[ ! "$requested_days" =~ ^[0-9]+$ || "$requested_days" -lt 1 || "$requested_days" -gt 180 ]]; do
      say "${red}Enter a number from 1 to 180.${reset}"
      requested_days="$(prompt "How many days back to query (1-180)" "$DLALL_DAYS")"
    done
    DLALL_DAYS="$requested_days"
  fi
fi
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

if [[ "$DLALL" == true ]]; then
  args+=(-dlall -dlalldays "$DLALL_DAYS")
  recordings_dir="$output_dir/$device_id - historical recordings"

  say
  say "${bold}Step 2: Downloading historical recordings${reset}"
  say "${yellow}This uses Ring video history. Returned files are MP4 recordings; JPG snapshots are created locally with --dlall-extract.${reset}"
  say
  log "The app will query $DLALL_DAYS day(s), save returned MP4 recordings, and write a manifest."
  log_command "$DOTNET run --project \"$PROJECT\" --configuration Release -- -username \"$email\" -password \"********\" -deviceid \"$device_id\" -out \"$output_dir\" -dlall -dlalldays \"$DLALL_DAYS\""
  describe_ring_output
  if ! run_ring "${args[@]}"; then
    say
    say "${red}Historical recording download failed.${reset}"
    say "Ring may not have video history available for this device/date range, account, or subscription."
    exit 1
  fi

  if [[ "$DLALL_EXTRACT" == true ]]; then
    say
    extract_frames "$recordings_dir"
    create_timelapse "$recordings_dir"
  fi

  say
  say "${green}Finished.${reset} Check this folder:"
  say "$output_dir"
  exit 0
fi

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
