using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StockDataWebsite.Data;
using StockDataWebsite.Models;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;

namespace StockDataWebsite.Controllers
{
    public class HomeController : Controller
    {
        private readonly ApplicationDbContext _context;

        public HomeController(ApplicationDbContext context)
        {
            _context = context;
        }

        public IActionResult Index()
        {
            // Retrieve all companies from the database
            var allCompanies = _context.CompaniesList
                .Select(c => new { c.CompanyName, c.CompanySymbol })
                .ToList();

            // Pass the list of companies to the view
            ViewBag.AllCompanies = allCompanies;

            return View();
        }

        [HttpGet]
        public IActionResult SearchResults(string CompanySearch)
        {
            if (string.IsNullOrEmpty(CompanySearch))
            {
                return RedirectToAction("Index");
            }

            // Redirect to StockDataController's StockData action
            return RedirectToAction("StockData", "StockData", new { companyName = CompanySearch });
        }
    }
}
