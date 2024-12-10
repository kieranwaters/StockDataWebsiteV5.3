using Microsoft.AspNetCore.Mvc;
using StockDataWebsite.Models;
using Nasdaq100FinancialScraper;

namespace StockDataWebsite.Controllers
{
    public class ScraperController : Controller
    {  // Action for displaying the form
        [HttpGet]
        public IActionResult Index()
        {
            return View();
        }
        [HttpPost]
        public async Task<IActionResult> ScrapeUnscrapedReports()
        {
            try
            {
                Console.WriteLine("[INFO] ScrapeUnscrapedReports action triggered.");

                // Start the scraping process asynchronously
                await StockScraperV3.URL.RunScraperForUnscrapedCompaniesAsync();

                Console.WriteLine("[INFO] Scraping process has started.");
                return Json(new { success = true, message = "Scraping has started!" });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] {ex.Message}");
                return Json(new { success = false, message = $"An error occurred: {ex.Message}" });
            }
        }


        [HttpPost]
        public async Task<IActionResult> Scrape(CompanySelection company)
        {
            Console.WriteLine("Scrape action called");

            if (string.IsNullOrEmpty(company.CompanySymbol))
            {
                Console.WriteLine("CompanySymbol is null or empty");
                return View("Index"); // Or return an error message
            }

            // Proceed with the scraping process
            var result = await StockScraperV3.URL.ScrapeReportsForCompanyAsync(company.CompanySymbol);
            ViewBag.Result = result;
            return View("Result");
        }

    }
}

