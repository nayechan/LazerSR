# 리플레이 비교 스킨 위젯 — 미니 프로젝트 현황

## 최종 목표

**인게임 플레이 중, 현재 나의 실시간 기록과 해당 맵+모드의 나의 최고기록 리플레이를 1대1 실시간 비교하는 스킨 위젯.**

멀티 리플레이 비교(lazersunnyscore 방식)가 아닌, **나 vs 내 최고기록** 단 하나의 비교.

## 안전성 참고 (2026-07-17 추가)

(2026-07-28) `PlayerGameplayPatch`는 무한 트레이닝 세션(`InfiniteTrainingPlayer`)에서는 조기 리턴한다 — 노트가 런타임에 계속 바뀌어 리플레이 비교가 성립하지 않기 때문.

`PlayerGameplayPatch`가 `Player.LoadComplete`를 패치하고 게임플레이 중 100ms마다 파이프로 브로드캐스트하는 것, `ReplayScoreTimeline`이 `ManiaScoreProcessor`를 구동하는 것 모두 `docs/guides/safety.md`의 현재 레드라인 기준으로 **문제없음** — 라이브 `ScoreProcessor`가 아니라 별도 인스턴스로 로컬 시뮬레이션만 하고, 읽은 값을 로컬 표시용으로만 쓴다(서버 제출 경로 미개입). 상세 근거는 `safety.md` "라이브 vs 시뮬레이션 인스턴스" 참고.

---

## 완료된 작업

### 1. 최고기록 리플레이 탐지 인프라 (2026-05-19)

**`LazerSR.Hook\Calculators\BestScoreFinder.cs`**
- `FindBestAccuracy()` — 정확도 반환 (Launcher GUI Best Acc 행)
- `FindBestReplayPath()` — ScoreInfo의 .osr 파일 절대경로 반환
  - `Storage.GetStorageForDirectory("files")` + `replayFile.File.GetStoragePath()`
  - RealmUser.Username은 메모리 필터 (embedded object LINQ 번역 오류 회피)

### 2. 인게임 리플레이 타임라인 비교 — V1 구현 (2026-05-19)

**자체 시뮬레이션 (lazersunny `calc_timeline_v2` 이식)**
- `.osr` 파싱 + LZMA 압축 해제 (SharpCompress reflection)
- 노트/키이벤트 매칭 + 점수 누적
- Launcher GUI "Replay" 행에 `replaycompare:{score}:{combo}` 브로드캐스트
- **알려진 한계**: osu! lazer 채점과 약간의 오차 발생

### 3. ReplayCompareWidget 스킨 위젯 (2026-05-19)

**`LazerSR.Hook\Widgets\ReplayCompareWidget.cs`** — `ISerialisableDrawable`
- osu! 스킨 에디터에서 추가 가능 (`SkinWidgetRegistrarPatch`에 등록)
- 레이아웃: 헤더 + 9행 (Score/Acc/PP/320/300/200/100/50/Miss)
- 각 행: `[live] [diff (큰 폰트, 색상)] [replay]`
- Diff 색상:
  - Score/Acc/PP/320 (rows 2-5): 양수=파랑, 음수=빨강
  - 300~Miss (rows 6-10): 양수=빨강, 음수=파랑 (반전)
- 100ms 폴링으로 갱신, **`IGameplayClock` 사용** (전역 클럭 아님 — 곡 선택 프리뷰 시점 미반영)

**스킨 에디터 노출 설정 (13개):**
- 투명도, 값 크기, Diff 크기, 행 간격(-4~20), 구분선 표시
- 헤더/Score/Acc/PP/320/300/200/100/50/Miss 표시 여부
- 세부정보(Live/Replay 수치) 표시 여부
- 글꼴 선택 (Venera/Torus/TorusAlternate/Inter)

**고정값 (mockup.json 기준):**
- 너비 284px, 패딩 10px, 헤더 폰트 14px, 테두리 반경 10px
- 색상: 양수 #4499ff, 음수 #ff4444, 0값 #888888, 구분선 #333355

**기타 파일:**
- `Data/ReplayFrame.cs` — `(Time, Score, Combo, J320, J300, J200, J100, J50, JMiss)` 구조체
- `Data/ReplayCompareState.cs` — `Timeline` + `ReplayDate` 정적 공유 상태
- `docs/widget-mockup.html` — 설정 시뮬레이션 가능한 HTML 모업
- `docs/mockup.json` — 위젯 기본값 정의

### 4. ReplayScoreTimeline 전면 리팩토링 (2026-05-19)

**문제:** 자체 구현이 osu! lazer 채점 결과와 일치하지 않음.
- 노트 매칭: 우리는 "최단거리", osu!는 "earliest + 노트락" (OrderedHitPolicy)
- 히트윈도우: DT/HT/HR mod 미적용
- HoldNote Body 끊김 미처리

**해결:** osu! lazer 컴포넌트 직접 사용.

| 항목 | 변경 전 | 변경 후 |
|------|---------|---------|
| 점수 공식 | 자체 구현 `150000*combo + 850000*acc^(2+2acc)*acc` | `ManiaScoreProcessor.ApplyResult()` |
| 히트윈도우 | 자체 `CalcHitWindows(od)` | `hitObject.HitWindows.ResultFor(offset)` (mod 자동 반영) |
| Combo multiplier | 자체 `ComboMult` | ScoreProcessor 내부 계산 |
| 노트 매칭 | 칼럼 내 closest unhit | OrderedHitPolicy 이식 (FIFO + next-startTime 강제 Miss) |
| HoldNote | Head + Tail 만 | Head + Tail + Body(IgnoreHit/ComboBreak) + HoldNote자체(IgnoreHit/Miss) |
| Mod 적용 (DT/HT/HR) | 미적용 | 자동 (HitWindows는 `IManiaRateAdjustmentMod`/`ManiaModHardRock`이 GetPlayableBeatmap 시점에 박아둠) |

**Tail RELEASE_WINDOW_LENIENCE = 1.5** osu! 상수 그대로 import.
**Head 미히트 시 Tail Meh 캡** osu! `DrawableHoldNoteTail.GetCappedResult` 동작 그대로.

---

### 5. Acc/PP 구현 (2026-05-19)

**Acc 공식**: 분모 305 고정 — `(305×j320 + 300×j300 + 200×j200 + 100×j100 + 50×j50) / (305×total) × 100`
- `CalcAcc()` 헬퍼, live/replay 모두 기존 판정 수에서 on-the-fly 계산
- `ReplayFrame`/`ReplayScoreTimeline` 변경 없음

**PP**: `ManiaPerformanceCalculator` + `TimedDifficultyAttributes` (osu! `PerformancePointsCounter` 동일 패턴)
- `load(BeatmapDifficultyCache)` → `GetTimedDifficultyAttributesAsync` 비동기 1회
- `GetAttributeAtTime(double time)` 이진탐색으로 현재 시점 attributes
- live/replay 각각 `Calculate(scoreInfo, attrib)` 호출
- `GameplayWorkingBeatmap` inner class 복사 (`protected internal` → `protected` 어셈블리 차이만 수정)

### 6. BestScoreFinder 모드 매칭 정책 수정 (2026-05-19)

- Mirror(`MR`) 양쪽에서 제거 후 비교
- `RateGroup()`: NC→DT, DC→HT 정규화
- 같은 군 + 다른 acronym (DT vs NC 등) → `speed_change` 값만 비교

### 7. 날짜 버그 수정 (2026-05-19)

- `.osr` 타임스탬프: `DateTime.FromFileTimeUtc` → `new DateTime(ticks, DateTimeKind.Utc)`
- `.osr`은 C# DateTime.Ticks(0001년 기준) 저장인데 FileTime(1601년 기준)으로 파싱하여 연도가 ~1600년 틀어지던 문제 수정

---

## 다음 단계

### 미정 / 추후 작업

1. **OD 입력에 따른 동작 검증**
   - 노모드 리플레이로 osu! 클라이언트 vs 우리 시뮬레이션 결과 비교 (사용자 테스트 중)
   - 차이 발견 시 OrderedHitPolicy 미세조정 또는 HoldNote 처리 보강

2. **위젯 UI 개선 후보 (미확정)**
   - 판정별 색상 강도, 애니메이션
   - 위치 프리셋

---

## 데이터 흐름

```
플레이 시작 (Player.LoadComplete Postfix)
  ↓
ReplayCompareState 리셋 (Timeline=null, ReplayDate="")
  ↓
background Task:
  BestScoreFinder.FindBestReplayPath()  → .osr 경로
  ReplayScoreTimeline.Calculate()       → ReplayFrame[] + date
    - ManiaScoreProcessor.ApplyBeatmap(beatmap)
    - .osr 파싱 → 키이벤트
    - 칼럼별 OrderedHitPolicy 시뮬레이션
    - 매 판정마다 ScoreProcessor.ApplyResult() → TotalScore.Value 캡처
  ↓
ReplayCompareState.Timeline 저장
  ↓
(병렬) PipeServer 100ms 폴링 → Launcher "Replay" 행
(병렬) ReplayCompareWidget 100ms 폴링:
  - IGameplayClock.CurrentTime로 이진탐색 → 현 시점 ReplayFrame
  - ScoreProcessor.Statistics에서 live 판정 카운트
  - 각 행 텍스트/색상 갱신
```

---

## 관련 파일 경로

| 파일 | 역할 |
|---|---|
| `LazerSR.Hook\Calculators\BestScoreFinder.cs` | Realm 쿼리 — 정확도 + .osr 경로 |
| `LazerSR.Hook\Calculators\ReplayScoreTimeline.cs` | .osr 파싱 + osu!컴포넌트 직접 시뮬레이션 |
| `LazerSR.Hook\Data\ReplayFrame.cs` | (Time, Score, Combo, 판정 누적) 구조체 |
| `LazerSR.Hook\Data\ReplayCompareState.cs` | Timeline + Date 정적 공유 |
| `LazerSR.Hook\Patches\DifficultyDisplayPatch.cs` | 선곡창 오케스트레이터 (Best Acc 브로드캐스트) |
| `LazerSR.Hook\Patches\PlayerGameplayPatch.cs` | 인게임 오케스트레이터 (Timeline 계산 + 폴링) |
| `LazerSR.Hook\Patches\SkinWidgetRegistrarPatch.cs` | ReplayCompareWidget 등록 |
| `LazerSR.Hook\Widgets\ReplayCompareWidget.cs` | 스킨 위젯 본체 |
| `LazerSR.Hook\Ipc\PipeServer.cs` | Hook→Launcher 브로드캐스트 |
| `LazerSR.Launcher\MainWindow.xaml(.cs)` | Launcher GUI (Best Acc, Replay 행) |
| `docs\widget-mockup.html` | 설정 시뮬레이션용 HTML 모업 |
| `docs\mockup.json` | 위젯 기본값 |

---

## osu! lazer 코드 참조 (변경 추적)

리팩토링에서 활용한 osu! 원본 위치:
- `osu.Game.Rulesets.Mania.Scoring.ManiaScoreProcessor` — V2 점수 공식 본체
- `osu.Game.Rulesets.Mania.Scoring.ManiaHitWindows` — OD/Mod 기반 히트윈도우
- `osu.Game.Rulesets.Mania.UI.OrderedHitPolicy` — 노트락 알고리즘 (12줄 이식)
- `osu.Game.Rulesets.Mania.Objects.TailNote.RELEASE_WINDOW_LENIENCE = 1.5`
- `osu.Game.Rulesets.Mania.Objects.Drawables.DrawableHoldNoteTail.GetCappedResult` — head 미히트 시 Meh 캡
- `osu.Game.Rulesets.Mania.Objects.Drawables.DrawableHoldNote.CheckForResult/OnReleased` — Tail/Body/HoldNote 판정 순서
- `osu.Game.Rulesets.Mania.Mods.IManiaRateAdjustmentMod` — DT/HT → `SpeedMultiplier` 적용
- `osu.Game.Rulesets.Mania.Mods.ManiaModHardRock` — HR → `DifficultyMultiplier = 1.4` 적용 (OD 자체는 변경 안 함)

osu! `IBeatmap.HitObjects`는 `GetPlayableBeatmap(ruleset, mods)` 결과이므로 HitWindows에 mod가 이미 적용된 상태로 들어옴.
