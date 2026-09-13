# Build / Runtime troubleshooting

## 1. .NET SDK를 찾을 수 없음

RoiLingo 1.3.0은 특정 `global.json` SDK 버전을 고정하지 않습니다. 프로젝트는 `net8.0-windows`를 대상으로 하며 .NET SDK 8 이상을 사용합니다.

확인:

```bat
dotnet --list-sdks
dotnet --version
```

SDK가 없거나 8보다 낮으면:

```bat
winget install --id Microsoft.DotNet.SDK.8 -e
```

Visual Studio를 사용한다면 Visual Studio Installer에서 **.NET desktop development** workload도 설치되어 있어야 합니다.

## 2. WebView2 Runtime 오류

증상 예:

```text
Microsoft Edge WebView2 Runtime이 설치되어 있지 않습니다.
```

일반 권한 터미널에서:

```bat
winget install --id Microsoft.EdgeWebView2Runtime -e
```

설치 후 RoiLingo를 다시 시작합니다.

## 3. 번역 탭은 보이는데 자동 결과를 못 읽음

Papago / Google Translate / DeepL은 외부 웹사이트이므로 다음이 원인일 수 있습니다.

- 쿠키/동의 화면
- 로그인 화면
- CAPTCHA / 봇 방지
- 사이트 UI/DOM 변경
- 일시적인 네트워크 오류

먼저 **번역 웹 / 교차 검증** 탭에서 해당 사이트가 정상적으로 번역되는지 직접 확인합니다. 사이트 자체는 정상인데 RoiLingo만 결과를 읽지 못하면 `src/RobloxLiveTranslator/Translation/Web/WebTranslationScripts.cs`의 해당 사이트 result selector가 변경된 것입니다.

한 사이트가 실패해도 다른 활성 Provider 결과가 있으면 번역은 계속됩니다.

## 4. Tesseract OCR 모델 다운로드 실패

RoiLingo는 첫 사용 시 필요한 `traineddata` 파일을 다운로드합니다. 회사/학교 네트워크나 보안 프로그램이 GitHub raw 다운로드를 차단할 수 있습니다.

저장 위치:

```text
%LocalAppData%\RobloxLiveTranslator\tessdata\
```

영어만 사용할 경우 최소 `eng.traineddata`가 필요합니다.

## 5. 빌드 확인

프로젝트 루트에서:

```bat
scripts\verify.cmd
```

또는:

```bat
dotnet restore RobloxLiveTranslator.sln
dotnet build RobloxLiveTranslator.sln -c Release -p:Platform=x64
```

NuGet restore 오류가 나면 인터넷 연결과 nuget.org 접근 가능 여부를 먼저 확인합니다. RoiLingo 1.3.0은 `Microsoft.Web.WebView2`, `Tesseract`, `System.Drawing.Common`, `System.Security.Cryptography.ProtectedData` 패키지를 사용합니다.

## 6. github-bootstrap.cmd가 바로 닫힘

1.3.0부터 `github-bootstrap.cmd`에는 복잡한 로직이 없습니다. PowerShell publisher를 실행하고 마지막에 `pause`합니다.

직접 실행하여 정확한 오류를 보려면:

```bat
powershell -NoLogo -NoProfile -ExecutionPolicy Bypass -File scripts\github-publish.ps1
```

필요 도구는 스크립트가 `winget`으로 설치를 시도합니다.

- Git: `Git.Git`
- .NET SDK: `Microsoft.DotNet.SDK.8`
- GitHub CLI: `GitHub.cli`

GitHub 인증이 없으면 `gh auth login --web --git-protocol https`가 시작됩니다.

## 7. 로그 위치

실행/번역 문제를 조사할 때 다음 폴더를 확인합니다.

```text
%LocalAppData%\RobloxLiveTranslator\logs\
%LocalAppData%\RobloxLiveTranslator\history\
```

앱의 **번역 기록 > 로그 폴더 열기** 버튼으로 바로 열 수 있습니다.


## GitHub 게시 중 `fatal: Needed a single revision`

1.2.0의 publisher는 새로 `git init`한 저장소에서 아직 첫 commit이 없는 상태를 확인하려고 `git rev-parse --verify HEAD`를 실행했습니다. Git 자체로는 정상적인 "HEAD 없음" 응답이지만, Windows PowerShell의 `$ErrorActionPreference = "Stop"` 환경에서는 stderr가 `NativeCommandError`로 승격되어 스크립트가 중단될 수 있었습니다.

1.3.0은 실패가 정상인 명령을 실행하지 않고 `.git/refs/heads/main` 또는 packed refs로 첫 commit 존재 여부를 판단합니다. 저장소/태그/Release 존재 확인처럼 비정상 종료 코드가 예상되는 probe도 별도 helper에서 처리합니다.

이전 실행으로 `.git` 폴더만 생긴 상태여도 삭제할 필요 없이 1.3.0의 `github-bootstrap.cmd`를 다시 실행하면 됩니다.
