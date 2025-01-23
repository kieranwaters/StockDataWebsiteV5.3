
using Microsoft.AspNetCore.Mvc;
using System;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using StockDataWebsite.Data;
using Microsoft.EntityFrameworkCore;

namespace StockDataWebsite.Controllers
{
    [ApiController]
    [Route("/")]
    public class SitemapController : Controller
    {
        private readonly ApplicationDbContext _context;
        private static readonly XNamespace sitemapNs = "http://www.sitemaps.org/schemas/sitemap/0.9";

        public SitemapController(ApplicationDbContext context)
        {
            _context = context;
        }

        [HttpGet("sitemap.xml")]
        public async Task<IActionResult> SitemapIndex()
        {
            var sitemapIndex = new XElement(sitemapNs + "sitemapindex");

            sitemapIndex.Add(
                new XElement(sitemapNs + "sitemap",
                    new XElement(sitemapNs + "loc", "https://alphastockdata.com/sitemap-home.xml"),
                    new XElement(sitemapNs + "lastmod", DateTime.UtcNow.ToString("yyyy-MM-dd"))
                )
            );

            int totalCompanies = await _context.CompaniesList.CountAsync();
            int chunkSize = 5000;
            int totalChunks = (int)Math.Ceiling((double)totalCompanies / chunkSize);

            for (int i = 1; i <= totalChunks; i++)
            {
                sitemapIndex.Add(
                    new XElement(sitemapNs + "sitemap",
                        new XElement(sitemapNs + "loc", $"https://alphastockdata.com/sitemap-stocks-{i}.xml"),
                        new XElement(sitemapNs + "lastmod", DateTime.UtcNow.ToString("yyyy-MM-dd"))
                    )
                );
            }

            var doc = new XDocument(new XDeclaration("1.0", "utf-8", "yes"), sitemapIndex);
            return Content(doc.ToString(), "application/xml", Encoding.UTF8);
        }

        [HttpGet("sitemap-home.xml")]
        public IActionResult SitemapHome()
        {
            var urlset = new XElement(sitemapNs + "urlset");

            // Home page
            urlset.Add(MakeUrl("https://alphastockdata.com/", "daily", 1.0));

            // Paginated pages
            for (int page = 2; page <= 1000; page++)
            {
                urlset.Add(MakeUrl($"https://alphastockdata.com/?page={page}", "daily", 0.8));
            }

            var doc = new XDocument(new XDeclaration("1.0", "utf-8", "yes"), urlset);
            return Content(doc.ToString(), "application/xml", Encoding.UTF8);
        }

        [HttpGet("sitemap-stocks-{chunk}.xml")]
        public IActionResult SitemapStocks(int chunk)
        {
            var urlset = new XElement(sitemapNs + "urlset");

            // Same chunk size as above
            int chunkSize = 5000;
            int skip = (chunk - 1) * chunkSize;

            // Grab 5k companies from the DB
            var companies = _context.CompaniesList
                        .OrderBy(c => c.CompanyName)
                        .Skip(skip)
                        .Take(chunkSize)
                        .ToList();

            foreach (var c in companies)
            {
                string encodedName = Uri.EscapeDataString(c.CompanyName ?? "");
                string baseUrl = $"https://alphastockdata.com/StockData/StockData?companyName={encodedName}";

                // Variation A
                urlset.Add(MakeUrl(baseUrl, "weekly", 0.7));
                // Variation B
                urlset.Add(MakeUrl(baseUrl + "&dataType=annual", "weekly", 0.7));
                // Variation C
                urlset.Add(MakeUrl(baseUrl + "&dataType=quarterly", "weekly", 0.7));
                // Variation D
                urlset.Add(MakeUrl(baseUrl + "&dataType=enhanced&baseType=quarterly", "weekly", 0.7));
                // Variation E
                urlset.Add(MakeUrl(baseUrl + "&dataType=enhanced&baseType=annual", "weekly", 0.7));
            }

            var doc = new XDocument(new XDeclaration("1.0", "utf-8", "yes"), urlset);
            return Content(doc.ToString(), "application/xml", Encoding.UTF8);
        }

        private XElement MakeUrl(string loc, string freq, double priority)
        {
            return new XElement(sitemapNs + "url",
                new XElement(sitemapNs + "loc", loc),
                new XElement(sitemapNs + "lastmod", DateTime.UtcNow.ToString("yyyy-MM-dd")),
                new XElement(sitemapNs + "changefreq", freq),
                new XElement(sitemapNs + "priority", priority.ToString("0.0"))
            );
        }
    }
}
