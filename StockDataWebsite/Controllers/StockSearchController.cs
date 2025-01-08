using Microsoft.AspNetCore.Mvc;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Threading.Tasks;

namespace StockDataWebsite.Controllers
{
    [ApiController]
    [Route("api/[controller]")] // base route: /api/stocksearch
    public class StockSearchController : ControllerBase
    {
        private readonly string _connectionString = "Server=localhost\\SQLEXPRESS;Database=StockDataScraperDatabase;Trusted_Connection=True;TrustServerCertificate=True";

        // GET /api/stocksearch/lookup?query=XYZ
        [HttpGet("lookup")]
        public async Task<IActionResult> Lookup(string query)
        {
            List<CompanySuggestion> suggestions = new List<CompanySuggestion>();

            using (SqlConnection connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();
                var command = new SqlCommand(@"
                    SELECT TOP 10 CompanyName, CompanySymbol
                    FROM CompaniesList
                    WHERE CompanyName LIKE @Query OR CompanySymbol LIKE @Query
                ", connection);

                command.Parameters.AddWithValue("@Query", "%" + query + "%");

                using (SqlDataReader reader = await command.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        suggestions.Add(new CompanySuggestion
                        {
                            CompanyName = reader["CompanyName"].ToString(),
                            CompanySymbol = reader["CompanySymbol"].ToString()
                        });
                    }
                }
            }

            return Ok(suggestions);
        }

        public class CompanySuggestion
        {
            public string CompanyName { get; set; }
            public string CompanySymbol { get; set; }
        }
    }
}
