using Microsoft.AspNetCore.Mvc;
using System.Threading.Tasks;
using StockInfoAPI; // Add reference to your StockInfoAPI project

namespace StockDataWebsite.Controllers
{
    public class StockInfoController : Controller
    {
        [HttpPost]
        public async Task<IActionResult> FetchStockInfo()
        {     // Call the method from your StockInfoAPI program
            await StockInfoAPI.Program.FetchAndStoreCompanyData();  // This is the method that fetches and stores data
            // Optionally, return a response or message to the user
            return Json(new { success = true, message = "Stock info fetched and stored successfully." });
        }
    }
}
