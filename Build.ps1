param([switch]$SelfContained)
$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try {
    $includeRuntime = $SelfContained.IsPresent.ToString().ToLowerInvariant()
    dotnet publish App/VideoNR.csproj -c Release -r win-x64 --self-contained $includeRuntime -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=$includeRuntime -p:DebugType=none -o build
    if ($LASTEXITCODE -ne 0) { throw 'Falha ao publicar o aplicativo.' }
} finally {
    Pop-Location
}
