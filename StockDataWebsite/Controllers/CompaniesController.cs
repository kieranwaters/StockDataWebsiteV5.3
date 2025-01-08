// /Controllers/CompaniesController.cs
using Microsoft.AspNetCore.Mvc;
using StockDataWebsite.Data;

namespace StockDataWebsite.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class CompaniesController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        public CompaniesController(ApplicationDbContext context)
        {
            _context = context;
        }
        [HttpGet("search")]
        public IActionResult Search(string query)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(query))
                    return Ok(new List<object>());

                var results = _context.CompaniesList
                    .Where(c => c.CompanyName.Contains(query) || c.CompanySymbol.Contains(query))
                    .Select(c => new { c.CompanyName, c.CompanySymbol })
                    .Take(20)
                    .ToList(); // <-- This likely triggers an exception

                return Ok(results);
            }
            catch (Exception ex)
            {
                // Log the exception or return it so you can see what's going on
                // WARNING: Return details only in development or debug; not recommended in production
                return StatusCode(500, "Error: " + ex.Message);
            }
        }

    }
}
