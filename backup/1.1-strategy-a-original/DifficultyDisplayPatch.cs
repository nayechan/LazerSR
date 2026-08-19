using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using HarmonyLib;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.Drawables;
using osu.Game.Configuration;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;
using osu.Game.Rulesets;
using osu.Game.Rulesets.Mods;
using osu.Game.Screens.Select;

namespace LazerSR.Hook.Patches;

[HarmonyPatch]
public static class DifficultyDisplayPatch
{
    private const string TARGET_TYPE_NAME = "osu.Game.Screens.Select.BeatmapTitleWedge+DifficultyDisplay";

    private static readonly ConditionalWeakTable<object, State> _states = new();

    // Drawable.Schedule(Action) is protected internal, so we must invoke via reflection.
    private static readonly MethodInfo? _scheduleMethod = AccessTools.Method(typeof(Drawable), "Schedule", new[] { typeof(Action) });

    public static MethodBase? TargetMethod()
    {
        Type? type = AccessTools.TypeByName(TARGET_TYPE_NAME);
        if (type == null)
            return null;

        return AccessTools.Method(type, "LoadComplete");
    }

    public static bool Prepare() => TargetMethod() != null;

    public static void Postfix(object __instance)
    {
        try
        {
            if (__instance is not Drawable owner)
                return;

            Type type = __instance.GetType();

            if (!AccessHelper.TryGet<IBindable<WorkingBeatmap>>(type, "beatmap", __instance, out var beatmap) || beatmap == null)
                return;
            if (!AccessHelper.TryGet<IBindable<RulesetInfo>>(type, "ruleset", __instance, out var ruleset) || ruleset == null)
                return;
            if (!AccessHelper.TryGet<IBindable<IReadOnlyList<Mod>>>(type, "mods", __instance, out var mods) || mods == null)
                return;
            if (!AccessHelper.TryGet<GridContainer>(type, "ratingAndNameContainer", __instance, out var ratingAndNameContainer) || ratingAndNameContainer == null)
                return;

            // GridContainer.ColumnDimensions has no getter; read the private backing field.
            if (!AccessHelper.TryGet<Dimension[]>(typeof(GridContainer), "columnDimensions", ratingAndNameContainer, out var existingColumns) || existingColumns == null)
                return;

            var sunnyDisplay = new StarRatingDisplay(default, animated: true)
            {
                Anchor = Anchor.CentreLeft,
                Origin = Anchor.CentreLeft,
                Alpha = 0f,
            };

            var danText = new OsuSpriteText
            {
                Anchor = Anchor.CentreLeft,
                Origin = Anchor.CentreLeft,
                Font = OsuFont.Default.With(size: 14f),
                Colour = Colour4.White,
                Alpha = 0f,
            };

            // 태양 아이콘으로 변경 (필드명은 osu 버전마다 다를 수 있음)
            var starIconField = AccessTools.Field(typeof(StarRatingDisplay), "star")
                ?? AccessTools.Field(typeof(StarRatingDisplay), "starIcon");
            if (starIconField?.GetValue(sunnyDisplay) is SpriteIcon icon)
                icon.Icon = FontAwesome.Solid.Sun;

            var existingContent = ratingAndNameContainer.Content;

            var newRow = new Drawable[]
            {
                existingContent[0][0],
                existingContent[0][1],
                sunnyDisplay,
                new Box { Width = 4, Alpha = 0 },
                danText,
                new Box { Width = 6, Alpha = 0 },
                existingContent[0][2],
            };

            ratingAndNameContainer.ColumnDimensions = new[]
            {
                existingColumns[0],
                existingColumns[1],
                new Dimension(GridSizeMode.AutoSize),
                new Dimension(GridSizeMode.Absolute, 4),
                new Dimension(GridSizeMode.AutoSize),
                new Dimension(GridSizeMode.Absolute, 6),
                existingColumns[2],
            };
            ratingAndNameContainer.Content = new[] { newRow };

            var state = new State(owner, beatmap, ruleset, mods, sunnyDisplay, danText);
            // ConditionalWeakTable keeps State alive exactly as long as __instance (DifficultyDisplay) is alive.
            // BindValueChanged closures use WeakReference<State> so long-lived global bindables cannot pin State.
            _states.Add(__instance, state);

            var weakState = new WeakReference<State>(state);

            beatmap.BindValueChanged(_ => { if (weakState.TryGetTarget(out var s)) s.TriggerRecalculate(); });
            ruleset.BindValueChanged(_ => { if (weakState.TryGetTarget(out var s)) s.TriggerRecalculate(); });
            mods.BindValueChanged(m =>
            {
                if (!weakState.TryGetTarget(out var s)) return;
                s.RebuildSettingTracker(m.NewValue);
                s.TriggerRecalculate();
            }, true);

            SunnyState.Register(state);
        }
        catch (Exception e)
        {
            Trace.WriteLine($"[LazerSR] DifficultyDisplayPatch.Postfix failed: {e}");
        }
    }

    internal static void ScheduleOn(Drawable owner, Action action)
    {
        if (_scheduleMethod == null)
            return;

        _scheduleMethod.Invoke(owner, new object[] { action });
    }

    private sealed class State : ISunnyStateSubscriber
    {
        private readonly Drawable owner;
        private readonly IBindable<WorkingBeatmap> beatmap;
        private readonly IBindable<RulesetInfo> ruleset;
        private readonly IBindable<IReadOnlyList<Mod>> mods;
        private readonly StarRatingDisplay sunnyDisplay;
        private readonly OsuSpriteText danText;
        private double lastSunny;

        private CancellationTokenSource? cts;
        private ModSettingChangeTracker? settingTracker;

        private readonly object _ctsLock = new();

        public State(Drawable owner, IBindable<WorkingBeatmap> beatmap,
            IBindable<RulesetInfo> ruleset, IBindable<IReadOnlyList<Mod>> mods,
            StarRatingDisplay sunnyDisplay, OsuSpriteText danText)
        {
            this.owner = owner;
            this.beatmap = beatmap;
            this.ruleset = ruleset;
            this.mods = mods;
            this.sunnyDisplay = sunnyDisplay;
            this.danText = danText;
        }

        ~State()
        {
            settingTracker?.Dispose();
            cts?.Cancel();
            cts?.Dispose();
        }

        public void RebuildSettingTracker(IReadOnlyList<Mod> newMods)
        {
            settingTracker?.Dispose();
            settingTracker = null;

            if (newMods.Count == 0)
                return;

            settingTracker = new ModSettingChangeTracker(newMods);
            settingTracker.SettingChanged += _ => TriggerRecalculate();
        }

        public void TriggerRecalculate()
        {
            CancellationToken token;
            lock (_ctsLock)
            {
                cts?.Cancel();
                cts?.Dispose();
                cts = new CancellationTokenSource();
                token = cts.Token;
            }

            ScheduleOn(owner, () => sunnyDisplay.Alpha = 0f);

            var workingBeatmap = beatmap.Value;
            var currentRuleset = ruleset.Value;
            var currentMods = mods.Value;

            if (workingBeatmap == null || currentRuleset == null || currentMods == null)
                return;

            if (currentRuleset.ShortName != "mania")
                return;

            Task.Run(() =>
            {
                try
                {
                    token.ThrowIfCancellationRequested();

                    IBeatmap playable = workingBeatmap.GetPlayableBeatmap(currentRuleset, currentMods, token);
                    double sunny = SunnyCalculatorRunner.Calculate(playable, currentMods, token);

                    token.ThrowIfCancellationRequested();

                    ScheduleOn(owner, () =>
                    {
                        if (token.IsCancellationRequested)
                            return;

                        sunnyDisplay.Current.Value = new StarDifficulty(sunny, 0);
                        sunnyDisplay.Alpha = SunnyState.Enabled && currentRuleset.ShortName == "mania" ? 1f : 0f;
                        lastSunny = sunny;
                        UpdateDanText();
                    });
                }
                catch (OperationCanceledException)
                {
                    // expected
                }
                catch (Exception e)
                {
                    Trace.WriteLine($"[LazerSR] sunny calc failed: {e}");
                }
            }, token);
        }

        public void OnEnabledChanged(bool enabled)
        {
            if (enabled)
                TriggerRecalculate();
            else
                ScheduleOn(owner, () =>
                {
                    sunnyDisplay.Alpha = 0f;
                    danText.Alpha = 0f;
                });
        }

        public void OnFeatureChanged(SunnyFeature feature, bool enabled)
        {
            if (feature == SunnyFeature.Dan)
                ScheduleOn(owner, UpdateDanText);
        }

        private void UpdateDanText()
        {
            bool isMania = ruleset.Value?.ShortName == "mania";
            if (!isMania || !SunnyState.Enabled)
            {
                danText.Alpha = 0f;
                return;
            }
            if (!SunnyState.DanEnabled)
            {
                danText.Alpha = 0f;
                return;
            }
            var info = DanCalculator.GetDanInfo(lastSunny);
            danText.Text = $"{info.Name} {info.Tier}";
            danText.Alpha = 1f;
        }
    }
}
