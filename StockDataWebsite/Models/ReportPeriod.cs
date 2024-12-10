namespace StockDataWebsite.Models
{
    public class ReportPeriod
    {
        public string DisplayName { get; set; }    // e.g., "2015" or "Q1Report 2024"
        public string CompositeKey { get; set; }   // e.g., "2015-0" or "2024-1"
    }
}
