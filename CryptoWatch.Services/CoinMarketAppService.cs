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

		private bool skipNextUpdate;
		private readonly object skipLock = new( );

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
		protected override string ServiceName { get; } = nameof( CoinMarketCapService );

		public bool SkipNextUpdate {
			get {
				lock( this.skipLock )
					return this.skipNextUpdate;
			}
			set {
				lock( this.skipLock )
					this.skipNextUpdate = value;
			}
		}

		#endregion;

		#region Methods

		protected override async Task ExecuteAsync( CancellationToken cancellationToken ) {
			this.watcher.PriceService = this;
			await this.watcher.StartAsync( cancellationToken );
			this.client = new CoinMarketCapClient( base.CoinMarketCapSettings.ApiKey );
			client.HttpClient.BaseAddress = new Uri( base.CoinMarketCapSettings.BaseUrl );
			while( true ) {
				var now = DateTime.Now;
				if( now.Hour >= base.GeneralSettings.DndStart || now.Hour < base.GeneralSettings.DndEnd ) {
					// don't bother with updates or notifications during do not disturb hours
					base.Logger.LogInformation( "Skipping update." );
				} else {
					if( this.SkipNextUpdate )
						this.SkipNextUpdate = false;
					else
						await ( (IPriceService) this ).UpdateBalances( cancellationToken );
				}

				base.Logger.LogInformation( $"Sleeping for {base.GeneralSettings.SleepInterval} minutes." );
				Console.WriteLine( );
				await Task.Delay( TimeSpan.FromMinutes( base.GeneralSettings.SleepInterval ), cancellationToken );
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

				if( this.assets.Count == 0 ) {
					// should always be at least the cash position, so create that
					await this.initializeAsync( cancellationToken );
					base.Logger.LogWarning( "Database initialized. Drop a transaction file in the watch directory to start the process." );
					return;
				}

				var parameters = new LatestQuoteParameters( );
				parameters.Symbols.AddRange( this.assets.Where( a => !a.Exclude ).Select( s => s.AltSymbol ) );
				var response = await this.client.GetLatestQuoteAsync( parameters, cancellationToken );
				foreach( var (_, value) in response.Data ) {
					var asset = this.assets.First( a => a.AltSymbol.Equals( value.Symbol, StringComparison.OrdinalIgnoreCase ) );
					var price = value.Quote[ base.GeneralSettings.CashSymbol ].Price;
					if( !price.HasValue )
						continue;
					asset.Price = Convert.ToDecimal( price.Value );
				}

				assets = this.assets.OrderByDescending( a => a.Value ).ToList( );
				var cash = this.assets.First( a => a.Symbol.Equals( base.GeneralSettings.CashSymbol ) );
				for( var i = 0; i < this.assets.Count; i++ ) {
					var current = this.assets[ i ];
					base.Logger.LogInformation( !current.Exclude
												   ? $"{current.Value,7:C} | limit: {current.BuyLimit,12:C4}...{current.Price.ToString( "C4" ).PadLeft( 12, '.' )}...{current.SellLimit.ToString( "C4" ).PadLeft( 12, '.' )} | {current.Symbol,-4} ({current.Name})"
												   : $"{current.Value,7:C} | {current.Symbol} ({current.Name})"
											  );
					if( current.Exclude || current.Value > current.BuyBoundary && current.Value < current.SellBoundary )
						continue;

					var showNewlimits = true;
					if( current.Value <= current.BuyBoundary ) {
						var amount = Math.Floor( current.BalanceTarget - current.Value ); // rough inclusion of fee
						if( cash.Value - amount > base.GeneralSettings.CashFloor ) {
							await base.SendNotificationAsync( $"@here {amount:C} BUY  {current.Symbol} ({current.Name}) cash: {cash.Value:C}", $"Buy {current.Symbol}" );
							base.Logger.LogWarning( $"\t\t{amount:C} BUY {current.Symbol} ({current.Name}) cash: {cash.Value:C}" );
							// assume the buy happens and adjust balances
							cash.Amount -= amount;
							base.Logger.LogInformation( $"\t\tCurrent shares: {current.Amount:N6}" );
							current.Amount += amount / current.Price;
							base.Logger.LogInformation( $"\t\tAdjusted cash: {cash.Amount:C}" );
							base.Logger.LogInformation( $"\t\t{current.Symbol}: {current.Value:C} / {current.Amount:N6}" );
						} else {
							if( !current.DisableNotifications )
								await base.SendNotificationAsync( $"@here {amount:C} BUY  {current.Symbol} ({current.Name}) cash: {cash.Value:C} *** not enough cash ***", $"Buy {current.Symbol} - not enough cash" );
							base.Logger.LogError( $"\t\t{amount:C} BUY {current.Symbol} ({current.Name}) cash: {cash.Value:C} *** not enough cash ***" );
							current.DisableNotifications = true;
							showNewlimits = false;
						}
					} else {
						var amount = Math.Floor( current.Value - current.BalanceTarget ); // rough inclusion of fee
						await base.SendNotificationAsync( $"@here {amount:C} SELL {current.Symbol} ({current.Name})", $"Sell {current.Symbol}" );
						base.Logger.LogWarning( $"\t\t{amount:C} SELL {current.Symbol} ({current.Name})" );
						// assume the sell happens and adjust balances
						cash.Amount += amount;
						base.Logger.LogInformation( $"\t\tCurrent shares: {current.Amount:N6}" );
						current.Amount -= amount / current.Price;
						base.Logger.LogInformation( $"\t\tAdjusted cash: {cash.Amount:C}" );
						base.Logger.LogInformation( $"\t\t{current.Symbol}: {current.Value:C} / {current.Amount:N6}" );
					}
					if( showNewlimits )
						base.Logger.LogInformation( $"        | new:   {current.BuyLimit,12:C4}...{current.Price.ToString( "C4" ).PadLeft( 12, '.' )}...{current.SellLimit.ToString( "C4" ).PadLeft( 12, '.' )} |" );
				}

				var sum = this.assets.Sum( a => a.Value );
				base.Logger.LogInformation( $"Account balance: {sum,10:C}" );
			} finally {
				lock( this.lockObject )
					this.processing = false;
			}
		}

		private async Task initializeAsync( CancellationToken cancellationToken ) {
			base.Logger.LogInformation( "Initializing database." );

			// add and exclude cash
			var crypto = new CryptoCurrency {
				Symbol = base.GeneralSettings.CashSymbol,
				AltSymbol = base.GeneralSettings.CashSymbol,
				Slug = base.GeneralSettings.CashSlug,
				Name = base.GeneralSettings.CashName,
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