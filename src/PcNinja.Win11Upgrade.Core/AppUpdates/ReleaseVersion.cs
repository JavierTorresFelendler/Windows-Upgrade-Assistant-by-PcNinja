using System.Globalization;
using System.Text.RegularExpressions;

namespace PcNinja.Win11Upgrade.Core;

/// <summary>Strict SemVer precedence; build metadata is validated but does not affect ordering.</summary>
public sealed class ReleaseVersion : IComparable<ReleaseVersion>
{
    private static readonly Regex Syntax = new(
        @"^(?<major>0|[1-9][0-9]*)\.(?<minor>0|[1-9][0-9]*)\.(?<patch>0|[1-9][0-9]*)(?:-(?<pre>[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?(?:\+(?<build>[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?$",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking, TimeSpan.FromMilliseconds(250));
    private readonly string[] _prerelease;
    public int Major { get; }
    public int Minor { get; }
    public int Patch { get; }
    public string Value { get; }
    public bool IsStable => _prerelease.Length == 0;
    public bool IsBeta => _prerelease.Length == 2 && _prerelease[0] == "beta" &&
        int.TryParse(_prerelease[1], NumberStyles.None, CultureInfo.InvariantCulture, out var number) && number > 0 && number <= 65534;
    public string Prerelease => string.Join('.', _prerelease);

    private ReleaseVersion(int major, int minor, int patch, string value, string[] prerelease)
    {
        Major = major; Minor = minor; Patch = patch; Value = value; _prerelease = prerelease;
    }

    public static ReleaseVersion Parse(string text) => TryParse(text, out var version) ? version! :
        throw new FormatException("Expected a strict three-part semantic release version.");

    public static bool TryParse(string? text, out ReleaseVersion? version)
    {
        version = null;
        if (string.IsNullOrEmpty(text) || text.Length > 256) return false;
        var match = Syntax.Match(text);
        if (!match.Success || match.Length != text.Length || !int.TryParse(match.Groups["major"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var major) ||
            !int.TryParse(match.Groups["minor"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var minor) ||
            !int.TryParse(match.Groups["patch"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var patch)) return false;
        var prerelease = match.Groups["pre"].Success ? match.Groups["pre"].Value.Split('.') : [];
        if (prerelease.Any(part => part.Length > 1 && part[0] == '0' && part.All(char.IsAsciiDigit))) return false;
        version = new(major, minor, patch, text, prerelease);
        return true;
    }

    public int CompareTo(ReleaseVersion? other)
    {
        if (other is null) return 1;
        var comparison = Major.CompareTo(other.Major);
        if (comparison == 0) comparison = Minor.CompareTo(other.Minor);
        if (comparison == 0) comparison = Patch.CompareTo(other.Patch);
        if (comparison != 0) return comparison;
        if (IsStable || other.IsStable) return IsStable == other.IsStable ? 0 : IsStable ? 1 : -1;
        for (var index = 0; index < Math.Min(_prerelease.Length, other._prerelease.Length); index++)
        {
            var left = _prerelease[index];
            var right = other._prerelease[index];
            var leftNumeric = left.All(char.IsAsciiDigit);
            var rightNumeric = right.All(char.IsAsciiDigit);
            comparison = leftNumeric && rightNumeric
                ? left.Length == right.Length ? string.CompareOrdinal(left, right) : left.Length.CompareTo(right.Length)
                : leftNumeric != rightNumeric ? leftNumeric ? -1 : 1 : string.CompareOrdinal(left, right);
            if (comparison != 0) return comparison;
        }
        return _prerelease.Length.CompareTo(other._prerelease.Length);
    }

    public override string ToString() => Value;
}
