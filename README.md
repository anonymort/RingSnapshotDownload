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

## Test

There are no automated unit tests in this repository yet. Use these checks before publishing or using a build:

```bash
dotnet build KoenZomers.Ring.SnapshotDownload.sln --configuration Release
dotnet list KoenZomers.Ring.SnapshotDownload.sln package --vulnerable --include-transitive
dotnet run --project ConsoleAppCore/ConsoleAppCore.csproj --configuration Release --
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
