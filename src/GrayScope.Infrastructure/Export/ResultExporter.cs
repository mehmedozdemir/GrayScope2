using System.Text;
using ClosedXML.Excel;

namespace GrayScope.Infrastructure.Export;

/// <summary>Writes a grid snapshot (headers + visible rows, in display order) to CSV or Excel.</summary>
public static class ResultExporter
{
    /// <summary>Excel cell text limit; longer values are truncated with a marker.</summary>
    private const int ExcelMaxCellLength = 32_767;

    public static async Task WriteCsvAsync(string path, IReadOnlyList<string> headers, IEnumerable<IReadOnlyList<string>> rows, CancellationToken ct = default)
    {
        // UTF-8 with BOM so Excel opens Turkish characters correctly.
        await using var writer = new StreamWriter(path, false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        await writer.WriteLineAsync(string.Join(',', headers.Select(Escape))).ConfigureAwait(false);
        foreach (var row in rows)
        {
            ct.ThrowIfCancellationRequested();
            await writer.WriteLineAsync(string.Join(',', row.Select(Escape))).ConfigureAwait(false);
        }
    }

    public static Task WriteExcelAsync(string path, IReadOnlyList<string> headers, IEnumerable<IReadOnlyList<string>> rows, CancellationToken ct = default) =>
        Task.Run(() =>
        {
            using var workbook = new XLWorkbook();
            var sheet = workbook.AddWorksheet("Sonuçlar");
            for (var c = 0; c < headers.Count; c++)
            {
                sheet.Cell(1, c + 1).Value = headers[c];
            }
            var header = sheet.Range(1, 1, 1, Math.Max(1, headers.Count));
            header.Style.Font.Bold = true;
            header.Style.Fill.BackgroundColor = XLColor.FromHtml("#E8EEF9");

            var r = 2;
            foreach (var row in rows)
            {
                ct.ThrowIfCancellationRequested();
                for (var c = 0; c < row.Count; c++)
                {
                    var value = row[c];
                    sheet.Cell(r, c + 1).SetValue(value.Length > ExcelMaxCellLength ? value[..(ExcelMaxCellLength - 1)] + "…" : value);
                }
                r++;
            }
            sheet.SheetView.FreezeRows(1);
            sheet.Range(1, 1, Math.Max(1, r - 1), Math.Max(1, headers.Count)).SetAutoFilter();
            sheet.Columns().AdjustToContents(1, Math.Min(r, 200), 8, 80);
            workbook.SaveAs(path);
        }, ct);

    internal static string Escape(string value) =>
        value.IndexOfAny([',', '"', '\n', '\r']) >= 0 ? "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"" : value;
}
