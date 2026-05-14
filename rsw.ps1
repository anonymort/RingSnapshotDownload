param()

$ErrorActionPreference = "Stop"
$RootDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$Project = Join-Path $RootDir "ConsoleAppCore/ConsoleAppCore.csproj"
$Solution = Join-Path $RootDir "KoenZomers.Ring.SnapshotDownload.sln"

function Write-Banner {
@"
    ___    _   _   ___    _   _  __   __  __  __   ___    ____   _____
   / _ \  | \ | | / _ \  | \ | | \ \ / / |  \/  | / _ \  |  _ \ |_   _|
  | |_| | |  \| || | | | |  \| |  \ V /  | |\/| || | | | | |_| |  | |
  |  _  | | |\  || |_| | | |\  |   | |   | |  | || |_| | |  _ <   | |
  |_| |_| |_| \_| \___/  |_| \_|   |_|   |_|  |_| \___/  |_| \_\  |_|

                     R I N G   S N A P S H O T   W I Z A R D
"@
}

function Read-Required {
    param([string]$Prompt)

    do {
        $Value = Read-Host $Prompt
        if ([string]::IsNullOrWhiteSpace($Value)) {
            Write-Host "Value is required." -ForegroundColor Red
        }
    } while ([string]::IsNullOrWhiteSpace($Value))

    return $Value
}

function Read-YesNo {
    param(
        [string]$Prompt,
        [bool]$DefaultYes = $true
    )

    $Hint = if ($DefaultYes) { "Y/n" } else { "y/N" }
    $Answer = Read-Host "$Prompt [$Hint]"
    if ([string]::IsNullOrWhiteSpace($Answer)) {
        return $DefaultYes
    }

    return $Answer -match "^[Yy]$"
}

function Find-DotNet {
    $DotNet = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($DotNet) {
        return $DotNet.Source
    }

    throw "Could not find dotnet. Install .NET 8 from https://dotnet.microsoft.com/download/dotnet/8.0"
}

function Invoke-Ring {
    param([string[]]$RingArgs)

    & $DotNet run --project $Project --configuration Release -- @RingArgs
}

Write-Banner
Write-Host "Welcome. This wizard will list your Ring devices and download one snapshot." -ForegroundColor Cyan
Write-Host "PowerShell version is included for convenience but has not been tested on Windows." -ForegroundColor Yellow
Write-Host "Do not share Settings.json or refresh tokens." -ForegroundColor Yellow
Write-Host

$DotNet = Find-DotNet
Write-Host "Using dotnet: $DotNet" -ForegroundColor Green

if (Read-YesNo "Build the project before running" $true) {
    & $DotNet build $Solution --configuration Release
}

$Email = Read-Required "Ring email"
$SecurePassword = Read-Host "Ring password" -AsSecureString
$PasswordPtr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($SecurePassword)
try {
    $Password = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($PasswordPtr)
}
finally {
    [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($PasswordPtr)
}

Write-Host
Write-Host "Step 1: Listing Ring devices" -ForegroundColor Cyan
Write-Host "If Ring asks for 2FA, type the code directly into this terminal." -ForegroundColor Yellow
try {
    Invoke-Ring @("-username", $Email, "-password", $Password, "-list")
}
catch {
    Write-Host
    Write-Host "Ring login or device listing failed." -ForegroundColor Red
    Write-Host "Check your email/password, complete any Ring security prompts, and try again."
    exit 1
}

do {
    $DeviceId = Read-Required "Device ID to download from"
    if ($DeviceId -notmatch "^[0-9]+$") {
        Write-Host "Enter a numeric device ID from the list above." -ForegroundColor Red
    }
} while ($DeviceId -notmatch "^[0-9]+$")

$OutputDir = Read-Host "Snapshot output folder [$RootDir\snapshots]"
if ([string]::IsNullOrWhiteSpace($OutputDir)) {
    $OutputDir = Join-Path $RootDir "snapshots"
}
New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null

$Args = @("-username", $Email, "-password", $Password, "-deviceid", $DeviceId, "-out", $OutputDir)

if (Read-YesNo "Force Ring to capture a fresh snapshot" $true) {
    $Args += "-forceupdate"
}

if (Read-YesNo "Validate the downloaded image" $true) {
    $Args += "-validateimage"
}

$MaxRetries = Read-Host "Maximum retries [3]"
if ([string]::IsNullOrWhiteSpace($MaxRetries)) {
    $MaxRetries = "3"
}
if ($MaxRetries -match "^[0-9]+$") {
    $Args += @("-maxretries", $MaxRetries)
}

Write-Host
Write-Host "Step 2: Downloading snapshot" -ForegroundColor Cyan
try {
    Invoke-Ring $Args
}
catch {
    Write-Host
    Write-Host "Snapshot download failed." -ForegroundColor Red
    Write-Host "Try again later, or run the wizard without forcing a fresh snapshot."
    exit 1
}

Write-Host
Write-Host "Finished. Check this folder:" -ForegroundColor Green
Write-Host $OutputDir
