using System;
using System.Runtime.CompilerServices;

namespace BNDK.Maui.Utils;

internal static class DoubleExtensions
{
    /// <summary>
    /// Returns whether or not the double is "close" to 0. Same as <see cref="T:DoubleUtil.AreClose"/> but faster.
    /// </summary>
    /// <returns>true if close to zero</returns>
    /// <param name="self">The double to compare to 0</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsZero(this double self) => Math.Abs(self) < 10.0 * double.Epsilon;
    
    /// <summary>
    /// Returns double clamped to the inclusive range of min and max.
    /// </summary>
    /// <param name="self">The value to be clamped.</param>
    /// <param name="min">The lower bound of the result.</param>
    /// <param name="max">The upper bound of the result.</param>
    /// <returns>value if min ≤ value ≤ max. -or- min if value &lt; min. -or- max if max &lt; value. -or- NaN if value equals NaN.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double Clamp(this double self, double min, double max)
    {
        if (max < min)
        {
            return max;
        }
        if (self < min)
        {
            return min;
        }
        return self > max ? max : self;
    }
}