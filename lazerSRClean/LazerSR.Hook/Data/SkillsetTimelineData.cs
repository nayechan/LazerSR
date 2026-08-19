namespace LazerSR.Hook.Data;

// Per-interval skillset difficulty arrays from MinaCalc calc_timeline (1.0x rate).
// Each array has IntervalCount elements; index i = interval i (~0.5s each).
// Skillsets: Stream, JS (Jumpstream), HS (Handstream), Jack, CJ (Chordjack), Tech.
public record SkillsetTimelineData(
    float[] Stream, float[] Js, float[] Hs, float[] Jack, float[] Cj, float[] Tech,
    int IntervalCount);
