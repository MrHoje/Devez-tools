; DevezTools 설치 파일. deploy.ps1 이 /DAppVersion=x.y.z 로 csproj 버전을 넘겨 컴파일한다.
#ifndef AppVersion
  #error AppVersion 이 없습니다. deploy.ps1 로 컴파일하십시오.
#endif
#define AppName     "DevezTools"
#define AppExeName  "devezTools.exe"
#define AppPublisher "Devez"

[Setup]
AppId={{B6F2D9A4-5C1E-4E7B-9A3D-8F2C1B7E6D40}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
; 자동 업데이트가 관리자 권한 없이 이 설치 파일을 다시 실행하므로 사용자 폴더에만 설치한다
DefaultDirName={localappdata}\{#AppName}
DisableProgramGroupPage=yes
DisableDirPage=yes
OutputDir=..\artifacts\installer
OutputBaseFilename=DevezTools_Setup_{#AppVersion}
; SetupIconFile 은 넣지 않는다: 아이콘 리소스를 고치는 단계에서 백신이 출력 파일을 잡아 컴파일이 실패한다(오류 110)
Compression=lzma
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest
UninstallDisplayIcon={app}\{#AppExeName}
UninstallDisplayName={#AppName}
CloseApplications=yes
RestartApplications=no
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

[Languages]
Name: "korean"; MessagesFile: "compiler:Languages\Korean.isl"

[Tasks]
Name: "desktopicon"; Description: "바탕 화면에 바로가기 만들기"; GroupDescription: "추가 작업:"

[Files]
; publish 결과물 전부(exe + OCR 네이티브 라이브러리·모델 + 글꼴 라이선스)
Source: "..\artifacts\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExeName}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExeName}"; Description: "{#AppName} 실행"; Flags: nowait postinstall skipifsilent
; 앱의 자동 업데이트(/VERYSILENT)로 설치했으면 끝난 뒤 앱을 다시 띄운다
Filename: "{app}\{#AppExeName}"; Parameters: "--updated"; Flags: nowait; Check: WizardSilent

[Code]
function IsDotNet10Installed: Boolean;
var
  Path: String;
  Names: TArrayOfString;
  i: Integer;
  FindRec: TFindRec;
begin
  Result := False;
  // .NET 호스트는 64비트 OS에서도 32비트 레지스트리 뷰에 기록하므로 HKLM32 를 먼저 본다.
  Path := 'SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.WindowsDesktop.App';
  if not RegGetValueNames(HKLM32, Path, Names) then
    RegGetValueNames(HKLM64, Path, Names);
  for i := 0 to GetArrayLength(Names) - 1 do
    if Copy(Names[i], 1, 3) = '10.' then
    begin
      Result := True;
      Exit;
    end;

  // 폴백: 공유 런타임 폴더 직접 확인
  if FindFirst(ExpandConstant('{commonpf64}\dotnet\shared\Microsoft.WindowsDesktop.App\10.*'), FindRec) then
  begin
    FindClose(FindRec);
    Result := True;
  end;
end;

function InitializeSetup: Boolean;
var
  Dummy: Integer;
begin
  Result := True;
  if not IsDotNet10Installed then
  begin
    if MsgBox(
      '.NET 10 Desktop Runtime이 설치되어 있지 않습니다.' + #13#10#13#10 +
      'DevezTools를 실행하려면 .NET 10 Desktop Runtime이 필요합니다.' + #13#10 +
      '지금 다운로드 페이지를 열고 설치를 중단할까요?',
      mbConfirmation, MB_YESNO) = IDYES then
    begin
      ShellExec('open', 'https://dotnet.microsoft.com/ko-kr/download/dotnet/10.0', '', '', SW_SHOWNORMAL, ewNoWait, Dummy);
      Result := False;
    end;
  end;
end;
