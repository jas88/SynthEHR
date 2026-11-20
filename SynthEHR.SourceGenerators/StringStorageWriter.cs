using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace SynthEHR.SourceGenerators;

/// <summary>
/// Writes the string storage of a generated data class: a UTF-8 blob holding every cell that is not interned,
/// the offset of each cell in that blob, and each cell's interned string ID.
/// </summary>
internal static class StringStorageWriter
{
    /// <summary>
    /// Writes the _stringData, _stringOffsets and _internedIndices arrays for <paramref name="stringColumns"/> of
    /// every row, and the StringColumnsCount constant.
    /// </summary>
    public static void Write(CodeBuilder code, string[][] rows, List<int> stringColumns, Dictionary<string, int> internedStrings)
    {
        var (blobBytes, offsets, internedIndices) = BuildBlob(rows, stringColumns, internedStrings);

        code.AppendXmlDocComment("UTF8 string data blob for non-interned string columns");
        AppendByteArray(code, "_stringData", blobBytes);

        code.AppendXmlDocComment("Offsets into string data blob");
        AppendIntArray(code, "_stringOffsets", offsets);

        code.AppendXmlDocComment("Interned string indices (-1 = not interned, >= 0 = interned string ID)");
        AppendIntArray(code, "_internedIndices", internedIndices);

        code.AppendLine(FormattableString.Invariant($"private const int StringColumnsCount = {stringColumns.Count};"));
        code.AppendLine();
    }

    /// <summary>
    /// Lays out every cell, row by row: interned values get their ID and a one-byte marker in the blob, the rest are
    /// stored in the blob as null-terminated UTF-8. The offsets end with one extra entry, the blob's length.
    /// </summary>
    private static (byte[] BlobBytes, List<int> Offsets, List<int> InternedIndices) BuildBlob(
        string[][] rows, List<int> stringColumns, Dictionary<string, int> internedStrings)
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

        var blobBytes = Encoding.UTF8.GetBytes(stringBlob.ToString());
        if (blobBytes.Length != byteOffset)
            throw new InvalidOperationException(FormattableString.Invariant($"String blob is {blobBytes.Length} bytes but offsets end at {byteOffset}"));

        return (blobBytes, offsets, internedIndices);
    }

    private static void AppendByteArray(CodeBuilder code, string name, byte[] bytes)
    {
        code.AppendLine($"private static ReadOnlySpan<byte> {name} => new byte[]");
        code.OpenBrace();

        // Write bytes in chunks of 20 per line for readability
        for (int i = 0; i < bytes.Length; i++)
        {
            if (i > 0 && i % 20 == 0)
            {
                code.AppendLine();
            }

            if (i > 0 && i % 20 != 0)
                code.Append(" ");

            code.Append(bytes[i].ToString(CultureInfo.InvariantCulture));

            if (i < bytes.Length - 1)
                code.Append(",");
        }

        if (bytes.Length > 0)
            code.AppendLine();

        code.CloseBrace(semicolon: true);
        code.AppendLine();
    }

    /// <summary>
    /// Writes one value per line.
    /// </summary>
    private static void AppendIntArray(CodeBuilder code, string name, List<int> values)
    {
        code.AppendLine($"private static ReadOnlySpan<int> {name} => new int[]");
        code.OpenBrace();

        for (int i = 0; i < values.Count; i++)
        {
            var isLast = i == values.Count - 1;
            code.AppendLine(FormattableString.Invariant($"{values[i]}{(isLast ? "" : ",")}"));
        }

        code.CloseBrace(semicolon: true);
        code.AppendLine();
    }
}
