namespace FrameAnalysis.Core.Analysis;

/// <summary>
/// Dense Cholesky factorization K = L·Lᵀ of a symmetric positive-definite matrix, factored once and solved for
/// each load case. Replaces the original's matrix inversion (fastinverse).
/// </summary>
internal sealed class Cholesky
{
    /// <summary>A pivot smaller than this fraction of its original diagonal term means the matrix is singular (a mechanism).</summary>
    private const double RelativePivotTolerance = 1e-9;

    private readonly double[,] _l;
    public int Size { get; }

    private Cholesky(double[,] l, int size)
    {
        _l = l;
        Size = size;
    }

    /// <summary>Factors <paramref name="a"/> (only the lower triangle is read). Returns null and the failing row when singular.</summary>
    public static Cholesky? TryFactor(double[,] a, out int failedRow)
    {
        var n = a.GetLength(0);
        var l = new double[n, n];
        for (var j = 0; j < n; j++)
        {
            var d = a[j, j];
            for (var k = 0; k < j; k++) d -= l[j, k] * l[j, k];
            if (!(d > RelativePivotTolerance * Math.Abs(a[j, j])) || !(a[j, j] > 0))
            {
                failedRow = j;
                return null;
            }
            var ljj = Math.Sqrt(d);
            l[j, j] = ljj;
            for (var i = j + 1; i < n; i++)
            {
                var s = a[i, j];
                for (var k = 0; k < j; k++) s -= l[i, k] * l[j, k];
                l[i, j] = s / ljj;
            }
        }
        failedRow = -1;
        return new Cholesky(l, n);
    }

    public double[] Solve(double[] b)
    {
        var n = Size;
        var y = new double[n];
        for (var i = 0; i < n; i++)
        {
            var s = b[i];
            for (var k = 0; k < i; k++) s -= _l[i, k] * y[k];
            y[i] = s / _l[i, i];
        }
        var x = new double[n];
        for (var i = n - 1; i >= 0; i--)
        {
            var s = y[i];
            for (var k = i + 1; k < n; k++) s -= _l[k, i] * x[k];
            x[i] = s / _l[i, i];
        }
        return x;
    }
}
