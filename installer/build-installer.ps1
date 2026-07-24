<#
    Публікує Family Tree одним самодостатнім файлом і збирає інсталятор через Inno Setup.

    Використання (з кореня репозиторію або будь-звідки):
        powershell -ExecutionPolicy Bypass -File installer\build-installer.ps1

    Вимоги:
        - .NET 10 SDK (dotnet у PATH)
        - Inno Setup 6 (ISCC.exe)
#>
$ErrorActionPreference = 'Stop'

# Корінь репозиторію = батьківська тека цього скрипта (installer\..).
$repoRoot = Split-Path -Parent $PSScriptRoot
Push-Location $repoRoot
try {
    Write-Host "==> Публікація (Release, win-x64, один файл)…" -ForegroundColor Cyan
    dotnet publish "FamilyTree.App/FamilyTree.App.csproj" -c Release /p:PublishProfile=win-x64

    # Пошук ISCC.exe (Inno Setup 6) у типових розташуваннях.
    $isccCandidates = @(
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "${env:ProgramFiles}\Inno Setup 6\ISCC.exe"
    )
    $iscc = $isccCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1
    if (-not $iscc) {
        throw "ISCC.exe не знайдено. Встанови Inno Setup 6: https://jrsoftware.org/isinfo.php"
    }

    Write-Host "==> Збірка інсталятора ($iscc)…" -ForegroundColor Cyan
    & $iscc "installer\FamilyTree.iss"

    Write-Host "==> Готово. Інсталятор у installer\output\" -ForegroundColor Green
}
finally {
    Pop-Location
}
