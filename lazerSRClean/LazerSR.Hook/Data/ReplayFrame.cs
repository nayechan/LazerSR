namespace LazerSR.Hook.Data;

public readonly struct ReplayFrame
{
    public readonly double Time;
    public readonly int Score, Combo;
    public readonly int J320, J300, J200, J100, J50, JMiss;

    public ReplayFrame(double time, int score, int combo,
                       int j320, int j300, int j200, int j100, int j50, int jMiss)
    {
        Time = time; Score = score; Combo = combo;
        J320 = j320; J300 = j300; J200 = j200; J100 = j100; J50 = j50; JMiss = jMiss;
    }
}
