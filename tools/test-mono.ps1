param([Parameter(Mandatory)][string]$GameDirectory)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$GameDirectory = (Resolve-Path -LiteralPath $GameDirectory).Path
$project = Join-Path $projectRoot 'tests/BlueChain.MonoSmoke'
& dotnet build (Join-Path $project 'BlueChain.MonoSmoke.csproj') -c Release --nologo "-p:SeaPowerDir=$GameDirectory"
if ($LASTEXITCODE -ne 0) { throw 'The Mono smoke test did not build.' }

$managed = Join-Path $GameDirectory 'Sea Power_Data/Managed'
$core = Join-Path $GameDirectory 'BepInEx/core'
$runtime = Join-Path $GameDirectory 'MonoBleedingEdge/EmbedRuntime/mono-2.0-bdwgc.dll'
$config = Join-Path $GameDirectory 'MonoBleedingEdge/etc'
$testAssembly = Join-Path $project 'bin/Release/netstandard2.1/BlueChain.MonoSmoke.dll'
$testDirectory = Split-Path -Parent $testAssembly
$scratch = Join-Path $project ('obj/fixtures-' + [guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($scratch) | Out-Null

$runtimeLiteral = $runtime.Replace('"', '""')
if (-not ('BlueChainNativeMono' -as [type])) {
    Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;

public static class BlueChainNativeMono {
    [DllImport(@"$runtimeLiteral", CallingConvention=CallingConvention.Cdecl)]
    public static extern void mono_set_dirs(string assemblyPath, string configPath);
    [DllImport(@"$runtimeLiteral", CallingConvention=CallingConvention.Cdecl)]
    public static extern void mono_set_assemblies_path(string path);
    [DllImport(@"$runtimeLiteral", CallingConvention=CallingConvention.Cdecl)]
    public static extern int mono_main(int argc, IntPtr argv);
}
"@
}

[BlueChainNativeMono]::mono_set_dirs($managed, $config)
[BlueChainNativeMono]::mono_set_assemblies_path((@($testDirectory, $managed, $core) -join ';'))
$arguments = @('BlueChainMono', '--runtime=v4.0.30319', $testAssembly, $scratch)
$strings = @($arguments | ForEach-Object { [Runtime.InteropServices.Marshal]::StringToHGlobalAnsi($_) })
$vector = [Runtime.InteropServices.Marshal]::AllocHGlobal([IntPtr]::Size * ($strings.Count + 1))
try {
    for ($index = 0; $index -lt $strings.Count; $index++) {
        [Runtime.InteropServices.Marshal]::WriteIntPtr($vector, [IntPtr]::Size * $index, $strings[$index])
    }
    [Runtime.InteropServices.Marshal]::WriteIntPtr($vector, [IntPtr]::Size * $strings.Count, [IntPtr]::Zero)
    $result = [BlueChainNativeMono]::mono_main($strings.Count, $vector)
    if ($result -ne 0) { throw "Blue Chain's Mono smoke test failed with exit code $result." }
} finally {
    foreach ($pointer in $strings) { [Runtime.InteropServices.Marshal]::FreeHGlobal($pointer) }
    [Runtime.InteropServices.Marshal]::FreeHGlobal($vector)
}
