using ClosedXML.Excel;

namespace ResumeTailor;

/// <summary>
/// Parses an Excel workbook and extracts job listing URLs from Column A.
/// </summary>
public class ExcelParser
{
    /// <summary>
    /// Opens the given Excel file and returns all non-empty URL strings
    /// found in Column A of the first worksheet, skipping the header row (row 1).
    /// </summary>
    /// <param name="filePath">Absolute or relative path to the .xlsx file.</param>
    /// <returns>A list of URL strings extracted from Column A.</returns>
    public List<string> GetUrls(string filePath)
    {
        var urls = new List<string>();

        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var workbook = new XLWorkbook(stream);
        var worksheet = workbook.Worksheets.First();

        // Determine the last used row so we don't iterate endlessly.
        int lastRow = worksheet.LastRowUsed()?.RowNumber() ?? 1;

        // Start at row 2 to skip the header.
        for (int row = 2; row <= lastRow; row++)
        {
            var cell = worksheet.Cell(row, 1);
            var cellValue = cell.GetValue<string>()?.Trim();

            if (!string.IsNullOrWhiteSpace(cellValue))
            {
                urls.Add(cellValue);
            }
        }

        return urls;
    }
}
