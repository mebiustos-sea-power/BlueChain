param(
    [Parameter(Mandatory)][string]$GameDirectory,
    [ValidatePattern('^[0-9a-fA-F]{7,64}$')][string]$SourceRevision
)

$ErrorActionPreference = 'Stop'
$GameDirectory = (Resolve-Path -LiteralPath $GameDirectory).Path
$project = Join-Path $PSScriptRoot 'src/BlueChain/BlueChain.csproj'
$properties = @("-p:SeaPowerDir=$GameDirectory")
if ($SourceRevision) { $properties += "-p:SourceRevisionId=$SourceRevision" }
& dotnet build $project -c Release --nologo @properties
if ($LASTEXITCODE -ne 0) { throw 'Blue Chain did not build.' }

$bin = Join-Path $PSScriptRoot 'src/BlueChain/bin/Release/netstandard2.1'
$loader = Join-Path $bin 'BlueChain.dll'
$version = [Reflection.AssemblyName]::GetAssemblyName($loader).Version.ToString(3)
$dist = Join-Path $PSScriptRoot 'dist'
[IO.Directory]::CreateDirectory($dist) | Out-Null
$archivePath = Join-Path $dist "BlueChain-$version.zip"
$temporaryArchive = Join-Path $dist ([IO.Path]::GetRandomFileName())

Add-Type -AssemblyName System.IO.Compression.FileSystem
try {
    $archive = [IO.Compression.ZipFile]::Open($temporaryArchive, [IO.Compression.ZipArchiveMode]::Create)
    try {
        $files = [ordered]@{
            'BepInEx/plugins/BlueChain/BlueChain.dll' = $loader
            'BepInEx/plugins/BlueChain/AnchorChain.dll' = Join-Path $bin 'AnchorChain.dll'
            'README.md' = Join-Path $PSScriptRoot 'README.md'
            'ThirdPartyNotices/AnchorChain.txt' = Join-Path $PSScriptRoot 'ThirdPartyNotices/AnchorChain.txt'
        }
        foreach ($entry in $files.GetEnumerator()) {
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
"$hash  $(Split-Path -Leaf $archivePath)" | Set-Content -LiteralPath "$archivePath.sha256" -Encoding ascii
Write-Output $archivePath
