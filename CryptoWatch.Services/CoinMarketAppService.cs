using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CoinMarketCap;
using CoinMarketCap.Models.Cryptocurrency;
using CryptoWatch.Core.Config;
using CryptoWatch.Entities.Contexts;
using CryptoWatch.Entities.Domains;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Slack.Webhooks;

namespace CryptoWatch.Services {
	public sealed class CoinMarketCapService : HostedService, IDisposable, IPriceService {
		#region Member Variables

		private readonly WatchService watcher;

		private CoinMarketCapClient client;
		private List<Balance> assets;
		private readonly object lockObject = new( );
		private bool processing = false;

		// set to false only if database is empty and symbols need to be added
		private bool initialized = true;

		#endregion

		#region Constructor

		public CoinMarketCapService(
			ILogger<CoinMarketCapService> logger,
			CoinMarketCapConfiguration config,
			IntegrationsConfiguration intConfig,
			GeneralConfiguration genConfig,
			CryptoContext context,
			SlackClient slack,
			WatchService watcher
		) : base( logger, context, intConfig, genConfig, config, slack ) {
			this.watcher = watcher;
		}

		#endregion

		#region Properties

		/// <inheritdoc />
		protected override string ServiceName { get; } = nameof(CoinMarketCapService);

		#endregion;

		#region Methods

		protected override async Task ExecuteAsync( CancellationToken cancellationToken ) {
			this.watcher.PriceService = this;
			await this.watcher.StartAsync( cancellationToken );
			this.client = new CoinMarketCapClient( base.CoinMarketCapSettings.ApiKey );
			client.HttpClient.BaseAddress = new Uri( base.CoinMarketCapSettings.BaseUrl );
			while( true ) {
				if( !this.initialized ) {
					await this.loadInitialData( cancellationToken );
					this.initialized = true;
				} else {
					await ( (IPriceService) this ).UpdateBalances( cancellationToken );
				}

				base.Logger.LogInformation( "Sleeping for five minutes." );
				Console.WriteLine( );
				await Task.Delay( TimeSpan.FromMinutes( 5 ), cancellationToken );
				if( cancellationToken.IsCancellationRequested )
					break;
			}
		}

		async Task IPriceService.UpdateBalances( CancellationToken cancellationToken ) {
			while( this.watcher.Processing ) {
				base.Logger.LogInformation( "File watcher is running. Sleeping for five seconds." );
				await Task.Delay( TimeSpan.FromSeconds( 5 ), cancellationToken );
			}

			lock( this.lockObject ) {
				// triggered externally but already running, so don't need to do it again
				if( this.processing )
					return;
			}

			lock( this.lockObject )
				this.processing = true;

			try {
				if( this.watcher.Changed || this.assets == null ) {
					this.assets = await base.Context.Balances.ToListAsync( cancellationToken );
					this.watcher.Changed = false;
				}

				var parameters = new LatestQuoteParameters( );
				parameters.Symbols.AddRange( this.assets.Where( a => !a.Exclude ).Select( s => s.AltSymbol ) );
				var response = await this.client.GetLatestQuoteAsync( parameters, cancellationToken );
				foreach( var (_, value) in response.Data ) {
					var asset = this.assets.First( a => a.AltSymbol.Equals( value.Symbol, StringComparison.OrdinalIgnoreCase ) );
					var price = value.Quote[ "USD" ].Price;
					if( !price.HasValue )
						continue;
					asset.Price = Convert.ToDecimal( price.Value );
				}

				assets = this.assets.OrderByDescending( a => a.Value ).ToList( );
				var cash = this.assets.First( a => a.Symbol.Equals( "USD" ) );
				for( var i = 0; i < this.assets.Count; i++ ) {
					var current = this.assets[ i ];
					base.Logger.LogInformation( !current.Exclude
												   ? $"{current.Symbol,-4} ({current.Name + "):",-23} {current.Value,7:C} / notified at {current.NotifiedAt,7:C} / range {current.BuyBoundary,7:C} - {current.SellBoundary,7:C}"
												   : $"{current.Symbol,-4} ({current.Name + "):",-23} {current.Value,7:C}"
											  );
					if( current.Exclude || current.Value > current.BuyBoundary && current.Value < current.SellBoundary )
						continue;

					if( current.Value <= current.BuyBoundary ) {
						var amount = current.NotifiedAt - current.Value;
						if( cash.Value - amount > base.GeneralSettings.CashFloor ) {
							await base.SendNotificationAsync( $"@here {amount:C} BUY  {current.Symbol} ({current.Name}) cash: {cash.Value:C}", $"Buy {current.Symbol}" );
							base.Logger.LogWarning( $"\t\t{amount:C} BUY {current.Symbol} ({current.Name}) cash: {cash.Value:C}" );
							// assume the buy happens and adjust balances
							amount = Math.Floor( amount );
							cash.Amount -= Math.Floor( amount ); // rough inclusion of fee
							base.Logger.LogInformation( $"\t\tCurrent shares: {current.Amount:N6}" );
							current.Amount += amount / current.Price;
							base.Logger.LogInformation( $"\t\tAdjusted cash: {cash.Amount:C}" );
							base.Logger.LogInformation( $"\t\t{current.Symbol}: {current.Value:C} / {current.Amount:N6}" );
						} else {
							await base.SendNotificationAsync( $"@here {amount:C} BUY  {current.Symbol} ({current.Name}) cash: {cash.Value:C} *** not enough cash ***", $"Buy {current.Symbol} - not enough cash" );
							base.Logger.LogError( $"\t\t{amount:C} BUY {current.Symbol} ({current.Name}) cash: {cash.Value:C} *** not enough cash ***" );
						}
					} else {
						var amount = current.Value - current.NotifiedAt;
						await base.SendNotificationAsync( $"@here {amount:C} SELL {current.Symbol} ({current.Name})", $"Sell {current.Symbol}" );
						base.Logger.LogWarning( $"\t\t{amount:C} SELL {current.Symbol} ({current.Name})" );
						// assume the sell happens and adjust balances
						cash.Amount += Math.Floor( amount ); // rough inclusion of fee
						base.Logger.LogInformation( $"\t\tCurrent shares: {current.Amount:N6}" );
						current.Amount -= amount / current.Price;
						base.Logger.LogInformation( $"\t\tAdjusted cash: {cash.Amount:C}" );
						base.Logger.LogInformation( $"\t\t{current.Symbol}: {current.Value:C} / {current.Amount:N6}" );
					}

					// prevent multiple notifications at roughly the same value
					current.NotifiedAt = current.Value;
				}

				var sum = this.assets.Sum( a => a.Value );
				base.Logger.LogInformation( $"Account balance: {sum,10:C}" );
			} finally {
				lock( this.lockObject )
					this.processing = false;
			}
		}

		private async Task loadInitialData( CancellationToken cancellationToken ) {
			base.Logger.LogInformation( "Loading initial data." );
			var parameters = new LatestQuoteParameters( );
			parameters.Symbols.AddRange( new[ ] { "ATOM", "BTG", "BTC", "ETH", "XRP", "ADA", "ZRX", "BAT", "BZX", "DOGE", "LTC" } );
			var response = await this.client.GetLatestQuoteAsync( parameters, cancellationToken );
			base.Logger.LogInformation( $"{response.Data.Count} symbols to add." );
			CryptoCurrency crypto;
			foreach( var (_, value) in response.Data ) {
				crypto = new CryptoCurrency {
					Symbol = value.Symbol,
					AltSymbol = value.Symbol,
					AddedToExchange = value.DateAdded?.Date,
					ExternalId = value.Id,
					Slug = value.Slug,
					Name = value.Name
				};
				await base.Context.CryptoCurrencies.AddAsync( crypto, cancellationToken );
			}

			// add and exclude USD and UPCO2
			crypto = new CryptoCurrency {
				Symbol = "USD",
				AltSymbol = "USD",
				Slug = "us-dollar",
				Name = "US Dollar",
				Exclude = true
			};
			await base.Context.CryptoCurrencies.AddAsync( crypto, cancellationToken );

			crypto = new CryptoCurrency {
				Symbol = "UPCO2",
				AltSymbol = "UPCO2",
				Slug = "universal-carbon",
				Name = "Universal Carbon",
				Exclude = true
			};
			await base.Context.CryptoCurrencies.AddAsync( crypto, cancellationToken );

			await base.Context.SaveChangesAsync( cancellationToken );
		}

		#endregion

		#region IDisposable

		/// <inheritdoc />
		public void Dispose( ) {
			this.watcher?.StopAsync( CancellationToken.None );
			this.watcher?.Dispose( );
		}

		#endregion
	}
}