namespace LazerSR.Hook.Patches;

public record DanInfo(string Name, string Tier);

public static class DanCalculator
{
    private static readonly string[] DAN_NAMES =
    {
        "1st","2nd","3rd","4th","5th","6th","7th","8th","9th","10th",
        "Alpha","Beta","Luminal","Gamma","Tachyon","Delta","Epsilon","Zeta","Eta","Theta"
    };

    private static readonly double[] DAN_THRESHOLDS =
    {
        3.3, 3.7, 4.2, 4.5, 4.9, 5.3, 5.6, 6.0, 6.2, 6.5,
        6.8, 7.1, 7.3, 7.7, 7.9, 8.2, 9.1, 9.6, 10.2, 10.8
    };

    private static readonly (double Lower, double Upper)[] _boundaries;

    static DanCalculator()
    {
        int n = DAN_THRESHOLDS.Length;
        _boundaries = new (double, double)[n];

        for (int i = 0; i < n; i++)
        {
            double lower = i == 0
                ? DAN_THRESHOLDS[0] - (DAN_THRESHOLDS[1] - DAN_THRESHOLDS[0]) / 2.0
                : (DAN_THRESHOLDS[i - 1] + DAN_THRESHOLDS[i]) / 2.0;

            double upper = i == n - 1
                ? DAN_THRESHOLDS[n - 1] + (DAN_THRESHOLDS[n - 1] - DAN_THRESHOLDS[n - 2]) / 2.0
                : (DAN_THRESHOLDS[i] + DAN_THRESHOLDS[i + 1]) / 2.0;

            _boundaries[i] = (lower, upper);
        }
    }

    public static DanInfo GetDanInfo(double sunnysr)
    {
        int n = DAN_THRESHOLDS.Length;

        if (sunnysr < _boundaries[0].Lower)
            return new DanInfo(DAN_NAMES[0], "low");

        if (sunnysr >= _boundaries[n - 1].Upper)
            return new DanInfo(DAN_NAMES[n - 1], "high");

        for (int i = 0; i < n; i++)
        {
            var (lower, upper) = _boundaries[i];
            if (sunnysr < upper)
            {
                double t = (sunnysr - lower) / (upper - lower);
                t = Math.Max(0.0, Math.Min(t, 1.0));
                string tier = t < 1.0 / 3.0 ? "low" : t < 2.0 / 3.0 ? "mid" : "high";
                return new DanInfo(DAN_NAMES[i], tier);
            }
        }

        return new DanInfo(DAN_NAMES[n - 1], "high");
    }
}
