Unicode True
RequestExecutionLevel user
SetCompressor /SOLID lzma
SetCompressorDictSize 64

!include "MUI2.nsh"
!include "nsDialogs.nsh"
!include "LogicLib.nsh"

!define PRODUCT_NAME "文件快速归档"
!define PRODUCT_VERSION "0.5.3"
!define PRODUCT_EXE "文件快速归档.exe"
!define PRODUCT_KEY "Software\FileQuickArchive"
!define UNINSTALL_KEY "Software\Microsoft\Windows\CurrentVersion\Uninstall\FileQuickArchive"
!define PROJECT_ROOT "${__FILEDIR__}\.."
!define SOURCE_DIR "${PROJECT_ROOT}\artifacts\FileQuickArchive-0.5.3-win-x64"

Name "${PRODUCT_NAME} ${PRODUCT_VERSION}"
OutFile "${PROJECT_ROOT}\artifacts\文件快速归档-${PRODUCT_VERSION}-安装程序.exe"
InstallDir "$LOCALAPPDATA\Programs\FileQuickArchive"
InstallDirRegKey HKCU "${PRODUCT_KEY}" "InstallDir"
BrandingText "${PRODUCT_NAME}"
ShowInstDetails show
ShowUninstDetails show

Icon "${PROJECT_ROOT}\src\GongSiOrganizer.RadialPrototype\Assets\GongSiOrganizer.ico"
UninstallIcon "${PROJECT_ROOT}\src\GongSiOrganizer.RadialPrototype\Assets\GongSiOrganizer.ico"

VIProductVersion "0.5.3.0"
VIAddVersionKey /LANG=2052 "ProductName" "${PRODUCT_NAME}"
VIAddVersionKey /LANG=2052 "FileDescription" "${PRODUCT_NAME} 安装程序"
VIAddVersionKey /LANG=2052 "FileVersion" "${PRODUCT_VERSION}"
VIAddVersionKey /LANG=2052 "ProductVersion" "${PRODUCT_VERSION}"
VIAddVersionKey /LANG=2052 "LegalCopyright" "Copyright © 2026"

!define MUI_ICON "${PROJECT_ROOT}\src\GongSiOrganizer.RadialPrototype\Assets\GongSiOrganizer.ico"
!define MUI_UNICON "${PROJECT_ROOT}\src\GongSiOrganizer.RadialPrototype\Assets\GongSiOrganizer.ico"
!define MUI_WELCOMEFINISHPAGE_BITMAP "${__FILEDIR__}\assets\wizard-side.bmp"
!define MUI_UNWELCOMEFINISHPAGE_BITMAP "${__FILEDIR__}\assets\wizard-side.bmp"
!define MUI_HEADERIMAGE
!define MUI_HEADERIMAGE_RIGHT
!define MUI_HEADERIMAGE_BITMAP "${__FILEDIR__}\assets\wizard-header.bmp"
!define MUI_ABORTWARNING
!define MUI_FINISHPAGE_RUN "$INSTDIR\${PRODUCT_EXE}"
!define MUI_FINISHPAGE_RUN_TEXT "启动${PRODUCT_NAME}"
!define MUI_FINISHPAGE_LINK "查看使用说明"
!define MUI_FINISHPAGE_LINK_LOCATION "$INSTDIR\使用说明.txt"

Var DesktopCheckbox
Var StartupCheckbox
Var DesktopState
Var StartupState

!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_DIRECTORY
Page custom OptionsPageCreate OptionsPageLeave
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH

!insertmacro MUI_UNPAGE_WELCOME
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_UNPAGE_FINISH

!insertmacro MUI_LANGUAGE "SimpChinese"

Function .onInit
  StrCpy $DesktopState ${BST_CHECKED}
  StrCpy $StartupState ${BST_UNCHECKED}
  ${If} ${FileExists} "$SMSTARTUP\${PRODUCT_NAME}.lnk"
    StrCpy $StartupState ${BST_CHECKED}
  ${ElseIf} ${FileExists} "$SMSTARTUP\公司文件轮盘整理.lnk"
    StrCpy $StartupState ${BST_CHECKED}
  ${EndIf}
  CheckRunning:
  System::Call 'kernel32::OpenMutexW(i 0x00100000, i 0, w "Local\GongSiOrganizer.RadialPrototype.v1") p.r0'
  StrCmp $0 0 NotRunning
  System::Call 'kernel32::CloseHandle(p r0)'
  MessageBox MB_RETRYCANCEL|MB_ICONEXCLAMATION "检测到${PRODUCT_NAME}正在运行。$\r$\n请先右击托盘图标并选择“退出文件快速归档”，然后点击“重试”。" IDRETRY CheckRunning IDCANCEL CancelInstall
  CancelInstall:
    Abort
  NotRunning:
FunctionEnd

Function un.onInit
  CheckUninstallRunning:
  System::Call 'kernel32::OpenMutexW(i 0x00100000, i 0, w "Local\GongSiOrganizer.RadialPrototype.v1") p.r0'
  StrCmp $0 0 UninstallNotRunning
  System::Call 'kernel32::CloseHandle(p r0)'
  MessageBox MB_RETRYCANCEL|MB_ICONEXCLAMATION "${PRODUCT_NAME}仍在运行。$\r$\n请先从托盘退出程序，然后点击“重试”。" IDRETRY CheckUninstallRunning IDCANCEL CancelUninstall
  CancelUninstall:
    Abort
  UninstallNotRunning:
FunctionEnd

Function OptionsPageCreate
  !insertmacro MUI_HEADER_TEXT "安装选项" "选择快捷方式和启动方式。"
  nsDialogs::Create 1018
  Pop $0
  ${If} $0 == error
    Abort
  ${EndIf}

  ${NSD_CreateLabel} 0 4u 100% 28u "你可以稍后删除这些快捷方式，不会影响程序本身。"
  Pop $0

  ${NSD_CreateCheckbox} 8u 42u 92% 18u "创建桌面快捷方式"
  Pop $DesktopCheckbox
  ${NSD_Check} $DesktopCheckbox

  ${NSD_CreateCheckbox} 8u 72u 92% 18u "开机时自动启动${PRODUCT_NAME}"
  Pop $StartupCheckbox
  ${If} $StartupState == ${BST_CHECKED}
    ${NSD_Check} $StartupCheckbox
  ${EndIf}

  ${NSD_CreateLabel} 28u 96u 88% 30u "开机自启默认不强制开启；勾选后会在当前用户登录 Windows 时启动。"
  Pop $0
  SetCtlColors $0 667588 transparent

  nsDialogs::Show
FunctionEnd

Function OptionsPageLeave
  ${NSD_GetState} $DesktopCheckbox $DesktopState
  ${NSD_GetState} $StartupCheckbox $StartupState
FunctionEnd

Section "安装主程序" MainSection
  SetOutPath "$INSTDIR"
  File /oname=${PRODUCT_EXE} "${SOURCE_DIR}\GongSiOrganizer.RadialPrototype.exe"
  File /oname=使用说明.txt "${SOURCE_DIR}\使用说明.txt"
  File /oname=应用图标.ico "${SOURCE_DIR}\应用图标.ico"
  WriteUninstaller "$INSTDIR\卸载${PRODUCT_NAME}.exe"

  WriteRegStr HKCU "${PRODUCT_KEY}" "InstallDir" "$INSTDIR"
  WriteRegStr HKCU "${UNINSTALL_KEY}" "DisplayName" "${PRODUCT_NAME}"
  WriteRegStr HKCU "${UNINSTALL_KEY}" "DisplayVersion" "${PRODUCT_VERSION}"
  WriteRegStr HKCU "${UNINSTALL_KEY}" "DisplayIcon" "$INSTDIR\${PRODUCT_EXE}"
  WriteRegStr HKCU "${UNINSTALL_KEY}" "InstallLocation" "$INSTDIR"
  WriteRegStr HKCU "${UNINSTALL_KEY}" "UninstallString" "$\"$INSTDIR\卸载${PRODUCT_NAME}.exe$\""
  WriteRegStr HKCU "${UNINSTALL_KEY}" "QuietUninstallString" "$\"$INSTDIR\卸载${PRODUCT_NAME}.exe$\" /S"
  WriteRegDWORD HKCU "${UNINSTALL_KEY}" "NoModify" 1
  WriteRegDWORD HKCU "${UNINSTALL_KEY}" "NoRepair" 1

  CreateDirectory "$SMPROGRAMS\${PRODUCT_NAME}"
  CreateShortcut "$SMPROGRAMS\${PRODUCT_NAME}\${PRODUCT_NAME}.lnk" "$INSTDIR\${PRODUCT_EXE}" "" "$INSTDIR\${PRODUCT_EXE}" 0
  CreateShortcut "$SMPROGRAMS\${PRODUCT_NAME}\卸载${PRODUCT_NAME}.lnk" "$INSTDIR\卸载${PRODUCT_NAME}.exe"

  Delete "$DESKTOP\${PRODUCT_NAME}.lnk"
  Delete "$DESKTOP\公司文件轮盘整理.lnk"
  ${If} $DesktopState == ${BST_CHECKED}
    CreateShortcut "$DESKTOP\${PRODUCT_NAME}.lnk" "$INSTDIR\${PRODUCT_EXE}" "" "$INSTDIR\${PRODUCT_EXE}" 0
  ${EndIf}

  Delete "$SMSTARTUP\${PRODUCT_NAME}.lnk"
  Delete "$SMSTARTUP\公司文件轮盘整理.lnk"
  ${If} $StartupState == ${BST_CHECKED}
    CreateShortcut "$SMSTARTUP\${PRODUCT_NAME}.lnk" "$INSTDIR\${PRODUCT_EXE}" "" "$INSTDIR\${PRODUCT_EXE}" 0
  ${EndIf}
SectionEnd

Section "Uninstall"
  Delete "$DESKTOP\${PRODUCT_NAME}.lnk"
  Delete "$SMSTARTUP\${PRODUCT_NAME}.lnk"
  Delete "$SMPROGRAMS\${PRODUCT_NAME}\${PRODUCT_NAME}.lnk"
  Delete "$SMPROGRAMS\${PRODUCT_NAME}\卸载${PRODUCT_NAME}.lnk"
  RMDir "$SMPROGRAMS\${PRODUCT_NAME}"

  Delete "$INSTDIR\${PRODUCT_EXE}"
  Delete "$INSTDIR\使用说明.txt"
  Delete "$INSTDIR\应用图标.ico"
  Delete "$INSTDIR\卸载${PRODUCT_NAME}.exe"
  RMDir "$INSTDIR"

  DeleteRegKey HKCU "${UNINSTALL_KEY}"
  DeleteRegKey HKCU "${PRODUCT_KEY}"
SectionEnd
