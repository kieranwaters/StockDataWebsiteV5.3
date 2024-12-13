// ScraperController.cs
using Microsoft.AspNetCore.Mvc;
using StockDataWebsite.Models;
using StockScraperV3;
using System.Threading.Tasks;

namespace StockDataWebsite.Controllers
{
    public class ScraperController : Controller
    {
        private readonly XBRLElementData _xbrlScraperService;

        // Constructor injection of XBRLElementData
        public ScraperController(XBRLElementData xbrlScraperService)
        {
            _xbrlScraperService = xbrlScraperService;
        }

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
        public async Task<IActionResult> ContinueScrapingUnscrapedReports()
        {
            try
            {
                // Start the scraping process asynchronously
                await StockScraperV3.URL.ContinueScrapingUnscrapedCompaniesAsync();

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

        // New action to trigger XBRL data parsing and saving
        [HttpPost]
        [ValidateAntiForgeryToken] // Ensures CSRF protection
        public async Task<IActionResult> ParseAndSaveXbrlData()
        {
            try
            {
                Console.WriteLine("[INFO] ParseAndSaveXbrlData action triggered.");

                // Start the scraping process asynchronously
                await _xbrlScraperService.ParseAndSaveXbrlDataAsync();

                Console.WriteLine("[INFO] XBRL data parsed and saved successfully.");
                return Json(new { success = true, message = "XBRL data has been parsed and saved successfully." });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] {ex.Message}");
                return Json(new { success = false, message = $"An error occurred: {ex.Message}" });
            }
        }

    }
}
