using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace StockDataWebsite.Models
{
     public class StockDataViewModel
    {
        public string CompanyName { get; set; }
        public string CompanySymbol { get; set; }
        public List<ReportPeriod> FinancialYears { get; set; }
        public string DataType { get; set; }
        public Dictionary<string, List<string>> FinancialDataElements { get; set; }
        public List<string> HtmlElementsOfInterest { get; set; }
        public List<StatementFinancialData> Statements { get; set; }
        public string StockPrice { get; set; }
    }
    public class DisplayMetricRow
    {
        public string DisplayName { get; set; }

        // The list of values for each financial year.
        public List<string> Values { get; set; }

        // Indicates whether this row is part of a merged group and should not display the DisplayName.
        public bool IsMergedRow { get; set; }

        // The number of rows to merge. Applicable only to the first row in a merged group.
        public int RowSpan { get; set; }
    }
    public class FinancialData
    {
        public int CompanyID { get; set; }
        public int Quarter { get; set; }
        public bool IsHtmlParsed { get; set; } // Tracks if HTML parsing is complete
        public int? Year { get; set; }
        public string FinancialDataJson { get; set; }
    }
    public class CompanySelection
    {
        [Key]
        public int CompanyID { get; set; }  // Add this property
        public string CompanyName { get; set; }
        [Required(ErrorMessage = "Company symbol is required.")]
        [Display(Name = "Company Symbol")]
        public string CompanySymbol { get; set; }
    }
}
//