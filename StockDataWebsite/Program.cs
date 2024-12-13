//TODO: ADD A QUARTER BUTTON, TO DISPLAY QUARTER DATA
// EXPAND ANNUAL REPORTS SO IT GOES BACK 10 YEARS
// ADD AN 'ENHANCED DATA' BUTTON TO SHOW THE XBRL DATA ON THE PAGE
// REARRANGE THE ORDER OF OPERATIONS STATEMENT IN PLACE OF INCOME STATEMENT IF IT IS PRESENT
// iNTEGRATE THE SCALING FACTORS INTO THE REPORTS ON THE STOCKDATA PAGE





using Microsoft.EntityFrameworkCore;
using StockDataWebsite.Data;
using StockScraperV3; // Added to reference XBRLElementData

var builder = WebApplication.CreateBuilder(args);

// Add services to the container
builder.Services.AddControllersWithViews();

// Register ApplicationDbContext with the DI container
builder.Services.AddDbContext<ApplicationDbContext>(options =>
{
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection"));
});

// Register XBRLElementData with the DI container
builder.Services.AddScoped<XBRLElementData>(provider =>
    new XBRLElementData(builder.Configuration.GetConnectionString("DefaultConnection"))
);

// Add distributed memory cache and session
builder.Services.AddSingleton<TwelveDataService>(sp => new TwelveDataService(
    new HttpClient(),
    "beaacd3af7c247a58531686c3838c04b"
));

builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(30); // Session timeout
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});

var app = builder.Build();

// Use session middleware
app.UseSession();

// Configure the HTTP request pipeline
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseAuthorization();

// Set default route to ScraperController
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Scraper}/{action=Index}/{id?}");

app.Run();




