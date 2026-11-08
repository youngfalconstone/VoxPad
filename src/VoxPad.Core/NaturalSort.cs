using System.Globalization;

namespace VoxPad.Core;

/// <summary>Natural ("sound2" before "sound10") case-insensitive comparer.</summary>
public sealed class NaturalStringComparer : IComparer<string?>, IComparer<object?>
{
    public static readonly NaturalStringComparer Instance = new();
    private readonly CompareInfo _ci = CultureInfo.CurrentCulture.CompareInfo;

    public int Compare(object? x, object? y) => Compare(x?.ToString(), y?.ToString());

    public int Compare(string? a, string? b)
    {
        if (ReferenceEquals(a, b)) return 0;
        if (a is null) return -1;
        if (b is null) return 1;
        int i = 0, j = 0;
        while (i < a.Length && j < b.Length)
        {
            if (char.IsDigit(a[i]) && char.IsDigit(b[j]))
            {
                int si = i, sj = j;
                while (i < a.Length && char.IsDigit(a[i])) i++;
                while (j < b.Length && char.IsDigit(b[j])) j++;
                var na = a.AsSpan(si, i - si).TrimStart('0');
                var nb = b.AsSpan(sj, j - sj).TrimStart('0');
                if (na.Length != nb.Length) return na.Length.CompareTo(nb.Length);
                var c = na.SequenceCompareTo(nb);
                if (c != 0) return c;
            }
            else
            {
                int si = i, sj = j;
                while (i < a.Length && !char.IsDigit(a[i])) i++;
                while (j < b.Length && !char.IsDigit(b[j])) j++;
                var c = _ci.Compare(a.Substring(si, i - si), b.Substring(sj, j - sj), CompareOptions.IgnoreCase);
                if (c != 0) return c;
            }
        }
        return (a.Length - i).CompareTo(b.Length - j);
    }
}
