; Instalador do Capturador para Windows (Inno Setup 6.3+).
; Instala só para o usuário atual, sem pedir administrador.
; Uso: iscc /DAppVersion=1.0.0 /DArch=x64 /DSourceDir=..\..\out\x64 Capturador.iss
; (Arch = x64 ou arm64; SourceDir = pasta do "dotnet publish" dessa arquitetura)

#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#ifndef Arch
  #define Arch "x64"
#endif
#ifndef SourceDir
  #define SourceDir "..\..\out\" + Arch
#endif

[Setup]
AppId={{F3EFD3D5-99A6-4C5C-AD16-E652F4DDD5E3}
AppName=Capturador
AppVersion={#AppVersion}
AppVerName=Capturador {#AppVersion}
AppPublisher=Marcéu Farias
AppCopyright=© 2026 Marcéu Farias
AppPublisherURL=https://github.com/marceuf/Capturador
DefaultDirName={localappdata}\Programs\Capturador
DisableDirPage=yes
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
#if Arch == "arm64"
ArchitecturesAllowed=arm64
ArchitecturesInstallIn64BitMode=arm64
#else
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
#endif
MinVersion=10.0.19045
OutputDir=Output
OutputBaseFilename=Capturador-{#AppVersion}-{#Arch}-setup
SetupIconFile=..\src\Capturador\Assets\Capturador.ico
UninstallDisplayIcon={app}\Capturador.exe
UninstallDisplayName=Capturador
LicenseFile=..\..\LICENSE
WizardStyle=modern
Compression=lzma2/max
SolidCompression=yes
CloseApplications=yes
; O app fica só na bandeja: o instalador reconhece que está aberto por este mutex e pede para fechar.
AppMutex=Capturador-tech.marceu.capturador

[Languages]
Name: "brazilianportuguese"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"

[Files]
Source: "{#SourceDir}\Capturador.exe"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{userprograms}\Capturador"; Filename: "{app}\Capturador.exe"

[Registry]
; Remove a abertura automática ao desinstalar (o app cria esse valor quando o usuário liga a opção).
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueName: "Capturador"; ValueType: none; Flags: uninsdeletevalue dontcreatekey

[Run]
Filename: "{app}\Capturador.exe"; Description: "Abrir o Capturador"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "{sys}\taskkill.exe"; Parameters: "/im Capturador.exe /f"; Flags: runhidden; RunOnceId: "FecharCapturador"
