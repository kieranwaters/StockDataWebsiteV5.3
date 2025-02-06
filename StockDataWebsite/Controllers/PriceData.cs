using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Net.Http;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace StockDataWebsite.Models
{
    public class PriceData
    {
        private readonly ILogger<PriceData> _logger;
        private readonly string _connectionString;

        // Constructor now falls back to DefaultConnection if StockDataScraperDatabase is not set.
        public PriceData(ILogger<PriceData> logger, IConfiguration configuration)
        {
            _logger = logger;
            _connectionString = configuration.GetConnectionString("StockDataScraperDatabase")
                              ?? configuration.GetConnectionString("DefaultConnection");
        }

        public class TradingViewResponse
        {
            public List<TradingViewRow> data { get; set; }
            public int totalCount { get; set; }
        }

        public class TradingViewRow
        {
            public string s { get; set; } // e.g. "NASDAQ:AAPL"
            public object[] d { get; set; } // e.g. ["AAPL", 173.7, 2.5, 1000000, 2.5E9]
        }

        public async Task ScrapeTradingViewWithoutSelenium()
        {
            using (HttpClient client = new HttpClient())
            {
                client.DefaultRequestHeaders.Add("Accept", "application/json"); // Request JSON
                client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0"); // Imitate a browser

                var payload = new
                {
                    filter = new object[] { },
                    options = new { lang = "en" },
                    markets = new string[] { "america" },
                    symbols = new { query = new { types = new string[] { } }, tickers = new string[] { } },
                    columns = new string[] { "name", "close", "change", "volume", "market_cap_basic" },
                    sort = new { sortBy = "market_cap_basic", sortOrder = "desc" },
                    range = new int[] { 0, 10000 }
                };

                var requestJson = System.Text.Json.JsonSerializer.Serialize(payload);
                var requestBody = new StringContent(requestJson, System.Text.Encoding.UTF8, "application/json");
                var response = await client.PostAsync("https://scanner.tradingview.com/america/scan", requestBody);
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogError("Request failed. Status code: " + response.StatusCode);
                    return;
                }
                var rawJson = await response.Content.ReadAsStringAsync();
                _logger.LogInformation(rawJson);
                var tvResponse = System.Text.Json.JsonSerializer.Deserialize<TradingViewResponse>(rawJson);
                if (tvResponse == null || tvResponse.data == null)
                {
                    _logger.LogError("No 'data' array found in JSON.");
                    return;
                }
                using (SqlConnection conn = new SqlConnection(_connectionString))
                {
                    conn.Open(); // Ensure _connectionString is initialized
                    foreach (var row in tvResponse.data)
                    {
                        string fullSymbol = row.s; // e.g. "NASDAQ:AAPL"
                        object[] columns = row.d;
                        if (columns == null || columns.Length < 4) continue; // Need at least name, close, change, volume
                        string ticker = columns[0]?.ToString();
                        decimal closePrice = 0m, dailyChange = 0m;
                        long volume = 0;
                        if (decimal.TryParse(columns[1]?.ToString(), out decimal tmpClose)) closePrice = tmpClose;
                        if (decimal.TryParse(columns[2]?.ToString(), out decimal tmpChange)) dailyChange = tmpChange;
                        if (long.TryParse(columns[3]?.ToString(), out long tmpVol)) volume = tmpVol;
                        _logger.LogInformation($"Ticker={ticker}, Close={closePrice}, Change={dailyChange}, Volume={volume}");
                        using (SqlCommand cmd = new SqlCommand(
                         "UPDATE [StockDataScraperDatabase].[dbo].[CompaniesList] " +
                         "SET [StockPrice]=@price, [DailyChange]=@change, [Volume]=@vol " +
                         "WHERE [CompanySymbol]=@symbol", conn))
                        {
                            cmd.Parameters.AddWithValue("@price", closePrice);
                            cmd.Parameters.AddWithValue("@change", dailyChange);
                            cmd.Parameters.AddWithValue("@vol", volume);
                            cmd.Parameters.AddWithValue("@symbol", ticker);
                            cmd.ExecuteNonQuery();
                        }
                    }
                }
            }
        }
    }
}
