using Microsoft.AspNetCore.Mvc;
using StockDataWebsite.Data;
using StockDataWebsite.Models;

public class AccountController : Controller
{
    private readonly ApplicationDbContext _context;

    public AccountController(ApplicationDbContext context)
    {
        _context = context;
    }

    // GET: /Account/SignUp
    [HttpGet]
    public IActionResult SignUp()
    {
        return View(); // Render the SignUp form
    }

    // POST: /Account/SignUp
    [HttpPost]
    public IActionResult SignUp(string username, string email, string password)
    {
        if (_context.Users.Any(u => u.Username == username || u.Email == email))
        {
            ViewBag.Error = "Username or email already exists.";
            return View();
        }

        // Hash the password
        string passwordHash = HashPassword(password);

        // Save the user to the database
        var user = new User
        {
            Username = username,
            Email = email,
            PasswordHash = passwordHash
        };
        _context.Users.Add(user);
        _context.SaveChanges();

        TempData["Message"] = "Account created successfully. Please login.";
        return RedirectToAction("Login");
    }
    //
    // GET: /Account/Login
    [HttpGet]
    public IActionResult Login()
    {
        return View(); // Render the Login form
    }

    // POST: /Account/Login
    [HttpPost]
    public IActionResult Login(string username, string password)
    {
        var user = _context.Users.FirstOrDefault(u => u.Username == username);
        if (user == null)
        {
            ViewBag.Error = "Invalid username or password.";
            return View();
        }

        string passwordHash = HashPassword(password);
        if (user.PasswordHash != passwordHash)
        {
            ViewBag.Error = "Invalid username or password.";
            return View();
        }

        HttpContext.Session.SetString("Username", user.Username);
        TempData["Message"] = "Login successful!";
        return RedirectToAction("Index", "Home");
    }

    // GET: /Account/Logout
    public IActionResult Logout()
    {
        HttpContext.Session.Clear();
        TempData["Message"] = "You have been logged out.";
        return RedirectToAction("Index", "Home");
    }

    private string HashPassword(string password)
    {
        using (var sha256 = System.Security.Cryptography.SHA256.Create())
        {
            byte[] bytes = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(password));
            return Convert.ToBase64String(bytes);
        }
    }
}

