using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using ClosedXML.Excel;
/// <summary>
/// Singleton-friendly, NON-static.
/// Provides two main features:
/// 1) Build JSON file from Excel stream (and archive older JSONs).
/// 2) Generate Dictionary<CustomerNumber, List<OrderRow>> from an Excel stream (no file I/O).
///
/// Never throws (logs and returns null/empty).
/// </summary>
public class OrdersExcelToJson
{
    private readonly HashSet<string> _allowedStatuses =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "Created",
            "In Progress",
            "Persumable arrive Date",
            "In house"
        };

    private readonly string[] _reservationDateFormatsC =
    {
        "MM-dd-yy", "M-d-yy", "MM-d-yy", "M-dd-yy"
    };

    private readonly string[] _supplyDateFormatsJ =
    {
        "dd-MM-yy", "d-M-yy", "dd-M-yy", "d-MM-yy"
    };

    // =========================================================
    // 1) Build JSON from Excel
    // =========================================================

    /// <summary>
    /// Builds a JSON file from the Excel stream.
    /// Before writing the new file, archives existing *.json files into outputDir\Old.
    /// Returns path to created json file, or null on failure.
    /// Never throws.
    /// </summary>
    public async Task<string?> BuildJsonFileFromExcelAsync(
        Stream excelStream,
        string outputDir = "OutOrdersJson",
        string? outputFileBaseName = null,
        int syntheticStartCustomerNumber = 1000,
        CancellationToken ct = default)
    {
        Console.WriteLine("[OrdersExcelToJson] START BuildJsonFileFromExcelAsync");

        var rows = await ReadAndNormalizeOrdersFromExcelAsync(excelStream, syntheticStartCustomerNumber, ct);
        if (rows == null || rows.Count == 0)
        {
            Console.WriteLine("[OrdersExcelToJson] No rows produced. EXIT (null)");
            return null;
        }

        Console.WriteLine("[OrdersExcelToJson] Serializing rows to JSON...");
        string json = SerializeToJson(rows);
        Console.WriteLine($"[OrdersExcelToJson] JSON size: {json.Length} chars");

        Console.WriteLine("[OrdersExcelToJson] Archiving existing JSON files...");
        ArchiveExistingJsonFiles(outputDir);

        Console.WriteLine("[OrdersExcelToJson] Writing new JSON file...");
        string jsonPath = await WriteJsonFileAsync(outputDir, outputFileBaseName, json, ct);

        Console.WriteLine($"[OrdersExcelToJson] DONE BuildJsonFileFromExcelAsync. File: {jsonPath}");
        return jsonPath;
    }

    // =========================================================
    // 2) Generate Dictionary from Excel
    // =========================================================

    /// <summary>
    /// Generates Dictionary<CustomerNumber, List<OrderRow>> from Excel stream.
    /// Each list is sorted so:
    ///   list[0]  = MOST RECENT ReservationDate
    ///   list[^1] = OLDEST ReservationDate
    /// Null ReservationDate goes last.
    ///
    /// This method does NOT write files.
    /// Returns null on failure. Never throws.
    /// </summary>
    public async Task<Dictionary<string, List<OrderRow>>?> GenerateCustomerOrdersDictionaryAsync(
        Stream excelStream,
        int syntheticStartCustomerNumber = 1000,
        CancellationToken ct = default)
    {
        Console.WriteLine("[OrdersExcelToJson] START GenerateCustomerOrdersDictionaryAsync");

        var rows = await ReadAndNormalizeOrdersFromExcelAsync(excelStream, syntheticStartCustomerNumber, ct);
        if (rows == null || rows.Count == 0)
        {
            Console.WriteLine("[OrdersExcelToJson] No rows produced. EXIT (null)");
            return null;
        }

        Console.WriteLine("[OrdersExcelToJson] Building customer dictionary...");
        var dict = BuildCustomerIndex(rows);

        Console.WriteLine($"[OrdersExcelToJson] DONE GenerateCustomerOrdersDictionaryAsync. Customers: {dict.Count}");
        return dict;
    }

    // =========================================================
    // Shared: Read + Normalize from Excel
    // =========================================================

    /// <summary>
    /// Reads Excel -> rows, fills missing customer numbers, trims strings,
    /// normalizes status, and sorts rows by customer number (for stable output).
    /// Never throws. Returns null on failure.
    /// </summary>
    private async Task<List<OrderRow>?> ReadAndNormalizeOrdersFromExcelAsync(
        Stream excelStream,
        int syntheticStartCustomerNumber,
        CancellationToken ct)
    {
        try
        {
            using var ms = await CopyToSeekableStreamAsync(excelStream, ct);

            using var wb = new XLWorkbook(ms);
            var ws = wb.Worksheets.First();

            var used = ws.RangeUsed();
            if (used == null)
            {
                Console.WriteLine("[OrdersExcelToJson] Excel sheet empty.");
                return null;
            }

            Console.WriteLine("[OrdersExcelToJson] Reading rows from Excel...");
            var rows = ReadRows(ws, used);
            Console.WriteLine($"[OrdersExcelToJson] Rows read: {rows.Count}");

            if (rows.Count == 0)
                return rows;

            Console.WriteLine("[OrdersExcelToJson] Resolving missing customer numbers...");
            var chosenNumberByName = BuildChosenCustomerNumberMap(rows, syntheticStartCustomerNumber);
            FillMissingCustomerNumbers(rows, chosenNumberByName);

            Console.WriteLine("[OrdersExcelToJson] Sorting rows by CustomerNumber (stable)...");
            rows = SortRowsByCustomerNumber(rows);

            return rows;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[OrdersExcelToJson] Error reading Excel: {ex.Message}");
            return null;
        }
    }

    private async Task<MemoryStream> CopyToSeekableStreamAsync(Stream input, CancellationToken ct)
    {
        var ms = new MemoryStream();
        await input.CopyToAsync(ms, ct);
        ms.Position = 0;
        return ms;
    }

    private List<OrderRow> ReadRows(IXLWorksheet ws, IXLRange usedRange)
    {
        int firstDataRow = 2;
        int lastRow = usedRange.RangeAddress.LastAddress.RowNumber;

        var rows = new List<OrderRow>();

        for (int r = firstDataRow; r <= lastRow; r++)
        {
            if (ws.Row(r).IsEmpty()) continue;

            rows.Add(new OrderRow
            {
                ExcelLineNumber = r,
                CustomerNumber = GetString(ws, r, 1),
                CustomerName = GetString(ws, r, 2),
                ReservationDate = GetDateWithFormats(ws, r, 3, _reservationDateFormatsC),
                Colour = GetString(ws, r, 4),
                OurNumber = GetString(ws, r, 5),
                Supplier = GetString(ws, r, 6),
                DatePaint = GetDateLoose(ws, r, 7),
                OrderNumber = GetString(ws, r, 8),
                Weight = GetDouble(ws, r, 9),
                SupplyDate = GetDateWithFormats(ws, r, 10, _supplyDateFormatsJ),
                Status = NormalizeStatus(GetString(ws, r, 11))
            });
        }

        return rows;
    }

    private Dictionary<string, string> BuildChosenCustomerNumberMap(
        List<OrderRow> rows,
        int syntheticStartCustomerNumber)
    {
        var freq = new Dictionary<string, Dictionary<string, int>>(StringComparer.OrdinalIgnoreCase);

        foreach (var r in rows)
        {
            if (string.IsNullOrWhiteSpace(r.CustomerName) ||
                string.IsNullOrWhiteSpace(r.CustomerNumber))
                continue;

            var name = r.CustomerName.Trim();
            var num = r.CustomerNumber.Trim();

            if (!freq.TryGetValue(name, out var f))
                freq[name] = f = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            f[num] = f.TryGetValue(num, out var c) ? c + 1 : 1;
        }

        var chosen = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var kvp in freq)
        {
            var best = kvp.Value
                .OrderByDescending(x => x.Value)
                .ThenBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
                .First();

            chosen[kvp.Key] = best.Key;
        }

        int next = syntheticStartCustomerNumber;

        foreach (var name in rows
                     .Select(r => r.CustomerName?.Trim())
                     .Where(n => !string.IsNullOrWhiteSpace(n))
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!chosen.ContainsKey(name!))
                chosen[name!] = (next++).ToString(CultureInfo.InvariantCulture);
        }

        return chosen;
    }

    private void FillMissingCustomerNumbers(List<OrderRow> rows, Dictionary<string, string> chosenByName)
    {
        foreach (var r in rows)
        {
            if (string.IsNullOrWhiteSpace(r.CustomerNumber) &&
                !string.IsNullOrWhiteSpace(r.CustomerName) &&
                chosenByName.TryGetValue(r.CustomerName.Trim(), out var v))
            {
                r.CustomerNumber = v;
            }

            r.CustomerNumber = r.CustomerNumber?.Trim();
            r.CustomerName = r.CustomerName?.Trim();

            r.Colour = r.Colour?.Trim();
            r.OurNumber = r.OurNumber?.Trim();
            r.Supplier = r.Supplier?.Trim();
            r.OrderNumber = r.OrderNumber?.Trim();
            r.Status = (r.Status ?? "Unknown").Trim();
        }
    }

    private List<OrderRow> SortRowsByCustomerNumber(List<OrderRow> rows)
    {
        return rows
            .OrderBy(r => string.IsNullOrWhiteSpace(r.CustomerNumber) ? 1 : 0)
            .ThenBy(r => int.TryParse(r.CustomerNumber, out var n) ? n : int.MaxValue)
            .ThenBy(r => r.CustomerNumber, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.ExcelLineNumber)
            .ToList();
    }

    // =========================================================
    // Dictionary builder (sorted per customer by ReservationDate DESC)
    // =========================================================

    private Dictionary<string, List<OrderRow>> BuildCustomerIndex(List<OrderRow> rows)
    {
        var dict = new Dictionary<string, List<OrderRow>>(StringComparer.OrdinalIgnoreCase);

        foreach (var r in rows)
        {
            if (string.IsNullOrWhiteSpace(r.CustomerNumber)) continue;

            var key = r.CustomerNumber.Trim();
            if (!dict.TryGetValue(key, out var list))
                dict[key] = list = new List<OrderRow>();

            list.Add(r);
        }

        foreach (var kvp in dict)
            kvp.Value.Sort(CompareReservationDateDescNullLast);

        return dict;
    }

    /// <summary>
    /// ReservationDate DESC (newest first), null dates last.
    /// </summary>
    private int CompareReservationDateDescNullLast(OrderRow a, OrderRow b)
    {
        if (!a.ReservationDate.HasValue && !b.ReservationDate.HasValue) return 0;
        if (!a.ReservationDate.HasValue) return 1;
        if (!b.ReservationDate.HasValue) return -1;
        return b.ReservationDate.Value.CompareTo(a.ReservationDate.Value);
    }

    // =========================================================
    // JSON + File output
    // =========================================================

    private string SerializeToJson(List<OrderRow> rows)
    {
        return JsonSerializer.Serialize(
            rows,
            new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            });
    }

    private void ArchiveExistingJsonFiles(string outputDir)
    {
        Directory.CreateDirectory(outputDir);
        var oldDir = Path.Combine(outputDir, "Old");
        Directory.CreateDirectory(oldDir);

        foreach (var f in Directory.GetFiles(outputDir, "*.json", SearchOption.TopDirectoryOnly))
        {
            var dst = Path.Combine(oldDir, Path.GetFileName(f));
            if (File.Exists(dst)) File.Delete(dst);
            File.Move(f, dst);
        }
    }

    private async Task<string> WriteJsonFileAsync(
        string outputDir,
        string? baseName,
        string json,
        CancellationToken ct)
    {
        string name = string.IsNullOrWhiteSpace(baseName) ? "Orders" : baseName.Trim();
        string path = Path.Combine(outputDir, $"{name}_{DateTime.Now:yyyyMMdd_HHmmss}.json");

        await File.WriteAllTextAsync(path, json, new UTF8Encoding(false), ct);
        return path;
    }

    // =========================================================
    // Cell helpers
    // =========================================================

    private string NormalizeStatus(string? raw)
    {
        var s = (raw ?? "").Trim();
        return s.Length == 0 || !_allowedStatuses.Contains(s) ? "Unknown" : s;
    }

    private string? GetString(IXLWorksheet ws, int r, int c)
    {
        var s = ws.Cell(r, c).GetFormattedString()?.Trim();
        return string.IsNullOrWhiteSpace(s) ? null : s;
    }

    private DateTime? GetDateWithFormats(IXLWorksheet ws, int r, int c, string[] formats)
    {
        var cell = ws.Cell(r, c);
        if (cell.IsEmpty()) return null;

        if (cell.DataType == XLDataType.DateTime)
            return cell.GetDateTime();

        var s = cell.GetFormattedString()?.Trim();
        if (string.IsNullOrWhiteSpace(s)) return null;

        return DateTime.TryParseExact(
            s,
            formats,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AllowWhiteSpaces,
            out var d) ? d : null;
    }

    private DateTime? GetDateLoose(IXLWorksheet ws, int r, int c)
    {
        var cell = ws.Cell(r, c);
        if (cell.IsEmpty()) return null;

        if (cell.DataType == XLDataType.DateTime)
            return cell.GetDateTime();

        var s = cell.GetFormattedString()?.Trim();
        if (string.IsNullOrWhiteSpace(s)) return null;

        if (DateTime.TryParse(s, CultureInfo.CurrentCulture, DateTimeStyles.None, out var dt))
            return dt;

        if (DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out dt))
            return dt;

        return null;
    }

    private double? GetDouble(IXLWorksheet ws, int r, int c)
    {
        var s = ws.Cell(r, c).GetFormattedString()?.Trim();
        if (string.IsNullOrWhiteSpace(s)) return null;

        if (double.TryParse(s, NumberStyles.Any, CultureInfo.CurrentCulture, out var d))
            return d;

        if (double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out d))
            return d;

        return null;
    }



    
    public async Task<Dictionary<string, List<OrderRow>>?> GenerateCustomerOrdersDictionaryFromJsonAsync(
        string outputDir = "OutOrdersJson",
        CancellationToken ct = default)
    {
        Console.WriteLine("[OrdersExcelToJson] START GenerateCustomerOrdersDictionaryFromJsonAsync");

        var jsonPath = TryGetSingleJsonFile(outputDir);
        if (string.IsNullOrWhiteSpace(jsonPath))
        {
            Console.WriteLine("[OrdersExcelToJson] No single JSON file found. EXIT (null)");
            return null;
        }

        var rows = await LoadRowsFromJsonAsync(jsonPath!, ct);
        if (rows.Count == 0)
        {
            Console.WriteLine("[OrdersExcelToJson] JSON rows=0. EXIT (null)");
            return null;
        }

        Console.WriteLine("[OrdersExcelToJson] Building customer dictionary from JSON rows...");
        var dict = BuildCustomerIndex(rows);

        Console.WriteLine($"[OrdersExcelToJson] DONE GenerateCustomerOrdersDictionaryFromJsonAsync. Customers: {dict.Count}");
        return dict;
    }

    private string? TryGetSingleJsonFile(string outputDir)
    {
        if (!Directory.Exists(outputDir))
        {
            Console.WriteLine($"[OrdersExcelToJson] Directory not found: {outputDir}");
            return null;
        }

        var files = Directory.GetFiles(outputDir, "*.json", SearchOption.TopDirectoryOnly);

        if (files.Length == 0)
        {
            Console.WriteLine($"[OrdersExcelToJson] No JSON file found in: {outputDir}");
            return null;
        }

        if (files.Length > 1)
        {
            Console.WriteLine($"[OrdersExcelToJson] Expected 1 JSON file in '{outputDir}', found {files.Length}");
            foreach (var f in files)
                Console.WriteLine($"[OrdersExcelToJson] Found: {Path.GetFileName(f)}");
            return null;
        }

        Console.WriteLine($"[OrdersExcelToJson] Using JSON file: {files[0]}");
        return files[0];
    }

    private async Task<List<OrderRow>> LoadRowsFromJsonAsync(string jsonPath, CancellationToken ct)
    {
        Console.WriteLine("[OrdersExcelToJson] Loading rows from JSON...");

        try
        {
            await using var fs = File.OpenRead(jsonPath);

            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };

            var rows = await JsonSerializer.DeserializeAsync<List<OrderRow>>(fs, options, ct);
            if (rows == null)
            {
                Console.WriteLine("[OrdersExcelToJson] Deserialize returned null");
                return new List<OrderRow>();
            }

            Console.WriteLine($"[OrdersExcelToJson] Rows loaded from JSON: {rows.Count}");
            return rows;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[OrdersExcelToJson] Error reading JSON: {ex.Message}");
            return new List<OrderRow>();
        }
    }

}
