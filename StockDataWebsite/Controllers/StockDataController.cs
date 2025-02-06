using Microsoft.AspNetCore.Mvc;
using StockDataWebsite.Data;
using StockDataWebsite.Models;
using Newtonsoft.Json;
using System.Text.RegularExpressions;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Data.SqlClient;
namespace StockDataWebsite.Controllers
{
    [ApiController] // Allows automatic model binding and simpler conventions
    [Route("api/[controller]")]
    public class FinancialDataController : ControllerBase
    {
        private readonly ApplicationDbContext _context;

        public FinancialDataController(ApplicationDbContext context)
        {
            _context = context;
        }

        // GET /api/financialdata?companySymbol=BRK-B
        [HttpGet]
        public IActionResult GetFinancialData([FromQuery] string companySymbol)
        {
            if (string.IsNullOrEmpty(companySymbol))
            {
                return BadRequest(new { error = "companySymbol is required" });
            }

            // TODO: Look up your database or business logic here
            // For now, return a placeholder object:
            var data = new
            {
                Symbol = companySymbol,
                ExampleValue = "Hello from /api/financialdata!"
            };

            return Ok(data);
        }
    }
    public class StockDataController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly TwelveDataService _twelveDataService;
        private readonly ILogger<StockDataController> _logger; // Declare ILogger
        private readonly string _connectionString;

        public StockDataController(
    ApplicationDbContext context,
    TwelveDataService twelveDataService,
    ILogger<StockDataController> logger,
    IConfiguration configuration)
        {
            _context = context;
            _twelveDataService = twelveDataService;
            _logger = logger;
            _connectionString = configuration.GetConnectionString("DefaultConnection");
        }

        // Updated GetCompanyDetails method using direct SQL (instead of EF)
        private (int CompanyId, string CompanySymbol) GetCompanyDetails(string companyName)
        {
            if (string.IsNullOrWhiteSpace(companyName))
                return (0, null);
            var normalizedName = companyName.Trim().ToLower();
            string sql = @"
        SELECT TOP 1 CompanyID, CompanySymbol 
        FROM CompaniesList 
        WHERE (CompanyName IS NOT NULL 
               AND LOWER(LTRIM(RTRIM(CompanyName))) = @NormalizedName)
           OR (CompanySymbol IS NOT NULL 
               AND LOWER(LTRIM(RTRIM(CompanySymbol))) = @NormalizedName)";
            using (var connection = new SqlConnection(_connectionString))
            {
                using (var command = new SqlCommand(sql, connection))
                {
                    command.Parameters.AddWithValue("@NormalizedName", normalizedName);
                    connection.Open();
                    using (var reader = command.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            int companyId = reader.GetInt32(reader.GetOrdinal("CompanyID"));
                            string companySymbol = reader.GetString(reader.GetOrdinal("CompanySymbol"));
                            return (companyId, companySymbol);
                        }
                    }
                }
            }
            return (0, null);
        }
        private (List<(int Year, int Quarter)> recentReportPairs, List<string> recentReportKeys, List<FinancialData> financialDataRecords, List<string> recentReports)
     FetchAnnualReports(int companyId)
        {
            var recentYears = new List<int>();
            using (var connection = new SqlConnection(_connectionString))
            {
                connection.Open();
                string sqlDistinct = @"
            SELECT DISTINCT Year 
            FROM FinancialData 
            WHERE CompanyID = @CompanyID 
              AND IsHtmlParsed = 1 
              AND Year IS NOT NULL 
              AND Quarter = 0 
            ORDER BY Year DESC";
                using (var command = new SqlCommand(sqlDistinct, connection))
                {
                    command.Parameters.AddWithValue("@CompanyID", companyId);
                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            recentYears.Add(reader.GetInt32(0));
                        }
                    }
                }
            }
            recentYears.Sort();
            var recentReportPairs = recentYears.Select(y => (Year: y, Quarter: 0)).ToList();
            var recentReports = recentYears.Select(y => $"AnnualReport {y}").ToList();
            var recentReportKeys = recentReportPairs.Select(rp => $"{rp.Year}-{rp.Quarter}").ToList();
            var financialDataRecords = new List<FinancialData>();

            // If there are no keys, return early to avoid an empty IN clause.
            if (!recentReportKeys.Any())
            {
                return (recentReportPairs, recentReportKeys, financialDataRecords, recentReports);
            }

            string inClause = string.Join(",", recentReportKeys.Select((key, index) => $"@key{index}"));
            string sqlRecords = $@"
        SELECT * FROM FinancialData 
        WHERE CompanyID = @CompanyID 
          AND IsHtmlParsed = 1 
          AND Year IS NOT NULL 
          AND (CAST(Year AS VARCHAR(10)) + '-' + CAST(Quarter AS VARCHAR(10))) 
               IN ({inClause})";  // Removed extra closing parenthesis here

            using (var connection = new SqlConnection(_connectionString))
            {
                connection.Open();
                using (var command = new SqlCommand(sqlRecords, connection))
                {
                    command.Parameters.AddWithValue("@CompanyID", companyId);
                    for (int i = 0; i < recentReportKeys.Count; i++)
                    {
                        command.Parameters.AddWithValue($"@key{i}", recentReportKeys[i]);
                    }
                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            var record = new FinancialData
                            {
                                CompanyID = reader.GetInt32(reader.GetOrdinal("CompanyID")),
                                Year = reader.IsDBNull(reader.GetOrdinal("Year"))
                                    ? (int?)null
                                    : reader.GetInt32(reader.GetOrdinal("Year")),
                                Quarter = reader.IsDBNull(reader.GetOrdinal("Quarter"))
                                    ? 0
                                    : reader.GetInt32(reader.GetOrdinal("Quarter")),
                                IsHtmlParsed = reader.GetBoolean(reader.GetOrdinal("IsHtmlParsed")),
                                FinancialDataJson = reader.IsDBNull(reader.GetOrdinal("FinancialDataJson"))
                                    ? null
                                    : reader.GetString(reader.GetOrdinal("FinancialDataJson"))
                            };
                            financialDataRecords.Add(record);
                        }
                    }
                }
            }
            financialDataRecords = HandleDuplicates(financialDataRecords);
            return (recentReportPairs, recentReportKeys, financialDataRecords, recentReports);
        }


        // Updated FetchQuarterlyReports method using direct SQL queries
        private (List<(int Year, int Quarter)> recentReportPairs, List<string> recentReportKeys, List<FinancialData> financialDataRecords, List<string> recentReports)
    FetchQuarterlyReports(int companyId)
        {
            var quarterData = new List<(int Year, int Quarter, DateTime EndDate)>();
            using (var connection = new SqlConnection(_connectionString))
            {
                connection.Open();
                string sqlQuarter = @"
            SELECT DISTINCT CompanyID, Year, Quarter, EndDate 
            FROM FinancialData 
            WHERE CompanyID = @CompanyID 
              AND IsHtmlParsed = 1 
              AND Year IS NOT NULL 
              AND Quarter >= 1 
            ORDER BY Year DESC, Quarter DESC";
                using (var command = new SqlCommand(sqlQuarter, connection))
                {
                    command.Parameters.AddWithValue("@CompanyID", companyId);
                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            int year = reader.GetInt32(reader.GetOrdinal("Year"));
                            int quarter = reader.GetInt32(reader.GetOrdinal("Quarter"));
                            DateTime endDate = reader.GetDateTime(reader.GetOrdinal("EndDate"));
                            quarterData.Add((year, quarter, endDate));
                        }
                    }
                }
            }
            var recentQuarterData = new List<(int Year, int Quarter)>();
            var uniqueReports = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var q in quarterData)
            {
                string reportKey = $"Q{q.Quarter}Report {q.Year}";
                if (!uniqueReports.Contains(reportKey))
                {
                    recentQuarterData.Add((q.Year, q.Quarter));
                    uniqueReports.Add(reportKey);
                }
            }
            recentQuarterData.Reverse();
            var recentReports = recentQuarterData
                .Select(rp => $"Q{rp.Quarter}Report {rp.Year}")
                .ToList();
            var recentReportKeys = recentQuarterData
                .Select(rp => $"{rp.Year}-{rp.Quarter}")
                .ToList();
            var financialDataRecords = new List<FinancialData>();
            if (!recentReportKeys.Any())
            {
                return (recentQuarterData, recentReportKeys, financialDataRecords, recentReports);
            }
            string inClause = string.Join(",", recentReportKeys.Select((key, index) => $"@key{index}"));
            string sqlRecords = $@"
        SELECT * FROM FinancialData 
        WHERE CompanyID = @CompanyID 
          AND IsHtmlParsed = 1 
          AND Year IS NOT NULL 
          AND (CAST(Year AS VARCHAR(10)) + '-' + CAST(Quarter AS VARCHAR(10))) 
               IN ({inClause})"; // Removed the extra ')' here
            using (var connection = new SqlConnection(_connectionString))
            {
                connection.Open();
                using (var command = new SqlCommand(sqlRecords, connection))
                {
                    command.Parameters.AddWithValue("@CompanyID", companyId);
                    for (int i = 0; i < recentReportKeys.Count; i++)
                    {
                        command.Parameters.AddWithValue($"@key{i}", recentReportKeys[i]);
                    }
                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            var record = new FinancialData
                            {
                                CompanyID = reader.GetInt32(reader.GetOrdinal("CompanyID")),
                                Year = reader.IsDBNull(reader.GetOrdinal("Year"))
                                    ? (int?)null
                                    : reader.GetInt32(reader.GetOrdinal("Year")),
                                Quarter = reader.IsDBNull(reader.GetOrdinal("Quarter"))
                                    ? 0
                                    : reader.GetInt32(reader.GetOrdinal("Quarter")),
                                IsHtmlParsed = reader.GetBoolean(reader.GetOrdinal("IsHtmlParsed")),
                                FinancialDataJson = reader.IsDBNull(reader.GetOrdinal("FinancialDataJson"))
                                    ? null
                                    : reader.GetString(reader.GetOrdinal("FinancialDataJson"))
                            };
                            financialDataRecords.Add(record);
                        }
                    }
                }
            }
            financialDataRecords = HandleDuplicates(financialDataRecords);
            return (recentQuarterData, recentReportKeys, financialDataRecords, recentReports);
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
        public async Task<IActionResult> StockData(string companyName, string dataType = "annual", string baseType = null, string yearFilter = "all")
        {
            try
            {
                if (dataType != "annual" && dataType != "quarterly" && dataType != "enhanced") dataType = "annual"; // Validate dataType if(baseType==null){ if(dataType=="enhanced") baseType="annual"; else baseType=dataType; } // Determine baseType var (companyId,companySymbol)=GetCompanyDetails(companyName); if(companyId==0){ _logger.LogWarning($"StockData: Company not found for Name = {companyName}"); return BadRequest("Company not found."); } var dataTypeForFetching=(dataType=="enhanced")?baseType:dataType; var (recentReportPairs,recentReportKeys,financialDataRecords,recentReports)=FetchRecentReportsAndData(companyId,dataTypeForFetching); if(!financialDataRecords.Any()){ _logger.LogWarning($"StockData: No financial records found for CompanyID = {companyId}"); return BadRequest("No financial records found."); } List<StatementFinancialData> orderedStatements; List<ReportPeriod> reportPeriods=CreateReportPeriods(dataTypeForFetching,recentReportPairs); if(dataType=="enhanced"){ var financialDataRecordsLookup=financialDataRecords.ToDictionary(fd=>$"{fd.Year.Value}-{fd.Quarter}",fd=>fd); var xbrlElements=ExtractXbrlElements(financialDataRecordsLookup,recentReportPairs); var displayMetrics=xbrlElements.Select(kvp=>new DisplayMetricRow1{ DisplayName=kvp.Key,Values=kvp.Value.ToList(),IsMergedRow=false,RowSpan=1 }).ToList(); var rawElementNames=displayMetrics.Select(dm=>dm.DisplayName).Distinct().ToList(); var labelMap=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase); if(rawElementNames.Any()){ var parameterizedNames=string.Join(",",rawElementNames.Select((_,index)=>$"@p{index}")); var sqlQuery=$"SELECT RawElementName, ElementLabel FROM XBRLDataTypes WHERE RawElementName IN ({parameterizedNames})"; using(var connection=_context.Database.GetDbConnection()){ connection.Open(); using(var command=connection.CreateCommand()){ command.CommandText=sqlQuery; for(int i=0;i<rawElementNames.Count;i++){ var parameter=command.CreateParameter(); parameter.ParameterName=$"@p{i}"; parameter.Value=rawElementNames[i]; command.Parameters.Add(parameter); } using(var reader=command.ExecuteReader()){ while(reader.Read()){ var rawName=reader.GetString(0); var label=reader.IsDBNull(1)?rawName:reader.GetString(1); labelMap[rawName]=label; } } } } } foreach(var metric in displayMetrics){ if(labelMap.TryGetValue(metric.DisplayName,out var label)) metric.DisplayName=label; } orderedStatements=new List<StatementFinancialData>{ new StatementFinancialData{ StatementType="Enhanced Data",DisplayMetrics=displayMetrics,ScalingLabel="" } }; } else{ var financialDataRecordsLookup=financialDataRecords.ToDictionary(fd=>$"{fd.Year.Value}-{fd.Quarter}",fd=>fd); var financialDataElements=InitializeFinancialDataElements(financialDataRecords); PopulateFinancialDataElements(financialDataElements,financialDataRecordsLookup,recentReportPairs); var statementsDict=GroupFinancialDataByStatement(financialDataElements); orderedStatements=CreateOrderedStatements(statementsDict,recentReports); } var stockPrice=await _twelveDataService.GetStockPriceAsync(companySymbol); var formattedStockPrice=stockPrice.HasValue?$"${stockPrice.Value:F2}":"N/A"; var uniqueYears=recentReportPairs.Select(rp=>rp.Year).Distinct().OrderByDescending(y=>y).ToList(); var model=new StockDataViewModel{ CompanyName=companyName,CompanySymbol=companySymbol,FinancialYears=CreateReportPeriods(dataTypeForFetching,recentReportPairs),Statements=orderedStatements,StockPrice=formattedStockPrice,DataType=dataType,BaseType=baseType,SelectedYearFilter=yearFilter,UniqueYears=uniqueYears }; model.YearFilterOptions=new List<SelectListItem>{ new SelectListItem{ Value="all",Text="All Years" }, new SelectListItem{ Value="5",Text="Last 5 Years" }, new SelectListItem{ Value="3",Text="Last 3 Years" }, new SelectListItem{ Value="1",Text="Last Year" } }; _logger.LogInformation($"StockData: Successfully retrieved data for CompanyID = {companyId}, Symbol = {companySymbol}"); return View(model); } catch(Exception ex){ _logger.LogError(ex,$"StockData: An exception occurred while processing data for CompanyName = {companyName}, DataType = {dataType}"); return StatusCode(500,"An error occurred while processing the data."); } }

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
                    // Enhanced data logic (EXISTING CODE - NO CHANGES)
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
                    // Basic data logic (EXISTING CODE - NO CHANGES)
                    var financialDataRecordsLookup = financialDataRecords.ToDictionary(fd => $"{fd.Year.Value}-{fd.Quarter}", fd => fd);

                    var financialDataElements = InitializeFinancialDataElements(financialDataRecords);
                    PopulateFinancialDataElements(financialDataElements, financialDataRecordsLookup, recentReportPairs);

                    var statementsDict = GroupFinancialDataByStatement(financialDataElements);
                    orderedStatements = CreateOrderedStatements(statementsDict, recentReports);
                }

                var stockPrice = await _twelveDataService.GetStockPriceAsync(companySymbol);
                var formattedStockPrice = stockPrice.HasValue ? $"${stockPrice.Value:F2}" : "N/A";
                var uniqueYears = recentReportPairs.Select(rp => rp.Year).Distinct().OrderByDescending(y => y).ToList();


                // Add yearFilter to the model (ONLY NEW LINE)
                var model = new StockDataViewModel
                {
                    CompanyName = companyName,
                    CompanySymbol = companySymbol,
                    FinancialYears = CreateReportPeriods(dataTypeForFetching, recentReportPairs),
                    Statements = orderedStatements,
                    StockPrice = formattedStockPrice,
                    DataType = dataType,
                    BaseType = baseType,
                    SelectedYearFilter = yearFilter,
                    UniqueYears = uniqueYears // Populate unique years
                };
                model.YearFilterOptions = new List<SelectListItem>
{
    new SelectListItem { Value = "all", Text = "All Years" },
    new SelectListItem { Value = "5", Text = "Last 5 Years" },
    new SelectListItem { Value = "3", Text = "Last 3 Years" },
    new SelectListItem { Value = "1", Text = "Last Year" }
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
