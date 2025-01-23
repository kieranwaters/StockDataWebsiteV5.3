namespace StockDataWebsite.Models
{
    public class StatementFinancialData
    {
        public string StatementType { get; set; }
        public List<DisplayMetricRow1> DisplayMetrics { get; set; }
        public string ScalingLabel { get; set; } //
    }

    public class Metric
    {
        public string DisplayName { get; set; }
        public List<string> Values { get; set; }
    }
}
