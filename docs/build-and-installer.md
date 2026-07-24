# Збірка та інсталятор (T-5.6)

Цей документ описує, як зібрати реліз Family Tree і створити інсталятор для Windows.

## Вимоги

- **.NET 10 SDK** (`dotnet` доступний у PATH).
- **Inno Setup 6** — лише для збірки інсталятора: <https://jrsoftware.org/isinfo.php>.

## 1. Публікація одним файлом

Застосунок публікується як **один самодостатній `FamilyTree.exe`** із вбудованим .NET-рантаймом (окремий .NET на машині користувача не потрібен). Параметри задано у профілі `FamilyTree.App/Properties/PublishProfiles/win-x64.pubxml`.

```powershell
dotnet publish FamilyTree.App/FamilyTree.App.csproj -c Release /p:PublishProfile=win-x64
```

Результат: `FamilyTree.App\bin\Release\net10.0-windows\publish\win-x64\FamilyTree.exe`.

Цей `.exe` уже можна запускати або роздавати «портативно», без інсталятора.

## 2. Інсталятор (Inno Setup)

Скрипт `installer/FamilyTree.iss` пакує опублікований вивід у `FamilyTree-<версія>-setup.exe`:

- ярлики в меню «Пуск» і (за бажанням) на робочому столі;
- **асоціація розширення `.familytree`** — подвійний клік по файлу відкриває його в застосунку (обробку аргументу командного рядка реалізовано в `App.OnStartup`);
- деінсталятор із коректним прибиранням записів реєстру;
- встановлення **без прав адміністратора** (per-user) за замовчуванням; користувач може обрати встановлення для всіх (тоді потрібні права адміністратора).

### Швидкий шлях

Один скрипт робить і публікацію, і інсталятор:

```powershell
powershell -ExecutionPolicy Bypass -File installer\build-installer.ps1
```

Готовий інсталятор з'явиться в `installer\output\`.

### Вручну

```powershell
dotnet publish FamilyTree.App/FamilyTree.App.csproj -c Release /p:PublishProfile=win-x64
& "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe" installer\FamilyTree.iss
```

## Версія

Версію застосунку задано у `FamilyTree.App/FamilyTree.App.csproj` (`<Version>`), і вона ж показується у вікні «Про програму». Оновлюючи реліз, зміни `<Version>` там і `#define AppVersion` у `installer/FamilyTree.iss`.

## Іконка (необов'язково)

Для власної іконки `.exe` та інсталятора додай `.ico` і пропиши `<ApplicationIcon>` у csproj та `SetupIconFile` у `.iss`. Без цього використовується стандартна іконка.
