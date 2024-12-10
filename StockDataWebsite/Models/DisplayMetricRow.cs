namespace StockDataWebsite.Models
{
    public class DisplayMetricRow1
    {
        public string DisplayName { get; set; }
        public List<string> Values { get; set; }
        public bool IsMergedRow { get; set; }
        public int RowSpan { get; set; }
    }
}
