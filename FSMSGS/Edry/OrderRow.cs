public sealed class OrderRow
{
    public int ExcelLineNumber { get; set; }      // Excel row number (1-based)
    public string? CustomerNumber { get; set; }   // A
    public string? CustomerName { get; set; }     // B
    public DateTime? ReservationDate { get; set; } // C
    public string? Colour { get; set; }           // D
    public string? OurNumber { get; set; }        // E
    public string? Supplier { get; set; }         // F
    public DateTime? DatePaint { get; set; }      // G
    public string? OrderNumber { get; set; }      // H
    public double? Weight { get; set; }           // I
    public DateTime? SupplyDate { get; set; }     // J
    public string Status { get; set; } = "Unknown"; // K normalized
}
