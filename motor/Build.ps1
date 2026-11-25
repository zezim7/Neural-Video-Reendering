param(
    [Parameter(Mandatory)][string]$FfmpegRoot,
    [Parameter(Mandatory)][string]$DlssSdkRoot,
    [Parameter(Mandatory)][string]$OpticalFlowSdkRoot,
    [Parameter(Mandatory)][string]$NvencHeadersRoot,
    [string]$LibDoviRoot
)

$ErrorActionPreference = 'Stop'
$archive = Join-Path $PSScriptRoot 'origem/motor-2.2.0-source.zip'
$expected = 'E0E3C4E940A74AFCCB966A911DB02A5777E1316916CBA777F9310D52BA457BE7'
if ((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash -ne $expected) {
    throw 'O arquivo de código-fonte não corresponde à versão registrada.'
}

$output = Join-Path $PSScriptRoot 'build'
$source = Join-Path $output 'source'
Expand-Archive -LiteralPath $archive -DestinationPath $source -Force
if (-not (Test-Path (Join-Path $source 'CMakeLists.txt'))) {
    $source = Get-ChildItem -LiteralPath $source -Directory |
        Where-Object { Test-Path (Join-Path $_.FullName 'CMakeLists.txt') } |
        Select-Object -First 1 -ExpandProperty FullName
}
if (-not $source) { throw 'CMakeLists.txt do motor não encontrado.' }

$build = Join-Path $output 'worker'
$settings = @(
    "-DVIDEONR_ENGINE_SOURCE=$source"
    "-DVEYRA_FFMPEG_ROOT=$((Resolve-Path -LiteralPath $FfmpegRoot).Path)"
    "-DVEYRA_DLSS_SDK_ROOT=$((Resolve-Path -LiteralPath $DlssSdkRoot).Path)"
    "-DVEYRA_NVOF_SDK_ROOT=$((Resolve-Path -LiteralPath $OpticalFlowSdkRoot).Path)"
    "-DVEYRA_NVENC_HEADERS_ROOT=$((Resolve-Path -LiteralPath $NvencHeadersRoot).Path)"
    '-DVEYRA_ENABLE_DEINTERLACE=OFF'
)
if ($LibDoviRoot) {
    $settings += '-DVEYRA_ENABLE_LIBDOVI=ON'
    $settings += "-DVEYRA_LIBDOVI_ROOT=$((Resolve-Path -LiteralPath $LibDoviRoot).Path)"
} else {
    $settings += '-DVEYRA_ENABLE_LIBDOVI=OFF'
}

cmake -S $PSScriptRoot -B $build -G 'Visual Studio 17 2022' -A x64 @settings
if ($LASTEXITCODE -ne 0) { throw 'Falha ao configurar o motor.' }
cmake --build $build --config Release --target VideoNR-RenderWorker --parallel
if ($LASTEXITCODE -ne 0) { throw 'Falha ao compilar o motor.' }
