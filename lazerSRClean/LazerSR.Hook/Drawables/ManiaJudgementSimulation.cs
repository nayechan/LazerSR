using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using LazerSR.Hook.Data;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Timing;
using osu.Game.Audio;
using osu.Game.Beatmaps;
using osu.Game.Rulesets;
using osu.Game.Rulesets.Judgements;
using osu.Game.Rulesets.Mania.Objects;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.UI;
using osu.Game.Scoring;
using osu.Game.Screens.Play;

namespace LazerSR.Hook.Drawables;

/// <summary>
/// 화면에 보이지 않는 두 번째 mania 룰셋("그림자 룰셋")에 리플레이를 먹이고 시계를 빠르게 돌려서,
/// <b>osu!가 실제로 발행하는 <see cref="JudgementResult"/></b>를 재생 전에 전부 수집한다.
///
/// <para>
/// 이렇게 하는 이유: 판정값은 재생 헤드가 노트를 지나야 생기는데, 우리는 아직 내려오는 중인
/// 입력 사각형에 그 값을 표시해야 한다. 자체 판정 알고리즘을 쓰면 짝짓기(노트락 포함)가 어긋나므로,
/// osu! 자신에게 미리 한 번 돌려보게 한다.
/// </para>
///
/// <para>
/// 같은 방식의 선례가 osu! 본체에 있다 — <c>osu.Game\Tests\Visual\ReplayStabilityTestScene.cs</c>가
/// <c>ReplayPlayer</c>를 띄우고 <c>ScoreProcessor.NewJudgement</c>를 모은다. 우리는 스크린을 띄울 수
/// 없으므로 <c>DrawableRuleset</c>만 따로 세운다.
/// </para>
/// </summary>
internal partial class ManiaJudgementSimulation : CompositeDrawable
{
    /// <summary>
    /// 로딩 화면을 붙잡고 있는 동안의 프레임당 CPU 예산. 여기선 끊겨도 무방하고,
    /// 긴 맵(13분대)을 게이트 한도 안에 끝내려면 이 정도는 써야 한다 (25ms에서는 24초가 걸렸다).
    /// </summary>
    private const double loading_budget_ms = 60;

    /// <summary>시뮬레이션이 죽지 않았음을 게이트에 알리기 위해 최소한 이만큼 진행하면 하트비트를 갱신한다.</summary>
    private const double heartbeat_progress_ms = 1;

    /// <summary>한 번에 앞으로 미는 게임플레이 시간. 프레임 안정성이 이걸 60Hz로 쪼개 소화한다.</summary>
    private const double step_ms = 200;

    /// <summary>혹시라도 끝나지 않을 때를 대비한 상한.</summary>
    private const int max_iterations = 200_000;

    /// <summary>시각이 이만큼 연속으로 안 움직이면 멈춘 것으로 보고 포기한다.</summary>
    private const int max_stalled_iterations = 2_000;

    private readonly Score score;
    private readonly RulesetInfo rulesetInfo;
    private readonly IReadOnlyList<Mod> mods;
    private readonly ManiaSimulationResult target;

    private readonly ManualClock manualClock = new ManualClock { IsRunning = true, Rate = 1 };
    private readonly List<ManiaSimJudgement> collected = new List<ManiaSimJudgement>();
    private readonly SilentSamples silence = new SilentSamples();

    private DrawableRuleset? drawableRuleset;
    private double startTime;
    private double endTime;
    private int iterations;
    private int stalledIterations;
    private double lastFrameStableTime = double.NegativeInfinity;
    private bool finished;

    [Resolved]
    private IBindable<WorkingBeatmap> workingBeatmap { get; set; } = null!;

    public ManiaJudgementSimulation(Score score, RulesetInfo rulesetInfo, IReadOnlyList<Mod> mods, ManiaSimulationResult target)
    {
        this.score = score;
        this.rulesetInfo = rulesetInfo;
        this.mods = mods;
        this.target = target;

        RelativeSizeAxes = Axes.Both;
        Alpha = 0f;

        // Alpha 0이어도 업데이트가 돌아야 판정이 진행된다.
        AlwaysPresent = true;

        // 이 드로어블은 Player 바깥(로딩 화면)에 붙으므로 DI에 IGameplayClock이 없고,
        // FrameStabilityContainer가 이 Clock을 기준으로 삼는다. 그래서 우리가 시계를 쥔다.
        Clock = new FramedClock(manualClock);
    }

    /// <summary>
    /// 그림자 룰셋에만 적용되는 무음 처리. 없으면 리플레이 전체의 타격음이 1~2초 안에 몰아서 재생된다.
    /// <c>PausableSkinnableSound.Play()</c>가 이 값이 true면 즉시 리턴한다.
    /// </summary>
    private sealed class SilentSamples : ISamplePlaybackDisabler
    {
        public IBindable<bool> SamplePlaybackDisabled { get; } = new BindableBool(true);
    }

    protected override IReadOnlyDependencyContainer CreateChildDependencies(IReadOnlyDependencyContainer parent)
    {
        var dependencies = new DependencyContainer(base.CreateChildDependencies(parent));
        dependencies.CacheAs<ISamplePlaybackDisabler>(silence);
        return dependencies;
    }

    [BackgroundDependencyLoader]
    private void load()
    {
        // 여기서 예외가 새면 드로어블 로드가 통째로 실패하고 결과가 영원히 준비되지 않는다
        // (실제로 겪은 실패 모드다). 실패하면 조용히 포기하되 반드시 완료 표시는 남긴다.
        try
        {
            setUp();
        }
        catch (Exception e)
        {
            HookLog.Write($"[LazerSR] ManiaJudgementSimulation setup failed: {e}");
            drawableRuleset = null;
            complete();
        }
    }

    private void setUp()
    {
        // 본 게임과 히트오브젝트 인스턴스를 공유하지 않도록 우리 몫의 변환본을 따로 만든다.
        // 모드도 복제한다 — 같은 Mod 인스턴스가 두 룰셋에 적용되면 상태가 섞인다.
        var simulationMods = mods.Select(m => m.DeepClone()).ToArray();
        var playable = workingBeatmap.Value.GetPlayableBeatmap(rulesetInfo, simulationMods);

        if (!playable.HitObjects.Any())
        {
            complete();
            return;
        }

        drawableRuleset = rulesetInfo.CreateInstance().CreateDrawableRulesetWith(playable, simulationMods);

        // SetReplayScore는 반드시 룰셋이 로드된 뒤에 불러야 한다.
        // 내부에서 frameStabilityContainer.ReplayInputHandler를 세우는데(DrawableRuleset.cs:315)
        // 그 컨테이너는 BDL load()에서 생성되므로(:177), 로드 전에 부르면 NullReferenceException이 난다.
        // osu! 본체가 PrepareReplay()를 load()가 아니라 LoadComplete()에서 부르는 이유가 이것.
        LoadComponent(drawableRuleset);

        drawableRuleset.SetReplayScore(score);
        drawableRuleset.NewResult += onNewResult;

        AddInternal(drawableRuleset);

        double firstObject = playable.HitObjects.Min(h => h.StartTime);
        double firstFrame = score.Replay.Frames.Count > 0 ? score.Replay.Frames[0].Time : firstObject;

        manualClock.CurrentTime = startTime = Math.Min(firstObject, firstFrame) - 2000;
        endTime = playable.HitObjects.Max(h => h.GetEndTime()) + 3000;

    }

    private void onNewResult(JudgementResult result)
    {
        if (result.HitObject is not ManiaHitObject maniaObject)
            return;

        // HoldNote 자체와 바디는 입력 하나에 대응하지 않으므로 제외한다.
        // TailNote가 Note를 상속하므로 순서가 중요하다.
        bool isTail = maniaObject is TailNote;

        if (!isTail && maniaObject is not Note)
            return;

        collected.Add(new ManiaSimJudgement(
            maniaObject.Column,
            result.TimeAbsolute,
            result.TimeOffset,
            maniaObject.StartTime,
            isTail));
    }

    /// <summary>
    /// 프레임당 예산만큼 시뮬레이션을 돌린다. 여러 번 <see cref="Drawable.UpdateSubTree"/>를 부르는 것이
    /// 곧 빨리 감기다 — 매 호출마다 <c>FrameStabilityContainer</c>가 60Hz 스텝을 소화한다.
    /// </summary>
    public override bool UpdateSubTree()
    {
        if (finished || drawableRuleset == null)
            return base.UpdateSubTree();

        double budget = loading_budget_ms;

        var stopwatch = Stopwatch.StartNew();
        bool result = true;

        do
        {
            // 아직 따라잡는 중이면 시계를 더 밀지 않는다 — 밀어두면 뒤처지기만 한다.
            if (drawableRuleset.FrameStableClock.CurrentTime >= manualClock.CurrentTime - 1)
                manualClock.CurrentTime += step_ms;

            result = base.UpdateSubTree();
            iterations++;

            double frameStableTime = drawableRuleset.FrameStableClock.CurrentTime;

            // 시각이 전혀 안 움직이면 영원히 매 프레임 예산을 태우게 된다. 멈춘 걸 감지하면 포기한다.
            // 초기값이 NaN이면 이 비교가 항상 false가 되어 정상 진행도 "멈춤"으로 세어버린다 (2026-07-29 실제 버그).
            if (!double.IsFinite(lastFrameStableTime) || Math.Abs(frameStableTime - lastFrameStableTime) > 0.01)
            {
                if (!double.IsFinite(lastFrameStableTime) || Math.Abs(frameStableTime - lastFrameStableTime) >= heartbeat_progress_ms)
                    target.Heartbeat.Restart();

                lastFrameStableTime = frameStableTime;
                stalledIterations = 0;
            }
            else
                stalledIterations++;

            target.Progress = Math.Clamp((frameStableTime - startTime) / Math.Max(1, endTime - startTime), 0, 1);

            if (iterations >= max_iterations || stalledIterations >= max_stalled_iterations || frameStableTime >= endTime)
            {
                complete();
                return result;
            }
        } while (stopwatch.Elapsed.TotalMilliseconds < budget);


        return result;
    }

    private void complete()
    {
        if (finished)
            return;

        finished = true;

        if (drawableRuleset != null)
        {
            drawableRuleset.NewResult -= onNewResult;

            // 그림자 룰셋을 그대로 두면 게임플레이 내내 매 프레임 업데이트되며 메모리도 붙잡는다.
            // 지금은 우리 UpdateSubTree 안이라 자식을 건드릴 수 없으므로 다음 업데이트로 미룬다.
            var toRemove = drawableRuleset;
            drawableRuleset = null;

            Schedule(() => RemoveInternal(toRemove, true));
        }

        collected.Sort((a, b) => a.Time.CompareTo(b.Time));

        target.Judgements = collected.ToArray();
        target.Progress = 1;
        target.Ready = true;


        Expire();
    }
}
