using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.RegularExpressions;

namespace HabitTracker.Core;

public enum WeightUnit { Pounds, Kilograms }
public enum SetKind { Working, Warmup }

/// <summary>An immutable snapshot of the physical plates on a bar, in one unit.</summary>
public sealed partial class PlateLoad
{
    public decimal BarWeight { get; }
    public ReadOnlyCollection<decimal> LeftPlates { get; }
    public ReadOnlyCollection<decimal> RightPlates { get; }
    public WeightUnit Unit { get; }
    public decimal LeftWeight => LeftPlates.Sum();
    public decimal RightWeight => RightPlates.Sum();
    public decimal TotalWeight => BarWeight + LeftWeight + RightWeight;
    public bool IsBalanced => LeftWeight == RightWeight;
    public string Symbol => Unit == WeightUnit.Pounds ? "lb" : "kg";
    public string Formula => $"{Format(BarWeight)} + ({FormatStack(LeftPlates)}) + ({FormatStack(RightPlates)}) = {Format(TotalWeight)} {Symbol}";

    public PlateLoad(decimal barWeight, IEnumerable<decimal> leftPlates,
        IEnumerable<decimal> rightPlates, WeightUnit unit)
    {
        if (!Enum.IsDefined(unit)) throw new ArgumentException("Choose lb or kg.");
        if (barWeight is < 0 or > 100) throw new ArgumentException("Bar weight must be between 0 and 100.");
        var left = ValidateStack(leftPlates);
        var right = ValidateStack(rightPlates);
        BarWeight = barWeight;
        LeftPlates = Array.AsReadOnly(left);
        RightPlates = Array.AsReadOnly(right);
        Unit = unit;
        if (TotalWeight > 2000) throw new ArgumentException("Total load must not exceed 2,000.");
    }

    public static PlateLoad Parse(decimal barWeight, string? left, string? right,
        bool mirror, WeightUnit unit)
    {
        var leftPlates = ParseStack(left, unit);
        return new(barWeight, leftPlates, mirror ? leftPlates : ParseStack(right, unit), unit);
    }

    /// <summary>Accepts e.g. 45lbs+25lbs+10; suffixes must match the chosen unit.</summary>
    public static decimal[] ParseStack(string? expression, WeightUnit unit)
    {
        if (!Enum.IsDefined(unit)) throw new ArgumentException("Choose lb or kg.");
        if (string.IsNullOrWhiteSpace(expression)) return [];
        if (expression.Length > 512) throw new ArgumentException("Plate expression is too long.");
        var terms = expression.Split('+');
        if (terms.Length > 16) throw new ArgumentException("Use at most 16 plates per side.");
        return terms.Select(term =>
        {
            var match = PlateTerm().Match(term.Trim());
            if (!match.Success || !decimal.TryParse(match.Groups[1].Value,
                    NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var weight))
                throw new ArgumentException("Enter plates separated by +, for example 45 + 25 + 10. Use a decimal point for fractional plates.");
            var suffix = match.Groups[2].Value.ToLowerInvariant();
            if (suffix.Length > 0 && (unit == WeightUnit.Pounds ? suffix is not ("lb" or "lbs") : suffix != "kg"))
                throw new ArgumentException("Plate suffixes must match the selected unit.");
            if (weight is <= 0 or > 250) throw new ArgumentException("Each plate must be greater than 0 and at most 250.");
            return weight;
        }).ToArray();
    }

    public static decimal Convert(decimal value, WeightUnit from, WeightUnit to)
    {
        if (!Enum.IsDefined(from) || !Enum.IsDefined(to)) throw new ArgumentException("Unknown weight unit.");
        return from == to ? value : from == WeightUnit.Pounds ? value * 0.45359237m : value / 0.45359237m;
    }

    public static string Format(decimal value) => value.ToString("0.##", CultureInfo.InvariantCulture);
    public static string FormatStack(IEnumerable<decimal> plates) => string.Join(" + ", plates.Select(Format)) is { Length: > 0 } text ? text : "0";

    private static decimal[] ValidateStack(IEnumerable<decimal>? source)
    {
        if (source is null) throw new ArgumentException("A plate stack is required; use an empty array for an empty sleeve.");
        var plates = source.Take(17).ToArray();
        if (plates.Length > 16 || plates.Any(p => p is <= 0 or > 250))
            throw new ArgumentException("Use at most 16 plates per side, each greater than 0 and at most 250.");
        return plates;
    }

    [GeneratedRegex(@"^(\d+(?:\.\d{1,3})?)\s*(lbs?|kg)?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PlateTerm();
}
