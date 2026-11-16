using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace SynthEHR.SourceGenerators;

/// <summary>
/// Incremental source generator that generates C# classes from CSV data files.
/// </summary>
[Generator]
public sealed class CsvDataSourceGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // Register a post-initialization output to provide common types
        context.RegisterPostInitializationOutput(ctx =>
        {
            ctx.AddSource("CsvDataAttributes.g.cs", SourceText.From(GenerateAttributesFile(), Encoding.UTF8));
        });

        // Get all CSV files from AdditionalFiles
        var csvFiles = context.AdditionalTextsProvider
            .Where(static file => file.Path.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
            .Select(static (file, cancellationToken) =>
            {
                // Use CsvDataParser for proper CSV parsing (handles quotes, multiline, etc.)
                var content = file.GetText(cancellationToken)?.ToString() ?? string.Empty;
                var fileName = Path.GetFileNameWithoutExtension(file.Path);

                if (string.IsNullOrEmpty(content))
                    return (string.Empty, Array.Empty<string>(), Array.Empty<string[]>());

                try
                {
                    var csvData = CsvDataParser.Parse(content, fileName);
                    return (fileName, csvData.Headers, csvData.Rows);
                }
                catch
                {
                    return (string.Empty, Array.Empty<string>(), Array.Empty<string[]>());
                }
            });

        // Generate code for each CSV file
        context.RegisterSourceOutput(csvFiles, (ctx, csvFile) =>
        {
            try
            {
                var (fileName, headers, rows) = csvFile;

                if (string.IsNullOrEmpty(fileName) || headers.Length == 0)
                    return; // Skip empty files

                var sourceCode = GenerateClassForCsv(fileName, headers, rows);
                ctx.AddSource($"{fileName}Data.g.cs", SourceText.From(sourceCode, Encoding.UTF8));
            }
            catch (Exception ex)
            {
                // Report diagnostic instead of throwing
                ctx.ReportDiagnostic(Diagnostic.Create(
                    new DiagnosticDescriptor(
                        "SYNTH001",
                        "CSV Generation Error",
                        $"Error generating source for CSV file: {ex.Message}",
                        "SynthEHR.SourceGenerators",
                        DiagnosticSeverity.Warning,
                        isEnabledByDefault: true),
                    Location.None));
            }
        });
    }

    private static string GenerateAttributesFile()
    {
        var code = new CodeBuilder();
        code.AppendFileHeader();
        code.AppendLine("using System;");
        code.AppendLine();
        code.AppendNamespace("SynthEHR.Core.Data");

        code.AppendXmlDocComment("Marks a class as generated from CSV data.");
        code.AppendGeneratedCodeAttribute();
        code.AppendLine("[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]");
        code.AppendLine("public sealed class CsvDataSourceAttribute : Attribute");
        code.OpenBrace();
        code.AppendXmlDocComment("Gets or sets the source CSV file name.");
        code.AppendProperty("string", "FileName");
        code.AppendXmlDocComment("Gets or sets the number of rows in the CSV file.");
        code.AppendProperty("int", "RowCount");
        code.CloseBrace();
        code.AppendLine();

        return code.ToString();
    }

    private static string GenerateClassForCsv(string fileName, string[] headers, string[][] rows)
    {
        var className = CsvDataParser.SanitizeIdentifier(fileName) + "Data";
        var code = new CodeBuilder();

        // Analyze column types
        var columnTypes = InferColumnTypes(headers, rows);

        // Pre-sort rows if a count column is detected
        var sortColumnIndex = DetectSortColumn(headers, columnTypes);
        if (sortColumnIndex >= 0)
        {
            rows = PreSortRows(rows, sortColumnIndex, columnTypes[sortColumnIndex]);
        }

        // File header
        code.AppendFileHeader();
        code.AppendLine("using System;");
        code.AppendLine("using System.Collections;");
        code.AppendLine("using System.Collections.Generic;");
        code.AppendLine("using System.Text;");
        code.AppendLine();

        // Namespace
        code.AppendNamespace("SynthEHR.Core.Data");

        // Class documentation
        var sortNote = sortColumnIndex >= 0
            ? $" Data is pre-sorted by {headers[sortColumnIndex]} (descending) for optimal performance."
            : "";
        code.AppendXmlDocComment(FormattableString.Invariant($"Generated data class for {fileName}.csv containing {rows.Length} rows using columnar storage.{sortNote}"));
        code.AppendGeneratedCodeAttribute();
        code.AppendLine(FormattableString.Invariant($"[CsvDataSource(FileName = \"{fileName}.csv\", RowCount = {rows.Length})]"));
        code.AppendClass(className, isStatic: true);
        code.OpenBrace();

        // Row class
        code.AppendXmlDocComment($"Represents a single row from {fileName}.csv");
        code.AppendGeneratedCodeAttribute();
        code.AppendLine($"public sealed class Row");
        code.OpenBrace();

        // Properties for each column - keep as strings for backward compatibility
        for (int i = 0; i < headers.Length; i++)
        {
            var propertyName = CsvDataParser.SanitizeIdentifier(headers[i]);
            code.AppendXmlDocComment($"Column: {headers[i]}");
            code.AppendProperty("string", propertyName, "string.Empty");
        }

        code.CloseBrace();
        code.AppendLine();

        // Generate columnar storage arrays and collect interning info
        var internedStrings = GenerateColumnarStorage(code, headers, rows);

        // Generate GetRow method with interning support
        GenerateGetRowMethod(code, headers, internedStrings);

        // Generate IReadOnlyList wrapper
        GenerateLazyRowList(code);

        // Public properties
        code.AppendXmlDocComment("All rows from the CSV file.");
        code.AppendLine(FormattableString.Invariant($"public static IReadOnlyList<Row> AllRows {{ get; }} = new LazyRowList({rows.Length});"));
        code.AppendLine();

        code.AppendXmlDocComment("Gets the total number of rows.");
        code.AppendLine(FormattableString.Invariant($"public static int Count => {rows.Length};"));
        code.AppendLine();

        code.AppendXmlDocComment("Gets a random row from the dataset.");
        code.AppendLine("public static Row GetRandom(Random? random = null)");
        code.OpenBrace();
        code.AppendLine("var rnd = random ?? Random.Shared;");
        code.AppendLine("return GetRow(rnd.Next(Count));");
        code.CloseBrace();
        code.AppendLine();

        // Close class
        code.CloseBrace();

        return code.ToString();
    }

    private static int DetectSortColumn(string[] headers, ColumnTypeInfo[] columnTypes)
    {
        // Look for columns named "Count", "RecordCount", or similar that are integer type
        for (int i = 0; i < headers.Length; i++)
        {
            var headerLower = headers[i].ToLowerInvariant();
            if (columnTypes[i].Type == ColumnDataType.Int &&
                (headerLower.Contains("count") || headerLower.Contains("records")))
            {
                return i;
            }
        }
        return -1;
    }

    private static string[][] PreSortRows(string[][] rows, int sortColumnIndex, ColumnTypeInfo columnType)
    {
        // Sort rows in descending order by the specified column
        // This allows consuming code to skip runtime sorting
        return columnType.Type switch
        {
            ColumnDataType.Int => rows.OrderByDescending(row =>
            {
                if (sortColumnIndex >= row.Length) return int.MinValue;
                var value = row[sortColumnIndex];
                return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result) ? result : int.MinValue;
            }).ToArray(),

            ColumnDataType.Double => rows.OrderByDescending(row =>
            {
                if (sortColumnIndex >= row.Length) return double.MinValue;
                var value = row[sortColumnIndex];
                return double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var result)
                    ? result : double.MinValue;
            }).ToArray(),

            _ => rows // No sorting for non-numeric types
        };
    }

    private static ColumnTypeInfo[] InferColumnTypes(string[] headers, string[][] rows)
    {
        var columnTypes = new ColumnTypeInfo[headers.Length];

        for (int colIndex = 0; colIndex < headers.Length; colIndex++)
        {
            bool hasNull = false;
            bool allInt = true;
            bool allDouble = true;
            bool allBool = true;

            foreach (var row in rows)
            {
                if (colIndex >= row.Length || string.IsNullOrEmpty(row[colIndex]) || row[colIndex] == "NULL")
                {
                    hasNull = true;
                    continue;
                }

                var value = row[colIndex];

                if (allInt && !int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
                    allInt = false;

                if (allDouble && !double.TryParse(value, System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out _))
                    allDouble = false;

                if (allBool && !IsBoolValue(value))
                    allBool = false;

                if (!allInt && !allDouble && !allBool)
                    break;
            }

            ColumnDataType type;
            if (allInt) type = ColumnDataType.Int;
            else if (allDouble) type = ColumnDataType.Double;
            else if (allBool) type = ColumnDataType.Bool;
            else type = ColumnDataType.String;

            columnTypes[colIndex] = new ColumnTypeInfo(type, hasNull);
        }

        return columnTypes;
    }

    private static bool IsBoolValue(string value)
    {
        return value == "true" || value == "false" || value == "True" || value == "False" ||
               value == "TRUE" || value == "FALSE" || value == "1" || value == "0" ||
               value == "yes" || value == "no" || value == "Yes" || value == "No";
    }

    private static Dictionary<string, int> GenerateColumnarStorage(CodeBuilder code, string[] headers, string[][] rows)
    {
        // Row exposes every column as a string, so every column is stored as its exact CSV text.
        // Converting numeric-looking columns to typed arrays and back would reformat values ("13.0" -> "13",
        // "007" -> "7"), make the output depend on the current culture, and turn empty cells into "NULL".
        var stringColumns = Enumerable.Range(0, headers.Length).ToList();

        // Analyze string frequency for interning
        var stringFrequency = AnalyzeStringFrequency(rows, stringColumns);
        var internedStrings = BuildInternedStringPool(stringFrequency, threshold: 10);

        // Generate interned string constants
        if (internedStrings.Count > 0)
        {
            code.AppendXmlDocComment(FormattableString.Invariant($"Interned strings (appearing 10+ times) - {internedStrings.Count} unique values"));
            foreach (var kvp in internedStrings.OrderBy(x => x.Value))
            {
                var constName = FormattableString.Invariant($"_InternedStr{kvp.Value}");
                var escapedValue = EscapeString(kvp.Key);
                var frequency = stringFrequency[kvp.Key];
                code.AppendLine(FormattableString.Invariant($"private const string {constName} = \"{escapedValue}\"; // appears {frequency}x"));
            }
            code.AppendLine();
        }

        if (stringColumns.Count > 0)
        {
            GenerateInternedStringStorage(code, rows, stringColumns, internedStrings);
        }

        return internedStrings;
    }

    private static Dictionary<string, int> AnalyzeStringFrequency(string[][] rows, List<int> stringColumns)
    {
        var frequency = new Dictionary<string, int>();

        foreach (var row in rows)
        {
            foreach (var colIndex in stringColumns)
            {
                var value = colIndex < row.Length ? (row[colIndex] ?? string.Empty) : string.Empty;
                frequency[value] = frequency.TryGetValue(value, out var count) ? count + 1 : 1;
            }
        }

        return frequency;
    }

    private static Dictionary<string, int> BuildInternedStringPool(Dictionary<string, int> frequency, int threshold)
    {
        var pool = new Dictionary<string, int>();
        int internId = 0;

        // Intern strings that appear >= threshold times
        foreach (var kvp in frequency.OrderByDescending(x => x.Value))
        {
            if (kvp.Value >= threshold)
            {
                pool[kvp.Key] = internId++;
            }
        }

        return pool;
    }

    private static void GenerateInternedStringStorage(CodeBuilder code, string[][] rows, List<int> stringColumns, Dictionary<string, int> internedStrings)
    {
        var stringBlob = new StringBuilder();
        // Offsets index the UTF-8 bytes of the blob, not its UTF-16 chars: any non-ASCII text would
        // otherwise shift every later value.
        var offsets = new List<int>();
        var byteOffset = 0;
        var internedIndices = new List<int>(); // -1 = not interned, >= 0 = interned string ID

        for (int rowIndex = 0; rowIndex < rows.Length; rowIndex++)
        {
            var row = rows[rowIndex];
            foreach (var colIndex in stringColumns)
            {
                offsets.Add(byteOffset);
                var value = colIndex < row.Length ? (row[colIndex] ?? string.Empty) : string.Empty;

                // Check if this string should be interned
                if (internedStrings.TryGetValue(value, out int internId))
                {
                    internedIndices.Add(internId);
                    // Add marker to blob (empty string with special offset handling)
                    stringBlob.Append('\0');
                    byteOffset += 1;
                }
                else
                {
                    internedIndices.Add(-1);
                    // Keep "NULL" as-is for string columns - tests expect this literal
                    stringBlob.Append(value);
                    stringBlob.Append('\0');
                    byteOffset += Encoding.UTF8.GetByteCount(value) + 1;
                }
            }
        }
        offsets.Add(byteOffset); // Final offset for bounds

        code.AppendXmlDocComment("UTF8 string data blob for non-interned string columns");
        var blobBytes = Encoding.UTF8.GetBytes(stringBlob.ToString());
        if (blobBytes.Length != byteOffset)
            throw new InvalidOperationException(FormattableString.Invariant($"String blob is {blobBytes.Length} bytes but offsets end at {byteOffset}"));
        code.AppendLine($"private static ReadOnlySpan<byte> _stringData => new byte[]");
        code.OpenBrace();

        // Write bytes in chunks of 20 per line for readability
        for (int i = 0; i < blobBytes.Length; i++)
        {
            if (i > 0 && i % 20 == 0)
            {
                code.AppendLine();
            }

            if (i > 0 && i % 20 != 0)
                code.Append(" ");

            code.Append(blobBytes[i].ToString(CultureInfo.InvariantCulture));

            if (i < blobBytes.Length - 1)
                code.Append(",");
        }

        if (blobBytes.Length > 0)
            code.AppendLine();

        code.CloseBrace(semicolon: true);
        code.AppendLine();

        code.AppendXmlDocComment("Offsets into string data blob");
        code.AppendLine($"private static ReadOnlySpan<int> _stringOffsets => new int[]");
        code.OpenBrace();

        for (int i = 0; i < offsets.Count; i++)
        {
            var isLast = i == offsets.Count - 1;
            code.AppendLine(FormattableString.Invariant($"{offsets[i]}{(isLast ? "" : ",")}"));
        }

        code.CloseBrace(semicolon: true);
        code.AppendLine();

        code.AppendXmlDocComment("Interned string indices (-1 = not interned, >= 0 = interned string ID)");
        code.AppendLine($"private static ReadOnlySpan<int> _internedIndices => new int[]");
        code.OpenBrace();

        for (int i = 0; i < internedIndices.Count; i++)
        {
            var isLast = i == internedIndices.Count - 1;
            code.AppendLine(FormattableString.Invariant($"{internedIndices[i]}{(isLast ? "" : ",")}"));
        }

        code.CloseBrace(semicolon: true);
        code.AppendLine();

        code.AppendLine(FormattableString.Invariant($"private const int StringColumnsCount = {stringColumns.Count};"));
        code.AppendLine();
    }

    private static void GenerateGetRowMethod(CodeBuilder code, string[] headers, Dictionary<string, int> internedStrings)
    {
        // Generate GetString helper if needed
        var hasStringColumns = headers.Length > 0;
        if (hasStringColumns)
        {
            code.AppendXmlDocComment("Extracts a string from the blob at the specified row and column index");
            code.AppendLine("private static string GetString(int rowIndex, int colIndex)");
            code.OpenBrace();
            code.AppendLine("int offsetIndex = rowIndex * StringColumnsCount + colIndex;");
            code.AppendLine("int internedId = _internedIndices[offsetIndex];");
            code.AppendLine();
            code.AppendLine("// Check if this is an interned string");
            code.AppendLine("if (internedId >= 0)");
            code.OpenBrace();
            code.AppendLine("return internedId switch");
            code.OpenBrace();

            // Generate switch cases for interned strings
            if (internedStrings.Count > 0)
            {
                foreach (var kvp in internedStrings.OrderBy(x => x.Value))
                {
                    code.AppendLine(FormattableString.Invariant($"{kvp.Value} => _InternedStr{kvp.Value},"));
                }
            }
            code.AppendLine("_ => string.Empty");

            code.CloseBrace(semicolon: true);
            code.CloseBrace();
            code.AppendLine();
            code.AppendLine("// Extract from blob for non-interned strings");
            code.AppendLine("int start = _stringOffsets[offsetIndex];");
            code.AppendLine("int end = _stringOffsets[offsetIndex + 1];");
            code.AppendLine("if (end <= start) return string.Empty;");
            code.AppendLine("var slice = _stringData.Slice(start, end - start - 1);"); // -1 to skip null terminator
            code.AppendLine("return Encoding.UTF8.GetString(slice);");
            code.CloseBrace();
            code.AppendLine();
        }

        code.AppendXmlDocComment("Gets a row at the specified index");
        code.AppendLine("public static Row GetRow(int index)");
        code.OpenBrace();
        code.AppendLine("return new Row");
        code.OpenBrace();

        for (int i = 0; i < headers.Length; i++)
        {
            var propertyName = CsvDataParser.SanitizeIdentifier(headers[i]);
            code.AppendLine(FormattableString.Invariant($"{propertyName} = GetString(index, {i}),"));
        }

        code.CloseBrace(semicolon: true);
        code.CloseBrace();
        code.AppendLine();
    }

    private static void GenerateLazyRowList(CodeBuilder code)
    {
        code.AppendXmlDocComment("Lazy IReadOnlyList wrapper for compatibility");
        code.AppendLine("private sealed class LazyRowList : IReadOnlyList<Row>");
        code.OpenBrace();
        code.AppendLine("private readonly int _count;");
        code.AppendLine("public LazyRowList(int count) => _count = count;");
        code.AppendLine("public Row this[int index] => GetRow(index);");
        code.AppendLine("public int Count => _count;");
        code.AppendLine("public IEnumerator<Row> GetEnumerator()");
        code.OpenBrace();
        code.AppendLine("for (int i = 0; i < _count; i++)");
        code.AppendLine("    yield return GetRow(i);");
        code.CloseBrace();
        code.AppendLine("IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();");
        code.CloseBrace();
        code.AppendLine();
    }



    private static string EscapeString(string value)
    {
        return value.Replace("\\", "\\\\")
                    .Replace("\"", "\\\"")
                    .Replace("\n", "\\n")
                    .Replace("\r", "\\r")
                    .Replace("\t", "\\t")
                    .Replace("\0", "\\0");
    }

    private enum ColumnDataType
    {
        String,
        Int,
        Double,
        Bool
    }

    private readonly struct ColumnTypeInfo
    {
        public ColumnDataType Type { get; }
        public bool IsNullable { get; }

        public ColumnTypeInfo(ColumnDataType type, bool isNullable)
        {
            Type = type;
            IsNullable = isNullable;
        }
    }
}
