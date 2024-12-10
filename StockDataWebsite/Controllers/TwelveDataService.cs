using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

public class TwelveDataService
{
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;

    public TwelveDataService(HttpClient httpClient, string apiKey)
    {
        _httpClient = httpClient;
        _apiKey = apiKey;
    }

    public async Task<decimal?> GetStockPriceAsync(string stockSymbol)
    {
        try
        {
            var url = $"https://api.twelvedata.com/price?symbol={stockSymbol}&apikey={_apiKey}";
            Console.WriteLine($"Fetching stock price from: {url}");

            var response = await _httpClient.GetAsync(url);

            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine($"Failed to fetch stock price. Status: {response.StatusCode}");
                return null;
            }

            var content = await response.Content.ReadAsStringAsync();
            var jsonResponse = JsonSerializer.Deserialize<JsonElement>(content);

            if (jsonResponse.TryGetProperty("price", out var priceProperty) &&
                decimal.TryParse(priceProperty.GetString(), out var price))
            {
                Console.WriteLine($"Fetched stock price for {stockSymbol}: {price}");
                return price;
            }

            Console.WriteLine($"Stock price for {stockSymbol} not found in response.");
            return null;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error fetching stock price: {ex.Message}");
            return null;
        }
    }
}
