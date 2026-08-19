using System;
using System.Linq;

namespace LazerSR.SunnyCalculator.Tuning;

/// <summary>
/// Fits a personal step to one player's own accuracy records, against Jacobians baked once at the
/// universal point (<see cref="PersonalJacobianBaker"/>). Since SR is linear in the step near that
/// point, the whole fit is a single ridge-regularised 13x13 linear solve - no sunny call involved.
/// Port of <c>OsuScoreModel/dev/sunnyplus/personal_fit.py</c>'s <c>solve()</c>; see that file for the
/// derivation and the reasoning behind the ridge.
/// </summary>
public static class PersonalFitSolver
{
    /// <summary>Fixed client-side ridge, chosen offline by 5-fold cross-validation (handover_lazersr.md §3). The client never re-picks it.</summary>
    public const double Ridge = 0.01;

    private const int passes = 3;

    /// <summary>
    /// <paramref name="unitStep"/> is in unit space (box half-width = 1 unit), already clipped to
    /// [-0.5, 0.5] so the fit never claims a value outside where the Jacobian was measured.
    /// <paramref name="alpha"/>/<paramref name="beta"/> are the profiled-out nuisance terms of
    /// y ~= alpha + beta * SR - kept here (unlike the offline tool, which discards them) because the
    /// widget's "accuracy 96% at what SR" figure is exactly their inverse.
    /// </summary>
    public record Result(double[] UnitStep, double Alpha, double Beta);

    /// <summary>
    /// y[n] = -log(1 - accuracy) for each queued item, sr0[n] = its SR at the universal point,
    /// jac[n] = its unit-space Jacobian (length <see cref="PersonalBox.Tuned"/>.Length). All three
    /// arrays must be the same length and index the same items.
    /// </summary>
    public static Result Solve(double[] y, double[] sr0, double[][] jac, double ridge = Ridge)
    {
        int n = y.Length;
        int tunedCount = PersonalBox.Tuned.Length;
        int dim = 2 + tunedCount;

        if (n == 0)
            return new Result(new double[tunedCount], 0.0, 0.0);

        var xtx = new double[dim, dim];
        var xty = new double[dim];
        var row = new double[dim];

        for (int k = 0; k < n; k++)
        {
            row[0] = 1.0;
            row[1] = sr0[k];
            for (int c = 0; c < tunedCount; c++)
                row[2 + c] = jac[k][c];

            for (int i = 0; i < dim; i++)
            {
                xty[i] += row[i] * y[k];
                for (int j = 0; j < dim; j++)
                    xtx[i, j] += row[i] * row[j];
            }
        }

        // Seed beta from the no-personalisation fit (intercept + SR0 alone), for the ridge scale.
        double det = xtx[0, 0] * xtx[1, 1] - xtx[0, 1] * xtx[1, 0];
        double alpha0, beta0;

        if (Math.Abs(det) < 1e-12)
        {
            alpha0 = y.Average();
            beta0 = 0.0;
        }
        else
        {
            alpha0 = (xty[0] * xtx[1, 1] - xty[1] * xtx[0, 1]) / det;
            beta0 = (xtx[0, 0] * xty[1] - xtx[1, 0] * xty[0]) / det;
        }

        double[] coef = new double[dim];
        coef[0] = alpha0;
        coef[1] = beta0;
        double beta = beta0;

        for (int pass = 0; pass < passes; pass++)
        {
            // A player whose accuracy doesn't track difficulty at all - nothing to personalise.
            if (!double.IsFinite(beta) || Math.Abs(beta) < 1e-9)
                return new Result(new double[tunedCount], alpha0, beta);

            var penalised = (double[,])xtx.Clone();
            double penalty = ridge * n / (beta * beta);
            for (int c = 0; c < tunedCount; c++)
                penalised[2 + c, 2 + c] += penalty;

            double[]? solved = linearSolve(penalised, xty);
            if (solved == null)
                return new Result(new double[tunedCount], alpha0, beta);

            coef = solved;
            beta = coef[1];
        }

        if (!double.IsFinite(beta) || Math.Abs(beta) < 1e-9)
            return new Result(new double[tunedCount], alpha0, beta);

        var unitStep = new double[tunedCount];
        for (int c = 0; c < tunedCount; c++)
        {
            double d = coef[2 + c] / beta;
            unitStep[c] = double.IsFinite(d) ? Math.Clamp(d, -0.5, 0.5) : 0.0;
        }

        // coef[0]은 마지막 ridge 반복까지 거친 alpha다 - alpha0(반복 시작 전, jac 없이 SR0만으로
        // 구한 값)을 여기서 쓰면 beta/unitStep과 짝이 안 맞는 모델을 섞어 쓰는 셈이 된다.
        return new Result(unitStep, coef[0], beta);
    }

    /// <summary>unit-space step -> delta in real constant units, for <see cref="PersonalDiff"/>.</summary>
    public static double[] ToRealDeltas(double[] unitStep)
    {
        var deltas = new double[SunnyConstants.Count];

        for (int c = 0; c < PersonalBox.Tuned.Length; c++)
            deltas[PersonalBox.Index[c]] = unitStep[c] * PersonalBox.RealWidth(c);

        return deltas;
    }

    /// <summary>Gaussian elimination with partial pivoting. Null if the system is (near-)singular.</summary>
    private static double[]? linearSolve(double[,] a, double[] b)
    {
        int dim = b.Length;
        var m = (double[,])a.Clone();
        var v = (double[])b.Clone();

        for (int col = 0; col < dim; col++)
        {
            int pivotRow = col;
            double pivotValue = Math.Abs(m[col, col]);

            for (int row = col + 1; row < dim; row++)
            {
                double candidate = Math.Abs(m[row, col]);
                if (candidate > pivotValue)
                {
                    pivotValue = candidate;
                    pivotRow = row;
                }
            }

            if (pivotValue < 1e-12)
                return null;

            if (pivotRow != col)
            {
                for (int c = 0; c < dim; c++)
                    (m[col, c], m[pivotRow, c]) = (m[pivotRow, c], m[col, c]);

                (v[col], v[pivotRow]) = (v[pivotRow], v[col]);
            }

            double pivot = m[col, col];

            for (int row = col + 1; row < dim; row++)
            {
                double factor = m[row, col] / pivot;
                if (factor == 0.0) continue;

                for (int c = col; c < dim; c++)
                    m[row, c] -= factor * m[col, c];

                v[row] -= factor * v[col];
            }
        }

        var x = new double[dim];

        for (int row = dim - 1; row >= 0; row--)
        {
            double sum = v[row];
            for (int c = row + 1; c < dim; c++)
                sum -= m[row, c] * x[c];

            x[row] = sum / m[row, row];
        }

        return x;
    }
}
