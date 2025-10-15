using System;
using System.Collections.Generic;
using System.Text;

namespace SynthEHR.SourceGenerators;

/// <summary>
/// Simple CSV parser for source generator use (standalone, no dependencies).
/// Handles quoted fields, escaped quotes, and multiline values.
/// </summary>
internal static class CsvDataParser
{
    public static CsvData Parse(string content, string fileName)
    {
        if (string.IsNullOrEmpty(content))
            return new CsvData(fileName, Array.Empty<string>(), Array.Empty<string[]>());

        var lines = SplitLines(content);
        if (lines.Count == 0)
            return new CsvData(fileName, Array.Empty<string>(), Array.Empty<string[]>());

        var headers = ParseLine(lines[0]);
        var rows = new List<string[]>();

        for (int i = 1; i < lines.Count; i++)
        {
            var line = lines[i];
            if (string.IsNullOrWhiteSpace(line))
                continue;

            var fields = ParseLine(line);
            rows.Add(fields);
        }

        return new CsvData(fileName, headers, rows.ToArray());
    }

    private static List<string> SplitLines(string content)
    {
        var lines = new List<string>();
        var currentLine = new StringBuilder();
        bool inQuotes = false;

        for (int i = 0; i < content.Length; i++)
        {
            char c = content[i];

            if (c == '"')
            {
                inQuotes = !inQuotes;
                currentLine.Append(c);
            }
            else if (c == '\n' && !inQuotes)
            {
                lines.Add(currentLine.ToString());
                currentLine.Clear();
            }
            else if (c == '\r' && i + 1 < content.Length && content[i + 1] == '\n' && !inQuotes)
            {
                lines.Add(currentLine.ToString());
                currentLine.Clear();
                i++; // Skip the \n
            }
            else
            {
                currentLine.Append(c);
            }
        }

        if (currentLine.Length > 0)
            lines.Add(currentLine.ToString());

        return lines;
    }

    public static string[] ParseHeaderLine(string line)
    {
        return ParseLine(line);
    }

    private static string[] ParseLine(string line)
    {
        var fields = new List<string>();
        var currentField = new StringBuilder();
        bool inQuotes = false;

        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];

            if (c == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    // Escaped quote
                    currentField.Append('"');
                    i++; // Skip next quote
                }
                else
                {
                    // Toggle quote state
                    inQuotes = !inQuotes;
                }
            }
            else if (c == ',' && !inQuotes)
            {
                // Field separator
                fields.Add(currentField.ToString());
                currentField.Clear();
            }
            else
            {
                currentField.Append(c);
            }
        }

        fields.Add(currentField.ToString());
        return fields.ToArray();
    }

    public static string SanitizeIdentifier(string name)
    {
        if (string.IsNullOrEmpty(name))
            return "Field";

        var sb = new StringBuilder();
        bool firstChar = true;

        foreach (char c in name)
        {
            if (char.IsLetterOrDigit(c))
            {
                sb.Append(firstChar ? char.ToUpper(c) : c);
                firstChar = false;
            }
            else if (c == '_' || c == ' ')
            {
                if (!firstChar)
                    firstChar = true; // Next char should be uppercase
            }
        }

        var result = sb.ToString();
        if (result.Length == 0)
            return "Field";

        // Ensure it doesn't start with a digit
        if (char.IsDigit(result[0]))
            result = "_" + result;

        return result;
    }
}

/// <summary>
/// Represents parsed CSV data.
/// </summary>
internal sealed class CsvData
{
    public string FileName { get; }
    public string[] Headers { get; }
    public string[][] Rows { get; }

    public CsvData(string fileName, string[] headers, string[][] rows)
    {
        FileName = fileName;
        Headers = headers;
        Rows = rows;
    }
}
