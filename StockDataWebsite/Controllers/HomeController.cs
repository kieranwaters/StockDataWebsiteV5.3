using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StockDataWebsite.Data;
using StockDataWebsite.Models;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;

namespace StockDataWebsite.Controllers
{
    public class HomeController : Controller
    {
        private readonly ApplicationDbContext _context;

        public HomeController(ApplicationDbContext context)
        {
            _context = context;
        }

        public IActionResult Index()
        {
            return View();
        }

        [HttpGet]
        public IActionResult SearchResults(string CompanySearch)
        {
            if (string.IsNullOrEmpty(CompanySearch))
            {
                return RedirectToAction("Index");
            }

            // Redirect to StockDataController's StockData action
            return RedirectToAction("StockData", "StockData", new { companyName = CompanySearch });
        }

        public IActionResult StockData(string companyName, string dataType = "annual")
        {
            try
            {
                var company = _context.CompaniesList
                    .Where(c => c.CompanyName == companyName)
                    .Select(c => new { c.CompanyID, c.CompanySymbol })
                    .FirstOrDefault();

                if (company == null)
                {
                    Console.WriteLine("No valid CompanyID found, aborting operation.");
                    return BadRequest("Company not found.");
                }

                int companyId = company.CompanyID;

                // Fetch the most recent 5 financial years where IsHtmlParsed is true
                var recentYears = _context.FinancialData
                    .Where(fd => fd.CompanyID == companyId && fd.IsHtmlParsed)
                    .Where(fd => fd.Year.HasValue && fd.Quarter == 0)
                    .OrderByDescending(fd => fd.Year.Value)
                    .Select(fd => fd.Year.Value)
                    .Distinct()
                    .Take(5)
                    .ToList();

                if (!recentYears.Any())
                {
                    Console.WriteLine("No valid financial years found.");
                    return BadRequest("No valid financial data found.");
                }

                // Load the financial data records into memory
                var financialDataRecords = _context.FinancialData
                    .Where(fd => fd.CompanyID == companyId && fd.Year.HasValue && recentYears.Contains(fd.Year.Value) && fd.Quarter == 0)
                    .OrderByDescending(fd => fd.Year)
                    .ToList();

                if (!financialDataRecords.Any())
                {
                    Console.WriteLine("No financial data records found.");
                    return BadRequest("No financial records found.");
                }

                // Prepare financial elements dictionary to store strings
                var financialDataElements = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

                // Elements of interest, as per the HTMLElementsOfInterest dictionary
                var elementsOfInterest = DataElements.FinancialElementLists.HTMLElementsOfInterest;

                // Collect all unique element keys from the elements of interest
                var allElementKeys = elementsOfInterest
                    .Select(e => e.Value.ColumnName)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToHashSet();

                // Iterate through the elements and retrieve data for all recent years
                foreach (var elementKey in allElementKeys)
                {
                    var elementDataForYears = new List<string>();

                    foreach (var record in financialDataRecords)
                    {
                        if (string.IsNullOrEmpty(record.FinancialDataJson))
                        {
                            elementDataForYears.Add("null");
                            continue;
                        }

                        var financialData = JsonConvert.DeserializeObject<Dictionary<string, object>>(record.FinancialDataJson);

                        if (financialData.TryGetValue(elementKey, out var value))
                        {
                            string valueAsString = value != null ? value.ToString() : "null";
                            elementDataForYears.Add(valueAsString);
                        }
                        else
                        {
                            elementDataForYears.Add("null");
                        }
                    }

                    // Check if the element has data for at least one year
                    if (elementDataForYears.Any(data => data != "null"))
                    {
                        string displayName = elementsOfInterest
                            .FirstOrDefault(e => e.Value.ColumnName.Equals(elementKey, StringComparison.OrdinalIgnoreCase))
                            .Key?.FirstOrDefault() ?? elementKey;

                        // Ensure the key is unique in the dictionary
                        string uniqueKey = displayName;
                        int counter = 1;
                        while (financialDataElements.ContainsKey(uniqueKey))
                        {
                            uniqueKey = $"{displayName}_{counter}";
                            counter++;
                        }

                        financialDataElements.Add(uniqueKey, elementDataForYears);
                    }
                }

                // Assign FinancialYears as List<ReportPeriod>
                var financialYearsList = recentYears.Select(y => new ReportPeriod
                {
                    DisplayName = y.ToString(),
                    CompositeKey = $"{y}-0"
                }).ToList();

                // Create view model
                var model = new StockDataViewModel
                {
                    CompanyName = companyName,
                    CompanySymbol = company.CompanySymbol,
                    FinancialYears = financialYearsList, // Assign List<ReportPeriod>
                    FinancialDataElements = financialDataElements
                };

                return View(model); // Pass the model to the view
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Exception occurred: {ex.Message}");
                return StatusCode(500, "An error occurred while processing the data.");
            }
        }
    }
}
