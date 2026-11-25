param([string]$Compiler = '', [switch]$SelfContained)
$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try {
    $includeRuntime = $SelfContained.IsPresent.ToString().ToLowerInvariant()
    dotnet publish App/VideoNR.csproj -c Release -r win-x64 --self-contained $includeRuntime -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=$includeRuntime -p:DebugType=none -o build
    if ($LASTEXITCODE -ne 0) { throw 'Falha ao publicar o aplicativo.' }

    $useMsvc = $false
    if ([string]::IsNullOrWhiteSpace($Compiler)) {
        if (Get-Command cl.exe -ErrorAction SilentlyContinue) {
            $Compiler = 'cl.exe'
            $useMsvc = $true
        } else {
            $Compiler = 'g++.exe'
        }
    } elseif ($Compiler -match '(^|[\\/])cl(\.exe)?$') {
        $useMsvc = $true
    }

    if ($useMsvc) {
        $objDir = Join-Path $PSScriptRoot 'build\engine-obj'
        New-Item -ItemType Directory -Force -Path $objDir | Out-Null
        try {
            & $Compiler /nologo /std:c++20 /EHsc /O2 /MT /DUNICODE /D_UNICODE /Iupstream/include Engine.cpp upstream/src/engine/EffectChain.cpp "/Fo:$objDir\" /Fe:build\Engine.exe /link kernel32.lib
            if ($LASTEXITCODE -ne 0) { throw 'Falha ao compilar o adaptador do motor com MSVC.' }
        } finally {
            Remove-Item -Recurse -Force -ErrorAction SilentlyContinue $objDir
        }
    } else {
        & $Compiler -std=c++20 -O2 -static -municode -DUNICODE -D_UNICODE -Iupstream/include Engine.cpp upstream/src/engine/EffectChain.cpp -o build/Engine.exe
        if ($LASTEXITCODE -ne 0) { throw 'Falha ao compilar o adaptador do motor.' }
    }
} finally {
    Pop-Location
}
