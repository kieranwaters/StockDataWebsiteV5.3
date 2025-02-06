//using Microsoft.AspNetCore.Mvc;
//using StockDataWebsite.Data;


//namespace StockDataWebsite.Controllers
//{
//    public class HomeController : Controller
//    {
//        private readonly ApplicationDbContext _context;

//        public HomeController(ApplicationDbContext context)
//        {
//            _context = context;
//        }
//        public IActionResult Index(int page = 1)// Added page parameter for pagination
//        {
//            int pageSize = 200; // Number of records per page
//            var query = _context.CompaniesList.Select(c => new { c.CompanyName, c.CompanySymbol }); // Query all companies
//            int totalItems = query.Count(); // Count total records
//            int totalPages = (int)Math.Ceiling(totalItems / (double)pageSize); // Calculate total pages
//            var pagedCompanies = query.Skip((page - 1) * pageSize).Take(pageSize).ToList(); // Get only the specified page

//            ViewBag.AllCompanies = pagedCompanies; // Pass paginated list
//            ViewBag.CurrentPage = page; // Current page number
//            ViewBag.TotalPages = totalPages; // Total pages available
//            return View();
//        }
//        [HttpGet]
//        public IActionResult SearchResults(string CompanySearch)
//        {
//            if (string.IsNullOrEmpty(CompanySearch))
//            {
//                return RedirectToAction("Index");
//            }

//            // Redirect to StockDataController's StockData action
//            return RedirectToAction("StockData", "StockData", new { companyName = CompanySearch });
//        }

//    }
//}
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using StockDataWebsite.Models;

namespace StockDataWebsite.Controllers
{
    public class HomeController : Controller
    {
        private readonly string _connectionString; // Direct SQL connection string

        // Inject IConfiguration so we can retrieve the connection string
        public HomeController(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection");
        }

        // Index action now uses ADO.NET to retrieve a paged list of companies
        public IActionResult Index(int page = 1)
        {
            int pageSize = 200; // records per page
            List<CompanyDto> companies = new List<CompanyDto>();
            int totalItems = 0;

            using (var connection = new SqlConnection(_connectionString))
            {
                connection.Open();

                // First, get the total number of records
                using (var countCmd = new SqlCommand("SELECT COUNT(*) FROM CompaniesList", connection))
                {
                    totalItems = (int)countCmd.ExecuteScalar();
                }

                // Use a common table expression (CTE) with ROW_NUMBER for pagination
                string sql = @"
  WITH CompanyCTE AS (
    SELECT CompanyName, CompanySymbol, ROW_NUMBER() OVER (ORDER BY CompanyID) AS RowNum
    FROM CompaniesList
  )
  SELECT CompanyName, CompanySymbol 
  FROM CompanyCTE 
  WHERE RowNum BETWEEN @StartRow AND @EndRow
  ORDER BY RowNum";


                using (var command = new SqlCommand(sql, connection))
                {
                    int startRow = ((page - 1) * pageSize) + 1;
                    int endRow = page * pageSize;
                    command.Parameters.AddWithValue("@StartRow", startRow);
                    command.Parameters.AddWithValue("@EndRow", endRow);

                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            companies.Add(new CompanyDto
                            {
                                CompanyName = reader["CompanyName"].ToString(),
                                CompanySymbol = reader["CompanySymbol"].ToString()
                            });
                        }
                    }
                }
            }

            int totalPages = (int)Math.Ceiling(totalItems / (double)pageSize);
            ViewBag.AllCompanies = companies;  // The homepage view expects a list in ViewBag.AllCompanies
            ViewBag.CurrentPage = page;
            ViewBag.TotalPages = totalPages;

            return View();
        }
    }
}
