using Microsoft.AspNetCore.Mvc;
using StockDataWebsite.Data;
using System.Linq;

namespace StockDataWebsite.Controllers
{
    public class StatisticsController : Controller
    {
        private readonly ApplicationDbContext _context;

        public StatisticsController(ApplicationDbContext context)
        {
            _context = context;
        }

        [HttpGet("api/companies/counts")]
        public IActionResult GetCompanyCounts()
        {
            // Total number of companies in CompaniesList
            var totalCompanies = _context.CompaniesList.Count();

            // Distinct CompanyIDs present in FinancialData
            var scrapedCompanies = _context.FinancialData
                .Select(fd => fd.CompanyID)
                .Distinct()
                .Count();

            var unscrapedCompanies = totalCompanies - scrapedCompanies;

            return Json(new
            {
                scrapedCompaniesCount = scrapedCompanies,
                unscrapedCompaniesCount = unscrapedCompanies
            });
        }
    }
}
