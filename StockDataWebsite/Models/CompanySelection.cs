using Microsoft.AspNetCore.Mvc.Rendering;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace StockDataWebsite.Models
{
   
        public class CompanyDto
        {
            public string CompanyName { get; set; } // Name of the company
            public string CompanySymbol { get; set; } // Ticker symbol
        }

    public class StockDataViewModel
    {
        public string CompanyName { get; set; }
        public string CompanySymbol { get; set; }
        public List<ReportPeriod> FinancialYears { get; set; }
        public string DataType { get; set; }
        public List<StatementFinancialData> Statements { get; set; }
        public string? StockPrice { get; set; }
        public string BaseType { get; set; }
        public string SelectedYearFilter { get; set; }
        public List<SelectListItem> YearFilterOptions { get; set; } = new List<SelectListItem>
    {
        new SelectListItem { Value = "all", Text = "All Years" },
        new SelectListItem { Value = "5", Text = "Last 5 Years" },
        new SelectListItem { Value = "3", Text = "Last 3 Years" },
        new SelectListItem { Value = "1", Text = "Last Year" },
    };
        public List<int> UniqueYears { get; set; }
        public decimal? DailyChange { get; internal set; }
        public long? Volume { get; internal set; }
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