namespace Waa.Core;

internal static class StringPathExtensions
{
    public static bool Contains(this string value, char character, StringComparison comparison)
    {
        _ = comparison;
        return value.Contains(character);
    }

    public static int LastIndexOf(this string value, char character, StringComparison comparison)
    {
        _ = comparison;
        return value.LastIndexOf(character);
    }

    public static bool StartsWith(this string value, char character, StringComparison comparison)
    {
        _ = comparison;
        return value.Length > 0 && value[0] == character;
    }
}
