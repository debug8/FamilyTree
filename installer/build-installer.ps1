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
    Write-Host "==> Публікація (Release, win-x64, один файл)..." -ForegroundColor Cyan
    dotnet publish "FamilyTree.App/FamilyTree.App.csproj" -c Release /p:PublishProfile=win-x64
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish завершився з кодом $LASTEXITCODE." }

    # Захист від найпідлішої помилки: Inno молча пакує те, що лежить у publish-теці.
    # Якщо публікація не відбулася, інсталятор збереться зі старим .exe і оновлення
    # "не спрацює" — версія застосунку залишиться попередньою.
    $publishedExe = "FamilyTree.App\bin\Release\net10.0-windows\publish\win-x64\FamilyTree.exe"
    if (-not (Test-Path $publishedExe)) {
        throw "Не знайдено $publishedExe - публікація не дала результату."
    }
    $exeVersion = (Get-Item $publishedExe).VersionInfo.FileVersion   # напр. 0.9.2.0
    $issMatch = Select-String -Path "installer\FamilyTree.iss" -Pattern '^#define AppVersion "([^"]+)"'
    if (-not $issMatch) { throw "У installer\FamilyTree.iss не знайдено #define AppVersion." }
    $issVersion = $issMatch.Matches[0].Groups[1].Value
    if ($exeVersion -notlike "$issVersion*") {
        Write-Host "Версії розійшлися:" -ForegroundColor Red
        Write-Host "  опублікований .exe : $exeVersion  (з <Version> у FamilyTree.App.csproj)"
        Write-Host "  AppVersion у .iss  : $issVersion"
        throw "Онови #define AppVersion в installer\FamilyTree.iss або <Version> у csproj - і перепублікуй."
    }
    Write-Host "==> Версія збігається: $issVersion (exe $exeVersion)" -ForegroundColor Green

    # Пошук ISCC.exe (Inno Setup 6) у типових розташуваннях.
    $isccCandidates = @(
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "${env:ProgramFiles}\Inno Setup 6\ISCC.exe"
    )
    $iscc = $isccCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1
    if (-not $iscc) {
        throw "ISCC.exe не знайдено. Встанови Inno Setup 6: https://jrsoftware.org/isinfo.php"
    }

    Write-Host "==> Збірка інсталятора ($iscc)..." -ForegroundColor Cyan
    & $iscc "installer\FamilyTree.iss"
    if ($LASTEXITCODE -ne 0) { throw "ISCC завершився з кодом $LASTEXITCODE." }

    Write-Host "==> Готово. Інсталятор у installer\output\" -ForegroundColor Green
}
finally {
    Pop-Location
}
