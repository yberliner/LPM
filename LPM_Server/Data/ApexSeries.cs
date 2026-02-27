using ApexCharts;  // Add this to resolve SeriesType
namespace ApexSeries
{
    public class SeriesConfig<TItem> where TItem : class
    {
        public SeriesType SeriesType { get; set; }
        public string? Name { get; set; } = string.Empty;
        public string? Color { get; set; } = string.Empty;
        public Func<TItem, object> XValueSelector { get; set; } = default!;
        public Func<TItem, decimal?> YValueSelector { get; set; } = default!;
        public Func<IEnumerable<TItem>, decimal?> YAggregateSelector { get; set; } = default!;
        public Func<TItem, decimal> OpenSelector  { get; set; } = default!;
        public Func<TItem, decimal> HighSelector  { get; set; } = default!;
        public Func<TItem, decimal> LowSelector  { get; set; } = default!;
        public Func<TItem, decimal> CloseSelector  { get; set; } = default!;
        public Func<ListPoint<TItem>, object> OrderByDescending { get; set; } = default!;
        public bool ShowDataLabels { get; set; } =false;
        public Func<DataPoint<TItem>, object> OrderBy { get; set; } = default!;
        public Action<DataPoint<TItem>> DataPointMutator { get; set; }= default!;
        public Func<TItem, decimal> TopSelector { get; set; }= default!;
        public Func<TItem, decimal> BottomSelector { get; set; }= default!;
        public Func<TItem, decimal> YMinValueSelector { get; set; }= default!;
        public Func<TItem, decimal> YMaxValueSelector { get; set; }= default!;
        public Func<TItem, string> PointColor{ get; set; }= default!;
        public Func<TItem, decimal> MinSelector { get; set; }= default!;
        public Func<TItem, decimal> Quantile1Selector { get; set; }= default!;
        public Func<TItem, decimal> MedianSelector { get; set; }= default!;
        public Func<TItem, decimal> Quantile3Selector { get; set; }= default!;
        public Func<TItem, decimal> MaxSelector { get; set; }= default!;

        
    }
}