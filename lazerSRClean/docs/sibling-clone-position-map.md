# Sibling Pill — 위치별 진행 상황

`C:\dev\lazerSR\docs\research\sunny-sibling-clone-strategy.md`(2026-05-14 작성, legacy 폴더에서 이관+수정)의 위치 번호 체계를 계승. 원본 문서의 "Strategy B(클래스 통째 클론)" 채택 권고는 **실제 결과로 반박됐다** — 아래 §전략 정정 참고.

---

## 위치 목록 및 상태

| 번호 | osu 클래스 | 위치 | 상태 | 구현 패치 |
|---|---|---|---|---|
| 1.1 | `BeatmapTitleWedge+DifficultyDisplay` | 맵 선택 메인 SR 표시 | ✅ 완료 (2026-05-17) | `DifficultyDisplayPatch.cs` |
| 2.1 | `BeatmapAttributesDisplay` | Mod Select 좌하단 | ✅ 완료 (2026-05-18) | `BeatmapAttributesDisplayPatch.cs` |
| 4.1 | `BeatmapMetadataDisplay` | 곡 로딩 화면 | ✅ 완료 (2026-05-18) | `BeatmapMetadataDisplayPatch.cs` |
| 4.2 | `ExpandedPanelMiddleContent` | 리절트 화면 성적 패널 | ✅ 완료 (2026-05-18) | `ExpandedPanelMiddleContentPatch.cs` |
| (부가) | `DifficultyIconTooltip.SetContent` | 난이도 아이콘 툴팁 | ✅ 완료 (2026-05-18, 버그 수정 2026-07-17 / 2026-07-29) | `DifficultyIconTooltipPatch.cs` — `ConditionalWeakTable`로 인스턴스별 pill·bindable·CTS 보관. **호버 대상마다 재계산**(2026-07-29, 그전엔 전역 `SunnyState.CurrentSr`에 묶여 멀티 로비에서 stale 값). 제약은 `architecture.md` §13 |
| 1.2 | `PanelBeatmap : Panel` | 캐러셀 개별 난이도 패널 | ❌ 미착수 | — |
| 1.5 | `PanelBeatmapSet+SpreadDisplay` | 비트맵셋 옆 난이도 스펙트럼 점 | ❌ 미착수 | — |
| 3.1 | `StarRatingRangeDisplay` | 멀티 로비 SR 범위 | ❌ 미착수 | — |
| 3.5 | `GameplayWarmupScreen+DifficultyDisplay` | 랭크드 워밍업 화면 | ❌ 미착수 | — |
| 3.6 | `MatchmakingSelectPanel+CardContentBeatmap` | 매칭 비트맵 선택 카드 | ❌ 미착수 | — |

---

## 전략 정정 (원본 문서 대비)

원본 `sunny-sibling-clone-strategy.md`는 "이전 시도(Strategy A: 부모 컨테이너 reflection 분해/재조립)는 그래픽 누락·레이아웃 분해 위험으로 롤백됨 → Strategy B(원본 클래스 통째 클론)를 채택해야 한다"고 결론지었다.

**실제로 2.1/4.1/4.2는 Strategy B가 아니라, reflection으로 기존 컨테이너에 pill 하나를 삽입하는 더 단순한 방식(Strategy A에 가까움, 정확히는 `sunnySR-pill-implementation-guide.md`가 검증한 "InsertSunnyPill" 패턴)으로 구현됐고 성공했다.** 원본 문서가 실패 사례로 든 "부모 컨테이너 통째 재조립"과 실제 성공 사례("기존 컨테이너에 자식 하나만 추가")는 침습 정도가 다르다 — 통째 재조립은 위험하지만, 단순 삽입은 안전하다는 게 재확인된 것.

**결론**: 남은 위치(1.2/1.5/3.1/3.5/3.6)도 먼저 `sunnySR-pill-implementation-guide.md`의 InsertSunnyPill 패턴(단순 삽입)을 시도한다. Strategy B(클래스 클론)는 대상 타입의 즉시 부모가 `internal` 베이스에 강하게 의존해서 단순 삽입 자체가 불가능한 경우에만 fallback으로 고려한다 — 특히 1.2(`Panel`이 `internal`일 가능성)가 그런 케이스가 될 수 있음, 착수 시 빌드 타임에 접근성부터 확인.

## 위치별 난이도 메모 (원본에서 유지)

- **1.2**: `Panel`이 `osu.Game.Graphics.Carousel.Panel`로 internal일 가능성 — 착수 전 빌드 테스트로 확인 필요.
- **1.5**: nested, `OsuAnimatedButton`(internal 가능) 베이스 — 점 나열 시각화라 부분 클론이 나을 수도 있음.
- **3.1/3.5/3.6**: `APIBeatmap`(온라인 메타데이터)만 받을 수 있어 로컬 비트맵이 없으면 sunny 계산 불가. **단, 로컬에 있으면 조회할 수 있다** — 2026-07-29에 툴팁에서 검증된 방식이 `BeatmapManager.QueryBeatmap`으로 **MD5 우선 → OnlineID 폴백**(osu!가 플레이리스트 항목의 "다운로드됨" 판정에 쓰는 순서)이다. 못 찾을 때만 기본값 유지/skip.
- **2.1(완료)**: mod-adjusted sunny 필요 (`Mods.Value` 적용) — 완료 코드가 이미 처리함.
- **1.5**: base SR(비-mod-adjusted) 표시가 원본과 일관.
