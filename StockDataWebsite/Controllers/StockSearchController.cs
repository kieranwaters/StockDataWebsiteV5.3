using Microsoft.AspNetCore.Mvc;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Threading.Tasks;

namespace StockDataWebsite.Controllers
{
    [Route("api/companies")]
    public class StockSearchController : Controller
    {
        private readonly string _connectionString = "Server=localhost\\SQLEXPRESS;Database=StockDataScraperDatabase;Trusted_Connection=True;TrustServerCertificate=True";
        [HttpGet("search")]
        public async Task<IActionResult> Search(string query)
        {
            List<CompanySuggestion> suggestions = new List<CompanySuggestion>();
            using (SqlConnection connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();
                var command = new SqlCommand(@"SELECT TOP 10 CompanyName, CompanySymbol 
                                       FROM CompaniesList
                                       WHERE CompanyName LIKE @Query OR CompanySymbol LIKE @Query", connection);
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
