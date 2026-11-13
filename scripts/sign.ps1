param(
    [string]$File,
    [string]$Pfx,
    [string]$Password,
    [string]$Thumbprint,
    [string]$Timestamp = "http://timestamp.digicert.com",
    [switch]$VerifyOnly
)

$ErrorActionPreference = "Stop"

# finds signtool in the Windows SDK or on PATH
function Find-SignTool {
    $kits = Join-Path ${env:ProgramFiles(x86)} "Windows Kits\10\bin"
    if (Test-Path $kits) {
        $found = Get-ChildItem $kits -Directory | Sort-Object Name -Descending | ForEach-Object {
            Join-Path $_.FullName "x64\signtool.exe"
        } | Where-Object { Test-Path $_ } | Select-Object -First 1
        if ($found) { return $found }
    }
    $onPath = Get-Command signtool.exe -ErrorAction SilentlyContinue
    if ($onPath) { return $onPath.Source }
    return $null
}

# picks the newest published executable when none is given
if (-not $File) {
    $File = Get-ChildItem "artifacts\U2cRumble-*-win-x64.exe" -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending | Select-Object -First 1 -ExpandProperty FullName
}
if (-not $File -or -not (Test-Path $File)) {
    throw "No executable found. Build it first or pass -File."
}
$File = (Resolve-Path $File).Path

$signtool = Find-SignTool
if (-not $signtool) {
    throw "signtool.exe was not found. Install the Signing Tools component of the Windows SDK."
}

if (-not $VerifyOnly) {
    if (-not $Thumbprint -and -not $Pfx) {
        throw "Pass -Thumbprint, -Pfx, or -VerifyOnly."
    }

    $signArgs = @("sign", "/fd", "SHA256", "/tr", $Timestamp, "/td", "SHA256")
    if ($Thumbprint) {
        $signArgs += @("/sha1", $Thumbprint)
    } else {
        $signArgs += @("/f", $Pfx)
        if ($Password) { $signArgs += @("/p", $Password) }
    }
    $signArgs += $File

    & $signtool @signArgs
    if ($LASTEXITCODE -ne 0) { throw "signtool sign failed with code $LASTEXITCODE." }
}

& $signtool verify /pa /v $File
if ($LASTEXITCODE -ne 0) { throw "signtool verify failed with code $LASTEXITCODE." }

$hash = (Get-FileHash $File -Algorithm SHA256).Hash.ToLower()
"SHA256 $hash  $(Split-Path $File -Leaf)"
"Update SHA256SUMS.txt in the release with this value."
