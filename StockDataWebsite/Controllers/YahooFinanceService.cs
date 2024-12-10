using System.Net.Http;
using System.Threading.Tasks;
using YahooFinanceApi;

public class YahooFinanceService
{
    private readonly HttpClient _httpClient;

    public YahooFinanceService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<decimal?> GetStockPriceAsync(string stockSymbol)
    {
        try
        {
            Console.WriteLine($"Fetching stock price for symbol: {stockSymbol}");

            // Fetch data from Yahoo Finance
            var securities = await Yahoo.Symbols(stockSymbol).Fields(Field.RegularMarketPrice).QueryAsync();

            if (securities == null || !securities.ContainsKey(stockSymbol))
            {
                Console.WriteLine("No data returned for the given stock symbol.");
                return null;
            }

            var security = securities[stockSymbol];

            // Access the RegularMarketPrice field directly
            var priceValue = security[Field.RegularMarketPrice];
            if (priceValue is decimal price)
            {
                Console.WriteLine($"Fetched stock price: {price}");
                return price;
            }

            Console.WriteLine("Market price not available for the given stock symbol.");
            return null;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error fetching stock price: {ex.Message}");
            return null;
        }
    }
}