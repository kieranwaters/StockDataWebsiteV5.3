using Microsoft.AspNetCore.Mvc;
using StockDataWebsite.Data;
using StockDataWebsite.Models;
using Newtonsoft.Json;
using System.Text.RegularExpressions;
using System.Globalization;
namespace StockDataWebsite.Controllers
{    
    public class StockDataController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly TwelveDataService _twelveDataService;
        public StockDataController(ApplicationDbContext context, TwelveDataService twelveDataService)
        {
            _context = context;
            _twelveDataService = twelveDataService;
        }
        private string NormalizeStatementType(string statementType)
        {
            if (string.IsNullOrEmpty(statementType))
                return statementType;            
            string normalized = statementType.ToLowerInvariant();// Convert to lowercase for consistent processing
            normalized = Regex.Replace(normalized, @"\s*\(.*?\)\s*", " ", RegexOptions.IgnoreCase);
            string[] keywordsToRemove = new[] { "consolidated", "condensed", "unaudited", "the" };
            foreach (var keyword in keywordsToRemove)
            {
                normalized = Regex.Replace(normalized, $@"\b{Regex.Escape(keyword)}\b", "", RegexOptions.IgnoreCase);
            }
            normalized = Regex.Replace(normalized, @"\s+", " ").Trim();
            normalized = CultureInfo.CurrentCulture.TextInfo.ToTitleCase(normalized);
            var statementTypeMappings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        { "Operations", "Statement of Operations" },
        { "Cashflows", "Cashflow Statement" },
        { "Balancesheets", "Balance Sheet" },
        { "Comprehensiveincome", "Income Statement" } 
    };
            if (statementTypeMappings.TryGetValue(normalized, out string mappedType))
            {
                return mappedType;
            }
            return "General";
        }
        public async Task<IActionResult> StockData(string companyName, string dataType = "annual")
        {
            try
            {
                dataType = ValidateDataType(dataType);
                var (companyId, companySymbol) = GetCompanyDetails(companyName);
                if (companyId == 0)
                {
                    return BadRequest("Company not found.");
                }

                var (recentReportPairs, recentReportKeys, financialDataRecords, recentReports) = FetchRecentReportsAndData(companyId, dataType);
                if (!financialDataRecords.Any())
                {
                    return BadRequest("No financial records found.");
                }

                var financialDataRecordsLookup = financialDataRecords.ToDictionary(fd => $"{fd.Year.Value}-{fd.Quarter}", fd => fd);
                var financialDataElements = InitializeFinancialDataElements(financialDataRecords);
                PopulateFinancialDataElements(financialDataElements, financialDataRecordsLookup, recentReportPairs);
                var statementsDict = GroupFinancialDataByStatement(financialDataElements);
                var orderedStatements = CreateOrderedStatements(statementsDict, recentReports);

                // Fetch the stock price
                var stockPrice = await _twelveDataService.GetStockPriceAsync(companySymbol);
                var formattedStockPrice = stockPrice.HasValue ? $"${stockPrice.Value:F2}" : "N/A";

                // Set StockPrice on the model (not just ViewBag)
                var model = new StockDataViewModel
                {
                    CompanyName = companyName,
                    CompanySymbol = companySymbol,
                    FinancialYears = CreateReportPeriods(dataType, recentReportPairs),
                    Statements = orderedStatements,
                    StockPrice = formattedStockPrice, // Add this line
                    DataType = dataType                // Set DataType as well if it's needed in the view
                };

                return View(model);
            }
            catch (Exception ex)
            {
                return StatusCode(500, "An error occurred while processing the data.");
            }
        }

        private Dictionary<string, List<string>> InitializeFinancialDataElements(List<FinancialData> financialDataRecords)
        {
            var financialDataElements = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            var htmlKeyRegex = new Regex(@"^(?:HTML_)?(?:AnnualReport|Q\dReport)_(?<StatementType>.*?)_(?<MetricName>.+)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
            foreach (var record in financialDataRecords)
            {
                var jsonData = record.FinancialDataJson ?? "{}";
                var financialData = JsonConvert.DeserializeObject<Dictionary<string, object>>(jsonData);
                if (financialData == null) continue;
                foreach (var kvp in financialData)
                {
                    if (kvp.Key.StartsWith("HTML_", StringComparison.OrdinalIgnoreCase))
                    {
                        var match = htmlKeyRegex.Match(kvp.Key);
                        string normalizedKey;
                        if (match.Success)
                        {
                            var statementType = NormalizeStatementType(match.Groups["StatementType"].Value.Trim());
                            var metricName = match.Groups["MetricName"].Value.Trim();
                            normalizedKey = $"{statementType}_{metricName}";
                        }
                        else
                        {
                            normalizedKey = FallbackNormalizeKey(kvp.Key);
                        }
                        if (!financialDataElements.ContainsKey(normalizedKey))
                        {
                            financialDataElements[normalizedKey] = new List<string>();
                        }
                    }
                }
            }
            return financialDataElements;
        }
        private void PopulateFinancialDataElements(Dictionary<string, List<string>> financialDataElements, Dictionary<string, FinancialData> financialDataRecordsLookup, List<(int Year, int Quarter)> recentReportPairs)
        {
            var htmlKeyRegex = new Regex(@"^(?:HTML_)?(?:AnnualReport|Q\dReport)_(?<StatementType>.*?)_(?<MetricName>.+)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
            foreach (var reportPair in recentReportPairs)
            {
                var key = $"{reportPair.Year}-{reportPair.Quarter}";
                if (!financialDataRecordsLookup.TryGetValue(key, out var record))
                {
                    foreach (var elementKey in financialDataElements.Keys.ToList())
                    {
                        financialDataElements[elementKey].Add("N/A");
                    }
                    continue;
                }
                var jsonData = record.FinancialDataJson ?? "{}";
                var financialData = JsonConvert.DeserializeObject<Dictionary<string, object>>(jsonData);
                if (financialData == null)
                {
                    foreach (var elementKey in financialDataElements.Keys.ToList())
                    {
                        financialDataElements[elementKey].Add("N/A");
                    }                   
                    continue;
                }
                var normalizedValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var kvp in financialData)
                {
                    if (kvp.Key.StartsWith("HTML_", StringComparison.OrdinalIgnoreCase))
                    {
                        var match = htmlKeyRegex.Match(kvp.Key);
                        string normalizedKey;
                        if (match.Success)
                        {
                            var statementType = NormalizeStatementType(match.Groups["StatementType"].Value.Trim());
                            var metricName = match.Groups["MetricName"].Value.Trim();
                            normalizedKey = $"{statementType}_{metricName}";
                        }
                        else
                        {
                            normalizedKey = FallbackNormalizeKey(kvp.Key);
                        }

                        var value = kvp.Value?.ToString() ?? "N/A";
                        normalizedValues[normalizedKey] = value;
                    }
                }
                foreach (var elementKey in financialDataElements.Keys.ToList())
                {
                    if (normalizedValues.TryGetValue(elementKey, out var value))
                    {
                        financialDataElements[elementKey].Add(value);
                    }
                    else
                    {
                        financialDataElements[elementKey].Add("N/A");
                    }
                }
            }
        }
        private Dictionary<string, Dictionary<string, List<string>>> GroupFinancialDataByStatement(Dictionary<string, List<string>> financialDataElements)
        {
            var statementsDict = new Dictionary<string, Dictionary<string, List<string>>>(StringComparer.OrdinalIgnoreCase);

            foreach (var element in financialDataElements)
            {
                var normalizedKey = element.Key;
                var keyParts = normalizedKey.Split('_');
                string statementType;
                string metricName;
                if (keyParts.Length >= 2)
                {
                    statementType = keyParts[0].Trim();
                    metricName = string.Join("_", keyParts.Skip(1)).Trim();
                }
                else
                {
                    statementType = "General";
                    metricName = normalizedKey.Trim();
                }
                if (!statementsDict.ContainsKey(statementType))
                {
                    statementsDict[statementType] = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
                }
                statementsDict[statementType][metricName] = element.Value;
            }
            return statementsDict;
        }
        private List<StatementFinancialData> CreateOrderedStatements(Dictionary<string, Dictionary<string, List<string>>> statementsDict, List<string> recentReports)
        {
            var desiredOrder = new List<string> { "Statements Of Operations", "Income Statement", "Cashflow", "Balance Sheet" };
            var operationsKey = statementsDict.Keys.FirstOrDefault(k => k.Equals("Statements Of Operations", StringComparison.OrdinalIgnoreCase) ||
                k.IndexOf("operations", StringComparison.OrdinalIgnoreCase) >= 0);
            if (operationsKey != null)
            {
                desiredOrder.Remove("Statements Of Operations");
                desiredOrder.Insert(0, operationsKey); // Insert the 'Operations' statement at the beginning
            }
            var orderedStatements = new List<StatementFinancialData>();
            StatementFinancialData ScaleAndMergeStatement(string statementKey, Dictionary<string, List<string>> metrics)
            {
                var exemptColumns = GetExemptColumns(metrics.Keys); // Define exempt columns using a helper method
                var valuesForScaling = metrics
                    .Where(kvp => !IsExemptColumn(kvp.Key, exemptColumns))
                    .SelectMany(kvp => kvp.Value)
                    .Where(v => decimal.TryParse(v, out _))
                    .Select(v => decimal.Parse(v))
                    .ToList();
                int scalingFactor = CalculateScalingFactor(valuesForScaling); // Calculate scaling factor for the current statement
                var scaledMetrics = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase); // Scale the financial metrics
                foreach (var kvp in metrics)
                {
                    if (IsExemptColumn(kvp.Key, exemptColumns))
                    {
                        scaledMetrics[kvp.Key] = kvp.Value;
                    }
                    else
                    {
                        scaledMetrics[kvp.Key] = kvp.Value.Select(v =>
                        {
                            if (decimal.TryParse(v, out var d))
                            {
                                var scaledValue = d / (decimal)Math.Pow(10, scalingFactor);
                                return scaledValue.ToString("F2");
                            }
                            return v;
                        }).ToList();
                    }
                }
                string scalingLabel = GetScalingLabel(scalingFactor); // Get scaling label for the current statement
                var metricsWithBaseName = scaledMetrics.Select(kvpMetric => new
                {
                    OriginalName = kvpMetric.Key,
                    BaseName = ExtractBaseName(kvpMetric.Key),
                    Values = kvpMetric.Value
                }).ToList();
                var groupedMetrics = metricsWithBaseName
                    .GroupBy(m => m.BaseName, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                var displayMetrics = new List<DisplayMetricRow1>(); // Prepare DisplayMetrics with merging information
                foreach (var group in groupedMetrics)
                {
                    var mergedValues = new List<string>();
                    for (int i = 0; i < recentReports.Count; i++)
                    {
                        if (i >= group.First().Values.Count)
                        {
                            mergedValues.Add("N/A");
                            continue;
                        }
                        string mergedValue = "N/A";
                        foreach (var metric in group)
                        {
                            // Check if the current value is valid
                            if (!string.IsNullOrEmpty(metric.Values[i]) && metric.Values[i] != "N/A")
                            {
                                mergedValue = metric.Values[i];
                                break; // Stop once the first valid value is found
                            }
                        }
                        mergedValues.Add(mergedValue); // Add the consolidated value for the period
                    }
                    displayMetrics.Add(new DisplayMetricRow1 // Add a single row with the merged values
                    {
                        DisplayName = group.First().OriginalName, // Use the first metric's name
                        Values = mergedValues,
                        IsMergedRow = false,
                        RowSpan = 1
                    });
                }
                var sfd = new StatementFinancialData // Add the statement with scaled metrics, merging info, and scaling label
                {
                    StatementType = statementKey,
                    DisplayMetrics = displayMetrics,
                    ScalingLabel = scalingLabel
                };
                return sfd;
            }            
            foreach (var desired in desiredOrder)// 13. Create ordered list of statements according to desired order
            {
                var matchedKey = statementsDict.Keys.FirstOrDefault(k =>
                    k.Equals(desired, StringComparison.OrdinalIgnoreCase) ||
                    k.Contains(desired, StringComparison.OrdinalIgnoreCase));
                if (matchedKey != null)
                {
                    var sfd = ScaleAndMergeStatement(matchedKey, statementsDict[matchedKey]);
                    orderedStatements.Add(sfd);
                }
            }            
            foreach (var kvp in statementsDict)// 14. Handle any other statements not in the desired order
            {
                if (!desiredOrder.Any(d =>
                    kvp.Key.Equals(d, StringComparison.OrdinalIgnoreCase) ||
                    kvp.Key.IndexOf(d, StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    var sfd = ScaleAndMergeStatement(kvp.Key, kvp.Value);
                    orderedStatements.Add(sfd);
                }
            }
            return orderedStatements;
        }
        private string ValidateDataType(string dataType)
        {
            if (dataType != "annual" && dataType != "quarterly")
            {
                return "annual";
            }
            return dataType;
        }
        private (int CompanyId, string CompanySymbol) GetCompanyDetails(string companyName)
        {
            var company = _context.CompaniesList
                .Where(c => c.CompanyName == companyName)
                .Select(c => new { c.CompanyID, c.CompanySymbol })
                .FirstOrDefault();
            if (company == null)
            {
                return (0, null);
            }
            return (company.CompanyID, company.CompanySymbol);
        }
        private (List<(int Year, int Quarter)> recentReportPairs, List<string> recentReportKeys, List<FinancialData> financialDataRecords, List<string> recentReports)
            FetchRecentReportsAndData(int companyId, string dataType)
        {
            if (dataType == "annual")
            {
                return FetchAnnualReports(companyId);
            }
            else
            {
                return FetchQuarterlyReports(companyId);
            }
        }
        private (List<(int Year, int Quarter)>, List<string>, List<FinancialData>, List<string>)
            FetchAnnualReports(int companyId)
        {
            var recentYears = _context.FinancialData
                .Where(fd => fd.CompanyID == companyId && fd.IsHtmlParsed && fd.Year.HasValue && fd.Quarter == 0)
                .Select(fd => fd.Year.Value)
                .Distinct()
                .OrderByDescending(y => y)
                .Take(10)
                .ToList();
            recentYears.Reverse(); // Ascending order
            var recentReportPairs = recentYears.Select(y => (Year: y, Quarter: 0)).ToList();
            var recentReports = recentYears.Select(y => $"AnnualReport {y}").ToList();
            var recentReportKeys = recentReportPairs.Select(rp => $"{rp.Year}-{rp.Quarter}").ToList();
            var financialDataRecords = _context.FinancialData
                .Where(fd => fd.CompanyID == companyId && fd.IsHtmlParsed && fd.Year.HasValue)
                .Where(fd => recentReportKeys.Contains(fd.Year.Value.ToString() + "-" + fd.Quarter.ToString()))
                .ToList();
            return (recentReportPairs, recentReportKeys, financialDataRecords, recentReports);
        }

        private (List<(int Year, int Quarter)>, List<string>, List<FinancialData>, List<string>)
            FetchQuarterlyReports(int companyId)
        {
            int desiredQuarterCount = 6;
            var allQuarterData = _context.FinancialData
                .Where(fd => fd.CompanyID == companyId && fd.IsHtmlParsed && fd.Year.HasValue && fd.Quarter >= 1)
                .Select(fd => new { Year = fd.Year.Value, fd.Quarter })
                .Distinct()
                .OrderByDescending(yq => yq.Year)
                .ThenByDescending(yq => yq.Quarter)
                .ToList();
            var recentQuarterData = new List<(int Year, int Quarter)>();
            var uniqueQuarters = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var yq in allQuarterData)
            {
                string reportKey = $"Q{yq.Quarter}Report {yq.Year}";
                if (!uniqueQuarters.Contains(reportKey))
                {
                    recentQuarterData.Add((yq.Year, yq.Quarter));
                    uniqueQuarters.Add(reportKey);
                }
                if (recentQuarterData.Count >= desiredQuarterCount)
                    break;
            }
            recentQuarterData.Reverse(); // Ascending order
            var recentReports = recentQuarterData.Select(rp => $"Q{rp.Quarter}Report {rp.Year}").ToList();
            var recentReportKeys = recentQuarterData.Select(rp => $"{rp.Year}-{rp.Quarter}").ToList();
            var financialDataRecords = _context.FinancialData
                .Where(fd => fd.CompanyID == companyId && fd.IsHtmlParsed && fd.Year.HasValue)
                .Where(fd => recentReportKeys.Contains(fd.Year.Value.ToString() + "-" + fd.Quarter.ToString()))
                .ToList();
            return (recentQuarterData, recentReportKeys, financialDataRecords, recentReports);
        }
        private string FallbackNormalizeKey(string originalKey)
        {
            var keyParts = originalKey.Split('_');
            if (keyParts.Length >= 4)
            {
                var statementType = NormalizeStatementType(keyParts[2].Trim());
                var metricName = string.Join("_", keyParts.Skip(3)).Trim();
                return $"{statementType}_{metricName}";
            }
            else
            {   // Assign to "General"
                return $"General_{originalKey.Trim()}";
            }
        }
        private List<ReportPeriod> CreateReportPeriods(string dataType, List<(int Year, int Quarter)> recentReportPairs)
        {
            if (dataType == "annual")
            {
                return recentReportPairs.Select(rp => new ReportPeriod
                {
                    DisplayName = rp.Year.ToString(),
                    CompositeKey = $"{rp.Year}-{rp.Quarter}"
                }).ToList();
            }
            else
            {
                return recentReportPairs.Select(rp => new ReportPeriod
                {
                    DisplayName = $"Q{rp.Quarter}Report {rp.Year}",
                    CompositeKey = $"{rp.Year}-{rp.Quarter}"
                }).ToList();
            }
        }
        private static bool TryGetValueIgnoreCaseAndWhitespace(Dictionary<string, object> dict, string key, out object value)
        {
            value = null;
            foreach (var dictKey in dict.Keys)
            {
                if (string.Equals(dictKey.Trim(), key.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    value = dict[dictKey];
                    return true;
                }
            }
            return false;
        }
        private bool IsExemptColumn(string columnName, HashSet<string> exemptColumns)
        {
            if (exemptColumns.Contains(columnName))
            {
                return true;
            }
            var exemptKeywords = new List<string> {"per share", "earnings per share", "EPS", "shares outstanding", "diluted"};
            foreach (var keyword in exemptKeywords)
            {
                if (columnName.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }
            return false;
        }
        private HashSet<string> GetExemptColumns(IEnumerable<string> metricKeys)
        {
            var staticExemptColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {"DividendsDeclared", "CommonStockIssued", "DilutedInShares", "BasicInShares", "SharesUsedInComputingEarningsPerShareDiluted", "SharesUsedInComputingEarningsPerShareBasic"};    
            return staticExemptColumns;
        }
        private int CalculateScalingFactor(IEnumerable<decimal> values)
        {
            if (!values.Any())
                return 0;
            decimal max = values.Max();
            if (max >= 1_000_000_000)
                return 9; // Billion
            if (max >= 1_000_000)
                return 6; // Million
            if (max >= 1_000)
                return 3; // Thousand
            return 0;
        }
        private string GetScalingLabel(int scalingFactor)
        {
            return scalingFactor switch
            {
                9 => "in Billions $", // Billion
                6 => "in Millions $", // Million
                3 => "in Thousands $", // Thousand
                _ => string.Empty
            };
        }
        private string ExtractBaseName(string metricName)
        {
            int bracketIndex = metricName.IndexOf('(');
            if (bracketIndex > 0)
            {
                return metricName.Substring(0, bracketIndex).Trim();
            }
            return metricName;
        }
        public async Task<IActionResult> ScrapeData(string companySymbol)
        {
            Console.WriteLine("Scrape action called");

            if (string.IsNullOrEmpty(companySymbol))
            {
                Console.WriteLine("CompanySymbol is null or empty");
                return View("Index"); // Or return an error message
            }

            // Proceed with the scraping process
            var result = await StockScraperV3.URL.ScrapeReportsForCompanyAsync(companySymbol);

            if (result.Contains("Error"))
            {
                ViewBag.Error = result;
                return View("Error");
            }

            ViewBag.Result = result;
            return View("Success");
        }

        public IActionResult AddToWatchlist(string stockName, string stockSymbol)
        {
            var username = HttpContext.Session.GetString("Username");
            if (username == null)
            {
                return Content("You must be logged in to add items to your watchlist.");
            }
            var user = _context.Users.FirstOrDefault(u => u.Username == username);
            if (user == null)
            {
                return Content("User not found.");
            }// Check if the stock already exists in the user's watchlist
            var existingItem = _context.Watchlist.FirstOrDefault(w => w.UserId == user.UserId && w.StockSymbol == stockSymbol);
            if (existingItem != null)
            {
                return Content("This stock is already in your watchlist.");
            }  // Add the stock to the watchlist
            var watchlistItem = new Watchlist
            {
                UserId = user.UserId,
                StockName = stockName,
                StockSymbol = stockSymbol
            };
            _context.Watchlist.Add(watchlistItem);
            _context.SaveChanges();
            return Content($"{stockName} has been added to your watchlist.");
        }
        [HttpPost]
        public IActionResult RemoveFromWatchlist(int watchlistId)
        {
            var username = HttpContext.Session.GetString("Username");
            if (username == null)
            {
                TempData["Error"] = "You must be logged in to modify your watchlist.";
                return RedirectToAction("Login", "Account");
            }
            var user = _context.Users.FirstOrDefault(u => u.Username == username);
            if (user == null)
            {
                TempData["Error"] = "User not found.";
                return RedirectToAction("Login", "Account");
            }
            var watchlistItem = _context.Watchlist.FirstOrDefault(w => w.WatchlistId == watchlistId && w.UserId == user.UserId);
            if (watchlistItem != null)
            {
                _context.Watchlist.Remove(watchlistItem);
                _context.SaveChanges();
                TempData["Message"] = $"{watchlistItem.StockName} has been removed from your watchlist.";
            }
            return RedirectToAction("Watchlist");
        }
        //
        public IActionResult Watchlist()
        {
            var username = HttpContext.Session.GetString("Username");
            if (username == null)
            {
                TempData["Error"] = "You must be logged in to view your watchlist.";
                return RedirectToAction("Login", "Account");
            }
            var user = _context.Users.FirstOrDefault(u => u.Username == username);
            if (user == null)
            {
                TempData["Error"] = "User not found.";
                return RedirectToAction("Login", "Account");
            }
            var watchlist = _context.Watchlist
                .Where(w => w.UserId == user.UserId)
                .ToList();
            return View("~/Views/StockData/Watchlist.cshtml", watchlist);
        }
        private bool CanMergeGroup(IGrouping<string, dynamic> group, int numberOfYears)
        {
            int[] numericalCountsPerYear = new int[numberOfYears];

            foreach (var metric in group)
            {
                for (int i = 0; i < numberOfYears; i++)
                {
                    string value = metric.Values[i];
                    if (!string.IsNullOrEmpty(value) && decimal.TryParse(value, out _))
                    {
                        numericalCountsPerYear[i]++;
                        if (numericalCountsPerYear[i] > 1)
                        {
                            return false; // More than one valid value in a year, cannot merge
                        }
                    }
                }
            }
            return true; // Merge allowed if no conflicts
        }
        //public async Task<IActionResult> StockDataWithPrice(string companyName, string stockSymbol)
        //{
        //    try
        //    {
        //        Console.WriteLine($"Fetching stock price for symbol: {stockSymbol} using Twelve Data API");
        //        // Fetch the latest stock price using Twelve Data Service
        //        var stockPrice = await _twelveDataService.GetStockPriceAsync(stockSymbol);
        //        // Check if the stock price is available
        //        ViewBag.StockPrice = stockPrice.HasValue ? $"${stockPrice.Value:F2}" : "N/A";
        //        return View("StockData"); // Explicitly specify the view if needed
        //    }
        //    catch (Exception ex)
        //    {
        //        return StatusCode(500, "An error occurred while fetching stock price.");
        //    }
        //}
        public async Task<IActionResult> StockDataWithPrice(string companyName, string dataType = "annual")
        {
            try
            {
                Console.WriteLine($"Fetching stock price for company: {companyName} using Twelve Data API");

                // Step 1: Validate dataType
                dataType = ValidateDataType(dataType);

                // Step 2: Fetch Company Details
                var (companyId, companySymbol) = GetCompanyDetails(companyName);
                if (companyId == 0)
                {
                    return BadRequest("Company not found.");
                }

                // Step 3 & 4: Fetch Recent Reports and Financial Data Records
                var (recentReportPairs, recentReportKeys, financialDataRecords, recentReports) = FetchRecentReportsAndData(companyId, dataType);
                if (!financialDataRecords.Any())
                {
                    return BadRequest("No financial records found.");
                }

                // Step 5 & 6: Build the financialDataElements dictionary (keys for metrics)
                var financialDataRecordsLookup = financialDataRecords.ToDictionary(fd => $"{fd.Year.Value}-{fd.Quarter}", fd => fd);
                var financialDataElements = InitializeFinancialDataElements(financialDataRecords);
                PopulateFinancialDataElements(financialDataElements, financialDataRecordsLookup, recentReportPairs);
                var statementsDict = GroupFinancialDataByStatement(financialDataElements);
                var orderedStatements = CreateOrderedStatements(statementsDict, recentReports);

                // Step 7: Fetch the latest stock price using Twelve Data Service
                var stockPrice = await _twelveDataService.GetStockPriceAsync(companySymbol);
                string formattedStockPrice = stockPrice.HasValue ? $"${stockPrice.Value:F2}" : "N/A";

                // Optional: Additional Logging for Verification
                Console.WriteLine($"Formatted Stock Price: {formattedStockPrice}");
                Console.WriteLine($"Model: CompanyName={companyName}, CompanySymbol={companySymbol}, StockPrice={formattedStockPrice}");

                // Step 8: Prepare the ViewModel
                var model = new StockDataViewModel
                {
                    CompanyName = companyName,
                    CompanySymbol = companySymbol,
                    FinancialYears = CreateReportPeriods(dataType, recentReportPairs),
                    Statements = orderedStatements,
                    StockPrice = formattedStockPrice, // Set the stock price here
                    DataType = dataType
                };

                return View("StockData", model); // Pass the model to the view
            }
            catch (Exception ex)
            {
                // Log the exception details if necessary
                Console.WriteLine($"Error in StockDataWithPrice: {ex.Message}");
                return StatusCode(500, "An error occurred while processing the data.");
            }
        }


        private int CountTrailingZeros(decimal value)
        {
            int trailingZeros = 0;
            while (value % 10 == 0 && value != 0)
            {
                trailingZeros++;
                value /= 10;
            }
            return trailingZeros;
        }
    }
}


//using Microsoft.EntityFrameworkCore;
//using Microsoft.AspNetCore.Mvc;
//using StockDataWebsite.Data;
//using StockDataWebsite.Models;
//using System.Linq;
//using System.Threading.Tasks;
//using System.Collections.Generic;
//using System;
//using YahooFinanceApi;
//using Newtonsoft.Json;
//namespace StockDataWebsite.Controllers
//{
//    public class StockDataController : Controller
//    {
//        private readonly ApplicationDbContext _context;
//        private readonly TwelveDataService _twelveDataService;

//        // Single constructor accepting both dependencies
//        public StockDataController(ApplicationDbContext context, TwelveDataService twelveDataService)
//        {
//            _context = context;
//            _twelveDataService = twelveDataService;
//        }
//        public async Task<IActionResult> StockData(string companyName, string dataType = "annual")
//        {
//            try
//            {
//                Console.WriteLine($"Entering StockData for company: {companyName} with dataType: {dataType}");

//                // Validate dataType
//                if (dataType != "annual" && dataType != "quarterly")
//                {
//                    dataType = "annual"; // Default to annual if invalid
//                }

//                // Fetch company details (ID and Symbol)
//                var company = _context.CompaniesList
//                    .Where(c => c.CompanyName == companyName)
//                    .Select(c => new { c.CompanyID, c.CompanySymbol })
//                    .FirstOrDefault();

//                if (company == null)
//                {
//                    Console.WriteLine("No valid CompanyID found, aborting operation.");
//                    return BadRequest("Company not found.");
//                }

//                int companyId = company.CompanyID;
//                string companySymbol = company.CompanySymbol;

//                Console.WriteLine($"Company ID found: {companyId}, Company Symbol: {companySymbol}");

//                List<int> recentYears = new List<int>();
//                List<string> recentQuarters = new List<string>();
//                List<FinancialData> financialDataRecords = new List<FinancialData>();

//                if (dataType == "annual")
//                {
//                    // Fetch the most recent 10 financial years where Quarter == 0
//                    recentYears = _context.FinancialData
//                        .Where(fd => fd.CompanyID == companyId && fd.IsHtmlParsed && fd.Year.HasValue && fd.Quarter == 0)
//                        .Select(fd => fd.Year.Value)
//                        .Distinct()
//                        .OrderByDescending(y => y)
//                        .Take(10)
//                        .ToList();

//                    if (!recentYears.Any())
//                    {
//                        Console.WriteLine("No valid financial years found.");
//                        return BadRequest("No valid financial data found.");
//                    }

//                    // Reverse the years to have them in ascending order (oldest to newest)
//                    recentYears.Reverse();

//                    Console.WriteLine($"Recent financial years: {string.Join(", ", recentYears)}");

//                    // Fetch financial data records for the recent years
//                    financialDataRecords = _context.FinancialData
//                        .Where(fd => fd.CompanyID == companyId && recentYears.Contains(fd.Year.Value) && fd.Quarter == 0)
//                        .ToList();

//                    if (!financialDataRecords.Any())
//                    {
//                        Console.WriteLine("No financial data records found.");
//                        return BadRequest("No financial records found.");
//                    }

//                    Console.WriteLine($"Total financial data records found: {financialDataRecords.Count}");
//                }
//                else if (dataType == "quarterly")
//                {
//                    // Fetch distinct Year-Quarter combinations
//                    var recentQuarterData = _context.FinancialData
//                        .Where(fd => fd.CompanyID == companyId && fd.IsHtmlParsed && fd.Year.HasValue && fd.Quarter >= 1)
//                        .Select(fd => new { Year = fd.Year.Value, fd.Quarter })
//                        .Distinct()
//                        .OrderByDescending(yq => yq.Year)
//                        .ThenByDescending(yq => yq.Quarter)
//                        .Take(8)
//                        .ToList();

//                    if (!recentQuarterData.Any())
//                    {
//                        Console.WriteLine("No valid financial quarters found.");
//                        return BadRequest("No valid financial data found.");
//                    }

//                    // Reverse to have them in ascending order (oldest to newest)
//                    recentQuarterData.Reverse();

//                    // Create recentQuarters list in the format "Q{Quarter} {Year}"
//                    recentQuarters = recentQuarterData
//                        .Select(yq => $"Q{yq.Quarter} {yq.Year}")
//                        .ToList();

//                    Console.WriteLine($"Recent financial quarters: {string.Join(", ", recentQuarters)}");

//                    // Create composite keys
//                    var compositeKeys = recentQuarterData
//                        .Select(yq => yq.Year * 100 + yq.Quarter)
//                        .ToList();

//                    // Fetch financial data records using composite keys
//                    financialDataRecords = _context.FinancialData
//                        .Where(fd => fd.CompanyID == companyId &&
//                                     compositeKeys.Contains(fd.Year.Value * 100 + fd.Quarter))
//                        .ToList();

//                    if (!financialDataRecords.Any())
//                    {
//                        Console.WriteLine("No financial data records found for quarters.");
//                        return BadRequest("No financial records found.");
//                    }

//                    Console.WriteLine($"Total financial data records found: {financialDataRecords.Count}");
//                }

//                // Create a lookup of financial data records by year/quarter for quick access
//                var financialDataRecordsLookup = new Dictionary<string, FinancialData>(StringComparer.OrdinalIgnoreCase);

//                foreach (var record in financialDataRecords)
//                {
//                    string key;
//                    if (dataType == "annual")
//                    {
//                        key = record.Year.Value.ToString();
//                    }
//                    else // quarterly
//                    {
//                        key = $"Q{record.Quarter} {record.Year}";
//                    }

//                    financialDataRecordsLookup[key] = record;
//                }

//                // Dictionary to hold the financial data elements
//                var financialDataElements = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

//                // Collect all unique element names
//                var allElementNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

//                // Parse FinancialDataJson and collect elements containing "HTML_"
//                foreach (var record in financialDataRecords)
//                {
//                    var jsonData = record.FinancialDataJson ?? "{}";
//                    var financialData = JsonConvert.DeserializeObject<Dictionary<string, object>>(jsonData);

//                    var htmlElements = financialData.Where(kvp => kvp.Key.Contains("HTML_", StringComparison.OrdinalIgnoreCase));

//                    foreach (var kvp in htmlElements)
//                    {
//                        allElementNames.Add(kvp.Key);
//                    }
//                }

//                // Collect values for each financial element across the recent years/quarters
//                foreach (var elementName in allElementNames)
//                {
//                    var valuesForPeriods = new List<string>();

//                    foreach (var period in (dataType == "annual" ? (List<string>)recentYears.Select(y => y.ToString()).ToList() : recentQuarters))
//                    {
//                        if (financialDataRecordsLookup.TryGetValue(period, out var record))
//                        {
//                            var jsonData = record.FinancialDataJson ?? "{}";
//                            var financialData = JsonConvert.DeserializeObject<Dictionary<string, object>>(jsonData);

//                            if (financialData.TryGetValue(elementName, out var value))
//                            {
//                                valuesForPeriods.Add(value?.ToString() ?? "N/A");
//                            }
//                            else
//                            {
//                                valuesForPeriods.Add("N/A");
//                            }
//                        }
//                        else
//                        {
//                            valuesForPeriods.Add("N/A");
//                        }
//                    }

//                    financialDataElements[elementName] = valuesForPeriods;
//                }

//                // Fetch the latest stock price
//                var stockPrice = await _twelveDataService.GetStockPriceAsync(companySymbol);
//                ViewBag.StockPrice = stockPrice.HasValue ? $"${stockPrice.Value:F2}" : "N/A";

//                // Group financial data by statement type
//                var statementsDict = new Dictionary<string, Dictionary<string, List<string>>>(StringComparer.OrdinalIgnoreCase);

//                foreach (var element in financialDataElements)
//                {
//                    var keyParts = element.Key.Split('_');
//                    if (keyParts.Length >= 4)
//                    {
//                        var statementType = keyParts[2].Trim();
//                        // Rename "Cover Page" to "General"
//                        if (statementType.Equals("Cover Page", StringComparison.OrdinalIgnoreCase))
//                        {
//                            statementType = "General";
//                        }
//                        var metricName = keyParts.Last();

//                        if (!statementsDict.ContainsKey(statementType))
//                        {
//                            statementsDict[statementType] = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
//                        }

//                        statementsDict[statementType][metricName] = element.Value;
//                    }
//                }

//                // Define the base desired order
//                var desiredOrder = new List<string> { "Income Statement", "Cashflow", "Balance Sheet" };

//                // Check if any statement contains 'operations' (case-insensitive)
//                var operationsKey = statementsDict.Keys.FirstOrDefault(k =>
//                    k.IndexOf("operations", StringComparison.OrdinalIgnoreCase) >= 0);

//                if (operationsKey != null)
//                {
//                    Console.WriteLine($"'Operations' statement found: {operationsKey}");

//                    // Remove 'Income Statement' from its current position in desiredOrder
//                    desiredOrder.Remove("Income Statement");

//                    // Insert the 'Operations' statement at the beginning of desiredOrder
//                    desiredOrder.Insert(0, operationsKey);

//                    // Append 'Income Statement' to the end of desiredOrder
//                    desiredOrder.Add("Income Statement");
//                }

//                // Create ordered list of statements based on the updated desiredOrder
//                var orderedStatements = new List<StatementFinancialData>();

//                foreach (var desired in desiredOrder)
//                {
//                    var matchedKey = statementsDict.Keys.FirstOrDefault(k =>
//                        k.Equals(desired, StringComparison.OrdinalIgnoreCase) ||
//                        k.Contains(desired, StringComparison.OrdinalIgnoreCase));

//                    if (matchedKey != null)
//                    {
//                        // **Per-Statement Scaling Starts Here**

//                        // Extract financial metrics for the current statement
//                        var metrics = statementsDict[matchedKey];

//                        // Define exempt columns using a helper method
//                        var exemptColumns = GetExemptColumns(metrics.Keys);

//                        // Collect values for scaling (excluding exempt columns)
//                        var valuesForScaling = metrics
//                            .Where(kvp => !IsExemptColumn(kvp.Key, exemptColumns))
//                            .SelectMany(kvp => kvp.Value)
//                            .Where(v => decimal.TryParse(v, out _))
//                            .Select(v => decimal.Parse(v))
//                            .ToList();

//                        // Calculate scaling factor for the current statement
//                        int scalingFactor = CalculateScalingFactor(valuesForScaling);

//                        // Scale the financial metrics
//                        var scaledMetrics = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

//                        foreach (var kvp in metrics)
//                        {
//                            if (IsExemptColumn(kvp.Key, exemptColumns))
//                            {
//                                scaledMetrics[kvp.Key] = kvp.Value;
//                            }
//                            else
//                            {
//                                scaledMetrics[kvp.Key] = kvp.Value.Select(v =>
//                                {
//                                    if (decimal.TryParse(v, out var d))
//                                    {
//                                        var scaledValue = d / (decimal)Math.Pow(10, scalingFactor);
//                                        return scaledValue.ToString("F2");
//                                    }
//                                    return v;
//                                }).ToList();
//                            }
//                        }

//                        // Get scaling label for the current statement
//                        string scalingLabel = GetScalingLabel(scalingFactor);

//                        // **Merging Rows Logic Starts Here**

//                        // Extract the "text outside the brackets" for each metric
//                        var metricsWithBaseName = scaledMetrics.Select(kvp => new
//                        {
//                            OriginalName = kvp.Key,
//                            BaseName = ExtractBaseName(kvp.Key),
//                            Values = kvp.Value
//                        }).ToList();

//                        // Group metrics by BaseName
//                        var groupedMetrics = metricsWithBaseName
//                            .GroupBy(m => m.BaseName, StringComparer.OrdinalIgnoreCase)
//                            .ToList();

//                        // Prepare DisplayMetrics with merging information
//                        var displayMetrics = new List<DisplayMetricRow1>();

//                        foreach (var group in groupedMetrics)
//                        {
//                            // Initialize a list to store the merged values for each period
//                            var mergedValues = new List<string>();

//                            for (int i = 0; i < (dataType == "annual" ? recentYears.Count : recentQuarters.Count); i++)
//                            {
//                                string mergedValue = "N/A";

//                                // Iterate over the group's metrics
//                                foreach (var metric in group)
//                                {
//                                    // Check if the current value is valid
//                                    if (!string.IsNullOrEmpty(metric.Values[i]) && metric.Values[i] != "N/A")
//                                    {
//                                        mergedValue = metric.Values[i];
//                                        break; // Stop once the first valid value is found
//                                    }
//                                }

//                                mergedValues.Add(mergedValue); // Add the consolidated value for the period
//                            }

//                            // Add a single row with the merged values
//                            displayMetrics.Add(new DisplayMetricRow1
//                            {
//                                DisplayName = group.First().OriginalName, // Use the first metric's name
//                                Values = mergedValues,                    // Use the merged values
//                                IsMergedRow = false,                      // It's the main row
//                                RowSpan = 1                               // Only one row
//                            });
//                        }

//                        // Add the statement with scaled metrics, merging info, and scaling label
//                        orderedStatements.Add(new StatementFinancialData
//                        {
//                            StatementType = matchedKey, // Use the actual key from statementsDict
//                            DisplayMetrics = displayMetrics,
//                            ScalingLabel = scalingLabel // Assign scaling label
//                        });
//                    }
//                }

//                // Add any other statements not in the desired order with their own scaling and merging
//                foreach (var kvp in statementsDict)
//                {
//                    if (!desiredOrder.Any(d =>
//                        kvp.Key.Equals(d, StringComparison.OrdinalIgnoreCase) ||
//                        kvp.Key.Contains(d, StringComparison.OrdinalIgnoreCase)))
//                    {
//                        // **Per-Statement Scaling for Additional Statements**

//                        var metrics = kvp.Value;

//                        // Define exempt columns using the helper method
//                        var exemptColumns = GetExemptColumns(metrics.Keys);

//                        // Collect values for scaling (excluding exempt columns)
//                        var valuesForScaling = metrics
//                            .Where(element => !IsExemptColumn(element.Key, exemptColumns))
//                            .SelectMany(element => element.Value)
//                            .Where(v => decimal.TryParse(v, out _))
//                            .Select(v => decimal.Parse(v))
//                            .ToList();

//                        // Calculate scaling factor for the current statement
//                        int scalingFactor = CalculateScalingFactor(valuesForScaling);

//                        // Scale the financial metrics
//                        var scaledMetrics = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

//                        foreach (var metric in metrics)
//                        {
//                            if (IsExemptColumn(metric.Key, exemptColumns))
//                            {
//                                scaledMetrics[metric.Key] = metric.Value;
//                            }
//                            else
//                            {
//                                scaledMetrics[metric.Key] = metric.Value.Select(v =>
//                                {
//                                    if (decimal.TryParse(v, out var d))
//                                    {
//                                        var scaledValue = d / (decimal)Math.Pow(10, scalingFactor);
//                                        return scaledValue.ToString("F2");
//                                    }
//                                    return v;
//                                }).ToList();
//                            }
//                        }

//                        // Get scaling label for the current statement
//                        string scalingLabel = GetScalingLabel(scalingFactor);

//                        // **Merging Rows Logic Starts Here**

//                        // Extract the "text outside the brackets" for each metric
//                        var metricsWithBaseName = scaledMetrics.Select(kvpMetric => new
//                        {
//                            OriginalName = kvpMetric.Key,
//                            BaseName = ExtractBaseName(kvpMetric.Key),
//                            Values = kvpMetric.Value
//                        }).ToList();

//                        // Group metrics by BaseName
//                        var groupedMetrics = metricsWithBaseName
//                            .GroupBy(m => m.BaseName, StringComparer.OrdinalIgnoreCase)
//                            .ToList();

//                        // Prepare DisplayMetrics with merging information
//                        var displayMetrics = new List<DisplayMetricRow1>();

//                        foreach (var group in groupedMetrics)
//                        {
//                            // Initialize a list to store the merged values for each period
//                            var mergedValues = new List<string>();

//                            for (int i = 0; i < (dataType == "annual" ? recentYears.Count : recentQuarters.Count); i++)
//                            {
//                                string mergedValue = "N/A";

//                                // Iterate over the group's metrics
//                                foreach (var metric in group)
//                                {
//                                    // Check if the current value is valid
//                                    if (!string.IsNullOrEmpty(metric.Values[i]) && metric.Values[i] != "N/A")
//                                    {
//                                        mergedValue = metric.Values[i];
//                                        break; // Stop once the first valid value is found
//                                    }
//                                }

//                                mergedValues.Add(mergedValue); // Add the consolidated value for the period
//                            }

//                            // Add the merged metric row with a single set of financial data
//                            displayMetrics.Add(new DisplayMetricRow1
//                            {
//                                DisplayName = group.First().OriginalName, // Use the first metric name as the display name
//                                Values = mergedValues,                    // Assign the merged values
//                                IsMergedRow = false,                      // Not a child row
//                                RowSpan = 1                               // Number of merged rows
//                            });
//                        }

//                        // Add the statement with scaled metrics, merging info, and scaling label
//                        orderedStatements.Add(new StatementFinancialData
//                        {
//                            StatementType = kvp.Key,
//                            DisplayMetrics = displayMetrics,
//                            ScalingLabel = scalingLabel
//                        });
//                    }
//                }

//                // Assign to model
//                var model = new StockDataViewModel
//                {
//                    CompanyName = companyName,
//                    CompanySymbol = companySymbol,
//                    FinancialYears = dataType == "annual" ?
//                                     recentYears.Select(y => y.ToString()).ToList() :
//                                     recentQuarters, // FinancialYears now contains either years or quarters
//                    Statements = orderedStatements // Assign grouped and ordered statements with scaling and merging
//                };

//                return View(model);
//            }
//            catch (Exception ex)
//            {
//                Console.WriteLine($"Exception occurred: {ex.Message}");
//                return StatusCode(500, "An error occurred while processing the data.");
//            }
//        }


//        /// <summary>
//        /// Determines if a column is exempt from scaling based on predefined exemptions and dynamic keywords.
//        /// </summary>
//        /// <param name="columnName">The name of the column.</param>
//        /// <param name="exemptColumns">A set of predefined exempt column names.</param>
//        /// <returns>True if the column is exempt; otherwise, false.</returns>
//        private bool IsExemptColumn(string columnName, HashSet<string> exemptColumns)
//        {
//            if (exemptColumns.Contains(columnName))
//            {
//                return true;
//            }

//            // Add dynamic checks for keywords like "per share"
//            var exemptKeywords = new List<string>
//    {
//        "per share",
//        "earnings per share",
//        "EPS",
//        "shares outstanding",
//        "diluted"
//        // Add other relevant keywords as needed
//    };

//            foreach (var keyword in exemptKeywords)
//            {
//                if (columnName.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0)
//                {
//                    return true;
//                }
//            }

//            return false;
//        }

//        /// <summary>
//        /// Generates the set of exempt columns by including static exemptions.
//        /// </summary>
//        /// <param name="metricKeys">The collection of metric keys.</param>
//        /// <returns>A set of exempt column names.</returns>
//        private HashSet<string> GetExemptColumns(IEnumerable<string> metricKeys)
//        {
//            var staticExemptColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
//    {
//        "DividendsDeclared", "CommonStockIssued", "DilutedInShares", "BasicInShares",
//        "SharesUsedInComputingEarningsPerShareDiluted", "SharesUsedInComputingEarningsPerShareBasic"
//    };

//            // Optionally, you can add more static exemptions here if needed

//            return staticExemptColumns;
//        }

//        /// <summary>
//        /// Calculates the scaling factor based on the range of values.
//        /// </summary>
//        /// <param name="values">The collection of decimal values.</param>
//        /// <returns>The scaling factor as an integer.</returns>
//        private int CalculateScalingFactor(IEnumerable<decimal> values)
//        {
//            if (!values.Any())
//                return 0;

//            decimal max = values.Max();
//            if (max >= 1_000_000_000)
//                return 9; // Billion
//            if (max >= 1_000_000)
//                return 6; // Million
//            if (max >= 1_000)
//                return 3; // Thousand

//            return 0;
//        }

//        /// <summary>
//        /// Returns a scaling label based on the scaling factor.
//        /// </summary>
//        /// <param name="scalingFactor">The scaling factor.</param>
//        /// <returns>A string representing the scaling label.</returns>
//        private string GetScalingLabel(int scalingFactor)
//        {
//            return scalingFactor switch
//            {
//                9 => "in Billions $", // Billion
//                6 => "in Millions $", // Million
//                3 => "in Thousands $", // Thousand
//                _ => string.Empty
//            };
//        }

//        /// <summary>
//        /// Extracts the base name of a metric by removing any text within brackets.
//        /// </summary>
//        /// <param name="metricName">The original metric name.</param>
//        /// <returns>The base name of the metric.</returns>
//        private string ExtractBaseName(string metricName)
//        {
//            int bracketIndex = metricName.IndexOf('(');
//            if (bracketIndex > 0)
//            {
//                return metricName.Substring(0, bracketIndex).Trim();
//            }
//            return metricName;
//        }

//        public async Task<IActionResult> ScrapeData(string companySymbol)
//        {
//            var result = await Nasdaq100FinancialScraper.Program.ScrapeReportsForCompany(companySymbol);
//            if (result.Contains("Error"))
//            {
//                ViewBag.Error = result;
//                return View("Error");
//            }
//            ViewBag.Result = result;
//            return View("Success");
//        }
//        [HttpPost]
//        [HttpPost]
//        [HttpPost]
//        public IActionResult AddToWatchlist(string stockName, string stockSymbol)
//        {
//            var username = HttpContext.Session.GetString("Username");
//            if (username == null)
//            {
//                return Content("You must be logged in to add items to your watchlist.");
//            }

//            var user = _context.Users.FirstOrDefault(u => u.Username == username);
//            if (user == null)
//            {
//                return Content("User not found.");
//            }

//            // Check if the stock already exists in the user's watchlist
//            var existingItem = _context.Watchlist
//                .FirstOrDefault(w => w.UserId == user.UserId && w.StockSymbol == stockSymbol);

//            if (existingItem != null)
//            {
//                return Content("This stock is already in your watchlist.");
//            }

//            // Add the stock to the watchlist
//            var watchlistItem = new Watchlist
//            {
//                UserId = user.UserId,
//                StockName = stockName,
//                StockSymbol = stockSymbol
//            };
//            _context.Watchlist.Add(watchlistItem);
//            _context.SaveChanges();

//            return Content($"{stockName} has been added to your watchlist.");
//        }

//        [HttpPost]
//        public IActionResult RemoveFromWatchlist(int watchlistId)
//        {
//            var username = HttpContext.Session.GetString("Username");
//            if (username == null)
//            {
//                TempData["Error"] = "You must be logged in to modify your watchlist.";
//                return RedirectToAction("Login", "Account");
//            }

//            var user = _context.Users.FirstOrDefault(u => u.Username == username);
//            if (user == null)
//            {
//                TempData["Error"] = "User not found.";
//                return RedirectToAction("Login", "Account");
//            }

//            var watchlistItem = _context.Watchlist.FirstOrDefault(w => w.WatchlistId == watchlistId && w.UserId == user.UserId);
//            if (watchlistItem != null)
//            {
//                _context.Watchlist.Remove(watchlistItem);
//                _context.SaveChanges();
//                TempData["Message"] = $"{watchlistItem.StockName} has been removed from your watchlist.";
//            }

//            return RedirectToAction("Watchlist");
//        }

//        public IActionResult Watchlist()
//        {
//            var username = HttpContext.Session.GetString("Username");
//            if (username == null)
//            {
//                TempData["Error"] = "You must be logged in to view your watchlist.";
//                return RedirectToAction("Login", "Account");
//            }

//            var user = _context.Users.FirstOrDefault(u => u.Username == username);
//            if (user == null)
//            {
//                TempData["Error"] = "User not found.";
//                return RedirectToAction("Login", "Account");
//            }

//            var watchlist = _context.Watchlist
//                .Where(w => w.UserId == user.UserId)
//                .ToList();

//            // Explicitly specify the view path
//            return View("~/Views/StockData/Watchlist.cshtml", watchlist);
//        }
//        private bool CanMergeGroup(IGrouping<string, dynamic> group, int numberOfYears)
//        {
//            int[] numericalCountsPerYear = new int[numberOfYears];

//            foreach (var metric in group)
//            {
//                for (int i = 0; i < numberOfYears; i++)
//                {
//                    string value = metric.Values[i];
//                    if (!string.IsNullOrEmpty(value) && decimal.TryParse(value, out _))
//                    {
//                        numericalCountsPerYear[i]++;
//                        if (numericalCountsPerYear[i] > 1)
//                        {
//                            return false; // More than one valid value in a year, cannot merge
//                        }
//                    }
//                }
//            }

//            return true; // Merge allowed if no conflicts
//        }
//        public async Task<IActionResult> StockDataWithPrice(string companyName, string stockSymbol)
//        {
//            try
//            {
//                Console.WriteLine($"Fetching stock price for symbol: {stockSymbol} using Twelve Data API");

//                // Fetch the latest stock price using Twelve Data Service
//                var stockPrice = await _twelveDataService.GetStockPriceAsync(stockSymbol);

//                // Check if the stock price is available
//                ViewBag.StockPrice = stockPrice.HasValue ? $"${stockPrice.Value:F2}" : "N/A";

//                // Log the stock price or indicate it wasn't found
//                if (stockPrice.HasValue)
//                {
//                    Console.WriteLine($"Fetched stock price for {stockSymbol}: {stockPrice.Value:F2}");
//                }
//                else
//                {
//                    Console.WriteLine($"Stock price for {stockSymbol} not found.");
//                }

//                return View("StockData"); // Explicitly specify the view if needed
//            }
//            catch (Exception ex)
//            {
//                Console.WriteLine($"Error occurred while fetching stock price: {ex.Message}");
//                return StatusCode(500, "An error occurred while fetching stock price.");
//            }
//        }
//        private int CountTrailingZeros(decimal value)
//        {
//            int trailingZeros = 0;
//            while (value % 10 == 0 && value != 0)
//            {
//                trailingZeros++;
//                value /= 10;
//            }
//            return trailingZeros;
//        }
//    }
//}
