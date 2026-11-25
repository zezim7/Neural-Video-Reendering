param([string]$Compiler = 'g++.exe', [switch]$SelfContained)
$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try {
    & $Compiler -std=c++20 -O2 -static -municode -DUNICODE -D_UNICODE -Iupstream/include Engine.cpp upstream/src/engine/EffectChain.cpp -o Engine.exe
    if ($LASTEXITCODE -ne 0) { throw 'Falha ao compilar o adaptador do motor.' }
    $includeRuntime = $SelfContained.IsPresent.ToString().ToLowerInvariant()
    dotnet publish App/VideoNR.csproj -c Release -r win-x64 --self-contained $includeRuntime -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=$includeRuntime -p:DebugType=none -o build
    if ($LASTEXITCODE -ne 0) { throw 'Falha ao publicar o aplicativo.' }
} finally {
    Pop-Location
}
