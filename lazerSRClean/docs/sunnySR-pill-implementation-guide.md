# sunnySR Pill 구현 가이드

새 위치(1.2, 2.x, 3.x, 4.x 등)에 sunnySR pill을 추가할 때 반드시 이 문서를 따른다.
**1.1의 구현 방식은 실제 동작이 검증된 유일한 기준이다. 이 방법에서 벗어나지 않는다.**

---

## 전체 흐름

```
[맵/룰셋/모드 변경]
       ↓
DifficultyDisplayPatch.Recalculate()          ← 오케스트레이터 (건드리지 않음)
       ↓  [Task.Run 백그라운드]
SunnyRunner.Calculate(working, ruleset, mods)
       ↓  [Schedule → update thread]
SunnyState.CurrentSr.Value = new StarDifficulty(sr, 0)
       ↓  [Bindable 자동 전파]
   ┌───────────────────────────────────────┐
[1.1 pill]  [1.2 pill]  ...  [4.2 pill]    ← 각 pill이 독립적으로 구독 중
   └───────────────────────────────────────┘
```

**새 위치를 추가할 때 건드리는 것:**
- 새 `[HarmonyPatch]` 클래스 1개 (또는 기존 patch 클래스에 헬퍼 메서드 추가)

**절대 건드리지 않는 것:**
- `SunnyRunner.cs` — 계산 로직
- `SunnyState.cs` — 공유 Bindable 채널
- `DifficultyDisplayPatch.Recalculate()` — 오케스트레이터

---

## 새 위치 추가 절차

### 1단계 — 후크 지점 선택

표시하려는 UI 요소를 감싸는 **top-level 타입의 `LoadComplete`** 를 후크 지점으로 잡는다.

- nested type(`+` 표기)의 메서드를 직접 타겟하면 HarmonyX가 detour를 붙이지 못한다 (silent failure).
- `LoadComplete`를 선택하는 이유: 이 시점에 자식 Drawable 트리가 모두 구성되어 있어 탐색 가능.

### 2단계 — Patch 클래스 작성

**기존 `DifficultyDisplayPatch`에 위치가 같은 타입이면** 헬퍼 메서드만 추가한다.
**다른 타입이 필요하면** 새 파일에 별도 `[HarmonyPatch]` 클래스를 만든다.

> 같은 메서드를 타겟하는 `[HarmonyPatch]` 클래스가 2개 이상 존재하면 두 번째부터 silent 누락된다.
> 같은 타겟을 공유해야 할 경우 반드시 하나의 클래스 안에 통합한다.

Patch 클래스 골격 (1.1의 구조를 그대로 복사):

```csharp
[HarmonyPatch]
public static class MyNewLocationPatch
{
    private const string TARGET_TYPE_NAME = "osu.Game.Screens.XXX.SomeTopLevelType";
    private static CancellationTokenSource? _cts;  // 필요한 경우에만

    public static MethodBase? TargetMethod()
    {
        Type? type = AccessTools.TypeByName(TARGET_TYPE_NAME);
        return type == null ? null : AccessTools.Method(type, "LoadComplete");
    }

    public static bool Prepare() => TargetMethod() != null;

    public static void Postfix(object __instance)
    {
        try
        {
            if (__instance is Drawable owner)
                InsertSunnyPill_X_X(owner);
        }
        catch (Exception ex)
        {
            HookLog.Write($"[LazerSR] MyNewLocationPatch.Postfix failed: {ex}");
        }
    }

    private static void InsertSunnyPill_X_X(Drawable owner)
    {
        // 아래 §3 참고
    }
}
```

**Patch 클래스 내부에서 절대 금지:**
- `yield return` / 제네릭 메서드 정의
- `ConditionalWeakTable<...>` 같은 제네릭 static 필드
- `private sealed class` 중첩 클래스
- 중첩 람다 `(Action)(() => (Action)(() => ...))`

### 3단계 — Pill 삽입 (InsertSunnyPill)

**검증된 1.1의 삽입 코드를 그대로 사용한다.** 위치마다 달라지는 것은 "어떤 컨테이너에 pill을 넣느냐"뿐이다.

#### Pill 생성 — 이 코드는 항상 동일

```csharp
var pill = new StarRatingDisplay(default, animated: true)
{
    Anchor = Anchor.CentreLeft,
    Origin = Anchor.CentreLeft,
};
pill.Current = SunnyState.CurrentSr;  // 구독 연결 — 이 한 줄이 전부
```

#### GridContainer에 삽입하는 경우 — 1.1과 동일한 방식

`GridContainer.ColumnDimensions`는 getter가 없는 write-only property이고,
`Content`는 getter가 있지만 `GridContainerContent` 타입을 반환한다.
이를 직접 읽으려면 아래의 검증된 패턴을 사용해야 한다.

```csharp
// 기존 dims 읽기: property getter가 없으므로 private field 직접 접근
var dimsField   = AccessTools.Field(typeof(GridContainer), "columnDimensions");
var dimsProp    = AccessTools.Property(typeof(GridContainer), "ColumnDimensions");
var contentProp = AccessTools.Property(typeof(GridContainer), "Content");
if (dimsField == null || dimsProp == null || contentProp == null) return;

var existingDims = dimsField.GetValue(container) as Dimension[] ?? Array.Empty<Dimension>();

// 기존 row 읽기: GridContainerContent → Item[0] indexer → IEnumerable 순회
var gridContent = contentProp.GetValue(container);
if (gridContent == null) return;
int rowCount = (int)(gridContent.GetType().GetProperty("Count")?.GetValue(gridContent) ?? 0);
if (rowCount == 0) return;
var itemProp = gridContent.GetType().GetProperty("Item");
var row0 = itemProp?.GetValue(gridContent, new object[] { 0 });
if (row0 == null) return;

var existingCells = new List<Drawable>();
foreach (var item in (IEnumerable)row0)
    if (item is Drawable d) existingCells.Add(d);

// 원하는 위치에 삽입 (예: col 0 바로 뒤)
var newDims = new Dimension[existingDims.Length + 2];
newDims[0] = existingDims[0];
newDims[1] = new Dimension(GridSizeMode.Absolute, 4);   // gap
newDims[2] = new Dimension(GridSizeMode.AutoSize);      // pill
for (int i = 1; i < existingDims.Length; i++)
    newDims[i + 2] = existingDims[i];

var newRow = new Drawable[existingCells.Count + 2];
newRow[0] = existingCells[0];
newRow[1] = new Container();   // gap
newRow[2] = pill;
for (int i = 1; i < existingCells.Count; i++)
    newRow[i + 2] = existingCells[i];

// 쓰기: dims는 property setter, content는 op_Implicit 경유
dimsProp.SetValue(container, newDims);
var opImplicit = typeof(GridContainerContent).GetMethod("op_Implicit", new[] { typeof(Drawable[][]) });
var newContent = opImplicit?.Invoke(null, new object[] { new Drawable[][] { newRow } });
contentProp.SetValue(container, newContent);
```

#### FillFlowContainer에 추가하는 경우

직접 `AddInternal` 또는 reflection으로 `InternalChildren`에 추가한다.
구체적인 방법은 해당 컨테이너 타입에 따라 달라진다.

### 4단계 — Drawable 탐색

대상 컨테이너를 찾을 때 `FindFirstChildOfType` 헬퍼를 재사용한다.
이 헬퍼는 `DifficultyDisplayPatch.cs`에 `private static` 으로 정의되어 있다.
다른 patch 클래스에서 필요하면 `AccessHelper.cs`로 이동해 공유하거나 동일 코드를 복사한다.

```csharp
// InternalChildren을 통한 재귀 탐색 — 검증된 방식
private static Drawable? FindFirstChildOfType(Drawable root, Type targetType)
{
    if (targetType.IsInstanceOfType(root)) return root;
    if (root is not CompositeDrawable composite) return null;

    var prop = AccessTools.Property(typeof(CompositeDrawable), "InternalChildren");
    if (prop?.GetValue(composite) is not IEnumerable children) return null;

    foreach (var child in children)
    {
        if (child is Drawable d)
        {
            var found = FindFirstChildOfType(d, targetType);
            if (found != null) return found;
        }
    }
    return null;
}
```

---

## 알려진 함정

| 함정 | 증상 | 해결 |
|---|---|---|
| nested type(`+`)을 TargetMethod로 직접 지정 | Prepare=true지만 Postfix 발화 안 함 | top-level 타입의 LoadComplete 사용 |
| 같은 메서드에 `[HarmonyPatch]` 클래스 2개 | 두 번째 클래스 silent 누락 | 하나의 클래스로 통합 |
| Patch 클래스 내 `yield return` / 제네릭 메서드 | TypeLoadException으로 어셈블리 전체 패치 실패 | 비제네릭 평범한 메서드만 사용 |
| `SunnyState.CurrentSr.Value`를 백그라운드에서 직접 세팅 | "Cannot mutate Transforms... not on update thread" | `scheduleMethod.Invoke(owner, ...)` 경유 |
| `GridContainer.ColumnDimensions.GetValue()` 호출 | "Property Get method was not found" | `AccessTools.Field("columnDimensions")` 로 읽기 |

---

## 파일 구조 원칙

```
LazerSR.Hook\
  Patches\
    DifficultyDisplayPatch.cs   ← 1.1 + 오케스트레이터 (기준 파일)
    NewLocationPatch.cs         ← 위치당 1개, 또는 기존 파일에 헬퍼만 추가
  SunnyState.cs                 ← 절대 수정하지 않음
LazerSR.SunnyCalculator\
  SunnyRunner.cs                ← 절대 수정하지 않음
```

계산/publish 로직은 절대 중복 작성하지 않는다. `SunnyState.CurrentSr` 구독 한 줄로 모든 pill이 동기화된다.
