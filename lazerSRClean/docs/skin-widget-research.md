# osu! 스킨 위젯 시스템 참고자료

원본: `C:\dev\lazerSR\docs\research\skin-widget-research.md` (2026-05-15 조사, 10개 병렬 서브에이전트). 이관하며 "구현 권고" 섹션을 실제 구현 완료 상태로 갱신. 아키텍처/안전성 결론은 `architecture.md`, `safety.md`로 이동했으므로 여기서는 osu! 자체 API 레퍼런스만 남김.

---

## 1. 스킨 에디터 진입점

- `osu.Game/Overlays/SkinEditor/SkinEditor.cs`, `Ctrl+Shift+S` → `GlobalAction.ToggleSkinEditor`
- 컴포넌트 목록: `SerialisedDrawableInfo.GetAllAvailableDrawables(ruleset)` (`osu.Game/Skinning/SerialisedDrawableInfo.cs`) — **`osu.Game.dll` 어셈블리만 스캔**, 외부 dll 타입은 자동으로 안 잡힘 → LazerSR이 `SkinWidgetRegistrarPatch`로 이 메서드를 Postfix해서 자기 타입을 concat (실제 구현됨, `feature-development.md` §패턴B).

## 2. `ISerialisableDrawable` 등록 조건

```csharp
public interface ISerialisableDrawable : IDrawable
{
    bool IsEditable { get; }          // 기본 true
    bool UsesFixedAnchor { get; set; }
}
```

- `public` + non-abstract + 인터페이스 구현
- **public 파라미터 없는 생성자** 필수 (`Activator.CreateInstance(type)`로 인스턴스화됨)
- 표시 라벨은 `type.Name` 그대로 — 별도 DisplayName 시스템 없음

## 3. 배치 가능한 화면 (`GlobalSkinnableContainers`, 단 3개)

| 값 | 위치 |
|---|---|
| `MainHUDComponents` | `HUDOverlay.cs` |
| `SongSelect` | `SongSelect.cs` — LazerSR이 실제로 쓰는 곳 |
| `Playfield` | `HUDOverlay.cs` |

## 4. 위젯 구현 체크리스트

`ArgonSongProgress`/`ArgonAccuracyCounter`/`BarHitErrorMeter`에서 추출:

1. `CompositeDrawable, ISerialisableDrawable` 구현
2. public 파라미터 없는 생성자
3. `UsesFixedAnchor` 프로퍼티
4. `[SettingSource]` — getter-only + 인라인 초기화 (`feature-development.md` §패턴B 코드 예시)
5. `[Resolved]`로 beatmap/ruleset/mods 주입
6. `[BackgroundDependencyLoader]`로 자식 트리 구성
7. `LoadComplete`에서 Bindable 구독
8. 외부 이벤트 구독 시 `Dispose`에서 반드시 해제 (누수 방지)

### SettingSource 자동 매핑 (컬러 피커 주의)

| Bindable 타입 | 생성 컨트롤 |
|---|---|
| `BindableNumber<float/double/int>` | `SettingsSlider<T>` |
| `Bindable<bool>` | `SettingsCheckbox` |
| `Bindable<string>` | `SettingsTextBox` |
| **`BindableColour4`** | `SettingsColour` (색상 피커) |
| `Bindable<TEnum>` | Enum dropdown |

⚠️ `Bindable<Colour4>`(non-Colour4 타입)는 enum dropdown으로 잘못 빠짐 — 반드시 `BindableColour4` 사용.

## 5. 빌트인 위젯 참고 (재사용 가능한 베이스)

`CompositeDrawable`(복합 위젯), `RollingCounter<T>`(수치 보간), `Box+Masking+CornerRadius`(바/카드), `FillFlowContainer`/`GridContainer`(리스트), `CircularProgress`(원형바), 커스텀 `DrawNode+IShader+IVertexBatch`(GPU 셰이더 — `StrainAreaGraph`가 이미 이 패턴 사용).

## 6. Song Select 데이터 소스

```csharp
[Resolved] private IBindable<WorkingBeatmap> beatmap { get; set; } = null!;
[Resolved] private IBindable<RulesetInfo> ruleset { get; set; } = null!;
[Resolved] private IBindable<IReadOnlyList<Mod>> mods { get; set; } = null!;
```

주요 전역 의존성: `BeatmapManager`, `BeatmapDifficultyCache`(live SR 캐시), `RealmAccess`(DB 직접 접근), `IAPIProvider`, `SkinManager`, `MusicController`.

`working.GetPlayableBeatmap(ruleset, mods, ct)`은 **무거움** — 반드시 `Task.Run` 배경 스레드. mania 키 수는 `CircleSize`가 아니라 `(GetPlayableBeatmap(...) as ManiaBeatmap)?.TotalColumns`로 확인.

## 7. Gameplay 데이터 소스 (참고용 — 실제 사용은 `safety.md` 레드라인 준수)

`Player.cs`가 캐시: `ScoreProcessor`, `HealthProcessor`, `GameplayState`, `IGameplayClock`, `DrawableRuleset`. `ScoreProcessor`는 `TotalScore`/`Accuracy`/`Combo`/`HighestCombo`/`NewJudgement` 이벤트를 노출 — **읽기만 허용, 이 라이브 인스턴스에 쓰지 않음** (`safety.md`).

## 8. Skin Layout JSON — 외부 DLL 미주입 시 위험

`SkinLayoutInfo` 저장 시 `Type.AssemblyQualifiedName`으로 직렬화. 복원 시 `Type.GetType(qualifiedName)` 실패하면 **`Skin.cs`의 try/catch가 layout 파일 전체를 폐기** — LazerSR 위젯뿐 아니라 같은 파일의 vanilla 위젯까지 함께 사라진다.

**완화책 (미구현, 필요 시 착수)**: namespace/클래스명 동결, 또는 LazerSR 위젯을 별도 layout 파일로 분리 저장. 현재 이 위험을 완화하는 코드는 없음 — 사용자가 LazerSR 없이 lazer를 실행했다가 SongSelect 레이아웃이 초기화될 가능성이 있음을 인지할 것.

## 9. 외부 도구 비교 (`safety.md` 근거자료)

| 도구 | 방식 |
|---|---|
| tosu | 외부 프로세스 메모리 리딩 + WebSocket |
| gosumemory | 메모리 리딩, WebSocket |
| StreamCompanion | 메모리 리딩 + plugin |
| **LazerSR** | in-process startup hook + HarmonyX 패치 — 검색상 이 방식은 사실상 unique. tosu 대비 침습성은 높지만 정확도/버전 호환성은 우월 |

osu! 공식 정책: peppy는 "수십~수백 개 외부 DLL 로드는 지원 안 함, 발견 시 온라인 플레이 disable"이라 밝힘 (Discussion #26987). 공식 확장 경로는 custom ruleset(DLL drop)뿐. 점수에 영향 없는 read-only overlay는 별도 카테고리로 암묵적 허용.

## 10. 구현 완료 상태 (2026-07-17 갱신)

원본 문서의 "구현 권고" 단계(PoC → 기존 패치 UI 재사용 → 안전 장치 → 패치 폐기)는 실제로:
- ✅ PoC + 위젯 등록 (`SkinWidgetRegistrarPatch`)
- ✅ `StrainAreaGraph`/`MsdBarChart` 등 GPU 렌더러 위젯화 완료
- ❌ Layout 폐기 방지 안전장치(§8) — 미구현
- ❌ 기존 `DifficultyDisplayPatch`/`MetadataWedgePatch` deprecated 처리 — 안 함, 오히려 sibling-pill 패턴(1.1/2.1/4.1/4.2)과 스킨 위젯 패턴이 공존 중 (`architecture.md` §4)
