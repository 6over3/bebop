param (
    [Parameter(HelpMessage = 'The version of bebopc to install')]
    [string]$bebopcVersion = '3.1.3',
    [Parameter(HelpMessage = 'The URL where bebpc artifacts will be fetched from')]
    [string]$manifestUrl = "https://api.github.com/repos/6over3/bebop/releases/tags/v${bebopcVersion}"
)

$COLLISION_UTF8 = [char]::ConvertFromUtf32(0x1F645)
$NOGO_UTF8 = [char]::ConvertFromUtf32(0x1F645)
$LOOKING_UTF8 = [char]::ConvertFromUtf32(0x1F638)
$GLASS_UTF8 = [char]::ConvertFromUtf32(0x1F485)
$ROCKET_UTF8 = [char]::ConvertFromUtf32(0x1F680)
$ABORT_UTF8 = [char]::ConvertFromUtf32(0x1F6AB)
$CONSTRUCTION_UTF8 = [char]::ConvertFromUtf32(0x1F6A7)
$SUCCESS_UTF8 = [char]::ConvertFromUtf32(0x2705)
$ERROR_UTF8 = [char]::ConvertFromUtf32(0x274C)
$HAPPY_UTF8 = [char]::ConvertFromUtf32(0x1F600)
$UNICORN_UTF8 = [char]::ConvertFromUtf32(0x1F984)
$FIRE_UTF8 = [char]::ConvertFromUtf32(0x1F525)
$LOCK_UTF8 = [char]::ConvertFromUtf32(0x1F512)
$EYES_UTF8 = [char]::ConvertFromUtf32(0x1F440)

function Write-Color {
    [alias('Write-Colour')]
    [CmdletBinding()]
    param (
        [alias ('T')] [String[]]$Text,
        [alias ('C', 'ForegroundColor', 'FGC')] [ConsoleColor[]]$Color = [ConsoleColor]::White,
        [alias ('B', 'BGC')] [ConsoleColor[]]$BackGroundColor = $null,
        [alias ('Indent')][int] $StartTab = 0,
        [int] $LinesBefore = 0,
        [int] $LinesAfter = 0,
        [int] $StartSpaces = 0,
        [alias ('L')] [string] $LogFile = '',
        [Alias('DateFormat', 'TimeFormat')][string] $DateTimeFormat = 'yyyy-MM-dd HH:mm:ss',
        [alias ('LogTimeStamp')][bool] $LogTime = $true,
        [int] $LogRetry = 2,
        [ValidateSet('unknown', 'string', 'unicode', 'bigendianunicode', 'utf8', 'utf7', 'utf32', 'ascii', 'default', 'oem')][string]$Encoding = 'Unicode',
        [switch] $ShowTime,
        [switch] $NoNewLine,
        [alias('HideConsole')][switch] $NoConsoleOutput
    )
    if (-not $NoConsoleOutput) {
        $DefaultColor = $Color[0]
        if ($null -ne $BackGroundColor -and $BackGroundColor.Count -ne $Color.Count) {
            Write-Error "Colors, BackGroundColors parameters count doesn't match. Terminated."
            return
        }
        if ($Text.Count -ne 0) {
            if ($Color.Count -ge $Text.Count) {
                # the real deal coloring
                if ($null -eq $BackGroundColor) {
                    for ($i = 0; $i -lt $Text.Length; $i++) { Write-Host -Object $Text[$i] -ForegroundColor $Color[$i] -NoNewline }
                }
                else {
                    for ($i = 0; $i -lt $Text.Length; $i++) { Write-Host -Object $Text[$i] -ForegroundColor $Color[$i] -BackgroundColor $BackGroundColor[$i] -NoNewline }
                }
            }
            else {
                if ($null -eq $BackGroundColor) {
                    for ($i = 0; $i -lt $Color.Length ; $i++) { Write-Host -Object $Text[$i] -ForegroundColor $Color[$i] -NoNewline }
                    for ($i = $Color.Length; $i -lt $Text.Length; $i++) { Write-Host -Object $Text[$i] -ForegroundColor $DefaultColor -NoNewline }
                }
                else {
                    for ($i = 0; $i -lt $Color.Length ; $i++) { Write-Host -Object $Text[$i] -ForegroundColor $Color[$i] -BackgroundColor $BackGroundColor[$i] -NoNewline }
                    for ($i = $Color.Length; $i -lt $Text.Length; $i++) { Write-Host -Object $Text[$i] -ForegroundColor $DefaultColor -BackgroundColor $BackGroundColor[0] -NoNewline }
                }
            }
        }
        if ($NoNewLine -eq $true) { Write-Host -NoNewline } else { Write-Host } # Support for no new line
        if ($LinesAfter -ne 0) { for ($i = 0; $i -lt $LinesAfter; $i++) { Write-Host -Object "`n" -NoNewline } }  # Add empty line after
    }
}

function Get-Downloader {
    <#
    .SYNOPSIS
    Gets a System.Net.WebClient to be used for downloading data.

    .DESCRIPTION
    Retrieves a WebClient object that is pre-configured.

    .EXAMPLE
    Get-Downloader
    #>
    [CmdletBinding()]
    param()
    $downloader = New-Object System.Net.WebClient
    $downloader.Headers.Add("user-agent", "bebopc-install/$bebopcVersion");
    $defaultCreds = [System.Net.CredentialCache]::DefaultCredentials
    if ($defaultCreds) {
        $downloader.Credentials = $defaultCreds
    }
    $downloader
}

function Install-Binary($install_args) {
    $old_erroractionpreference = $ErrorActionPreference
    $ErrorActionPreference = 'stop'
    Initialize-Environment

    $fetched = Download "$downloadUrl"
    # FIXME: add a flag that lets the user not do this step
    Invoke-Installer $fetched "$install_args"

    $ErrorActionPreference = $old_erroractionpreference
}

function Get-TargetArtifacts() {
    try {
        # NOTE: this might return X64 on ARM64 Windows, which is OK since emulation is available.
        # It works correctly starting in PowerShell Core 7.3 and Windows PowerShell in Win 11 22H2.
        # Ideally this would just be
        #   [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture
        # but that gets a type from the wrong assembly on Windows PowerShell (i.e. not Core)
        $a = [System.Reflection.Assembly]::LoadWithPartialName("System.Runtime.InteropServices.RuntimeInformation")
        $t = $a.GetType("System.Runtime.InteropServices.RuntimeInformation")
        $p = $t.GetProperty("OSArchitecture")
        $arch = $p.GetValue($null).ToString();
        switch ($arch) {
            "X64" { return @("bebopc-windows-x64.zip", "chordc-windows-x64.zip") }
            "Arm64" { return @("bebopc-windows-arm64.zip", "chordc-windows-arm64.zip") }
            default {
                throw "Unsupported CPU architecture '$arch'"
            }
        }
    }
    catch {
        Write-Verbose "Get-TargetArtifacts: exception when trying to determine OS architecture."
        Write-Verbose $_
    }

    Write-Verbose("Get-TargetArtifacts: falling back to Is64BitOperatingSystem.")
    if ([System.Environment]::Is64BitOperatingSystem) {
        return @("bebopc-windows-x64.zip", "chordc-windows-x64.zip")
    }
    else {
        throw "Unsupported CPU architecture 'X86'"
    }
}


function Test-BebopcInstalled {
    [CmdletBinding()]
    param()
    $compilerPath = "$env:PROGRAMDATA\bebop"
    if (Get-Command bebopc -CommandType Application -ErrorAction Ignore) {
        $true
    }
    elseif (-not (Test-Path $compilerPath)) {
        # Install folder doesn't exist
        $false
    }
    elseif (-not (Get-ChildItem -Path $compilerPath)) {
        # Install folder exists but is empty
        $false
    }
    else {
        $true
    }
}

function Test-BebopcVersion {
    $compilerPath = "$env:PROGRAMDATA\bebop\bebopc.exe"
    $installedVersion = ([string](& "$compilerPath" --version)).Trim()
    $remoteVersion = $bebopcVersion
    if ([System.Version]$installedVersion -gt [System.Version]$remoteVersion) {
        # Installed version is greater than the version to install
        Write-Color "$COLLISION_UTF8 bebopc $installedVersion is already installed, and and a higher version than targeted $remoteVersion." -Color Red
        break
    }
    elseif ([System.Version]$installedVersion -lt [System.Version]$remoteVersion) {
        Write-Color "bebopc $installedVersion will be upgraded to $remoteVersion." -Color White
    }
    else {
        # Installed version and version to install are the same
        Write-Color "$ROCKET_UTF8 bebopc $installedVersion is already installed and up to date." -Color White 
        break
    }
}

function Invoke-Install {
    Write-Color "Locating releases...." -Color White
    $artifacts = Get-TargetArtifacts
    $downloader = Get-Downloader
    $releaseData = $downloader.DownloadString($manifestUrl)
    $pattern = '"browser_download_url":\s*"([^"]+)"'
    $downloadUrls = $releaseData | Select-String -Pattern $pattern -AllMatches | ForEach-Object { $_.Matches.Value } | ForEach-Object { $_.Split('"')[3] } | Where-Object { $_ -like "*$($artifacts[0])*" -or $_ -like "*$($artifacts[1])*" }
    
    Write-Host -NoNewline "$SUCCESS_UTF8"
    Write-Color "Downloading $($artifacts[0]) and $($artifacts[1])..." -Color White
    $tmp = [System.IO.Path]::GetTempPath()
    $downloadPaths = @()
    foreach ($url in $downloadUrls) {
        $artifact = [System.IO.Path]::GetFileName($url)
        $downloadPath = "$tmp\$artifact"
        $downloader.DownloadFile($url, $downloadPath)
        $downloadPaths += $downloadPath
    }

    Write-Host -NoNewline "$SUCCESS_UTF8"
    Write-Color "Installing bebopc and chordc" -Color White

    $compilerPath = Join-Path $env:PROGRAMDATA "bebop"
    foreach ($path in $downloadPaths) {
        Expand-Archive -Path $path -DestinationPath "$compilerPath" -Force
    }

    if (!(Test-Path -Path "$env:PROGRAMDATA\bebop\bebopc.exe")) {
        Write-Color "$ERROR_UTF8 bebopc failed to install." -Color Red
        break
    }

    # Get the current value of the PATH environment variable
    $userPath = [Environment]::GetEnvironmentVariable("PATH", "User")
    # Check if the folder path is already present in the PATH variable
    if ($userPath -split ";" -contains $compilerPath) {
        
    }
    else {
        # Add the folder path to the PATH variable
        $newPath = $userPath + ";" + $compilerPath
        # Set the updated PATH value
        [Environment]::SetEnvironmentVariable("PATH", $newPath, "User")
    }
    $env:Path = [System.Environment]::GetEnvironmentVariable("PATH", "User")
    
    if (-not (Get-Command bebopc -CommandType Application -ErrorAction Ignore)) {
        Write-Color "'$env:PROGRAMDATA\bebop\bebopc.exe' is not in the PATH environment variable." -Color Red
    }

    Write-Point "${ROCKET_UTF8} Installation successful!"
}

function Initialize-Environment() {
    If (($PSVersionTable.PSVersion.Major) -lt 5) {

        Write-Color "$COLLISION_UTF8 PowerShell 5 or later is required to install bebopc." -Color Red
        Write-Color "Upgrade PowerShell: https://docs.microsoft.com/en-us/powershell/scripting/setup/installing-windows-powershell" -Color Red
        break
    }

    # show notification to change execution policy:
    $allowedExecutionPolicy = @('Unrestricted', 'RemoteSigned', 'ByPass')
    If ((Get-ExecutionPolicy).ToString() -notin $allowedExecutionPolicy) {
        Write-Color "$COLLISION_UTF8 PowerShell requires an execution policy in [$($allowedExecutionPolicy -join ", ")] to run $app_name." -Color Red
        Write-Color "For example, to set the execution policy to 'RemoteSigned' please run :" -Color Red
        Write-Color "'Set-ExecutionPolicy RemoteSigned -scope CurrentUser'" -Color Red
        break
    }

    # Attempt to set highest encryption available for SecurityProtocol.
    # PowerShell will not set this by default (until maybe .NET 4.6.x). This
    # will typically produce a message for PowerShell v2 (just an info
    # message though)
    try {
        # Set TLS 1.2 (3072) as that is the minimum required by github.com.
        # Use integers because the enumeration value for TLS 1.2 won't exist
        # in .NET 4.0, even though they are addressable if .NET 4.5+ is
        # installed (.NET 4.5 is an in-place upgrade).
        Write-Color "Forcing web requests to allow TLS v1.2" -Color Yellow
        [System.Net.ServicePointManager]::SecurityProtocol = [System.Net.ServicePointManager]::SecurityProtocol -bor 3072
    }
    catch {
        $errorMessage = @(
            "$COLLISION_UTF8 Unable to set PowerShell to use TLS 1.2. This is required for fetch bebopc releases."
            'If you see underlying connection closed or trust errors, you may need to do one or more of the following:'
            '(1) upgrade to .NET Framework 4.5+ and PowerShell v3+,'
            '(2) Call [System.Net.ServicePointManager]::SecurityProtocol = 3072; in PowerShell prior to attempting installation'
        ) -join [Environment]::NewLine
        Write-Color $errorMessage -Color Red
        break
    }
}

function Write-Point {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Message
    )
    Write-Color "==> ", "$Message" -Color Blue, White
}

Write-Point "$GLASS_UTF8 Checking environment..."
Initialize-Environment
if (Test-BebopcInstalled) {
    Test-BebopcVersion
}
else {
    Write-Point "$EYES_UTF8 This script will install:"
    Write-Host "- $env:PROGRAMDATA\bebop\bebopc.exe"
    Write-Host "- $env:PROGRAMDATA\bebop\chordc.exe"
}

Write-Point "$UNICORN_UTF8 Downloading and installing Bebop $bebopcVersion..."
Invoke-Install
Write-Point "Tempo, an RPC framework built on top of Bebop is in public preview. Check it out:"
Write-Host "https://tempo.im"

Write-Color "- Run bebopc --help to get started" -Color White
Write-Host "- Further documentation: "
Write-Color "  https://docs.bebop.sh" -Color White