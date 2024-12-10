namespace StockDataWebsite.Models
{
    public class StatementFinancialData
    {
        public string StatementType { get; set; }
        public Dictionary<string, List<string>> FinancialMetrics { get; set; }
        public List<DisplayMetricRow1> DisplayMetrics { get; set; }
        public string ScalingLabel { get; set; } //
    }

}