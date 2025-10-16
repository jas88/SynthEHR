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
        code.AppendXmlDocComment($"Generated data class for {fileName}.csv containing {rows.Length} rows using columnar storage.{sortNote}");
        code.AppendGeneratedCodeAttribute();
        code.AppendLine($"[CsvDataSource(FileName = \"{fileName}.csv\", RowCount = {rows.Length})]");
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

        // Generate columnar storage arrays
        GenerateColumnarStorage(code, headers, rows, columnTypes);

        // Generate GetRow method
        GenerateGetRowMethod(code, headers, columnTypes);

        // Generate IReadOnlyList wrapper
        GenerateLazyRowList(code);

        // Public properties
        code.AppendXmlDocComment("All rows from the CSV file.");
        code.AppendLine($"public static IReadOnlyList<Row> AllRows {{ get; }} = new LazyRowList({rows.Length});");
        code.AppendLine();

        code.AppendXmlDocComment("Gets the total number of rows.");
        code.AppendLine($"public static int Count => {rows.Length};");
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
                return int.TryParse(value, out var result) ? result : int.MinValue;
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

                if (allInt && !int.TryParse(value, out _))
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

    private static void GenerateColumnarStorage(CodeBuilder code, string[] headers, string[][] rows, ColumnTypeInfo[] columnTypes)
    {
        // Group columns by type
        var stringColumns = new List<int>();
        for (int i = 0; i < columnTypes.Length; i++)
        {
            if (columnTypes[i].Type == ColumnDataType.String)
                stringColumns.Add(i);
        }

        // Generate typed column arrays
        for (int colIndex = 0; colIndex < headers.Length; colIndex++)
        {
            var columnType = columnTypes[colIndex];
            var propertyName = CsvDataParser.SanitizeIdentifier(headers[colIndex]);

            if (columnType.Type == ColumnDataType.String)
                continue; // Handle strings separately with blob

            string spanType = columnType.Type switch
            {
                ColumnDataType.Int => "int",
                ColumnDataType.Double => "double",
                ColumnDataType.Bool => "bool",
                _ => "string"
            };

            code.AppendXmlDocComment($"Column data for {headers[colIndex]}");
            code.AppendLine($"private static ReadOnlySpan<{spanType}> _{propertyName}Data => new {spanType}[]");
            code.OpenBrace();

            for (int rowIndex = 0; rowIndex < rows.Length; rowIndex++)
            {
                var row = rows[rowIndex];
                var value = colIndex < row.Length ? (row[colIndex] ?? string.Empty) : string.Empty;
                var isLast = rowIndex == rows.Length - 1;

                string literalValue;
                if (string.IsNullOrEmpty(value) || value == "NULL")
                {
                    literalValue = columnType.Type switch
                    {
                        ColumnDataType.Int => "0",
                        ColumnDataType.Double => "0.0",
                        ColumnDataType.Bool => "false",
                        _ => "\"\""
                    };
                }
                else
                {
                    literalValue = columnType.Type switch
                    {
                        ColumnDataType.Int => value,
                        ColumnDataType.Double => FormatDoubleLiteral(value),
                        ColumnDataType.Bool => ParseBoolLiteral(value),
                        _ => $"\"{EscapeString(value)}\""
                    };
                }

                code.AppendLine($"{literalValue}{(isLast ? "" : ",")}");
            }

            code.CloseBrace(semicolon: true);
            code.AppendLine();
        }

        // Generate nullable flags for columns that need them
        for (int colIndex = 0; colIndex < headers.Length; colIndex++)
        {
            var columnType = columnTypes[colIndex];
            if (!columnType.IsNullable || columnType.Type == ColumnDataType.String)
                continue;

            var propertyName = CsvDataParser.SanitizeIdentifier(headers[colIndex]);
            code.AppendXmlDocComment($"Null flags for {headers[colIndex]}");
            code.AppendLine($"private static ReadOnlySpan<bool> _{propertyName}Nulls => new bool[]");
            code.OpenBrace();

            for (int rowIndex = 0; rowIndex < rows.Length; rowIndex++)
            {
                var row = rows[rowIndex];
                var value = colIndex < row.Length ? (row[colIndex] ?? string.Empty) : string.Empty;
                var isLast = rowIndex == rows.Length - 1;
                var isNull = string.IsNullOrEmpty(value) || value == "NULL";

                code.AppendLine($"{(isNull ? "true" : "false")}{(isLast ? "" : ",")}");
            }

            code.CloseBrace(semicolon: true);
            code.AppendLine();
        }

        // Generate string blob and offsets
        if (stringColumns.Count > 0)
        {
            var stringBlob = new StringBuilder();
            var offsets = new List<int>();

            for (int rowIndex = 0; rowIndex < rows.Length; rowIndex++)
            {
                var row = rows[rowIndex];
                foreach (var colIndex in stringColumns)
                {
                    offsets.Add(stringBlob.Length);
                    var value = colIndex < row.Length ? (row[colIndex] ?? string.Empty) : string.Empty;
                    // Keep "NULL" as-is for string columns - tests expect this literal
                    stringBlob.Append(value);
                    stringBlob.Append('\0');
                }
            }
            offsets.Add(stringBlob.Length); // Final offset for bounds

            code.AppendXmlDocComment("UTF8 string data blob for all string columns");
            // Use byte array instead of u8 literal to avoid escaping issues
            var blobBytes = Encoding.UTF8.GetBytes(stringBlob.ToString());
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

                code.Append(blobBytes[i].ToString());

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
                code.AppendLine($"{offsets[i]}{(isLast ? "" : ",")}");
            }

            code.CloseBrace(semicolon: true);
            code.AppendLine();

            code.AppendLine($"private const int StringColumnsCount = {stringColumns.Count};");
            code.AppendLine();
        }
    }

    private static void GenerateGetRowMethod(CodeBuilder code, string[] headers, ColumnTypeInfo[] columnTypes)
    {
        // Generate GetString helper if needed
        var hasStringColumns = columnTypes.Any(ct => ct.Type == ColumnDataType.String);
        if (hasStringColumns)
        {
            code.AppendXmlDocComment("Extracts a string from the blob at the specified row and column index");
            code.AppendLine("private static string GetString(int rowIndex, int colIndex)");
            code.OpenBrace();
            code.AppendLine("int offsetIndex = rowIndex * StringColumnsCount + colIndex;");
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

        int stringColIndex = 0;
        for (int i = 0; i < headers.Length; i++)
        {
            var propertyName = CsvDataParser.SanitizeIdentifier(headers[i]);
            var columnType = columnTypes[i];

            string assignment;
            if (columnType.Type == ColumnDataType.String)
            {
                assignment = $"GetString(index, {stringColIndex})";
                stringColIndex++;
            }
            else
            {
                // Convert typed values back to strings for backward compatibility
                string access = $"_{propertyName}Data[index]";
                if (columnType.IsNullable)
                {
                    // Return "NULL" string for nulls - existing code expects this literal
                    assignment = $"_{propertyName}Nulls[index] ? \"NULL\" : {access}.ToString()";
                }
                else
                {
                    assignment = $"{access}.ToString()";
                }
            }

            code.AppendLine($"{propertyName} = {assignment},");
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

    private static string ParseBoolLiteral(string value)
    {
        return value switch
        {
            "true" or "True" or "TRUE" or "1" or "yes" or "Yes" => "true",
            _ => "false"
        };
    }

    private static string FormatDoubleLiteral(string value)
    {
        // Ensure double literals are valid C# - must have decimal point and at least one digit after
        if (!value.Contains('.'))
            return value + ".0";
        if (value.EndsWith("."))
            return value + "0";
        return value;
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
