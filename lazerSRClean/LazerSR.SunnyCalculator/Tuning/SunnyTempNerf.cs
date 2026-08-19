using System;

namespace LazerSR.SunnyCalculator.Tuning;

/// <summary>
/// 임시 sunnySR 억제. 상수 조정본(<see cref="SunnyConstants"/>)이 전체적으로 값을 키운 것을 되돌린다.
/// 짧은 맵 보정 직후 · 고SR 억제 직전에 한 번 곱한다 (<c>Difficulty/Skills/Strain.cs</c>).
///
/// 억제량은 <see cref="anchors"/> 4점을 정확히 지나는 3차 곡선이다. 앵커만 고치면 곡선이 다시 유도되므로
/// 값 조정에 수식을 건드릴 필요가 없다. 임시 장치이므로 상수 조정이 끝나면 통째로 제거한다.
/// </summary>
public static class SunnyTempNerf
{
    /// <summary>(sunnySR, 그 지점에서 빼는 양). 오름차순, 3차 곡선이므로 정확히 4점.</summary>
    private static readonly (double Sr, double Drop)[] anchors =
    {
        (6.0, 0.3),
        (8.0, 0.4),
        (9.0, 0.5),
        (10.0, 0.7),
    };

    private static readonly double[] coefficients = newtonCoefficients();

    /// <summary>억제를 적용한 값.</summary>
    public static double Apply(double sr) => sr <= 0.0 ? sr : sr * FactorFor(sr);

    /// <summary>억제 배율. 표시·디버그용으로 따로 열어둔다.</summary>
    public static double FactorFor(double sr) => sr <= 0.0 ? 1.0 : (sr - dropAt(sr)) / sr;

    private static double dropAt(double sr)
    {
        // 마지막 앵커 위로는 그 지점의 기울기로 직선 연장한다. 3차식을 그대로 늘리면 기울기가 1을 넘어
        // 억제 후 값이 되레 감소하는 구간이 생긴다 (현재 앵커에서는 x≈13.2부터).
        (double lastSr, double lastDrop) = anchors[^1];
        double drop = sr <= lastSr ? evaluate(sr) : lastDrop + slopeAt(lastSr) * (sr - lastSr);

        // 곡선은 단조증가라 낮은 쪽에서 음수가 된다 = 억제가 아니라 증폭. 0에서 끊는다.
        return Math.Max(0.0, drop);
    }

    private static double evaluate(double x)
    {
        double result = coefficients[0];
        double product = 1.0;

        for (int i = 1; i < coefficients.Length; i++)
        {
            product *= x - anchors[i - 1].Sr;
            result += coefficients[i] * product;
        }

        return result;
    }

    private static double slopeAt(double x)
    {
        double slope = 0.0;

        for (int i = 1; i < coefficients.Length; i++)
        {
            double termSum = 0.0;

            for (int skipped = 0; skipped < i; skipped++)
            {
                double product = 1.0;

                for (int j = 0; j < i; j++)
                {
                    if (j != skipped)
                        product *= x - anchors[j].Sr;
                }

                termSum += product;
            }

            slope += coefficients[i] * termSum;
        }

        return slope;
    }

    /// <summary>뉴턴 분할차분 — 앵커를 정확히 지나는 보간 다항식의 계수.</summary>
    private static double[] newtonCoefficients()
    {
        int count = anchors.Length;
        double[] working = new double[count];
        double[] coeff = new double[count];

        for (int i = 0; i < count; i++)
            working[i] = anchors[i].Drop;

        coeff[0] = working[0];

        for (int order = 1; order < count; order++)
        {
            for (int i = 0; i < count - order; i++)
                working[i] = (working[i + 1] - working[i]) / (anchors[i + order].Sr - anchors[i].Sr);

            coeff[order] = working[0];
        }

        return coeff;
    }
}
