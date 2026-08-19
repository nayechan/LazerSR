# 작업 마무리 체크리스트

## 1. 배포 (Inno Setup)

```powershell
dotnet publish "LazerSR.Launcher\LazerSR.Launcher.csproj" -c Release
```

출력: `LazerSR.Launcher\bin\Release\net8.0-windows\win-x64\publish\`

배포할 새 버전이 있으면:
1. `installer\LazerSRClean.iss` 2번째 줄 `MyAppVersion` 값을 올린다 (예: `4.1.0` → `4.2.0`)
2. Inno Setup Compiler(ISCC)로 컴파일 — 설치 위치: `C:\Users\shins\AppData\Local\Programs\Inno Setup 6\ISCC.exe` (PATH에 없음, 매번 전체 경로로 호출):
   ```powershell
   & "C:\Users\shins\AppData\Local\Programs\Inno Setup 6\ISCC.exe" "C:\dev\lazerSR\lazerSRClean\installer\LazerSRClean.iss"
   ```
3. 결과물: `installer\output\LazerSR-v{version}-Setup.exe`

**주의**: `AppId`는 절대 바꾸지 않는다 (업그레이드 인식용, 최초 LazerSR과 동일 유지). `[Files]` 목록에 새 의존 DLL을 추가했다면(`architecture.md` §8) `.iss`의 `[Files]` 섹션도 같이 갱신.

## 2. progress 로그 작성

`progress\YYYY-MM-DD.md` (오늘 날짜, 없으면 생성)에 간결하게 추가:

```
- ~~무엇~~ 구현/생성/수정/삭제
```

설명은 최소화 — 작업 흐름만 남긴다. (`CLAUDE.md`의 로그 규칙과 동일, 여기 다시 요약된 것뿐)

## 3. 살아있는 문서 갱신 확인

아래 문서들은 **코드/진행상황을 서술**하므로 세션 중 관련 변경이 있었으면 반드시 갱신 여부를 확인한다 — 안 하면 문서가 실제와 어긋나는 문제가 반복된다(2026-07-17 전면 감사의 원인이 바로 이거였음):

| 변경 종류 | 확인할 문서 |
|---|---|
| 패치 추가/제거, 아키텍처 변경 | `docs\guides\architecture.md`, `docs\guides\ui-patching.md` |
| 안전성에 영향 줄 수 있는 변경(네트워크, 파일쓰기, 라이브 인스턴스 접근 등) | `docs\guides\safety.md` |
| 새 기능/계산기/위젯 패턴 추가 | `docs\guides\feature-development.md` |
| 폴더/파일 구조 변경 | `docs\INDEX.md` |
| 새 위치에 sunnySR pill 추가 | `docs\sibling-clone-position-map.md` (상태 표 갱신) |
| replay-compare 관련 작업 | `docs\replay-compare-widget-plan.md` |

## 4. 대기

각 작업 단위 완료 후 사용자 명령 대기 (`CLAUDE.md` 워크플로 규칙).
