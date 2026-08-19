using System;
using System.Collections.Generic;
using LazerSR.Hook.Training.Patterns;

namespace LazerSR.Hook.Training;

/// <summary>
/// 세부패턴 하나. 생성 규칙과 탐색 상수를 함께 들고 있다.
/// </summary>
/// <param name="StartBpm">단기 탐색 시작 bpm. 사용자가 대기 화면에서 수정할 수 있고, 측정이 끝나면 확정값으로 덮인다.</param>
/// <param name="BpmSnap">bpm 격자. 탐색은 이 배수로만 움직인다.</param>
/// <param name="RestMs">휴식상승에서 세트 사이에 넣는 휴식. <c>TrainingSequencer</c>의 lookahead보다 커야 한다.</param>
public record SubPattern(
    string Id,
    string DisplayName,
    Func<IPatternGenerator> CreateGenerator,
    double StartBpm,
    int BpmSnap,
    double RestMs);

/// <summary>
/// 패턴 대분류 하나. 목록 표시와 묶음 단위일 뿐이고 탐색 상수는 갖지 않는다 —
/// 정확도 임계는 패턴과 무관하게 <b>단기/무한 두 가지</b>뿐이고 사용자가 대기 화면에서 직접 정한다.
/// </summary>
public record PatternGroup(
    string Id,
    string DisplayName,
    IReadOnlyList<SubPattern> SubPatterns);

/// <summary>
/// 훈련 대상 패턴 목록. 대분류 7개는 전부 정의해두고, 세부패턴은 생성 규칙이 만들어진 것만 채운다.
/// </summary>
public static class PatternCatalog
{
    public static readonly IReadOnlyList<PatternGroup> Groups = new[]
    {
        new PatternGroup("stream", "스트림", new[]
        {
            new SubPattern("rollstream", "드르륵", () => new RollStreamGenerator(), 100, 5, 2000),
        }),
        new PatternGroup("jumpstream", "점프스트림", new[]
        {
            new SubPattern("jumpstream", "점프스트림", () => new JumpStreamGenerator(), 80, 5, 2000),
        }),
        new PatternGroup("handstream", "핸드스트림", new[]
        {
            new SubPattern("singlehandstream", "싱글핸드스트림", () => new SingleHandStreamGenerator(), 80, 5, 2000),
            new SubPattern("densehandstream", "덴시핸드스트림", () => new DenseHandStreamGenerator(), 80, 5, 2000),
            new SubPattern("continuoushandstream", "연속핸드스트림", () => new ContinuousHandStreamGenerator(), 80, 5, 2000),
        }),
        new PatternGroup("dump", "덤프", new[]
        {
            new SubPattern("singledump", "싱글덤프", () => new SingleDumpGenerator(), 100, 5, 2000),
            new SubPattern("densedump", "덴시덤프", () => new DenseDumpGenerator(), 100, 5, 2000),
        }),
        new PatternGroup("tech", "테크", new[]
        {
            new SubPattern("rolltech", "드르륵테크", () => new RollTechGenerator(), 100, 5, 2000),
            new SubPattern("axisjack", "축연타", () => new AxisJackGenerator(), 100, 5, 2000),
        }),
        new PatternGroup("minijack", "미니잭", new[]
        {
            new SubPattern("minijack13", "13미니잭", () => new MiniJack13Generator(), 120, 5, 2000),
            new SubPattern("minijack12", "12미니잭", () => new MiniJack12Generator(), 130, 5, 2000),
            new SubPattern("minijack22", "22미니잭", () => new MiniJack22Generator(), 120, 5, 2000),
        }),
        new PatternGroup("chordjack", "코드잭", new[]
        {
            new SubPattern("chordjack23", "23코드잭", () => new ChordJack23Generator(), 110, 2, 2000),
            new SubPattern("chordjack24", "24코드잭", () => new ChordJack24Generator(), 110, 2, 2000),
            new SubPattern("chordjack34", "34코드잭", () => new ChordJack34Generator(), 90, 2, 2000),
        }),
    };

    /// <summary>대분류 순서대로 펼친 전체 세부패턴 목록.</summary>
    public static IReadOnlyList<SubPattern> AllSubPatterns { get; } = flatten();

    private static SubPattern[] flatten()
    {
        var all = new List<SubPattern>();

        foreach (var group in Groups)
            all.AddRange(group.SubPatterns);

        return all.ToArray();
    }
}
