namespace StockDataWebsite.Models
{
    public class Watchlist
    {
        public int WatchlistId { get; set; } // Primary key
        public int UserId { get; set; } // Foreign key to Users
        public string StockName { get; set; } // Stock name
        public string StockSymbol { get; set; } // Stock symbol

        // Navigation property for User
        public User User { get; set; }
    }
}
