
    using Microsoft.Extensions.Hosting;
    using Microsoft.Extensions.Logging;
    using StockScraperV3;
    using System;
    using System.Threading;
    using System.Threading.Tasks;

    namespace StockDataWebsite.Controllers
    {
        public class PriceDataHostedService : IHostedService, IDisposable
        {
            private readonly ILogger<PriceDataHostedService> _logger;
            private Timer _timer;
            private readonly PriceData _priceData;

            public PriceDataHostedService(ILogger<PriceDataHostedService> logger, PriceData priceData)
            {
                _logger = logger;
                _priceData = priceData;
            }

            public Task StartAsync(CancellationToken cancellationToken)
            {
                _logger.LogInformation("PriceData Hosted Service running.");

                // Schedule to run every 15 minutes
                _timer = new Timer(DoWork, null, TimeSpan.Zero, TimeSpan.FromMinutes(15));

                return Task.CompletedTask;
            }

            private async void DoWork(object state)
            {
                _logger.LogInformation("PriceData Hosted Service is working.");
                await _priceData.ScrapeTradingViewWithoutSelenium();
            }

            public Task StopAsync(CancellationToken cancellationToken)
            {
                _logger.LogInformation("PriceData Hosted Service is stopping.");

                _timer?.Change(Timeout.Infinite, 0);

                return Task.CompletedTask;
            }

            public void Dispose()
            {
                _timer?.Dispose();
            }
        }
    }


