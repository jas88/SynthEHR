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
                var content = file.GetText(cancellationToken)?.ToString() ?? string.Empty;
                var fileName = Path.GetFileNameWithoutExtension(file.Path);
                return (fileName, content);
            });

        // Generate code for each CSV file
        context.RegisterSourceOutput(csvFiles, (ctx, csvFile) =>
        {
            try
            {
                var (fileName, content) = csvFile;
                var csvData = CsvDataParser.Parse(content, fileName);

                if (csvData.Headers.Length == 0)
                    return; // Skip empty files

                var sourceCode = GenerateClassForCsv(csvData);
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

    private static string GenerateClassForCsv(CsvData csvData)
    {
        var className = CsvDataParser.SanitizeIdentifier(csvData.FileName) + "Data";
        var code = new CodeBuilder();

        // File header
        code.AppendFileHeader();
        code.AppendLine("using System;");
        code.AppendLine("using System.Collections.Generic;");
        code.AppendLine("using System.Linq;");
        code.AppendLine();

        // Namespace
        code.AppendNamespace("SynthEHR.Core.Data");

        // Class documentation
        code.AppendXmlDocComment($"Generated data class for {csvData.FileName}.csv containing {csvData.Rows.Length} rows.");
        code.AppendGeneratedCodeAttribute();
        code.AppendLine($"[CsvDataSource(FileName = \"{csvData.FileName}.csv\", RowCount = {csvData.Rows.Length})]");
        code.AppendClass(className, isStatic: true);
        code.OpenBrace();

        // Row class
        code.AppendXmlDocComment($"Represents a single row from {csvData.FileName}.csv");
        code.AppendGeneratedCodeAttribute();
        code.AppendLine($"public sealed class Row");
        code.OpenBrace();

        // Properties for each column
        for (int i = 0; i < csvData.Headers.Length; i++)
        {
            var propertyName = CsvDataParser.SanitizeIdentifier(csvData.Headers[i]);
            code.AppendXmlDocComment($"Column: {csvData.Headers[i]}");
            code.AppendProperty("string", propertyName, "string.Empty");
        }

        code.CloseBrace();
        code.AppendLine();

        // Static data array
        code.AppendXmlDocComment("All rows from the CSV file.");
        code.AppendLine("public static readonly IReadOnlyList<Row> AllRows = new[]");
        code.OpenBrace();

        // Generate row data
        for (int rowIndex = 0; rowIndex < csvData.Rows.Length; rowIndex++)
        {
            var row = csvData.Rows[rowIndex];
            var isLast = rowIndex == csvData.Rows.Length - 1;

            code.AppendLine($"new Row");
            code.OpenBrace();

            for (int colIndex = 0; colIndex < csvData.Headers.Length && colIndex < row.Length; colIndex++)
            {
                var propertyName = CsvDataParser.SanitizeIdentifier(csvData.Headers[colIndex]);
                var value = row[colIndex] ?? string.Empty;
                var escapedValue = value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r");
                code.AppendLine($"{propertyName} = \"{escapedValue}\",");
            }

            code.CloseBrace(semicolon: false);
            if (!isLast)
                code.Append(",");
            code.AppendLine();
        }

        code.CloseBrace(semicolon: true);
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
