namespace LazerSR.Hook.Data;

public static class SectionTimerState
{
    /// <summary>Song select side: updated on every map/ruleset/mods change.</summary>
    public static volatile SectionInterval[]? PendingSections;

    /// <summary>Gameplay side: snapshot of PendingSections taken on Player.LoadComplete.</summary>
    public static volatile SectionInterval[]? ActiveSections;
}
