using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.AspNetCore.Rewrite;
using Microsoft.EntityFrameworkCore;
using StockDataWebsite.Controllers;
using StockDataWebsite.Data;
using StockScraperV3; // Reference to XBRLElementData

var builder = WebApplication.CreateBuilder(args);

// 1. Register Services

// Add Controllers with Views
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
builder.Services.AddControllers();
// Register PriceData as Singleton
builder.Services.AddSingleton<PriceData>();

// Register Hosted Service for PriceData
builder.Services.AddHostedService<PriceDataHostedService>();

// Register URL (Assuming 'URL' is a custom class. If it's System.URL, this should be renamed to avoid conflicts)
builder.Services.AddTransient<URL>(provider =>
{
    var configuration = provider.GetRequiredService<IConfiguration>();
    var connectionString = configuration.GetConnectionString("StockDataScraperDatabase");
    return new URL(connectionString);
});

// Register IHttpContextAccessor
builder.Services.AddHttpContextAccessor();

// Add TwelveDataService as Singleton
builder.Services.AddSingleton<TwelveDataService>(sp => new TwelveDataService(
    new HttpClient(),
    "beaacd3af7c247a58531686c3838c04b"
));

// Add Distributed Memory Cache and Session
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(30); // Session timeout
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});

// Add Response Caching services
builder.Services.AddResponseCaching();

// Add Response Compression services
builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = true;
    options.MimeTypes = ResponseCompressionDefaults.MimeTypes.Concat(
        new[] { "image/svg+xml" }
    );
});

// 2. Configure Kestrel (Conditional for Production)
if (builder.Environment.IsProduction())
{
    builder.WebHost.ConfigureKestrel(options =>
    {
        // Listen on port 80 (HTTP)
        options.ListenAnyIP(80, listenOptions =>
        {
            // Optionally, enforce HTTPS redirection here
        });

        // Listen on port 443 (HTTPS) with HTTP/2 support
        options.ListenAnyIP(443, listenOptions =>
        {
            listenOptions.UseHttps(httpsOptions =>
            {
                // Specify TLS versions
                httpsOptions.SslProtocols = System.Security.Authentication.SslProtocols.Tls12 | System.Security.Authentication.SslProtocols.Tls13;

                // Configure HTTPS options (certificates, etc.) if necessary
            });

            // Specify supported protocols
            listenOptions.Protocols = Microsoft.AspNetCore.Server.Kestrel.Core.HttpProtocols.Http1AndHttp2;
        });
    });
}

var app = builder.Build();

// 3. Configure Middleware

// Custom middleware to remove server headers
app.Use(async (context, next) =>
{
    context.Response.Headers.Remove("Server");
    context.Response.Headers.Remove("X-Powered-By");
    await next();
});

// Use Session Middleware
app.UseSession();

// Use Response Compression Middleware
app.UseResponseCompression();

// Configure the HTTP request pipeline
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();

// Configure Static File Middleware with caching
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = ctx =>
    {
        // Cache static files for 30 days
        ctx.Context.Response.Headers["Cache-Control"] = "public,max-age=2592000";
        // Optionally, set Expires header
        // ctx.Context.Response.Headers["Expires"] = DateTime.UtcNow.AddDays(30).ToString("R");
    }
});

// Configure URL Rewrite Middleware
var rewriteOptions = new RewriteOptions()
    .AddRedirectToWwwPermanent(); // Redirect non-www to www permanently (301)

app.UseRewriter(rewriteOptions);

// Use Response Caching Middleware
app.UseResponseCaching();

app.UseRouting();

app.UseAuthorization();

// Map attribute-routed controllers
app.MapControllers();
// In Startup.ConfigureServices:
// Tells ASP.NET Core we want to serve controller endpoints

// In Startup.Configure:
app.UseEndpoints(endpoints =>
{
    endpoints.MapControllers(); // Maps attribute-routed controllers like the one above
});


// Map conventional default route
app.MapDefaultControllerRoute();

app.Run();
