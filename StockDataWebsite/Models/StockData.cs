namespace StockDataWebsite.Models
{
    public class StatementFinancialData
    {
        public string StatementType { get; set; }
        public Dictionary<string, List<string>> FinancialMetrics { get; set; }
        public List<DisplayMetricRow1> DisplayMetrics { get; set; }
        public string ScalingLabel { get; set; } //
    }
    public class FinancialYear
    {
        public string DisplayName { get; set; } // e.g., "2023 Report Q1"
    }

    public class Statement
    {
        public string StatementType { get; set; }
        public string ScalingLabel { get; set; }
        public List<Metric> DisplayMetrics { get; set; }
        // Other properties...
    }

    public class Metric
    {
        public string DisplayName { get; set; }
        public List<string> Values { get; set; }
    }
}
