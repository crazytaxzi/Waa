using System.Text;

namespace Waa.Core;

internal static class CsvReader
{
    public static IReadOnlyList<string[]> Read(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var rows = new List<string[]>();
        var fields = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;
        var fieldStarted = false;

        for (var index = 0; index < text.Length; index++)
        {
            var character = text[index];

            if (inQuotes)
            {
                if (character == '"')
                {
                    if (index + 1 < text.Length && text[index + 1] == '"')
                    {
                        field.Append('"');
                        index++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    field.Append(character);
                }

                continue;
            }

            if (character == '"' && !fieldStarted)
            {
                inQuotes = true;
                fieldStarted = true;
                continue;
            }

            if (character == ',')
            {
                fields.Add(field.ToString());
                field.Clear();
                fieldStarted = false;
                continue;
            }

            if (character is '\r' or '\n')
            {
                fields.Add(field.ToString());
                field.Clear();
                fieldStarted = false;

                if (character == '\r' && index + 1 < text.Length && text[index + 1] == '\n')
                {
                    index++;
                }

                AddRowUnlessBlank(rows, fields);
                fields.Clear();
                continue;
            }

            field.Append(character);
            fieldStarted = true;
        }

        if (inQuotes)
        {
            throw new ReportValidationException("CSV ends inside a quoted field.");
        }

        if (field.Length > 0 || fieldStarted || fields.Count > 0)
        {
            fields.Add(field.ToString());
            AddRowUnlessBlank(rows, fields);
        }

        return rows;
    }

    private static void AddRowUnlessBlank(List<string[]> rows, List<string> fields)
    {
        if (fields.Count == 1 && string.IsNullOrWhiteSpace(fields[0]))
        {
            return;
        }

        rows.Add(fields.ToArray());
    }
}
