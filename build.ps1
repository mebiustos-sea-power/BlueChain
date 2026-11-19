param(
    [Parameter(Mandatory)][string]$GameDirectory,
    [ValidatePattern('^[0-9a-fA-F]{7,64}$')][string]$SourceRevision
)

$ErrorActionPreference = 'Stop'
$GameDirectory = (Resolve-Path -LiteralPath $GameDirectory).Path
$properties = @("-p:SeaPowerDir=$GameDirectory")
if ($SourceRevision) {
    $properties += "-p:SourceRevisionId=$SourceRevision"
} else {
    $properties += '-p:IncludeSourceRevisionInInformationalVersion=false'
}

& dotnet build (Join-Path $PSScriptRoot 'src/BlueChain/BlueChain.csproj') -c Release --nologo @properties
if ($LASTEXITCODE -ne 0) { throw 'Blue Chain runtime did not build.' }
& dotnet build (Join-Path $PSScriptRoot 'src/BlueChain.Preloader/BlueChain.Preloader.csproj') -c Release --nologo @properties
if ($LASTEXITCODE -ne 0) { throw 'Blue Chain Preloader did not build.' }

$runtimeBin = Join-Path $PSScriptRoot 'src/BlueChain/bin/Release/netstandard2.1'
$preloader = Join-Path $PSScriptRoot 'src/BlueChain.Preloader/bin/Release/netstandard2.1/BlueChain.Preloader.dll'
$version = [Reflection.AssemblyName]::GetAssemblyName($preloader).Version.ToString(3)
$dist = Join-Path $PSScriptRoot 'dist'
[IO.Directory]::CreateDirectory($dist) | Out-Null

Add-Type -AssemblyName System.IO.Compression.FileSystem
function Write-Package([string]$Name, [System.Collections.IDictionary]$Files) {
    $archivePath = Join-Path $dist $Name
    $temporaryArchive = Join-Path $dist ([IO.Path]::GetRandomFileName())
    try {
        $archive = [IO.Compression.ZipFile]::Open($temporaryArchive, [IO.Compression.ZipArchiveMode]::Create)
        try {
            foreach ($entry in $Files.GetEnumerator()) {
                [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $entry.Value, $entry.Key) | Out-Null
            }
        } finally {
            $archive.Dispose()
        }
        Move-Item -LiteralPath $temporaryArchive -Destination $archivePath -Force
    } finally {
        if (Test-Path -LiteralPath $temporaryArchive) { Remove-Item -LiteralPath $temporaryArchive }
    }

    $hash = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  $Name" | Set-Content -LiteralPath "$archivePath.sha256" -Encoding ascii
    Write-Output $archivePath
}

$license = Join-Path $PSScriptRoot 'LICENSE'
$preloaderFiles = [ordered]@{
    'BepInEx/plugins/BlueChain.Preloader.dll' = $preloader
    'README.md' = Join-Path $PSScriptRoot 'README.md'
    'LICENSE' = $license
}
$workshopFiles = [ordered]@{
    '_info.ini' = Join-Path $PSScriptRoot 'package/Workshop/_info.ini'
    'BlueChain.dll' = Join-Path $runtimeBin 'BlueChain.dll'
    'AnchorChain.dll' = Join-Path $runtimeBin 'AnchorChain.dll'
    'LICENSE' = $license
    'ThirdPartyNotices/AnchorChain.txt' = Join-Path $PSScriptRoot 'ThirdPartyNotices/AnchorChain.txt'
}
Write-Package "BlueChain-$version-Preloader.zip" $preloaderFiles
Write-Package "BlueChain-$version-Workshop.zip" $workshopFiles

$localFiles = [ordered]@{}
foreach ($entry in $preloaderFiles.GetEnumerator()) {
    $localFiles.Add($entry.Key, $entry.Value)
}
foreach ($entry in $workshopFiles.GetEnumerator()) {
    if ($entry.Key -eq 'LICENSE') { continue }
    $localFiles.Add("Sea Power_Data/StreamingAssets/BlueChain/$($entry.Key)", $entry.Value)
}
Write-Package "BlueChain-$version.zip" $localFiles
