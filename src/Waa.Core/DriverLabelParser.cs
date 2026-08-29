namespace Waa.Core;

public static class DriverLabelParser
{
    public static DriverIdentity Parse(string? rawLabel)
    {
        if (string.IsNullOrWhiteSpace(rawLabel))
        {
            throw new ReportValidationException("Driver label is blank.");
        }

        var trimmed = rawLabel.Trim();
        var splitIndex = FindFirstWhitespace(trimmed);
        if (splitIndex <= 0)
        {
            throw new ReportValidationException(
                $"Driver label '{rawLabel}' does not contain a Driver Code followed by Driver Name.");
        }

        var driverCode = trimmed[..splitIndex];
        var driverName = trimmed[splitIndex..].Trim();

        if (driverCode.Any(character => !char.IsAsciiLetterOrDigit(character)))
        {
            throw new ReportValidationException(
                $"Driver Code '{driverCode}' contains something other than letters or digits.");
        }

        if (driverName.Length == 0)
        {
            throw new ReportValidationException(
                $"Driver label '{rawLabel}' is missing the Driver Name after Driver Code '{driverCode}'.");
        }

        return new DriverIdentity(driverCode.ToUpperInvariant(), driverName, rawLabel);
    }

    public static string ParseDriverLeader(string? rawDriverLeader)
    {
        if (string.IsNullOrWhiteSpace(rawDriverLeader))
        {
            throw new ReportValidationException("Driver Leader code is blank.");
        }

        var code = rawDriverLeader.Trim();
        if (code.Any(character => !char.IsAsciiLetterOrDigit(character)))
        {
            throw new ReportValidationException(
                $"Driver Leader code '{rawDriverLeader}' contains something other than letters or digits.");
        }

        return code.ToUpperInvariant();
    }

    private static int FindFirstWhitespace(string value)
    {
        for (var index = 0; index < value.Length; index++)
        {
            if (char.IsWhiteSpace(value[index]))
            {
                return index;
            }
        }

        return -1;
    }
}
