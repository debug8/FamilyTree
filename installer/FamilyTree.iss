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

; ── Вигляд вікна майстра ─────────────────────────────────────────────
WizardStyle=modern
; Іконка самого setup.exe.
SetupIconFile=..\FamilyTree.App\Resources\app-icon.ico
; Боковий баннер на екранах вітання та завершення (кілька розмірів під різні DPI).
WizardImageFile=assets\wizard-image.bmp,assets\wizard-image-125.bmp,assets\wizard-image-150.bmp,assets\wizard-image-200.bmp
; Логотип у шапці решти екранів.
WizardSmallImageFile=assets\wizard-small.bmp,assets\wizard-small-125.bmp,assets\wizard-small-150.bmp,assets\wizard-small-200.bmp
WizardImageStretch=yes
; Показуємо екран вітання — інакше баннер не було б видно.
DisableWelcomePage=no
; Графіку генерує installer\make-wizard-images.py (палітра #82C596 → #15556B).
; ─────────────────────────────────────────────────────────────────────

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

[Code]
{ ─── Кольори вікна майстра ────────────────────────────────────────────
  Inno Setup не має директив для кольорів, тому перефарбовуємо контроли
  вручну. Увага: TColor записується як $00BBGGRR (байти навпаки від HTML).
  Палітра: #82C596 (зелений акцент) та #15556B (глибокий синій). }
const
  ColPageBg   = $ECF2E9;  { #E9F2EC — мʼятний фон сторінок }
  ColHeaderBg = $FFFFFF;  { білий фон шапки з логотипом    }
  ColHeading  = $6B5515;  { #15556B — заголовки            }
  ColMuted    = $756E5A;  { #5A6E75 — приглушений підпис   }
  ColBody     = $443A1E;  { #1E3A44 — основний текст       }

{ Рекурсивно проходить усі контроли й задає колір тексту. }
procedure StyleTextControls(Parent: TWinControl);
var
  I: Integer;
  C: TControl;
begin
  for I := 0 to Parent.ControlCount - 1 do
  begin
    C := Parent.Controls[I];
    if C is TNewStaticText then
      TNewStaticText(C).Font.Color := ColBody
    else if C is TNewCheckBox then
      TNewCheckBox(C).Font.Color := ColBody
    else if C is TNewRadioButton then
      TNewRadioButton(C).Font.Color := ColBody;
    if C is TWinControl then
      StyleTextControls(TWinControl(C));
  end;
end;

procedure InitializeWizard;
begin
  { Фон вікна та сторінок. }
  WizardForm.Color := ColPageBg;
  WizardForm.MainPanel.Color := ColHeaderBg;

  WizardForm.WelcomePage.Color := ColPageBg;
  WizardForm.InnerPage.Color := ColPageBg;
  WizardForm.LicensePage.Color := ColPageBg;
  WizardForm.PasswordPage.Color := ColPageBg;
  WizardForm.InfoBeforePage.Color := ColPageBg;
  WizardForm.UserInfoPage.Color := ColPageBg;
  WizardForm.SelectDirPage.Color := ColPageBg;
  WizardForm.SelectComponentsPage.Color := ColPageBg;
  WizardForm.SelectProgramGroupPage.Color := ColPageBg;
  WizardForm.SelectTasksPage.Color := ColPageBg;
  WizardForm.ReadyPage.Color := ColPageBg;
  WizardForm.PreparingPage.Color := ColPageBg;
  WizardForm.InstallingPage.Color := ColPageBg;
  WizardForm.InfoAfterPage.Color := ColPageBg;
  WizardForm.FinishedPage.Color := ColPageBg;

  { Колір тексту для всіх підписів і чекбоксів. }
  StyleTextControls(WizardForm);

  { Заголовки — фірменним синім. }
  WizardForm.PageNameLabel.Font.Color := ColHeading;
  WizardForm.PageDescriptionLabel.Font.Color := ColMuted;

  WizardForm.WelcomeLabel1.Font.Color := ColHeading;
  WizardForm.WelcomeLabel1.Font.Size := 13;
  WizardForm.WelcomeLabel2.Font.Color := ColBody;

  WizardForm.FinishedHeadingLabel.Font.Color := ColHeading;
  WizardForm.FinishedHeadingLabel.Font.Size := 13;
  WizardForm.FinishedLabel.Font.Color := ColBody;

  { Прибираємо роздільні лінії — вигляд стає пласким і сучаснішим.
    Якщо хочеться повернути лінії, закоментуй два рядки нижче. }
  if Assigned(WizardForm.Bevel) then WizardForm.Bevel.Visible := False;
  if Assigned(WizardForm.Bevel1) then WizardForm.Bevel1.Visible := False;
end;

{ Те саме для вікна видалення програми. }
procedure InitializeUninstallProgressForm;
begin
  UninstallProgressForm.Color := ColPageBg;
  UninstallProgressForm.MainPanel.Color := ColHeaderBg;
  UninstallProgressForm.InnerPage.Color := ColPageBg;
  UninstallProgressForm.InstallingPage.Color := ColPageBg;
  UninstallProgressForm.PageNameLabel.Font.Color := ColHeading;
  UninstallProgressForm.PageDescriptionLabel.Font.Color := ColMuted;
  StyleTextControls(UninstallProgressForm);
  if Assigned(UninstallProgressForm.Bevel) then UninstallProgressForm.Bevel.Visible := False;
  if Assigned(UninstallProgressForm.Bevel1) then UninstallProgressForm.Bevel1.Visible := False;
end;
