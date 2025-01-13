//TODO: ADD A QUARTER BUTTON, TO DISPLAY QUARTER DATA
// EXPAND ANNUAL REPORTS SO IT GOES BACK 10 YEARS
// ADD AN 'ENHANCED DATA' BUTTON TO SHOW THE XBRL DATA ON THE PAGE
// REARRANGE THE ORDER OF OPERATIONS STATEMENT IN PLACE OF INCOME STATEMENT IF IT IS PRESENT
// iNTEGRATE THE SCALING FACTORS INTO THE REPORTS ON THE STOCKDATA PAGE

//using Microsoft.EntityFrameworkCore;
//using StockDataWebsite.Data;
//using StockScraperV3; // Added to reference XBRLElementData

//var builder = WebApplication.CreateBuilder(args);

//// Configure Kestrel to listen on port 80 (HTTP) and 443 (HTTPS) on all network interfaces
//builder.WebHost.ConfigureKestrel(options =>
//{
//    options.ListenAnyIP(80); // HTTP
//    options.ListenAnyIP(443, listenOptions =>
//    {
//        listenOptions.UseHttps(); // Requires a valid SSL certificate
//    });
//});

//// Add services to the container
//builder.Services.AddControllersWithViews();

//// Register ApplicationDbContext with the DI container
//builder.Services.AddDbContext<ApplicationDbContext>(options =>
//{
//    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection"));
//});

//// Register XBRLElementData with the DI container
//builder.Services.AddScoped<XBRLElementData>(provider =>
//    new XBRLElementData(builder.Configuration.GetConnectionString("DefaultConnection"))
//);
//// 
////
//builder.Services.AddTransient<URL>(provider =>
//{
//    var configuration = provider.GetRequiredService<IConfiguration>();
//    var connectionString = configuration.GetConnectionString("StockDataScraperDatabase");
//    return new URL(connectionString);
//});
//builder.Services.AddHttpContextAccessor();

//// Add distributed memory cache and session
//builder.Services.AddSingleton<TwelveDataService>(sp => new TwelveDataService(
//    new HttpClient(),
//    "beaacd3af7c247a58531686c3838c04b"
//));

//builder.Services.AddDistributedMemoryCache();
//builder.Services.AddSession(options =>
//{
//    options.IdleTimeout = TimeSpan.FromMinutes(30); // Session timeout
//    options.Cookie.HttpOnly = true;
//    options.Cookie.IsEssential = true;
//});

//var app = builder.Build();

//// Use session middleware
//app.UseSession();

//// Configure the HTTP request pipeline
//if (!app.Environment.IsDevelopment())
//{
//    app.UseExceptionHandler("/Home/Error");
//    app.UseHsts();
//}

//app.UseHttpsRedirection();
//app.UseStaticFiles();

//app.UseRouting();

//app.UseAuthorization();

//// Set default route to ScraperController
//app.MapControllerRoute(
//    name: "default",
//    pattern: "{controller=Home}/{action=Index}/{id?}");

//app.Run();

using Microsoft.EntityFrameworkCore;
using StockDataWebsite.Data;
using StockScraperV3; // Reference to XBRLElementData

var builder = WebApplication.CreateBuilder(args);

// Conditional Kestrel configuration
if (builder.Environment.IsProduction())
{
    builder.WebHost.ConfigureKestrel(options =>
    {
        options.ListenAnyIP(80); // HTTP
        options.ListenAnyIP(443, listenOptions =>
        {
            listenOptions.UseHttps(); // Requires a valid SSL certificate
        });
    });
}

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

builder.Services.AddTransient<URL>(provider =>
{
    var configuration = provider.GetRequiredService<IConfiguration>();
    var connectionString = configuration.GetConnectionString("StockDataScraperDatabase");
    return new URL(connectionString);
});

builder.Services.AddHttpContextAccessor();

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

// Map attribute-routed controllers
app.MapControllers();

// Map conventional default route
app.MapDefaultControllerRoute();

// No need for additional MapControllerRoute
// Remove or comment out the following if present
// app.MapControllerRoute(
//     name: "default",
//     pattern: "{controller=Home}/{action=Index}/{id?}");

// Run the application
app.Run();


