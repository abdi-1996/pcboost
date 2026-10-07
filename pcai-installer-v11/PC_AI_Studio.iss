[Setup]
AppId={{C53A9A1E-0E8A-4F7D-9D14-86C9E9F91D01}
AppName=PC AI Studio
AppVersion=1.1.0
AppPublisher=PC AI Studio
DefaultDirName={localappdata}\PC AI Studio Installer
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputDir=out
OutputBaseFilename=PC_AI_Studio_Setup_v1.1_x64
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
Uninstallable=no

[Files]
Source: "payload\*"; DestDir: "{tmp}\PCAIStudioPayload"; Flags: recursesubdirs createallsubdirs deleteafterinstall

[Run]
Filename: "{sys}\WindowsPowerShell\v1.0\powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{tmp}\PCAIStudioPayload\install.ps1"""; Flags: waituntilterminated

[Code]
procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssDone then
  begin
    if FileExists(ExpandConstant('{userdesktop}\PC AI Studio.lnk')) then
      MsgBox('PC AI Studio установлен. Ярлык создан на рабочем столе.', mbInformation, MB_OK)
    else
      MsgBox('Установка завершена, но ярлык не найден. Откройте меню Пуск -> PC AI Studio.', mbError, MB_OK);
  end;
end;