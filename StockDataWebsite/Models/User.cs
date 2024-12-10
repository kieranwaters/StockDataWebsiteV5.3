namespace StockDataWebsite.Models
{
    public class User
    {
        public int UserId { get; set; } // Primary key
        public string Username { get; set; } // Unique username
        public string Email { get; set; } // Unique email
        public string PasswordHash { get; set; } // Securely hashed password
    }
}
