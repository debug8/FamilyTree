; Inno Setup скрипт для Family Tree (T-5.6).
; Збирає інсталятор із опублікованого одного файлу FamilyTree.exe.
;
; Перед компіляцією опублікуй застосунок:
;   dotnet publish FamilyTree.App/FamilyTree.App.csproj -c Release /p:PublishProfile=win-x64
; або скористайся installer\build-installer.ps1 (робить і публікацію, і інсталятор).
;
; Потрібен Inno Setup 6: https://jrsoftware.org/isinfo.php

#define AppName "Family Tree"
#define AppVersion "0.9.1"
#define AppPublisher "Family Tree"
#define AppExeName "FamilyTree.exe"
; Шлях до результату публікації (відносно цього .iss-файла).
#define PublishDir "..\FamilyTree.App\bin\Release\net10.0-windows\publish\win-x64"

[Setup]
; Унікальний ідентифікатор застосунку (не змінювати між версіями).
AppId={{7B2F1E64-2C3A-4D5E-9F10-A1B2C3D4E5F6}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
WizardStyle=modern
DefaultDirName={autopf}\FamilyTree
DefaultGroupName=Family Tree
DisableProgramGroupPage=yes
UninstallDisplayIcon={app}\{#AppExeName}
OutputDir=output
OutputBaseFilename=FamilyTree-{#AppVersion}-setup
Compression=lzma2
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
; Дозволяємо встановлення без прав адміністратора (per-user); користувач може обрати інше.
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog

[Languages]
Name: "uk"; MessagesFile: "compiler:Languages\Ukrainian.isl"
Name: "en"; MessagesFile: "compiler:Default.isl"

[CustomMessages]
uk.AssocDesc=Пов'язати файли .familytree із застосунком
en.AssocDesc=Associate .familytree files with the application
uk.AssocGroup=Асоціації файлів:
en.AssocGroup=File associations:

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "associate"; Description: "{cm:AssocDesc}"; GroupDescription: "{cm:AssocGroup}"

[Files]
Source: "{#PublishDir}\{#AppExeName}"; DestDir: "{app}"; Flags: ignoreversion
; Решта файлів публікації (напр. нативні бібліотеки, які не вбудувалися в один файл), якщо є.
Source: "{#PublishDir}\*"; DestDir: "{app}"; Excludes: "*.pdb"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\Family Tree"; Filename: "{app}\{#AppExeName}"
Name: "{group}\{cm:UninstallProgram,Family Tree}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\Family Tree"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Registry]
; Асоціація розширення .familytree із застосунком (подвійний клік відкриває файл).
; HKA = HKLM при адмін-встановленні або HKCU при per-user.
Root: HKA; Subkey: "Software\Classes\.familytree"; ValueType: string; ValueName: ""; ValueData: "FamilyTree.Document"; Flags: uninsdeletevalue; Tasks: associate
Root: HKA; Subkey: "Software\Classes\FamilyTree.Document"; ValueType: string; ValueName: ""; ValueData: "Family Tree document"; Flags: uninsdeletekey; Tasks: associate
Root: HKA; Subkey: "Software\Classes\FamilyTree.Document\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: "{app}\{#AppExeName},0"; Tasks: associate
Root: HKA; Subkey: "Software\Classes\FamilyTree.Document\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#AppExeName}"" ""%1"""; Tasks: associate

[Run]
Filename: "{app}\{#AppExeName}"; Description: "{cm:LaunchProgram,Family Tree}"; Flags: nowait postinstall skipifsilent
