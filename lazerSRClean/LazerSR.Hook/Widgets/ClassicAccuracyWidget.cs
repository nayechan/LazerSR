using System;
using System.Collections.Generic;
using LazerSR.Hook.Calculators;
using LazerSR.Hook;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Game.Configuration;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;
using osu.Game.Rulesets.Judgements;
using osu.Game.Rulesets.Mania.Beatmaps;
using osu.Game.Rulesets.Mania.Objects;
using osu.Game.Rulesets.Objects;
using osu.Game.Screens.Play;
using osu.Game.Skinning;

namespace LazerSR.Hook.Widgets;

// Stable(구 클라이언트) 방식 정확도 표시: 300g 고정 16ms + 64/97/127/151/188-3*OD 판정창,
// 분모 300 기준 정확도. 롱노트는 head/release 원본(raw) 오차의 산술평균으로 판정 1개만 발생시킨다.
public class ClassicAccuracyWidget : CompositeDrawable, ISerialisableDrawable
{
    public bool UsesFixedAnchor { get; set; }

    [SettingSource("글꼴")]
    public Bindable<Typeface> Font { get; } = new Bindable<Typeface>(Typeface.Torus);

    [Resolved(canBeNull: true)]
    private GameplayState? gameplayState { get; set; }

    private OsuSpriteText text = null!;

    private readonly Dictionary<HitObject, HoldNote> headOf = new();
    private readonly Dictionary<HitObject, HoldNote> tailOf = new();
    private readonly Dictionary<HoldNote, double> pendingHead = new();
    private readonly Dictionary<HoldNote, double> resolvedHead = new();

    private double overallDifficulty;
    private long numerator;
    private int noteCount;

    public ClassicAccuracyWidget()
    {
        AutoSizeAxes = Axes.Both;
    }

    [BackgroundDependencyLoader]
    private void load()
    {
        InternalChild = text = new OsuSpriteText
        {
            Text = "100.00%",
            Font = OsuFont.GetFont(Font.Value, size: 24, weight: FontWeight.Bold),
        };
    }

    protected override void LoadComplete()
    {
        base.LoadComplete();

        Font.BindValueChanged(e => text.Font = OsuFont.GetFont(e.NewValue, size: 24, weight: FontWeight.Bold), true);

        if (gameplayState?.Beatmap is not ManiaBeatmap) return;

        overallDifficulty = gameplayState.Beatmap.Difficulty.OverallDifficulty;

        foreach (var ho in gameplayState.Beatmap.HitObjects)
        {
            if (ho is HoldNote hn)
            {
                headOf[hn.Head] = hn;
                tailOf[hn.Tail] = hn;
            }
        }

        gameplayState.ScoreProcessor.NewJudgement += onNewJudgement;
        gameplayState.ScoreProcessor.JudgementReverted += onJudgementReverted;
        Scheduler.AddDelayed(updateDisplay, 100, true);
    }

    // TimeOffset은 비트맵 시간 기준이라 배속(DT/HT/사용자 배속)에 비례해 커진다. 반면 mania의
    // 판정창은 같은 배속만큼 함께 늘어나므로(ManiaHitWindows.SpeedMultiplier) 고정 판정창에
    // 대입하려면 배속으로 나눠 실시간 기준 오차로 되돌려야 한다. osu! 본체가 UR을 계산할 때
    // 쓰는 보정과 동일하다(HitEventExtensions: "Division by gameplay rate ...").
    // 되감기 중에는 GameplayRate가 음수일 수 있으나 판정에는 절댓값만 쓰므로 여기서 절댓값을 취한다.
    private static double normalisedOffset(JudgementResult result)
    {
        double rate = Math.Abs(result.GameplayRate ?? 1.0);
        return rate > 0 ? result.TimeOffset / rate : result.TimeOffset;
    }

    private void onNewJudgement(JudgementResult result)
    {
        var ho = result.HitObject;
        double offset = normalisedOffset(result);

        if (headOf.TryGetValue(ho, out var headHold))
        {
            pendingHead[headHold] = offset;
            return;
        }

        if (tailOf.TryGetValue(ho, out var tailHold))
        {
            double headOffset = pendingHead.TryGetValue(tailHold, out double h) ? h : offset;
            pendingHead.Remove(tailHold);
            resolvedHead[tailHold] = headOffset;
            addJudgment((headOffset + offset) / 2.0);
            return;
        }

        // HoldNote(부모) 자체 결과 및 Body(홀드 유지) 결과는 정확도에 반영하지 않는다.
        if (ho is HoldNote || ho is HoldNoteBody) return;

        addJudgment(offset);
    }

    // NewJudgement와 정확히 대칭. 되감기/리플레이 스킵 시 Playfield가 시간 역순(LIFO)으로
    // 발화시키므로(tail이 head보다 먼저 되돌려짐), tail 되돌림에서 pendingHead를 복원해두면
    // 이어지는 head 되돌림·재정방향 재생 모두 자연히 맞아떨어진다.
    private void onJudgementReverted(JudgementResult result)
    {
        var ho = result.HitObject;
        double offset = normalisedOffset(result);

        if (headOf.TryGetValue(ho, out var headHold))
        {
            pendingHead.Remove(headHold);
            return;
        }

        if (tailOf.TryGetValue(ho, out var tailHold))
        {
            if (resolvedHead.TryGetValue(tailHold, out double headOffset))
            {
                resolvedHead.Remove(tailHold);
                removeJudgment((headOffset + offset) / 2.0);
                pendingHead[tailHold] = headOffset;
            }
            return;
        }

        if (ho is HoldNote || ho is HoldNoteBody) return;

        removeJudgment(offset);
    }

    private void addJudgment(double offsetMs)
    {
        numerator += ClassicManiaAccuracy.Judge(Math.Abs(offsetMs), overallDifficulty);
        noteCount++;
    }

    private void removeJudgment(double offsetMs)
    {
        numerator -= ClassicManiaAccuracy.Judge(Math.Abs(offsetMs), overallDifficulty);
        noteCount--;
    }

    private void updateDisplay()
    {
        double accuracy = noteCount == 0 ? 100.0 : numerator / (300.0 * noteCount) * 100.0;
        text.Text = accuracy.ToString("F2") + "%";

        if (gameplayState != null)
        {
            ClassicAccuracyState.ScoreId = gameplayState.Score.ScoreInfo.ID;
            ClassicAccuracyState.Accuracy = accuracy;
        }
    }

    protected override void Dispose(bool isDisposing)
    {
        base.Dispose(isDisposing);
        if (gameplayState != null)
        {
            gameplayState.ScoreProcessor.NewJudgement -= onNewJudgement;
            gameplayState.ScoreProcessor.JudgementReverted -= onJudgementReverted;
        }
        Scheduler.CancelDelayedTasks();
    }
}
