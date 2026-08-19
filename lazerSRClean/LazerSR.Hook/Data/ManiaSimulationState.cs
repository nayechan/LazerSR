using System;
using System.Diagnostics;
using osu.Game.Scoring;

namespace LazerSR.Hook.Data;

/// <summary>
/// osu!가 실제로 발행한 판정 하나. <see cref="ManiaJudgementSimulation"/>이 채운다.
/// </summary>
/// <param name="Column">컬럼 인덱스.</param>
/// <param name="Time">판정이 일어난 게임플레이 시각(<c>JudgementResult.TimeAbsolute</c>). 입력이 원인이면 그 입력의 리플레이 프레임 시각과 같다.</param>
/// <param name="Offset">노트 시각으로부터의 오차(<c>JudgementResult.TimeOffset</c>). 음수면 얼리, 양수면 레이트.</param>
/// <param name="NoteTime">대상 노트의 판정 시각. 같은 순간에 여러 판정이 몰릴 때 짝을 고르는 데 쓴다.</param>
/// <param name="IsTail">롱노트 릴리즈 판정인지.</param>
public readonly record struct ManiaSimJudgement(int Column, double Time, double Offset, double NoteTime, bool IsTail);

/// <summary>
/// 시뮬레이션 한 판의 결과. 로딩 화면에서 만들어지고 게임플레이 화면이 소비한다.
/// <see cref="Ready"/>가 false인 동안 로딩 화면이 대기한다.
/// </summary>
public sealed class ManiaSimulationResult
{
    /// <summary>
    /// 이 시간 동안 <b>진척이 전혀 없으면</b> 시뮬레이션이 죽은 것으로 보고 로딩을 풀어준다.
    ///
    /// <para>
    /// 총 소요 시간으로 자르지 않는 이유: 정상 소요 시간이 맵 길이와 기기 성능에 따라 크게 달라
    /// 시계 기반 한도는 멀쩡히 돌던 시뮬레이션을 잘라먹는다 (2026-07-29에 13.5분 맵이 95.4%에서 잘렸다).
    /// 진척 여부로 판단하면 아무리 오래 걸려도 안 자르면서 사고만 걸러낼 수 있다.
    /// </para>
    /// </summary>
    private static readonly TimeSpan heartbeat_timeout = TimeSpan.FromSeconds(10);

    /// <summary>어느 스코어의 결과인지. 소비 측이 <c>ReferenceEquals</c>로 확인한다.</summary>
    public readonly Score Score;

    /// <summary>마지막으로 진척이 있었던 시점. 시뮬레이션이 진행할 때마다 갱신한다.</summary>
    public readonly Stopwatch Heartbeat = Stopwatch.StartNew();

    public ManiaSimulationResult(Score score)
    {
        Score = score;
    }

    public volatile bool Ready;

    public ManiaSimJudgement[] Judgements = [];

    /// <summary>분석 진행률 0~1. 진행 표시용.</summary>
    public double Progress;

    /// <summary>시뮬레이션이 살아서 진행 중인지.</summary>
    public bool Alive => Heartbeat.Elapsed < heartbeat_timeout;

    /// <summary>로딩 화면을 계속 붙잡아 둬야 하는지.</summary>
    public bool ShouldBlockLoading => !Ready && Alive;
}

/// <summary>
/// 시뮬레이션 결과 단일 슬롯. 리플레이는 한 번에 하나만 재생되므로 슬롯 하나로 충분하다.
/// </summary>
public static class ManiaSimulationState
{
    public static ManiaSimulationResult? Current;
}
