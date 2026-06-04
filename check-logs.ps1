# Script para verificar logs do BepInEx após rodar o jogo
# Procura por entradas do QuickPocket no log

$logPath = "C:\Users\Hiarly\AppData\Roaming\r2modmanPlus-local\REPO\profiles\Default\BepInEx\LogOutput.log"

if (-not (Test-Path $logPath)) {
    Write-Host "Log não encontrado em: $logPath" -ForegroundColor Red
    Write-Host "Verifique se o jogo foi executado pelo r2modman."
    exit 1
}

Write-Host "=== QuickPocket — Entradas de Log ===" -ForegroundColor Cyan
Get-Content $logPath | Where-Object { $_ -match "\[QuickPocket\]" } | ForEach-Object {
    if ($_ -match "ERROR|Exception") {
        Write-Host $_ -ForegroundColor Red
    } elseif ($_ -match "Warning|WARN") {
        Write-Host $_ -ForegroundColor Yellow
    } else {
        Write-Host $_ -ForegroundColor Green
    }
}

Write-Host "`n=== Verificações esperadas na Fase 1 ===" -ForegroundColor Cyan
Write-Host "[ ] v1.0.0 inicializado"
Write-Host "[ ] PocketManager adicionado ao PlayerAvatar local"
Write-Host "[ ] Host publicou limite (se host)"
Write-Host "[ ] Limite efetivo calculado (se entrou na sala)"
Write-Host "[ ] HUD aparece com valor correto ou 'syncing...'"
