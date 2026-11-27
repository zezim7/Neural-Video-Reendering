param([Parameter(Mandatory = $true)][string]$Directory)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
foreach ($vendor in 'NVIDIA', 'AMD') {
    $temp = Join-Path ([IO.Path]::GetTempPath()) ("VideoNR-package-" + [Guid]::NewGuid().ToString('N'))
    $app = $null
    try {
        $zip = Resolve-Path (Join-Path $Directory "VideoNR-$vendor-win64.zip")
        [IO.Compression.ZipFile]::ExtractToDirectory($zip.Path, $temp)
        foreach ($item in 'VideoNR.exe', 'Engine.exe', 'README.pt-BR.md', 'docs/screenshots/editor-main.png') {
            if (!(Test-Path (Join-Path $temp $item))) { throw "Arquivo ausente: $item" }
        }
        $app = Start-Process (Join-Path $temp 'VideoNR.exe') -WorkingDirectory $temp -PassThru
        $label = [Windows.Automation.PropertyCondition]::new(
            [Windows.Automation.AutomationElement]::NameProperty, "Pacote $vendor instalado")
        $deadline = [DateTime]::UtcNow.AddSeconds(45)
        $found = $false
        do {
            Start-Sleep -Milliseconds 400
            $app.Refresh()
            if ($app.HasExited) { throw "VideoNR.exe encerrou durante a abertura do pacote $vendor." }
            if ($app.MainWindowHandle -ne [IntPtr]::Zero) {
                $window = [Windows.Automation.AutomationElement]::FromHandle($app.MainWindowHandle)
                $found = $null -ne $window.FindFirst([Windows.Automation.TreeScope]::Descendants, $label)
            }
        } until ($found -or [DateTime]::UtcNow -ge $deadline)
        if (!$found) { throw "A janela não identificou o motor $vendor do ZIP." }
        Write-Host "ZIP $vendor abriu e selecionou o motor instalado."
    } finally {
        if ($app -and !$app.HasExited) {
            [void]$app.CloseMainWindow()
            if (!$app.WaitForExit(5000)) { $app.Kill(); $app.WaitForExit() }
        }
        if (Test-Path $temp) { Remove-Item -Recurse -Force $temp }
    }
}
