# 세션 시작 체크리스트

새 세션 시작 시 아래 순서대로 읽는다.

1. **`C:\dev\lazerSR\lazerSRClean\CLAUDE.md`** — 이미 로드됨(세션 시작 시 자동). 여기서 이 문서로 안내됨.
2. **`docs\INDEX.md`** — 전체 경로 색인. 뭐가 어디 있는지, 뭘 건드리면 안 되는지 여기서 먼저 확인.
3. **`docs\guides\` 4개** — `architecture.md`, `safety.md`, `feature-development.md`, `ui-patching.md`.
4. **`progress\` 폴더에서 가장 최근 날짜 파일 1개** — 직전 세션 작업 흐름 파악.
5. 새 위치에 sunnySR pill을 추가하는 작업이면 **`docs\sunnySR-pill-implementation-guide.md`** + **`docs\sibling-clone-position-map.md`**도 읽는다.

## 참고 코드 위치

- `C:\dev\lazerSR\LazerSR\` — 기능 테스트 참고 환경 (lazerSRClean과 별개 유지)
- `C:\dev\lazerSR\backup\` — 검증된 옛 코드 백업
- `C:\dev\lazerSR\osu\`, `C:\dev\lazerSR\sunnyosu\` — 빌드 필수 참조 소스, **삭제 금지**
