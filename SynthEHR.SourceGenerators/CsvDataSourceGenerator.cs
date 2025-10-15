using System;
using System.Collections.Generic;
using System.Collections.Immutable;
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
                // Only read first line to get headers - don't parse entire file!
                var text = file.GetText(cancellationToken);
                if (text == null) return (fileName: string.Empty, headers: Array.Empty<string>());

                var fileName = Path.GetFileNameWithoutExtension(file.Path);
                var firstLine = text.Lines.FirstOrDefault().ToString();
                var headers = CsvDataParser.ParseHeaderLine(firstLine);
                return (fileName, headers);
            });

        // Generate code for each CSV file
        context.RegisterSourceOutput(csvFiles, (ctx, csvFile) =>
        {
            try
            {
                var (fileName, headers) = csvFile;

                if (string.IsNullOrEmpty(fileName) || headers.Length == 0)
                    return; // Skip empty files

                var sourceCode = GenerateClassForCsv(fileName, headers);
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

    private static string GenerateClassForCsv(string fileName, string[] headers)
    {
        var className = CsvDataParser.SanitizeIdentifier(fileName) + "Data";
        var code = new CodeBuilder();

        // File header
        code.AppendFileHeader();
        code.AppendLine("using System;");
        code.AppendLine("using System.Collections.Generic;");
        code.AppendLine("using System.IO;");
        code.AppendLine("using System.Reflection;");
        code.AppendLine("using System.Text;");
        code.AppendLine("using System.Linq;");
        code.AppendLine();

        // Namespace
        code.AppendNamespace("SynthEHR.Core.Data");

        // Class documentation
        code.AppendXmlDocComment($"Generated data class for {fileName}.csv. Data is loaded from embedded resources on first access.");
        code.AppendGeneratedCodeAttribute();
        code.AppendLine($"[CsvDataSource(FileName = \"{fileName}.csv\", RowCount = -1)]");
        code.AppendClass(className, isStatic: true);
        code.OpenBrace();

        // Row class
        code.AppendXmlDocComment($"Represents a single row from {fileName}.csv");
        code.AppendGeneratedCodeAttribute();
        code.AppendLine($"public sealed class Row");
        code.OpenBrace();

        // Properties for each column
        for (int i = 0; i < headers.Length; i++)
        {
            var propertyName = CsvDataParser.SanitizeIdentifier(headers[i]);
            code.AppendXmlDocComment($"Column: {headers[i]}");
            code.AppendProperty("string", propertyName, "string.Empty");
        }

        code.CloseBrace();
        code.AppendLine();

        // Generate header names array for runtime parsing
        code.AppendLine("private static readonly string[] _headerNames = new[]");
        code.OpenBrace();
        for (int i = 0; i < headers.Length; i++)
        {
            var comma = i < headers.Length - 1 ? "," : "";
            code.AppendLine($"\"{headers[i]}\"{comma}");
        }
        code.CloseBrace(semicolon: true);
        code.AppendLine();

        // Generate property name mapping
        code.AppendLine("private static readonly string[] _propertyNames = new[]");
        code.OpenBrace();
        for (int i = 0; i < headers.Length; i++)
        {
            var propertyName = CsvDataParser.SanitizeIdentifier(headers[i]);
            var comma = i < headers.Length - 1 ? "," : "";
            code.AppendLine($"\"{propertyName}\"{comma}");
        }
        code.CloseBrace(semicolon: true);
        code.AppendLine();

        // Lazy initialization field
        code.AppendLine("private static readonly Lazy<IReadOnlyList<Row>> _allRows = new Lazy<IReadOnlyList<Row>>(LoadData);");
        code.AppendLine();

        // Public property
        code.AppendXmlDocComment("All rows from the CSV file. Data is loaded lazily from embedded resources on first access.");
        code.AppendLine("public static IReadOnlyList<Row> AllRows => _allRows.Value;");
        code.AppendLine();

        // LoadData method
        code.AppendXmlDocComment("Loads the CSV data from embedded resources.");
        code.AppendLine("private static IReadOnlyList<Row> LoadData()");
        code.OpenBrace();
        code.AppendLine("var assembly = typeof(" + className + ").Assembly;");
        code.AppendLine($"var resourceName = \"SynthEHR.Core.Datasets.{fileName}.csv\";");
        code.AppendLine();
        code.AppendLine("using var stream = assembly.GetManifestResourceStream(resourceName)");
        code.AppendLine("    ?? throw new InvalidOperationException($\"Embedded resource not found: {resourceName}\");");
        code.AppendLine();
        code.AppendLine("using var reader = new StreamReader(stream, Encoding.UTF8);");
        code.AppendLine("var rows = new List<Row>();");
        code.AppendLine();
        code.AppendLine("// Skip header line");
        code.AppendLine("var headerLine = reader.ReadLine();");
        code.AppendLine("if (headerLine == null) return rows;");
        code.AppendLine();
        code.AppendLine("// Parse data rows");
        code.AppendLine("string? line;");
        code.AppendLine("while ((line = reader.ReadLine()) != null)");
        code.OpenBrace();
        code.AppendLine("if (string.IsNullOrWhiteSpace(line)) continue;");
        code.AppendLine();
        code.AppendLine("var fields = ParseCsvLine(line);");
        code.AppendLine("var row = new Row();");
        code.AppendLine();

        // Generate property assignments using property names
        code.AppendLine("for (int i = 0; i < Math.Min(fields.Length, _propertyNames.Length); i++)");
        code.OpenBrace();
        code.AppendLine("switch (i)");
        code.OpenBrace();
        for (int i = 0; i < headers.Length; i++)
        {
            var propertyName = CsvDataParser.SanitizeIdentifier(headers[i]);
            code.AppendLine($"case {i}: row.{propertyName} = fields[{i}]; break;");
        }
        code.CloseBrace();
        code.CloseBrace();
        code.AppendLine();
        code.AppendLine("rows.Add(row);");
        code.CloseBrace();
        code.AppendLine();
        code.AppendLine("return rows;");
        code.CloseBrace();
        code.AppendLine();

        // Simple CSV line parser
        code.AppendXmlDocComment("Parses a single CSV line, handling quoted fields and escaped quotes.");
        code.AppendLine("private static string[] ParseCsvLine(string line)");
        code.OpenBrace();
        code.AppendLine("var fields = new List<string>();");
        code.AppendLine("var currentField = new StringBuilder();");
        code.AppendLine("bool inQuotes = false;");
        code.AppendLine();
        code.AppendLine("for (int i = 0; i < line.Length; i++)");
        code.OpenBrace();
        code.AppendLine("char c = line[i];");
        code.AppendLine();
        code.AppendLine("if (c == '\"')");
        code.OpenBrace();
        code.AppendLine("if (inQuotes && i + 1 < line.Length && line[i + 1] == '\"')");
        code.OpenBrace();
        code.AppendLine("currentField.Append('\"');");
        code.AppendLine("i++; // Skip next quote");
        code.CloseBrace();
        code.AppendLine("else");
        code.OpenBrace();
        code.AppendLine("inQuotes = !inQuotes;");
        code.CloseBrace();
        code.CloseBrace();
        code.AppendLine("else if (c == ',' && !inQuotes)");
        code.OpenBrace();
        code.AppendLine("fields.Add(currentField.ToString());");
        code.AppendLine("currentField.Clear();");
        code.CloseBrace();
        code.AppendLine("else");
        code.OpenBrace();
        code.AppendLine("currentField.Append(c);");
        code.CloseBrace();
        code.CloseBrace();
        code.AppendLine();
        code.AppendLine("fields.Add(currentField.ToString());");
        code.AppendLine("return fields.ToArray();");
        code.CloseBrace();
        code.AppendLine();

        // Helper methods
        code.AppendXmlDocComment("Gets the total number of rows.");
        code.AppendLine("public static int Count => AllRows.Count;");
        code.AppendLine();

        code.AppendXmlDocComment("Gets a random row from the dataset.");
        code.AppendLine("public static Row GetRandom(Random? random = null)");
        code.OpenBrace();
        code.AppendLine("var rnd = random ?? Random.Shared;");
        code.AppendLine("return AllRows[rnd.Next(AllRows.Count)];");
        code.CloseBrace();
        code.AppendLine();

        // Close class
        code.CloseBrace();

        return code.ToString();
    }
}
