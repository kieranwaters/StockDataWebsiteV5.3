using Microsoft.EntityFrameworkCore;
using StockDataWebsite.Models;

namespace StockDataWebsite.Data
{
    public class ApplicationDbContext : DbContext
    {
        // Constructor for ApplicationDbContext (Inject DbContextOptions)
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options)
        {

        }
        public class XBRLDataType
        {
            public int CompanyID { get; set; }
            public string RawElementName { get; set; }
            public string ElementLabel { get; set; }
            // Other fields like BalanceType, Definition if needed
        }
        // Define DbSet properties for your tables
        public DbSet<CompanySelection> CompaniesList { get; set; }
        public DbSet<FinancialData> FinancialData { get; set; }
        public DbSet<User> Users { get; set; } // Add this line for the Users table
        public DbSet<Watchlist> Watchlist { get; set; }

        // Configure model properties and precision for fields
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<FinancialData>()
           .HasKey(fd => new { fd.CompanyID, fd.Year, fd.Quarter });

            // Configure Year as required in the composite key
            modelBuilder.Entity<FinancialData>()
                .Property(fd => fd.Year)
                .IsRequired();

            base.OnModelCreating(modelBuilder); // Always call base OnModelCreating
        }
    }
}
