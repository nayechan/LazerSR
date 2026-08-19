using System;

namespace LazerSR.Hook.Training;

/// <summary>
/// 세부패턴 하나의 생성 규칙. 마디 단위로 생성한다.
/// <para>
/// 구현체는 <b>bpm을 알지 못한다</b> — 슬롯 배치만 결정하고, ms 변환은 전적으로 주입기 책임이다.
/// </para>
/// </summary>
public interface IPatternGenerator
{
    /// <param name="rng">난수원. 시드 고정 없이 매 세션 새로 만든다.</param>
    /// <param name="previous">
    /// 직전 마디. 마디 경계에서 의도치 않은 잭이 생기는 것을 규칙이 피할 수 있도록 통째로 넘긴다.
    /// 경계가 항상 결정론적인 패턴(예: 24코드잭)은 무시해도 된다. 세션 첫 마디면 <c>null</c>.
    /// </param>
    TrainingMeasure Generate(Random rng, TrainingMeasure? previous);
}
