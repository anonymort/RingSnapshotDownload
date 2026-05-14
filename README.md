# Ring Snapshot Download

A small command-line tool that downloads the latest snapshot image from a Ring camera or doorbell.

Think of it like this:

1. You sign in to Ring once.
2. The app asks Ring for your cameras.
3. You pick a device ID.
4. The app saves a `.jpg` snapshot to a folder.
5. The app saves a refresh token in `Settings.json` so future runs can work without asking for your password again.

This is an unofficial tool. Ring can change its private API at any time.

## What You Need

- A Ring account.
- A Ring camera or doorbell that supports snapshots.
- .NET 8 SDK if you want to build from source.

Download .NET 8 from:

```text
https://dotnet.microsoft.com/download/dotnet/8.0
```

Released builds are self-contained, so users of a packaged release do not need to install .NET.

## Build

From the project folder:

```bash
dotnet restore
dotnet build KoenZomers.Ring.SnapshotDownload.sln --configuration Release
```

## Easiest Way: Guided Terminal Wizard

On macOS or Linux, run:

```bash
./rsw.sh
```

For extra explanations while it runs, use verbose log mode:

```bash
./rsw.sh --log
```

To try downloading historical snapshot capture footage, use experimental `--dlall` mode:

```bash
./rsw.sh --log --dlall
```

`--dlall` asks Ring for historical periodic footage clips for the selected device. Ring returns MP4 clips created from periodic snapshots when they are available, not individual JPEG snapshots. By default it checks the last 14 days:

```bash
./rsw.sh --log --dlall --dlall-days 7
```

To turn those downloaded MP4 clips into local JPG snapshots, add `--dlall-extract`:

```bash
./rsw.sh --log --dlall --dlall-extract
```

By default this extracts one JPG frame per second from each downloaded MP4. Choose a different interval like this:

```bash
./rsw.sh --log --dlall --dlall-extract --dlall-extract-interval 5
```

That example extracts one JPG every 5 seconds.

Frame extraction is local-only and uses `ffmpeg`. Install it on macOS with:

```bash
brew install ffmpeg
```

If you run `--dlall-extract` and `ffmpeg` is missing, the wizard will ask whether to install it with Homebrew. If Homebrew is not installed, it will print manual install instructions.

Historical downloads are saved under:

```text
snapshots/<device-id> - historical snapshot footage/
```

Extracted JPG frames are saved under:

```text
snapshots/<device-id> - historical snapshot footage/jpg-frames/
```

The wizard will:

1. Show an Anonymort ASCII logo.
2. Build the project if you want.
3. Ask for your Ring email.
4. Ask for your Ring password with hidden input.
5. List your Ring devices.
6. Ask which device ID to use.
7. Ask where to save snapshots.
8. Download and optionally validate the image.

If Ring asks for a two-factor code, type it directly into the terminal.

The script does not save your password. The app may still save a Ring refresh token in `Settings.json`, which you should keep private.

Windows PowerShell users can try the untested helper:

```powershell
.\rsw.ps1
```

## Test

There are no automated unit tests in this repository yet. Use these checks before publishing or using a build:

```bash
dotnet build KoenZomers.Ring.SnapshotDownload.sln --configuration Release
dotnet list KoenZomers.Ring.SnapshotDownload.sln package --vulnerable --include-transitive
dotnet run --project ConsoleAppCore/ConsoleAppCore.csproj --configuration Release --
./test-scripts.sh
```

The last command should print the help text and exit because no username or device ID was provided.

## Build A macOS App

Apple Silicon Mac:

```bash
dotnet publish ConsoleAppCore/ConsoleAppCore.csproj \
  --configuration Release \
  -r osx-arm64 \
  --self-contained true \
  -p:PublishSingleFile=true \
  -p:PublishTrimmed=false \
  -o ./release/osx-arm64
```

Intel Mac:

```bash
dotnet publish ConsoleAppCore/ConsoleAppCore.csproj \
  --configuration Release \
  -r osx-x64 \
  --self-contained true \
  -p:PublishSingleFile=true \
  -p:PublishTrimmed=false \
  -o ./release/osx-x64
```

Run the built file from the output folder:

```bash
./release/osx-arm64/RingSnapshotDownload
```

macOS may warn because local builds are not signed or notarized.

## First Run: Find Your Ring Device ID

Run this:

```bash
dotnet run --project ConsoleAppCore/ConsoleAppCore.csproj --configuration Release -- \
  -username you@example.com \
  -password 'your-ring-password' \
  -list
```

If your Ring account uses two-factor authentication, the app will ask for the code.

The output will show device IDs. Copy the ID for the camera or doorbell you want.

## Download A Snapshot

Replace `123456` with your device ID:

```bash
dotnet run --project ConsoleAppCore/ConsoleAppCore.csproj --configuration Release -- \
  -username you@example.com \
  -password 'your-ring-password' \
  -deviceid 123456 \
  -out ./snapshots \
  -forceupdate \
  -validateimage
```

The file name will look like this:

```text
123456 - 2026-05-14 12-30-00.jpg
```

## Use A Built Release

After publishing, run the executable directly instead of `dotnet run`.

Example:

```bash
./RingSnapshotDownload \
  -username you@example.com \
  -password 'your-ring-password' \
  -deviceid 123456 \
  -out ./snapshots \
  -forceupdate \
  -validateimage
```

On Windows, use:

```powershell
.\RingSnapshotDownload.exe -username you@example.com -password "your-ring-password" -deviceid 123456 -out .\snapshots -forceupdate -validateimage
```

## Settings.json

The app stores settings next to the executable in `Settings.json`.

Most importantly, it stores `RingRefreshToken`. That lets the app sign in again later without your password.

Keep this file private. Anyone with the refresh token may be able to access your Ring account through this tool.

## Command Options

| Option | Meaning |
| --- | --- |
| `-username` | Ring account email address. |
| `-password` | Ring account password. |
| `-list` | Show your Ring devices and IDs. |
| `-deviceid` | The Ring device ID to download from. |
| `-out` | Folder where the snapshot should be saved. |
| `-forceupdate` | Ask Ring for a fresh snapshot instead of using the cached one. |
| `-validateimage` | Check that the downloaded file is a real image. |
| `-maxretries` | Number of retry attempts when Ring is slow or returns an error. Default is `3`. |
| `-dlall` | Experimental. Download historical periodic snapshot footage clips, where Ring returns them. |
| `-dlalldays` | Number of days to query with `-dlall`. Default is `14`. |

## Historical Snapshot Strategy

Ring's current mobile-style endpoints do not clearly expose every historical snapshot as an individual JPEG. The available historical path this tool uses is Ring's periodic footage endpoint:

```text
https://api.ring.com/recordings/public/footages/{device-id}
```

That endpoint can return MP4 clips built from periodic snapshot capture. The tool saves those clips and writes a `manifest.json` file with clip metadata.

If you pass `--dlall-extract`, the wrapper then uses local `ffmpeg` to extract JPG frames from those MP4 clips. This means the JPG files are generated locally from downloaded footage; they are not separate JPEG files returned directly by Ring.

## Current Status

This version:

- Targets .NET 8.
- Uses Ring's current app snapshot endpoint.
- Supports Windows, Linux, Raspberry Pi, Intel macOS, and Apple Silicon macOS builds.
- Uses refresh tokens for unattended runs after the first login.
- Handles two-factor authentication during the first login.

## Important Notes

- This is not the official Ring Partner API.
- It uses the same private/mobile-style Ring API pattern used by unofficial Ring clients.
- Ring may change or block these endpoints without notice.
- Snapshot availability depends on your device, Ring settings, motion settings, subscription, and whether the device is online.

## License

Apache-2.0. See [LICENSE](LICENSE).
