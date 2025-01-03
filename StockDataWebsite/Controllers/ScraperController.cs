// ScraperController.cs
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using StockDataWebsite.Data;
using StockDataWebsite.Models;
using StockScraperV3;
using System.Data;
using System.Data.SqlClient;

namespace StockDataWebsite.Controllers
{//
    public static class SessionExtensions
    {
        // Extension method to set an object in session
        public static void SetObject(this ISession session, string key, object value)
        {
            session.SetString(key, JsonConvert.SerializeObject(value));
        }

        // Extension method to get an object from session
        public static T GetObject<T>(this ISession session, string key)
        {
            var value = session.GetString(key);
            return value == null ? default(T) : JsonConvert.DeserializeObject<T>(value);
        }
    }
    public class ScraperController : Controller
    {
        private readonly string ConnectionString = "Server=DESKTOP-SI08RN8\\SQLEXPRESS;Database=StockDataScraperDatabase;Integrated Security=True;";
        private readonly XBRLElementData _xbrlScraperService;
        private readonly ApplicationDbContext _context;
        private readonly ILogger<ScraperController> _logger;
        private readonly URL _urlService; // Injected URL service

        // Constructor injection of XBRLElementData and URL
        public ScraperController(
            XBRLElementData xbrlScraperService,
            ApplicationDbContext context,
            ILogger<ScraperController> logger,
            URL urlService) // Add URL to constructor
        {
            _xbrlScraperService = xbrlScraperService;
            _context = context;
            _logger = logger;
            _urlService = urlService; // Assign to private field
        }
        [HttpGet]
        public IActionResult Index()
        {
            // 1) Check if the user is logged in
            var username = HttpContext.Session.GetString("Username");
            if (username == null)
            {
                // Not logged in at all, redirect or show error
                return RedirectToAction("Login", "Account");
                // or return Unauthorized();
            }

            // 2) Lookup the user in your DB or your EF context
            var user = _context.Users.FirstOrDefault(u => u.Username == username);
            if (user == null)
            {
                // Also invalid
                return RedirectToAction("Login", "Account");
            }

            // 3) Check if user.Admin == true
            if (!user.Admin)
            {
                // The user isn't admin
                return Content("Access Denied. You do not have Admin privileges.");
                // or return Forbid();
            }

            // 4) If we get here, the user is admin
            return View(); // Show the Scraper page
        }

        [HttpGet("extract-and-log-xbrl-elements")]
        public async Task<IActionResult> ExtractAndLogXbrlElements()
        {
            try
            {
                _logger.LogInformation("Starting to extract XBRL elements (console mode).");

                var uniqueElements = await _urlService.ExtractAndOutputUniqueXbrlElementsAsync();

                // Instead of returning them to the webpage, log to console
                Console.WriteLine("[ExtractAndLogXbrlElements] Unique XBRL Elements:");
                if (uniqueElements.Count == 0)
                {
                    Console.WriteLine("None found.");
                }
                else
                {
                    foreach (var element in uniqueElements)
                    {
                        Console.WriteLine($" - {element}");
                    }
                }

                _logger.LogInformation("Extraction done. Results logged to console.");

                // Return a simple message so the user knows it finished
                return Ok("Successfully extracted and logged XBRL elements to the console.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while extracting XBRL elements (console mode).");
                return BadRequest($"An error occurred: {ex.Message}");
            }
        }
        [HttpGet("api/scraper/unique-xbrl-elements")]
        public async Task<IActionResult> UniqueXbrlElements()
        {
            try
            {
                var uniqueElements = await _urlService.ExtractAndOutputUniqueXbrlElementsAsync();

                // 1) Log them to the server console:
                Console.WriteLine("[INFO] Unique XBRL Elements:");
                if (uniqueElements.Count == 0)
                {
                    Console.WriteLine("  None found.");
                }
                else
                {
                    foreach (var element in uniqueElements)
                    {
                        Console.WriteLine($"  - {element}");
                    }
                }

                // 2) Return a simple text or JSON response telling the user to check console:
                return Ok("XBRL elements were logged to the server console. Check the console window!");
            }
            catch (Exception ex)
            {
                // Log or handle error
                Console.WriteLine($"[ERROR] {ex.Message}");
                return StatusCode(500, "An error occurred. Check server logs for more details.");
            }
        }
        [HttpGet]
        public async Task<IActionResult> CountNumericalValues()
        {
            // Define the SQL query to count numerical values excluding "Year" and "Quarter"
            string sql = @"
                SELECT COUNT(*) AS NumericCount
                FROM FinancialData
                CROSS APPLY OPENJSON(FinancialDataJson)
                WHERE [key] NOT LIKE 'Year%'
                  AND [key] NOT LIKE 'Quarter%'
                  AND TRY_CAST([value] AS DECIMAL(38,10)) IS NOT NULL;";

            long numericalValueCount = 0;

            try
            {
                _logger.LogInformation("Starting count of numerical values.");

                // Get the database connection from the context
                var connection = _context.Database.GetDbConnection();

                // Ensure the connection is open
                if (connection.State != ConnectionState.Open)
                    await connection.OpenAsync();

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = sql;
                    command.CommandType = CommandType.Text;

                    // Increase the command timeout (e.g., to 120 seconds)
                    command.CommandTimeout = 12000000; // Timeout in seconds

                    // Execute the command and retrieve the scalar result
                    var result = await command.ExecuteScalarAsync();

                    if (result != null && long.TryParse(result.ToString(), out long count))
                    {
                        numericalValueCount = count;
                        _logger.LogInformation($"Numerical values counted successfully: {numericalValueCount}");
                    }
                    else
                    {
                        // Handle cases where the result is null or cannot be parsed
                        _logger.LogWarning("Failed to retrieve or parse the numerical value count.");
                        return StatusCode(500, new { success = false, message = "Failed to retrieve the numerical value count." });
                    }
                }
            }
            catch (Exception ex)
            {
                // Log the exception and handle it appropriately
                _logger.LogError($"An error occurred while counting numerical values: {ex.Message}");
                return StatusCode(500, new { success = false, message = "An error occurred while processing your request." });
            }

            return Json(new { Count = numericalValueCount });
        }
        public class RemoveCompanyRequest
        {
            public string CompanySymbol { get; set; }
        }

        [HttpPost]
        [ValidateAntiForgeryToken] // Ensures CSRF protection
        public async Task<IActionResult> RemoveSelectedCompany([FromBody] RemoveCompanyRequest request)
        {
            if (request == null || string.IsNullOrEmpty(request.CompanySymbol))
            {
                _logger.LogWarning("RemoveSelectedCompany called with invalid data.");
                return Json(new { success = false, message = "Invalid company symbol." });
            }

            int companyId;

            // Step 1: Retrieve CompanyID using CompanySymbol
            string getCompanyIdQuery =
  "SELECT CompanyID FROM [dbo].[CompaniesList] WHERE CompanySymbol = @CompanySymbol";



            using (SqlConnection conn = new SqlConnection(ConnectionString))
            {
                await conn.OpenAsync();

                using (SqlCommand cmd = new SqlCommand(getCompanyIdQuery, conn))
                {
                    cmd.Parameters.AddWithValue("@CompanySymbol", request.CompanySymbol);

                    object result = await cmd.ExecuteScalarAsync();

                    if (result == null)
                    {
                        _logger.LogWarning($"Company with symbol '{request.CompanySymbol}' not found.");
                        return Json(new { success = false, message = "Company not found." });
                    }

                    companyId = Convert.ToInt32(result);
                }

                // Step 2: Delete FinancialData records with the retrieved CompanyID
                string deleteFinancialDataQuery = "DELETE FROM FinancialData WHERE CompanyID = @CompanyID";

                using (SqlCommand cmd = new SqlCommand(deleteFinancialDataQuery, conn))
                {
                    cmd.Parameters.AddWithValue("@CompanyID", companyId);

                    int rowsAffected = await cmd.ExecuteNonQueryAsync();

                    _logger.LogInformation($"Deleted {rowsAffected} FinancialData records for CompanyID {companyId}.");
                }

                // Optionally, delete the company from the Companies table if needed
                // string deleteCompanyQuery = "DELETE FROM Companies WHERE CompanyID = @CompanyID";
                // using (SqlCommand cmd = new SqlCommand(deleteCompanyQuery, conn))
                // {
                //     cmd.Parameters.AddWithValue("@CompanyID", companyId);
                //     int rowsAffected = await cmd.ExecuteNonQueryAsync();
                //     _logger.LogInformation($"Deleted {rowsAffected} Company records for CompanyID {companyId}.");
                // }
            }

            // Step 3: Remove the company from the session's SelectedCompanies list
            var selectedCompanies = HttpContext.Session.GetObject<List<CompanySelection>>("SelectedCompanies") ?? new List<CompanySelection>();
            var companyToRemove = selectedCompanies.FirstOrDefault(c =>
                c.CompanySymbol.Equals(request.CompanySymbol, StringComparison.OrdinalIgnoreCase));

            if (companyToRemove != null)
            {
                selectedCompanies.Remove(companyToRemove);
                HttpContext.Session.SetObject("SelectedCompanies", selectedCompanies);
                _logger.LogInformation($"Company '{request.CompanySymbol}' removed from selected companies list in session.");
                return Json(new { success = true, message = "Company removed and related financial data deleted successfully." });
            }
            else
            {
                _logger.LogWarning($"Company '{request.CompanySymbol}' not found in session's selected companies list.");
                return Json(new { success = false, message = "Company not found in selected companies list." });
            }
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
