using Microsoft.AspNetCore.Mvc;
using StockDataWebsite.Data;
using StockDataWebsite.Models;
using Newtonsoft.Json;
using System.Text.RegularExpressions;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
namespace StockDataWebsite.Controllers
{    
    public class StockDataController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly TwelveDataService _twelveDataService;
        private readonly ILogger<StockDataController> _logger; // Declare ILogger

        // Corrected Constructor with ILogger injection
        public StockDataController(ApplicationDbContext context, TwelveDataService twelveDataService, ILogger<StockDataController> logger)
        {
            _context = context;
            _twelveDataService = twelveDataService;
            _logger = logger;
        }
        public async Task<IActionResult> StockData(string companyName, string dataType = "annual", string baseType = null)
        {
            try
            {
                // Validate and sanitize dataType
                // The allowed values are now "annual", "quarterly", and "enhanced"
                if (dataType != "annual" && dataType != "quarterly" && dataType != "enhanced")
                {
                    dataType = "annual";
                }

                // Determine baseType if not provided
                if (baseType == null)
                {
                    if (dataType == "enhanced")
                    {
                        // Default to "annual" if no baseType was provided
                        // Or you can choose to default to "quarterly"
                        baseType = "annual";
                    }
                    else
                    {
                        // If not enhanced, baseType = dataType
                        baseType = dataType;
                    }
                }

                var (companyId, companySymbol) = GetCompanyDetails(companyName);
                if (companyId == 0)
                {
                    _logger.LogWarning($"StockData: Company not found for Name = {companyName}");
                    return BadRequest("Company not found.");
                }

                // If we're showing enhanced data, we still need to fetch reports as per the baseType (annual or quarterly)
                var dataTypeForFetching = (dataType == "enhanced") ? baseType : dataType;

                // Fetch recent reports and financial data based on dataTypeForFetching
                var (recentReportPairs, recentReportKeys, financialDataRecords, recentReports) = FetchRecentReportsAndData(companyId, dataTypeForFetching);
                if (!financialDataRecords.Any())
                {
                    _logger.LogWarning($"StockData: No financial records found for CompanyID = {companyId}");
                    return BadRequest("No financial records found.");
                }

                List<StatementFinancialData> orderedStatements;
                List<ReportPeriod> reportPeriods = CreateReportPeriods(dataTypeForFetching, recentReportPairs);

                if (dataType == "enhanced")
                {
                    // Enhanced data logic
                    var financialDataRecordsLookup = financialDataRecords.ToDictionary(fd => $"{fd.Year.Value}-{fd.Quarter}", fd => fd);
                    var xbrlElements = ExtractXbrlElements(financialDataRecordsLookup, recentReportPairs);

                    var displayMetrics = xbrlElements.Select(kvp => new DisplayMetricRow1
                    {
                        DisplayName = kvp.Key,
                        Values = kvp.Value.ToList(),
                        IsMergedRow = false,
                        RowSpan = 1
                    }).ToList();

                    // Perform raw SQL query to map RawElementName to ElementLabel as previously shown
                    var rawElementNames = displayMetrics.Select(dm => dm.DisplayName).Distinct().ToList();
                    var parameterizedNames = string.Join(",", rawElementNames.Select((_, index) => $"@p{index}"));
                    var sqlQuery = $"SELECT RawElementName, ElementLabel FROM XBRLDataTypes WHERE RawElementName IN ({parameterizedNames})";

                    var labelMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    using (var connection = _context.Database.GetDbConnection())
                    {
                        connection.Open();
                        using (var command = connection.CreateCommand())
                        {
                            command.CommandText = sqlQuery;
                            for (int i = 0; i < rawElementNames.Count; i++)
                            {
                                var parameter = command.CreateParameter();
                                parameter.ParameterName = $"@p{i}";
                                parameter.Value = rawElementNames[i];
                                command.Parameters.Add(parameter);
                            }

                            using (var reader = command.ExecuteReader())
                            {
                                while (reader.Read())
                                {
                                    var rawName = reader.GetString(0);
                                    var label = reader.IsDBNull(1) ? rawName : reader.GetString(1);
                                    labelMap[rawName] = label;
                                }
                            }
                        }
                    }

                    foreach (var metric in displayMetrics)
                    {
                        if (labelMap.TryGetValue(metric.DisplayName, out var label))
                        {
                            metric.DisplayName = label;
                        }
                    }

                    orderedStatements = new List<StatementFinancialData>
            {
                new StatementFinancialData
                {
                    StatementType = "Enhanced Data",
                    DisplayMetrics = displayMetrics,
                    ScalingLabel = ""
                }
            };
                }
                else
                {
                    // Basic data logic
                    var financialDataRecordsLookup = financialDataRecords.ToDictionary(fd => $"{fd.Year.Value}-{fd.Quarter}", fd => fd);

                    var financialDataElements = InitializeFinancialDataElements(financialDataRecords);
                    PopulateFinancialDataElements(financialDataElements, financialDataRecordsLookup, recentReportPairs);

                    var statementsDict = GroupFinancialDataByStatement(financialDataElements);
                    orderedStatements = CreateOrderedStatements(statementsDict, recentReports);
                }

                var stockPrice = await _twelveDataService.GetStockPriceAsync(companySymbol);
                var formattedStockPrice = stockPrice.HasValue ? $"${stockPrice.Value:F2}" : "N/A";

                var model = new StockDataViewModel
                {
                    CompanyName = companyName,
                    CompanySymbol = companySymbol,
                    FinancialYears = reportPeriods,
                    Statements = orderedStatements,
                    StockPrice = formattedStockPrice,
                    DataType = dataType,
                    BaseType = baseType // Store the baseType in the model
                };

                _logger.LogInformation($"StockData: Successfully retrieved data for CompanyID = {companyId}, Symbol = {companySymbol}");
                return View(model);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"StockData: An exception occurred while processing data for CompanyName = {companyName}, DataType = {dataType}");
                return StatusCode(500, "An error occurred while processing the data.");
            }
        }
        private (List<(int Year, int Quarter)> recentReportPairs, List<string> recentReportKeys,
         List<FinancialData> financialDataRecords, List<string> recentReports)
    FetchAnnualReports(int companyId)
        {
            // Previously used .Take(10) to limit data. Removed for full listing.
            var recentYears = _context.FinancialData
                .Where(fd => fd.CompanyID == companyId
                             && fd.IsHtmlParsed
                             && fd.Year.HasValue
                             && fd.Quarter == 0)
                .Select(fd => fd.Year.Value)
                .Distinct()
                .OrderByDescending(y => y)
                .ToList();  // No more .Take(10)

            // Reverse to get ascending order of years
            recentYears.Reverse();

            // Create the (Year, Quarter=0) pairs for annual data
            var recentReportPairs = recentYears
                .Select(y => (Year: y, Quarter: 0))
                .ToList();

            // Build a human-readable label (e.g., "AnnualReport 2021")
            var recentReports = recentYears
                .Select(y => $"AnnualReport {y}")
                .ToList();

            // Convert (Year, Quarter) to a "YYYY-0" string key
            var recentReportKeys = recentReportPairs
                .Select(rp => $"{rp.Year}-{rp.Quarter}")
                .ToList();

            // Fetch all FinancialData rows matching these year-quarter keys
            var financialDataRecords = _context.FinancialData
                .Where(fd => fd.CompanyID == companyId
                             && fd.IsHtmlParsed
                             && fd.Year.HasValue)
                .Where(fd => recentReportKeys.Contains(fd.Year.Value.ToString() + "-" + fd.Quarter.ToString()))
                .ToList();

            // Handle duplicates as before
            financialDataRecords = HandleDuplicates(financialDataRecords);

            return (recentReportPairs, recentReportKeys, financialDataRecords, recentReports);
        }

        private (List<(int Year, int Quarter)> recentReportPairs, List<string> recentReportKeys,
                 List<FinancialData> financialDataRecords, List<string> recentReports)
            FetchQuarterlyReports(int companyId)
        {
            // Removed the old "desiredQuarterCount = 6" logic. We now retrieve *all* quarters.
            var allQuarterData = _context.FinancialData
                .Where(fd => fd.CompanyID == companyId
                             && fd.IsHtmlParsed
                             && fd.Year.HasValue
                             && fd.Quarter >= 1)
                .Select(fd => new { fd.CompanyID, fd.Year, fd.Quarter, fd.EndDate })
                .Distinct()
                .OrderByDescending(yq => yq.Year)
                .ThenByDescending(yq => yq.Quarter)
                .ToList();

            // Instead of limiting to 6, we gather *all* of them
            var recentQuarterData = new List<(int Year, int Quarter)>();
            var uniqueQuarters = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var yq in allQuarterData)
            {
                // Build a unique "Q{quarter}Report {year}" key
                string reportKey = $"Q{yq.Quarter}Report {yq.Year}";

                if (!uniqueQuarters.Contains(reportKey))
                {
                    recentQuarterData.Add((yq.Year.Value, yq.Quarter));
                    uniqueQuarters.Add(reportKey);
                }
            }

            // Now we reverse to get them in ascending order
            recentQuarterData.Reverse();

            // Create the display labels (e.g., "Q1Report 2022")
            var recentReports = recentQuarterData
                .Select(rp => $"Q{rp.Quarter}Report {rp.Year}")
                .ToList();

            // Convert (Year, Quarter) to a "YYYY-Q" string key
            var recentReportKeys = recentQuarterData
                .Select(rp => $"{rp.Year}-{rp.Quarter}")
                .ToList();

            // Fetch all FinancialData rows matching these year-quarter keys
            var financialDataRecords = _context.FinancialData
                .Where(fd => fd.CompanyID == companyId
                             && fd.IsHtmlParsed
                             && fd.Year.HasValue)
                .Where(fd => recentReportKeys.Contains(fd.Year.Value.ToString() + "-" + fd.Quarter.ToString()))
                .ToList();

            // Handle duplicates if necessary
            financialDataRecords = HandleDuplicates(financialDataRecords);

            return (recentQuarterData, recentReportKeys, financialDataRecords, recentReports);
        }


        private Dictionary<string, List<string>> ExtractXbrlElements(Dictionary<string, FinancialData> financialDataRecordsLookup, List<(int Year, int Quarter)> recentReportPairs)
        {
            var xbrlElements = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

            for (int p = 0; p < recentReportPairs.Count; p++)
            {
                var reportPair = recentReportPairs[p];
                var key = $"{reportPair.Year}-{reportPair.Quarter}";

                // Get the record for this period
                financialDataRecordsLookup.TryGetValue(key, out var record);

                Dictionary<string, object> financialData = null;
                if (record?.FinancialDataJson != null)
                {
                    financialData = JsonConvert.DeserializeObject<Dictionary<string, object>>(record.FinancialDataJson);
                }

                // Extract only non-HTML keys for this period
                Dictionary<string, object> xbrlDataForThisPeriod;
                if (financialData != null)
                {
                    xbrlDataForThisPeriod = financialData
                        .Where(kvp => !kvp.Key.StartsWith("HTML_", StringComparison.OrdinalIgnoreCase))
                        .ToDictionary(k => k.Key, v => v.Value);
                }
                else
                {
                    // No data for this period
                    xbrlDataForThisPeriod = new Dictionary<string, object>();
                }

                // Step 1: For existing keys, add "N/A" to represent this new period
                foreach (var existingKey in xbrlElements.Keys.ToList())
                {
                    xbrlElements[existingKey].Add("N/A");
                }

                // Step 2: Handle new keys found this period
                foreach (var kvp in xbrlDataForThisPeriod)
                {
                    if (!xbrlElements.ContainsKey(kvp.Key))
                    {
                        // This is a new key. Add N/A for all previous periods
                        xbrlElements[kvp.Key] = new List<string>();
                        for (int i = 0; i < p; i++)
                        {
                            xbrlElements[kvp.Key].Add("N/A");
                        }
                        // Add "N/A" for the current period as well
                        xbrlElements[kvp.Key].Add("N/A");
                    }
                }

                // Step 3: Now assign actual values for this period's keys
                foreach (var kvp in xbrlDataForThisPeriod)
                {
                    var value = kvp.Value?.ToString() ?? "N/A";
                    if (decimal.TryParse(value, out var num))
                    {
                        // Format with commas for thousands separator
                        value = string.Format("{0:N0}", num);
                    }
                    // Replace the N/A for this period (index p) with the actual value
                    xbrlElements[kvp.Key][p] = value;
                }
            }

            return xbrlElements;
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


        public Dictionary<string, List<string>> InitializeFinancialDataElements(List<FinancialData> financialDataRecords)
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
        public void PopulateFinancialDataElements(Dictionary<string, List<string>> financialDataElements, Dictionary<string, FinancialData> financialDataRecordsLookup, List<(int Year, int Quarter)> recentReportPairs)
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
        public Dictionary<string, Dictionary<string, List<string>>> GroupFinancialDataByStatement(Dictionary<string, List<string>> financialDataElements)
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
        public List<StatementFinancialData> CreateOrderedStatements(Dictionary<string, Dictionary<string, List<string>>> statementsDict, List<string> recentReports)
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
        public (List<(int Year, int Quarter)> recentReportPairs, List<string> recentReportKeys, List<FinancialData> financialDataRecords, List<string> recentReports)
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
        //    private (List<(int Year, int Quarter)> recentReportPairs, List<string> recentReportKeys, List<FinancialData> financialDataRecords, List<string> recentReports)
        //FetchAnnualReports(int companyId)
        //    {
        //        var recentYears = _context.FinancialData
        //            .Where(fd => fd.CompanyID == companyId && fd.IsHtmlParsed && fd.Year.HasValue && fd.Quarter == 0)
        //            .Select(fd => fd.Year.Value)
        //            .Distinct()
        //            .OrderByDescending(y => y)
        //            .Take(10)
        //            .ToList();
        //        recentYears.Reverse(); // Ascending order
        //        var recentReportPairs = recentYears.Select(y => (Year: y, Quarter: 0)).ToList();
        //        var recentReports = recentYears.Select(y => $"AnnualReport {y}").ToList();
        //        var recentReportKeys = recentReportPairs.Select(rp => $"{rp.Year}-{rp.Quarter}").ToList();

        //        var financialDataRecords = _context.FinancialData
        //            .Where(fd => fd.CompanyID == companyId && fd.IsHtmlParsed && fd.Year.HasValue)
        //            .Where(fd => recentReportKeys.Contains(fd.Year.Value.ToString() + "-" + fd.Quarter.ToString()))
        //            .ToList();

        //        // Handle duplicates here:
        //        financialDataRecords = HandleDuplicates(financialDataRecords);

        //        return (recentReportPairs, recentReportKeys, financialDataRecords, recentReports);
        //    }

        //    private (List<(int Year, int Quarter)>, List<string>, List<FinancialData>, List<string>)
        //        FetchQuarterlyReports(int companyId)
        //    {
        //        int desiredQuarterCount = 6;
        //        var allQuarterData = _context.FinancialData
        //            .Where(fd => fd.CompanyID == companyId && fd.IsHtmlParsed && fd.Year.HasValue && fd.Quarter >= 1)
        //            .Select(fd => new { fd.CompanyID, fd.Year, fd.Quarter, fd.EndDate })
        //            .Distinct()
        //            .OrderByDescending(yq => yq.Year)
        //            .ThenByDescending(yq => yq.Quarter)
        //            .ToList();

        //        var recentQuarterData = new List<(int Year, int Quarter)>();
        //        var uniqueQuarters = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        //        foreach (var yq in allQuarterData)
        //        {
        //            string reportKey = $"Q{yq.Quarter}Report {yq.Year}";
        //            if (!uniqueQuarters.Contains(reportKey))
        //            {
        //                recentQuarterData.Add((yq.Year.Value, yq.Quarter));
        //                uniqueQuarters.Add(reportKey);
        //            }
        //            if (recentQuarterData.Count >= desiredQuarterCount)
        //                break;
        //        }

        //        recentQuarterData.Reverse(); // Ascending order
        //        var recentReports = recentQuarterData.Select(rp => $"Q{rp.Quarter}Report {rp.Year}").ToList();
        //        var recentReportKeys = recentQuarterData.Select(rp => $"{rp.Year}-{rp.Quarter}").ToList();

        //        var financialDataRecords = _context.FinancialData
        //            .Where(fd => fd.CompanyID == companyId && fd.IsHtmlParsed && fd.Year.HasValue)
        //            .Where(fd => recentReportKeys.Contains(fd.Year.Value.ToString() + "-" + fd.Quarter.ToString()))
        //            .ToList();

        //        // Handle duplicates here as well:
        //        financialDataRecords = HandleDuplicates(financialDataRecords);

        //        return (recentQuarterData, recentReportKeys, financialDataRecords, recentReports);
        //    }
        private List<FinancialData> HandleDuplicates(List<FinancialData> records)
        {
            var grouped = records
                .GroupBy(r => new { r.Year, r.Quarter })
                .Where(g => g.Count() > 1) // Only groups with duplicates
                .ToList();

            foreach (var group in grouped)
            {
                // Select the record with the latest EndDate
                var primary = group.OrderByDescending(g => g.EndDate).First();

                // Identify the duplicates (other than the primary)
                var duplicates = group.Where(g => g != primary).ToList();

                // For duplicates, decrement Year by 1
                foreach (var duplicate in duplicates)
                {
                    int originalYear = duplicate.Year.Value;
                    int newYear = originalYear - 1;

                    // Just update the property on the tracked entity
                    duplicate.Year = newYear;

                    // If you need to adjust other fields (like Quarter), do so here:
                    // duplicate.Quarter = ...
                }
            }

            // Save changes in the database after all updates
            _context.SaveChanges();

            // Now 'records' list has updated entities. No need to re-query.
            return records;
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
        public List<ReportPeriod> CreateReportPeriods(string dataType, List<(int Year, int Quarter)> recentReportPairs)
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

//using Microsoft.AspNetCore.Mvc;
//using StockDataWebsite.Data;
//using StockDataWebsite.Models;
//using Newtonsoft.Json;
//using System.Text.RegularExpressions;
//using System.Globalization;
//namespace StockDataWebsite.Controllers
//{
//    public class StockDataController : Controller
//    {
//        private readonly ApplicationDbContext _context;
//        private readonly TwelveDataService _twelveDataService;
//        private readonly ILogger<StockDataController> _logger; // Declare ILogger

//        // Corrected Constructor with ILogger injection
//        public StockDataController(ApplicationDbContext context, TwelveDataService twelveDataService, ILogger<StockDataController> logger)
//        {
//            _context = context;
//            _twelveDataService = twelveDataService;
//            _logger = logger;
//        }
//        private string NormalizeStatementType(string statementType)
//        {
//            if (string.IsNullOrEmpty(statementType))
//                return statementType;
//            string normalized = statementType.ToLowerInvariant();// Convert to lowercase for consistent processing
//            normalized = Regex.Replace(normalized, @"\s*\(.*?\)\s*", " ", RegexOptions.IgnoreCase);
//            string[] keywordsToRemove = new[] { "consolidated", "condensed", "unaudited", "the" };
//            foreach (var keyword in keywordsToRemove)
//            {
//                normalized = Regex.Replace(normalized, $@"\b{Regex.Escape(keyword)}\b", "", RegexOptions.IgnoreCase);
//            }
//            normalized = Regex.Replace(normalized, @"\s+", " ").Trim();
//            normalized = CultureInfo.CurrentCulture.TextInfo.ToTitleCase(normalized);
//            var statementTypeMappings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
//    {
//        { "Operations", "Statement of Operations" },
//        { "Cashflows", "Cashflow Statement" },
//        { "Balancesheets", "Balance Sheet" },
//        { "Comprehensiveincome", "Income Statement" }
//    };
//            if (statementTypeMappings.TryGetValue(normalized, out string mappedType))
//            {
//                return mappedType;
//            }
//            return "General";
//        }
//        public async Task<IActionResult> StockData(string companyName, string dataType = "annual")
//        {
//            try
//            {
//                // Validate and sanitize dataType
//                dataType = ValidateDataType(dataType);

//                // Retrieve company details based on companyName
//                var (companyId, companySymbol) = GetCompanyDetails(companyName);
//                if (companyId == 0)
//                {
//                    _logger.LogWarning($"StockData: Company not found for Name = {companyName}");
//                    return BadRequest("Company not found.");
//                }

//                // Fetch recent reports and financial data
//                var (recentReportPairs, recentReportKeys, financialDataRecords, recentReports) = FetchRecentReportsAndData(companyId, dataType);
//                if (!financialDataRecords.Any())
//                {
//                    _logger.LogWarning($"StockData: No financial records found for CompanyID = {companyId}");
//                    return BadRequest("No financial records found.");
//                }

//                // Create a lookup for financial data records
//                var financialDataRecordsLookup = financialDataRecords.ToDictionary(fd => $"{fd.Year.Value}-{fd.Quarter}", fd => fd);

//                // Initialize and populate financial data elements
//                var financialDataElements = InitializeFinancialDataElements(financialDataRecords);
//                PopulateFinancialDataElements(financialDataElements, financialDataRecordsLookup, recentReportPairs);

//                // Group financial data by statement and create ordered statements
//                var statementsDict = GroupFinancialDataByStatement(financialDataElements);
//                var orderedStatements = CreateOrderedStatements(statementsDict, recentReports);

//                // Fetch the stock price asynchronously
//                var stockPrice = await _twelveDataService.GetStockPriceAsync(companySymbol);
//                var formattedStockPrice = stockPrice.HasValue ? $"${stockPrice.Value:F2}" : "N/A";

//                // Construct the view model with all necessary data
//                var model = new StockDataViewModel
//                {
//                    CompanyName = companyName,
//                    CompanySymbol = companySymbol,
//                    FinancialYears = CreateReportPeriods(dataType, recentReportPairs),
//                    Statements = orderedStatements,
//                    StockPrice = formattedStockPrice,
//                    DataType = dataType
//                };

//                // Log successful data retrieval
//                _logger.LogInformation($"StockData: Successfully retrieved data for CompanyID = {companyId}, Symbol = {companySymbol}");

//                return View(model);
//            }
//            catch (Exception ex)
//            {
//                // Log the exception with detailed information
//                _logger.LogError(ex, $"StockData: An exception occurred while processing data for CompanyName = {companyName}, DataType = {dataType}");

//                // Optionally, you can pass the error message to the view or handle it as needed
//                return StatusCode(500, "An error occurred while processing the data.");
//            }
//        }


//        private Dictionary<string, List<string>> InitializeFinancialDataElements(List<FinancialData> financialDataRecords)
//        {
//            var financialDataElements = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
//            var htmlKeyRegex = new Regex(@"^(?:HTML_)?(?:AnnualReport|Q\dReport)_(?<StatementType>.*?)_(?<MetricName>.+)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
//            foreach (var record in financialDataRecords)
//            {
//                var jsonData = record.FinancialDataJson ?? "{}";
//                var financialData = JsonConvert.DeserializeObject<Dictionary<string, object>>(jsonData);
//                if (financialData == null) continue;
//                foreach (var kvp in financialData)
//                {
//                    if (kvp.Key.StartsWith("HTML_", StringComparison.OrdinalIgnoreCase))
//                    {
//                        var match = htmlKeyRegex.Match(kvp.Key);
//                        string normalizedKey;
//                        if (match.Success)
//                        {
//                            var statementType = NormalizeStatementType(match.Groups["StatementType"].Value.Trim());
//                            var metricName = match.Groups["MetricName"].Value.Trim();
//                            normalizedKey = $"{statementType}_{metricName}";
//                        }
//                        else
//                        {
//                            normalizedKey = FallbackNormalizeKey(kvp.Key);
//                        }
//                        if (!financialDataElements.ContainsKey(normalizedKey))
//                        {
//                            financialDataElements[normalizedKey] = new List<string>();
//                        }
//                    }
//                }
//            }
//            return financialDataElements;
//        }
//        private void PopulateFinancialDataElements(Dictionary<string, List<string>> financialDataElements, Dictionary<string, FinancialData> financialDataRecordsLookup, List<(int Year, int Quarter)> recentReportPairs)
//        {
//            var htmlKeyRegex = new Regex(@"^(?:HTML_)?(?:AnnualReport|Q\dReport)_(?<StatementType>.*?)_(?<MetricName>.+)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
//            foreach (var reportPair in recentReportPairs)
//            {
//                var key = $"{reportPair.Year}-{reportPair.Quarter}";
//                if (!financialDataRecordsLookup.TryGetValue(key, out var record))
//                {
//                    foreach (var elementKey in financialDataElements.Keys.ToList())
//                    {
//                        financialDataElements[elementKey].Add("N/A");
//                    }
//                    continue;
//                }
//                var jsonData = record.FinancialDataJson ?? "{}";
//                var financialData = JsonConvert.DeserializeObject<Dictionary<string, object>>(jsonData);
//                if (financialData == null)
//                {
//                    foreach (var elementKey in financialDataElements.Keys.ToList())
//                    {
//                        financialDataElements[elementKey].Add("N/A");
//                    }
//                    continue;
//                }
//                var normalizedValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
//                foreach (var kvp in financialData)
//                {
//                    if (kvp.Key.StartsWith("HTML_", StringComparison.OrdinalIgnoreCase))
//                    {
//                        var match = htmlKeyRegex.Match(kvp.Key);
//                        string normalizedKey;
//                        if (match.Success)
//                        {
//                            var statementType = NormalizeStatementType(match.Groups["StatementType"].Value.Trim());
//                            var metricName = match.Groups["MetricName"].Value.Trim();
//                            normalizedKey = $"{statementType}_{metricName}";
//                        }
//                        else
//                        {
//                            normalizedKey = FallbackNormalizeKey(kvp.Key);
//                        }

//                        var value = kvp.Value?.ToString() ?? "N/A";
//                        normalizedValues[normalizedKey] = value;
//                    }
//                }
//                foreach (var elementKey in financialDataElements.Keys.ToList())
//                {
//                    if (normalizedValues.TryGetValue(elementKey, out var value))
//                    {
//                        financialDataElements[elementKey].Add(value);
//                    }
//                    else
//                    {
//                        financialDataElements[elementKey].Add("N/A");
//                    }
//                }
//            }
//        }
//        private Dictionary<string, Dictionary<string, List<string>>> GroupFinancialDataByStatement(Dictionary<string, List<string>> financialDataElements)
//        {
//            var statementsDict = new Dictionary<string, Dictionary<string, List<string>>>(StringComparer.OrdinalIgnoreCase);

//            foreach (var element in financialDataElements)
//            {
//                var normalizedKey = element.Key;
//                var keyParts = normalizedKey.Split('_');
//                string statementType;
//                string metricName;
//                if (keyParts.Length >= 2)
//                {
//                    statementType = keyParts[0].Trim();
//                    metricName = string.Join("_", keyParts.Skip(1)).Trim();
//                }
//                else
//                {
//                    statementType = "General";
//                    metricName = normalizedKey.Trim();
//                }
//                if (!statementsDict.ContainsKey(statementType))
//                {
//                    statementsDict[statementType] = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
//                }
//                statementsDict[statementType][metricName] = element.Value;
//            }
//            return statementsDict;
//        }
//        private List<StatementFinancialData> CreateOrderedStatements(Dictionary<string, Dictionary<string, List<string>>> statementsDict, List<string> recentReports)
//        {
//            var desiredOrder = new List<string> { "Statements Of Operations", "Income Statement", "Cashflow", "Balance Sheet" };
//            var operationsKey = statementsDict.Keys.FirstOrDefault(k => k.Equals("Statements Of Operations", StringComparison.OrdinalIgnoreCase) ||
//                k.IndexOf("operations", StringComparison.OrdinalIgnoreCase) >= 0);
//            if (operationsKey != null)
//            {
//                desiredOrder.Remove("Statements Of Operations");
//                desiredOrder.Insert(0, operationsKey); // Insert the 'Operations' statement at the beginning
//            }
//            var orderedStatements = new List<StatementFinancialData>();
//            StatementFinancialData ScaleAndMergeStatement(string statementKey, Dictionary<string, List<string>> metrics)
//            {
//                var exemptColumns = GetExemptColumns(metrics.Keys); // Define exempt columns using a helper method
//                var valuesForScaling = metrics
//                    .Where(kvp => !IsExemptColumn(kvp.Key, exemptColumns))
//                    .SelectMany(kvp => kvp.Value)
//                    .Where(v => decimal.TryParse(v, out _))
//                    .Select(v => decimal.Parse(v))
//                    .ToList();
//                int scalingFactor = CalculateScalingFactor(valuesForScaling); // Calculate scaling factor for the current statement
//                var scaledMetrics = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase); // Scale the financial metrics
//                foreach (var kvp in metrics)
//                {
//                    if (IsExemptColumn(kvp.Key, exemptColumns))
//                    {
//                        scaledMetrics[kvp.Key] = kvp.Value;
//                    }
//                    else
//                    {
//                        scaledMetrics[kvp.Key] = kvp.Value.Select(v =>
//                        {
//                            if (decimal.TryParse(v, out var d))
//                            {
//                                var scaledValue = d / (decimal)Math.Pow(10, scalingFactor);
//                                return scaledValue.ToString("F2");
//                            }
//                            return v;
//                        }).ToList();
//                    }
//                }
//                string scalingLabel = GetScalingLabel(scalingFactor); // Get scaling label for the current statement
//                var metricsWithBaseName = scaledMetrics.Select(kvpMetric => new
//                {
//                    OriginalName = kvpMetric.Key,
//                    BaseName = ExtractBaseName(kvpMetric.Key),
//                    Values = kvpMetric.Value
//                }).ToList();
//                var groupedMetrics = metricsWithBaseName
//                    .GroupBy(m => m.BaseName, StringComparer.OrdinalIgnoreCase)
//                    .ToList();
//                var displayMetrics = new List<DisplayMetricRow1>(); // Prepare DisplayMetrics with merging information
//                foreach (var group in groupedMetrics)
//                {
//                    var mergedValues = new List<string>();
//                    for (int i = 0; i < recentReports.Count; i++)
//                    {
//                        if (i >= group.First().Values.Count)
//                        {
//                            mergedValues.Add("N/A");
//                            continue;
//                        }
//                        string mergedValue = "N/A";
//                        foreach (var metric in group)
//                        {
//                            // Check if the current value is valid
//                            if (!string.IsNullOrEmpty(metric.Values[i]) && metric.Values[i] != "N/A")
//                            {
//                                mergedValue = metric.Values[i];
//                                break; // Stop once the first valid value is found
//                            }
//                        }
//                        mergedValues.Add(mergedValue); // Add the consolidated value for the period
//                    }
//                    displayMetrics.Add(new DisplayMetricRow1 // Add a single row with the merged values
//                    {
//                        DisplayName = group.First().OriginalName, // Use the first metric's name
//                        Values = mergedValues,
//                        IsMergedRow = false,
//                        RowSpan = 1
//                    });
//                }
//                var sfd = new StatementFinancialData // Add the statement with scaled metrics, merging info, and scaling label
//                {
//                    StatementType = statementKey,
//                    DisplayMetrics = displayMetrics,
//                    ScalingLabel = scalingLabel
//                };
//                return sfd;
//            }
//            foreach (var desired in desiredOrder)// 13. Create ordered list of statements according to desired order
//            {
//                var matchedKey = statementsDict.Keys.FirstOrDefault(k =>
//                    k.Equals(desired, StringComparison.OrdinalIgnoreCase) ||
//                    k.Contains(desired, StringComparison.OrdinalIgnoreCase));
//                if (matchedKey != null)
//                {
//                    var sfd = ScaleAndMergeStatement(matchedKey, statementsDict[matchedKey]);
//                    orderedStatements.Add(sfd);
//                }
//            }
//            foreach (var kvp in statementsDict)// 14. Handle any other statements not in the desired order
//            {
//                if (!desiredOrder.Any(d =>
//                    kvp.Key.Equals(d, StringComparison.OrdinalIgnoreCase) ||
//                    kvp.Key.IndexOf(d, StringComparison.OrdinalIgnoreCase) >= 0))
//                {
//                    var sfd = ScaleAndMergeStatement(kvp.Key, kvp.Value);
//                    orderedStatements.Add(sfd);
//                }
//            }
//            return orderedStatements;
//        }
//        private string ValidateDataType(string dataType)
//        {
//            if (dataType != "annual" && dataType != "quarterly")
//            {
//                return "annual";
//            }
//            return dataType;
//        }
//        private (int CompanyId, string CompanySymbol) GetCompanyDetails(string companyName)
//        {
//            var company = _context.CompaniesList
//                .Where(c => c.CompanyName == companyName)
//                .Select(c => new { c.CompanyID, c.CompanySymbol })
//                .FirstOrDefault();
//            if (company == null)
//            {
//                return (0, null);
//            }
//            return (company.CompanyID, company.CompanySymbol);
//        }
//        private (List<(int Year, int Quarter)> recentReportPairs, List<string> recentReportKeys, List<FinancialData> financialDataRecords, List<string> recentReports)
//            FetchRecentReportsAndData(int companyId, string dataType)
//        {
//            if (dataType == "annual")
//            {
//                return FetchAnnualReports(companyId);
//            }
//            else
//            {
//                return FetchQuarterlyReports(companyId);
//            }
//        }
//        private (List<(int Year, int Quarter)> recentReportPairs, List<string> recentReportKeys, List<FinancialData> financialDataRecords, List<string> recentReports)
//    FetchAnnualReports(int companyId)
//        {
//            var recentYears = _context.FinancialData
//                .Where(fd => fd.CompanyID == companyId && fd.IsHtmlParsed && fd.Year.HasValue && fd.Quarter == 0)
//                .Select(fd => fd.Year.Value)
//                .Distinct()
//                .OrderByDescending(y => y)
//                .Take(10)
//                .ToList();
//            recentYears.Reverse(); // Ascending order
//            var recentReportPairs = recentYears.Select(y => (Year: y, Quarter: 0)).ToList();
//            var recentReports = recentYears.Select(y => $"AnnualReport {y}").ToList();
//            var recentReportKeys = recentReportPairs.Select(rp => $"{rp.Year}-{rp.Quarter}").ToList();

//            var financialDataRecords = _context.FinancialData
//                .Where(fd => fd.CompanyID == companyId && fd.IsHtmlParsed && fd.Year.HasValue)
//                .Where(fd => recentReportKeys.Contains(fd.Year.Value.ToString() + "-" + fd.Quarter.ToString()))
//                .ToList();

//            // Handle duplicates here:
//            financialDataRecords = HandleDuplicates(financialDataRecords);

//            return (recentReportPairs, recentReportKeys, financialDataRecords, recentReports);
//        }

//        private (List<(int Year, int Quarter)>, List<string>, List<FinancialData>, List<string>)
//            FetchQuarterlyReports(int companyId)
//        {
//            int desiredQuarterCount = 6;
//            var allQuarterData = _context.FinancialData
//                .Where(fd => fd.CompanyID == companyId && fd.IsHtmlParsed && fd.Year.HasValue && fd.Quarter >= 1)
//                .Select(fd => new { fd.CompanyID, fd.Year, fd.Quarter, fd.EndDate })
//                .Distinct()
//                .OrderByDescending(yq => yq.Year)
//                .ThenByDescending(yq => yq.Quarter)
//                .ToList();

//            var recentQuarterData = new List<(int Year, int Quarter)>();
//            var uniqueQuarters = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
//            foreach (var yq in allQuarterData)
//            {
//                string reportKey = $"Q{yq.Quarter}Report {yq.Year}";
//                if (!uniqueQuarters.Contains(reportKey))
//                {
//                    recentQuarterData.Add((yq.Year.Value, yq.Quarter));
//                    uniqueQuarters.Add(reportKey);
//                }
//                if (recentQuarterData.Count >= desiredQuarterCount)
//                    break;
//            }

//            recentQuarterData.Reverse(); // Ascending order
//            var recentReports = recentQuarterData.Select(rp => $"Q{rp.Quarter}Report {rp.Year}").ToList();
//            var recentReportKeys = recentQuarterData.Select(rp => $"{rp.Year}-{rp.Quarter}").ToList();

//            var financialDataRecords = _context.FinancialData
//                .Where(fd => fd.CompanyID == companyId && fd.IsHtmlParsed && fd.Year.HasValue)
//                .Where(fd => recentReportKeys.Contains(fd.Year.Value.ToString() + "-" + fd.Quarter.ToString()))
//                .ToList();

//            // Handle duplicates here as well:
//            financialDataRecords = HandleDuplicates(financialDataRecords);

//            return (recentQuarterData, recentReportKeys, financialDataRecords, recentReports);
//        }
//        private List<FinancialData> HandleDuplicates(List<FinancialData> records)
//        {
//            var grouped = records
//                .GroupBy(r => new { r.Year, r.Quarter })
//                .Where(g => g.Count() > 1) // Only groups with duplicates
//                .ToList();

//            foreach (var group in grouped)
//            {
//                // Select the record with the latest EndDate
//                var primary = group.OrderByDescending(g => g.EndDate).First();

//                // Identify the duplicates (other than the primary)
//                var duplicates = group.Where(g => g != primary).ToList();

//                // For duplicates, decrement Year by 1
//                foreach (var duplicate in duplicates)
//                {
//                    int originalYear = duplicate.Year.Value;
//                    int newYear = originalYear - 1;

//                    // Just update the property on the tracked entity
//                    duplicate.Year = newYear;

//                    // If you need to adjust other fields (like Quarter), do so here:
//                    // duplicate.Quarter = ...
//                }
//            }

//            // Save changes in the database after all updates
//            _context.SaveChanges();

//            // Now 'records' list has updated entities. No need to re-query.
//            return records;
//        }
//        private string FallbackNormalizeKey(string originalKey)
//        {
//            var keyParts = originalKey.Split('_');
//            if (keyParts.Length >= 4)
//            {
//                var statementType = NormalizeStatementType(keyParts[2].Trim());
//                var metricName = string.Join("_", keyParts.Skip(3)).Trim();
//                return $"{statementType}_{metricName}";
//            }
//            else
//            {   // Assign to "General"
//                return $"General_{originalKey.Trim()}";
//            }
//        }
//        private List<ReportPeriod> CreateReportPeriods(string dataType, List<(int Year, int Quarter)> recentReportPairs)
//        {
//            if (dataType == "annual")
//            {
//                return recentReportPairs.Select(rp => new ReportPeriod
//                {
//                    DisplayName = rp.Year.ToString(),
//                    CompositeKey = $"{rp.Year}-{rp.Quarter}"
//                }).ToList();
//            }
//            else
//            {
//                return recentReportPairs.Select(rp => new ReportPeriod
//                {
//                    DisplayName = $"Q{rp.Quarter}Report {rp.Year}",
//                    CompositeKey = $"{rp.Year}-{rp.Quarter}"
//                }).ToList();
//            }
//        }
//        private static bool TryGetValueIgnoreCaseAndWhitespace(Dictionary<string, object> dict, string key, out object value)
//        {
//            value = null;
//            foreach (var dictKey in dict.Keys)
//            {
//                if (string.Equals(dictKey.Trim(), key.Trim(), StringComparison.OrdinalIgnoreCase))
//                {
//                    value = dict[dictKey];
//                    return true;
//                }
//            }
//            return false;
//        }
//        private bool IsExemptColumn(string columnName, HashSet<string> exemptColumns)
//        {
//            if (exemptColumns.Contains(columnName))
//            {
//                return true;
//            }
//            var exemptKeywords = new List<string> { "per share", "earnings per share", "EPS", "shares outstanding", "diluted" };
//            foreach (var keyword in exemptKeywords)
//            {
//                if (columnName.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0)
//                {
//                    return true;
//                }
//            }
//            return false;
//        }
//        private HashSet<string> GetExemptColumns(IEnumerable<string> metricKeys)
//        {
//            var staticExemptColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
//    {"DividendsDeclared", "CommonStockIssued", "DilutedInShares", "BasicInShares", "SharesUsedInComputingEarningsPerShareDiluted", "SharesUsedInComputingEarningsPerShareBasic"};
//            return staticExemptColumns;
//        }
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
//            Console.WriteLine("Scrape action called");

//            if (string.IsNullOrEmpty(companySymbol))
//            {
//                Console.WriteLine("CompanySymbol is null or empty");
//                return View("Index"); // Or return an error message
//            }

//            // Proceed with the scraping process
//            var result = await StockScraperV3.URL.ScrapeReportsForCompanyAsync(companySymbol);

//            if (result.Contains("Error"))
//            {
//                ViewBag.Error = result;
//                return View("Error");
//            }

//            ViewBag.Result = result;
//            return View("Success");
//        }

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
//            }// Check if the stock already exists in the user's watchlist
//            var existingItem = _context.Watchlist.FirstOrDefault(w => w.UserId == user.UserId && w.StockSymbol == stockSymbol);
//            if (existingItem != null)
//            {
//                return Content("This stock is already in your watchlist.");
//            }  // Add the stock to the watchlist
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
//        //
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
//        //public async Task<IActionResult> StockDataWithPrice(string companyName, string stockSymbol)
//        //{
//        //    try
//        //    {
//        //        Console.WriteLine($"Fetching stock price for symbol: {stockSymbol} using Twelve Data API");
//        //        // Fetch the latest stock price using Twelve Data Service
//        //        var stockPrice = await _twelveDataService.GetStockPriceAsync(stockSymbol);
//        //        // Check if the stock price is available
//        //        ViewBag.StockPrice = stockPrice.HasValue ? $"${stockPrice.Value:F2}" : "N/A";
//        //        return View("StockData"); // Explicitly specify the view if needed
//        //    }
//        //    catch (Exception ex)
//        //    {
//        //        return StatusCode(500, "An error occurred while fetching stock price.");
//        //    }
//        //}
//        public async Task<IActionResult> StockDataWithPrice(string companyName, string dataType = "annual")
//        {
//            try
//            {
//                Console.WriteLine($"Fetching stock price for company: {companyName} using Twelve Data API");

//                // Step 1: Validate dataType
//                dataType = ValidateDataType(dataType);

//                // Step 2: Fetch Company Details
//                var (companyId, companySymbol) = GetCompanyDetails(companyName);
//                if (companyId == 0)
//                {
//                    return BadRequest("Company not found.");
//                }

//                // Step 3 & 4: Fetch Recent Reports and Financial Data Records
//                var (recentReportPairs, recentReportKeys, financialDataRecords, recentReports) = FetchRecentReportsAndData(companyId, dataType);
//                if (!financialDataRecords.Any())
//                {
//                    return BadRequest("No financial records found.");
//                }

//                // Step 5 & 6: Build the financialDataElements dictionary (keys for metrics)
//                var financialDataRecordsLookup = financialDataRecords.ToDictionary(fd => $"{fd.Year.Value}-{fd.Quarter}", fd => fd);
//                var financialDataElements = InitializeFinancialDataElements(financialDataRecords);
//                PopulateFinancialDataElements(financialDataElements, financialDataRecordsLookup, recentReportPairs);
//                var statementsDict = GroupFinancialDataByStatement(financialDataElements);
//                var orderedStatements = CreateOrderedStatements(statementsDict, recentReports);

//                // Step 7: Fetch the latest stock price using Twelve Data Service
//                var stockPrice = await _twelveDataService.GetStockPriceAsync(companySymbol);
//                string formattedStockPrice = stockPrice.HasValue ? $"${stockPrice.Value:F2}" : "N/A";

//                // Optional: Additional Logging for Verification
//                Console.WriteLine($"Formatted Stock Price: {formattedStockPrice}");
//                Console.WriteLine($"Model: CompanyName={companyName}, CompanySymbol={companySymbol}, StockPrice={formattedStockPrice}");

//                // Step 8: Prepare the ViewModel
//                var model = new StockDataViewModel
//                {
//                    CompanyName = companyName,
//                    CompanySymbol = companySymbol,
//                    FinancialYears = CreateReportPeriods(dataType, recentReportPairs),
//                    Statements = orderedStatements,
//                    StockPrice = formattedStockPrice, // Set the stock price here
//                    DataType = dataType
//                };

//                return View("StockData", model); // Pass the model to the view
//            }
//            catch (Exception ex)
//            {
//                // Log the exception details if necessary
//                Console.WriteLine($"Error in StockDataWithPrice: {ex.Message}");
//                return StatusCode(500, "An error occurred while processing the data.");
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
