using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using HarmonyLib;
using osu.Framework.Input.Handlers;
using osu.Framework.Input.StateChanges;
using osu.Framework.Platform;
using osuTK.Input;

namespace LazerSR.Hook.Input;

/// <summary>
/// osu!가 <b>포커스를 잃은 상태에서도</b> 키 입력을 받게 한다.
/// <para>
/// 패턴 복제 모드에서만 켠다. 사용자는 대상 게임을 조작하고 osu!는 오버레이로 비춰 보기만 하는데,
/// 그러면 osu!는 포커스가 없어 SDL 기본 키보드 핸들러가 아무것도 받지 못한다.
/// </para>
/// <para>
/// 사용자가 실제로 누른 하드웨어 키를 <b>전달만</b> 한다 — 합성 입력을 만들지 않고, 대상 게임의
/// 입력 경로도 건드리지 않는다. 이 세션은 무한 트레이닝과 동일하게 점수 제출도 realm 기록도 없는
/// 완전 로컬이라 <c>docs\guides\safety.md</c>의 레드라인에 걸리는 것이 없다.
/// <b>모드 수명에 정확히 묶어둔다</b> — 상시 켜져 있으면 성격이 달라지는 기능이다.
/// </para>
/// </summary>
public static class RawKeyRelay
{
    private static RawKeyboardListener? listener;
    private static RelayHandler? handler;
    private static GameHost? boundHost;

    public static bool Running => handler != null;

    /// <summary>실패해도 예외를 던지지 않는다 — 입력 릴레이가 안 돼도 모드 자체는 돌아야 한다.</summary>
    public static void Start(GameHost host)
    {
        if (handler != null) return;

        try
        {
            var relay = new RelayHandler();
            relay.Initialize(host);

            if (!AddToHost(host, relay))
            {
                HookLog.Write("[LazerSR] RawKeyRelay: could not register input handler.");
                return;
            }

            var raw = new RawKeyboardListener();
            raw.KeyChanged += relay.Relay;

            if (!raw.Start())
            {
                raw.Dispose();
                RemoveFromHost(host, relay);
                HookLog.Write("[LazerSR] RawKeyRelay: raw input listener failed to start.");
                return;
            }

            handler = relay;
            listener = raw;
            boundHost = host;
        }
        catch (Exception ex)
        {
            HookLog.Write($"[LazerSR] RawKeyRelay.Start failed: {ex}");
            Stop();
        }
    }

    public static void Stop()
    {
        try
        {
            listener?.Dispose();
            listener = null;

            if (handler != null)
            {
                // 붙잡고 있던 키를 떼지 않고 끄면 osu!에 눌린 채로 남는다.
                handler.ReleaseAll();

                if (boundHost != null)
                    RemoveFromHost(boundHost, handler);
            }
        }
        catch (Exception ex)
        {
            HookLog.Write($"[LazerSR] RawKeyRelay.Stop failed: {ex}");
        }
        finally
        {
            handler = null;
            boundHost = null;
        }
    }

    /// <summary>
    /// <c>UserInputManager</c>가 매 프레임 폴링하는 대상이 <c>Host.AvailableInputHandlers</c>다.
    /// 이 프로퍼티는 <c>{ get; private set; }</c>이라 auto-property 백킹 필드를 통해서만 손댈 수 있다
    /// (<c>docs\guides\ui-patching.md</c> §3의 패턴).
    /// </summary>
    private static bool AddToHost(GameHost host, InputHandler add)
    {
        var current = GetHandlers(host);

        if (current == null) return false;

        // 맨 뒤 = 가장 낮은 우선순위. 기본 핸들러들을 밀어내지 않는다.
        SetHandlers(host, current.Value.Add(add));
        return true;
    }

    private static void RemoveFromHost(GameHost host, InputHandler remove)
    {
        var current = GetHandlers(host);

        if (current == null) return;

        SetHandlers(host, current.Value.Remove(remove));
    }

    private static ImmutableArray<InputHandler>? GetHandlers(GameHost host)
    {
        var field = AccessTools.Field(typeof(GameHost), "<AvailableInputHandlers>k__BackingField");

        return field?.GetValue(host) as ImmutableArray<InputHandler>?;
    }

    private static void SetHandlers(GameHost host, ImmutableArray<InputHandler> value)
    {
        AccessTools.Field(typeof(GameHost), "<AvailableInputHandlers>k__BackingField")?.SetValue(host, value);
    }

    /// <summary>실제로 프레임워크 입력 큐에 키를 밀어 넣는 핸들러.</summary>
    private class RelayHandler : InputHandler
    {
        private GameHost? host;

        /// <summary>우리가 눌러둔 키. 중복 발화 억제와 "떼기 누락" 방어에 함께 쓴다.</summary>
        private readonly HashSet<Key> held = new();

        public override bool IsActive => true;

        public override string Description => "LazerSR pattern copy relay";

        public override bool Initialize(GameHost host)
        {
            this.host = host;
            return true;
        }

        /// <summary><b>Raw Input 스레드에서 호출된다.</b></summary>
        public void Relay(Key key, bool pressed)
        {
            if (host == null) return;

            lock (held)
            {
                // osu!가 포커스를 가진 동안에는 SDL 기본 핸들러가 이미 같은 키를 넣는다.
                // 여기서도 넣으면 한 번 누른 키가 두 번 들어간다.
                if (host.IsActive.Value)
                {
                    // 누른 채로 포커스가 넘어갔다면 그 키는 영영 안 떼어진다 — 여기서 정리한다.
                    releaseAllLocked();
                    return;
                }

                if (pressed ? !held.Add(key) : !held.Remove(key))
                    return;   // 이미 눌린 키의 반복 발화(auto-repeat)이거나, 누른 적 없는 키의 떼기

                PendingInputs.Enqueue(new KeyboardKeyInput(key, pressed));
            }
        }

        public void ReleaseAll()
        {
            lock (held)
                releaseAllLocked();
        }

        private void releaseAllLocked()
        {
            if (held.Count == 0) return;

            foreach (var key in held)
                PendingInputs.Enqueue(new KeyboardKeyInput(key, false));

            held.Clear();
        }
    }
}
