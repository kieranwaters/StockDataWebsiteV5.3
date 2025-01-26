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
        public List<StatementFinancialData> Statements { get; set; }
        public string StockPrice { get; set; }
        public string BaseType { get; set; }
        public string SelectedYearFilter { get; set; }
        public List<int> UniqueYears { get; set; }
    }
    public class FinancialData
    {
        public int CompanyID { get; set; }
        public int Quarter { get; set; }
        public bool IsHtmlParsed { get; set; } // Tracks if HTML parsing is complete
        public int? Year { get; set; }
        public DateTime EndDate { get; set; }
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