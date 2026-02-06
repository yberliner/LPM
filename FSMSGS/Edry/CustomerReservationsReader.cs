public class CustomerReservationsProvider
{
    private readonly OrdersExcelToJson _ordersExcelToJson;

    private Dictionary<string, List<OrderRow>>? _ordersByCustomerNumber;

    public CustomerReservationsProvider(OrdersExcelToJson ordersExcelToJson)
    {
        _ordersExcelToJson = ordersExcelToJson;
    }

    /// <summary>
    /// Returns reservations for customerNumber.
    /// Generates and caches the dictionary ONCE (lazy) from the single JSON file in OutOrdersJson.
    ///
    /// Returns null if:
    /// - customerNumber is empty
    /// - dictionary cannot be generated (no json / invalid json)
    /// - customer does not exist
    ///
    /// Never throws.
    /// </summary>
    public async Task<List<OrderRow>?> GetCustomerReservationsFromExcelAsync(
    string customerNumber,
    int days = 0,
    string? status = null,
    string outputDir = "OutOrdersJson",
    CancellationToken ct = default)
    {
        Console.WriteLine("[CustomerReservationsProvider] START GetCustomerReservationsFromExcelAsync");

        if (!TryNormalizeInputs(ref customerNumber, ref days, ref status))
            return null;

        var baseList = await TryGetCustomerBaseListAsync(customerNumber, outputDir, ct);
        if (baseList == null)
            return null;

        var filtered = ApplyFilters(baseList, customerNumber, days, status);

        LogSummary(customerNumber, filtered);

        Console.WriteLine(
            $"[CustomerReservationsProvider] DONE. " +
            $"CustomerNumber: {customerNumber}, Days: {days}, Status: {(status ?? "ALL")}, Matches: {filtered.Count}");

        return filtered;
    }

    // ===================== Sub functions =====================

    private bool TryNormalizeInputs(ref string customerNumber, ref int days, ref string? status)
    {
        if (string.IsNullOrWhiteSpace(customerNumber))
        {
            Console.WriteLine("[CustomerReservationsProvider] CustomerNumber is empty. Returning null.");
            return false;
        }

        customerNumber = customerNumber.Trim();

        if (days < 0)
        {
            Console.WriteLine($"[CustomerReservationsProvider] CustomerNumber: {customerNumber}. Invalid days={days}. Returning null.");
            return false;
        }

        status = string.IsNullOrWhiteSpace(status) ? null : status.Trim();

        Console.WriteLine(
            $"[CustomerReservationsProvider] Inputs. " +
            $"CustomerNumber: {customerNumber}, Days: {days}, Status: {(status ?? "ALL")}");

        return true;
    }

    private async Task<List<OrderRow>?> TryGetCustomerBaseListAsync(
        string customerNumber,
        string outputDir,
        CancellationToken ct)
    {
        await EnsureDictionaryAsync(outputDir, ct);

        if (_ordersByCustomerNumber == null || _ordersByCustomerNumber.Count == 0)
        {
            Console.WriteLine($"[CustomerReservationsProvider] CustomerNumber: {customerNumber}. Dictionary missing/empty. Returning null.");
            return null;
        }

        if (!_ordersByCustomerNumber.TryGetValue(customerNumber, out var list))
        {
            Console.WriteLine($"[CustomerReservationsProvider] CustomerNumber: {customerNumber}. Not found. Returning null.");
            return null;
        }

        Console.WriteLine($"[CustomerReservationsProvider] Base list found. CustomerNumber: {customerNumber}, Orders: {list.Count}");
        return list;
    }

    private List<OrderRow> ApplyFilters(
        List<OrderRow> baseList,
        string customerNumber,
        int days,
        string? status)
    {
        IEnumerable<OrderRow> query = baseList;

        query = ApplyDaysFilter(query, customerNumber, days);
        query = ApplyStatusFilter(query, customerNumber, status);

        return query.ToList();
    }

    private IEnumerable<OrderRow> ApplyDaysFilter(
        IEnumerable<OrderRow> query,
        string customerNumber,
        int days)
    {
        if (days <= 0)
            return query;

        var cutoff = DateTime.Today.AddDays(-days);

        Console.WriteLine(
            $"[CustomerReservationsProvider] CustomerNumber: {customerNumber}. " +
            $"Days filter: last {days} days (from {cutoff:yyyy-MM-dd})");

        return query.Where(o =>
            o.ReservationDate.HasValue &&
            o.ReservationDate.Value.Date >= cutoff);
    }

    private IEnumerable<OrderRow> ApplyStatusFilter(
        IEnumerable<OrderRow> query,
        string customerNumber,
        string? status)
    {
        if (string.IsNullOrWhiteSpace(status))
            return query;

        Console.WriteLine(
            $"[CustomerReservationsProvider] CustomerNumber: {customerNumber}. " +
            $"Status filter: '{status}'");

        return query.Where(o =>
            !string.IsNullOrWhiteSpace(o.Status) &&
            string.Equals(o.Status.Trim(), status.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Force refresh the cached dictionary from JSON next time.
    /// </summary>
    public async Task ClearCacheAsync(
    string outputDir = "OutOrdersJson",
    CancellationToken ct = default)
    {
        Console.WriteLine("[CustomerReservationsProvider] ClearCacheAsync called.");

        _ordersByCustomerNumber = null;
        Console.WriteLine("[CustomerReservationsProvider] Cache cleared.");

        await EnsureDictionaryAsync(outputDir, ct);

        if (_ordersByCustomerNumber == null || _ordersByCustomerNumber.Count == 0)
        {
            Console.WriteLine("[CustomerReservationsProvider] Cache rebuild FAILED or empty.");
        }
        else
        {
            Console.WriteLine($"[CustomerReservationsProvider] Cache rebuilt successfully. Customers: {_ordersByCustomerNumber.Count}");
        }
    }


    // ===================== Internal =====================

    private async Task EnsureDictionaryAsync(string outputDir, CancellationToken ct)
    {
        if (_ordersByCustomerNumber != null && _ordersByCustomerNumber.Count > 0)
        {
            Console.WriteLine($"[CustomerReservationsProvider] Using cached dictionary. Customers: {_ordersByCustomerNumber.Count}");
            return;
        }

        Console.WriteLine("[CustomerReservationsProvider] Dictionary not cached. Generating from OrdersExcelToJson (JSON)...");
        _ordersByCustomerNumber = await _ordersExcelToJson.GenerateCustomerOrdersDictionaryFromJsonAsync(outputDir, ct);

        if (_ordersByCustomerNumber == null)
        {
            Console.WriteLine("[CustomerReservationsProvider] Dictionary generation FAILED (null).");
            return;
        }

        Console.WriteLine($"[CustomerReservationsProvider] Dictionary generated. Customers: {_ordersByCustomerNumber.Count}");
    }

    private void LogSummary(string customerNumber, List<OrderRow> orders)
    {
        var names = orders
            .Select(o => o.CustomerName?.Trim())
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        string namesStr = names.Count == 0 ? "NONE" : string.Join(" | ", names);

        Console.WriteLine(
            $"[CustomerReservationsProvider] SUMMARY. " +
            $"CustomerNumber: {customerNumber}, " +
            $"Matches: {orders.Count}, " +
            $"CustomerNames: {namesStr}");
    }




    public async Task<List<OrderRow>?> GetAllCustomerPerStatusDiff(
    int days = 0,
    string? status = null,
    string outputDir = "OutOrdersJson",
    CancellationToken ct = default)
    {
        Console.WriteLine("[CustomerReservationsProvider] START GetAllCustomerPerStatusDiff");

        if (!TryNormalizeInputsForAll(days, status, out int normalizedDays, out string? normalizedStatus))
            return null;

        await EnsureDictionaryAsync(outputDir, ct);

        if (_ordersByCustomerNumber == null || _ordersByCustomerNumber.Count == 0)
        {
            Console.WriteLine("[CustomerReservationsProvider] Dictionary missing/empty. Returning null.");
            return null;
        }

        var allOrders = FlattenAllOrders(_ordersByCustomerNumber);

        var filtered = ApplyDaysFilter(allOrders, normalizedDays);

        filtered = ApplyStatusDiffFilter(filtered, normalizedStatus);

        var result = filtered
            .OrderByDescending(o => o.ReservationDate ?? DateTime.MinValue) // newest first overall
            .ToList();

        Console.WriteLine(
            $"[CustomerReservationsProvider] DONE GetAllCustomerPerStatusDiff. " +
            $"Days: {normalizedDays}, StatusDiff: {(normalizedStatus ?? "ALL")}, Matches: {result.Count}");

        return result;
    }

    // ===================== Sub functions =====================

    private bool TryNormalizeInputsForAll(int days, string? status, out int normalizedDays, out string? normalizedStatus)
    {
        normalizedDays = days;
        normalizedStatus = string.IsNullOrWhiteSpace(status) ? null : status.Trim();

        if (days < 0)
        {
            Console.WriteLine($"[CustomerReservationsProvider] Invalid days={days}. Returning null.");
            return false;
        }

        Console.WriteLine(
            $"[CustomerReservationsProvider] Inputs. Days: {normalizedDays}, StatusDiff: {(normalizedStatus ?? "ALL")}");

        return true;
    }

    private IEnumerable<OrderRow> FlattenAllOrders(Dictionary<string, List<OrderRow>> dict)
    {
        Console.WriteLine($"[CustomerReservationsProvider] Flattening orders. Customers: {dict.Count}");
        return dict.Values.SelectMany(v => v);
    }

    private IEnumerable<OrderRow> ApplyDaysFilter(IEnumerable<OrderRow> query, int days)
    {
        if (days <= 0)
            return query;

        var cutoff = DateTime.Today.AddDays(-days);

        Console.WriteLine($"[CustomerReservationsProvider] Days filter: last {days} days (from {cutoff:yyyy-MM-dd})");

        return query.Where(o =>
            o.ReservationDate.HasValue &&
            o.ReservationDate.Value.Date >= cutoff);
    }

    private IEnumerable<OrderRow> ApplyStatusDiffFilter(IEnumerable<OrderRow> query, string? status)
    {
        // If status not provided -> "different than nothing" doesn't make sense,
        // so we return ALL orders (no diff filter).
        if (string.IsNullOrWhiteSpace(status))
            return query;

        Console.WriteLine($"[CustomerReservationsProvider] Status DIFF filter: status != '{status}'");

        return query.Where(o =>
        {
            var s = o.Status?.Trim();
            if (string.IsNullOrWhiteSpace(s))
                s = "Unknown";

            return !string.Equals(s, status, StringComparison.OrdinalIgnoreCase);
        });
    }

}
