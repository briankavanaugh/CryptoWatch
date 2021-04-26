using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CoinMarketCap;
using CoinMarketCap.Models.Cryptocurrency;
using CryptoWatch.Core.Config;
using CryptoWatch.Core.Utilities;
using CryptoWatch.Entities.Contexts;
using CryptoWatch.Entities.Domains;
using CryptoWatch.Services.Mappers;
using CryptoWatch.Services.Models;
using CsvHelper;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Slack.Webhooks;

namespace CryptoWatch.Services {
	public sealed class WatchService : HostedService, IDisposable {
		#region Member Variables

		private readonly GeneralConfiguration generalConfig;
		private readonly CoinMarketCapConfiguration cmcConfig;
		private readonly CryptoContext context;
		private readonly SlackClient slack;

		private FileSystemWatcher watcher;
		private CoinMarketCapClient client;
		private readonly object processingLock = new( );
		private readonly object changedLock = new( );
		private bool changed;

		#endregion

		#region Constructor

		public WatchService(
			ILogger<WatchService> logger,
			GeneralConfiguration generalConfig,
			CoinMarketCapConfiguration cmcConfig,
			CryptoContext context,
			SlackClient slack ) : base( logger ) {
			this.generalConfig = generalConfig;
			this.cmcConfig = cmcConfig;
			this.context = context;
			this.slack = slack;
		}

		#endregion

		#region Properties

		/// <inheritdoc />
		protected override string ServiceName { get; } = nameof(WatchService);

		public bool Processing { get; private set; }

		public Guid InstanceId { get; } = Guid.NewGuid( );

		public bool Changed {
			get {
				lock( this.changedLock )
					return this.changed;
			}
			set {
				lock( this.changedLock )
					this.changed = value;
			}
		}

		#endregion;

		#region Event Handlers

		private async Task OnChangedAsync( FileSystemEventArgs e, CancellationToken cancellationToken ) {
			// can be multiple calls, so only process one
			if( e.ChangeType != WatcherChangeTypes.Changed || this.Processing )
				return;
			lock( this.processingLock )
				this.Processing = true;
			base.Logger.LogInformation( $"Changed: {e.FullPath}" );
			base.Logger.LogInformation( "Sleeping five seconds to ensure file is fully written." );
			await Task.Delay( TimeSpan.FromSeconds( 5 ), cancellationToken );
			try {
				await this.processFileAsync( e.FullPath, cancellationToken );
			} catch( Exception ex ) {
				base.Logger.LogError( ex, "Failed to process file" );
			} finally {
				lock( this.processingLock )
					this.Processing = false;
			}
		}

		#endregion

		#region Methods

		protected override async Task ExecuteAsync( CancellationToken cancellationToken ) {
			this.Logger.LogInformation( $"{this.ServiceName} instance: {this.InstanceId}" );
			this.client = new CoinMarketCapClient( this.cmcConfig.ApiKey );
			client.HttpClient.BaseAddress = new Uri( this.cmcConfig.BaseUrl );
			base.Logger.LogInformation( $"Starting file watcher for {this.generalConfig.WatchDirectory}" );
			// create directory if it doesn't exist
			Directory.CreateDirectory( this.generalConfig.WatchDirectory );
			this.watcher = new FileSystemWatcher( this.generalConfig.WatchDirectory ) {
				NotifyFilter = NotifyFilters.Attributes
				               | NotifyFilters.CreationTime
				               | NotifyFilters.DirectoryName
				               | NotifyFilters.FileName
				               | NotifyFilters.LastAccess
				               | NotifyFilters.LastWrite
				               | NotifyFilters.Security
				               | NotifyFilters.Size,
				Filter = "*.csv",
				IncludeSubdirectories = false,
				EnableRaisingEvents = true
			};
			watcher.Changed += async ( _, e ) => await OnChangedAsync( e, cancellationToken );

			await Task.Delay( TimeSpan.FromSeconds( 5 ), cancellationToken );
		}

		private async Task processFileAsync( string fileName, CancellationToken cancellationToken ) {
			var fileTransactions = this.readFile( fileName );
			var newTransactions = fileTransactions.Where( ft => !this.context.Transactions.Any( t => t.ExternalId.Equals( ft.Id, StringComparison.OrdinalIgnoreCase ) ) ).ToList( );
			await this.slack.SendMessageAsync( $"@here {fileName} modified: {fileTransactions.Count} transactions, {newTransactions.Count} new" );
			List<CryptoCurrency> assets;
			try {
				assets = await this.createAssetsAsync( newTransactions, cancellationToken );
			} catch( Exception ex ) {
				this.Logger.LogError( ex, "Failed to load assets" );
				await this.slack.SendMessageAsync( $@"here failed to load assets: {ex.Message}" );
				return;
			}

			var count = 0;
			for( var i = 0; i < newTransactions.Count; i++ ) {
				// anything that is a transfer (buy/sell) creates two transactions (destination = buy, origin = sell)
				// anything that is an in (transfer in) creates a buy (destination)
				// anything that is an out (transfer out) creates a sell (origin)
				var current = newTransactions[ i ];
				switch( current.Type.ToUpperInvariant( ) ) {
					case "TRANSFER": {
						var intx = new Transaction {
							Amount = current.DestinationAmount,
							CryptoCurrencyId = assets.First( a => a.Symbol.Equals( current.DestinationCurrency, StringComparison.OrdinalIgnoreCase ) ).Id,
							Destination = current.Destination,
							ExternalId = current.Id,
							Origin = current.Origin,
							Status = current.Status,
							Type = current.Type,
							TransactionDate = current.Date
						};
						await this.context.Transactions.AddAsync( intx, cancellationToken );

						var outtx = new Transaction {
							Amount = current.OriginAmount * -1m,
							CryptoCurrencyId = assets.First( a => a.Symbol.Equals( current.OriginCurrency, StringComparison.OrdinalIgnoreCase ) ).Id,
							Destination = current.Destination,
							ExternalId = current.Id,
							Origin = current.Origin,
							Status = current.Status,
							Type = current.Type,
							TransactionDate = current.Date
						};
						await this.context.Transactions.AddAsync( outtx, cancellationToken );
						count += 2;

						break;
					}
					case "IN": {
						var intx = new Transaction {
							Amount = current.DestinationAmount,
							CryptoCurrencyId = assets.First( a => a.Symbol.Equals( current.DestinationCurrency, StringComparison.OrdinalIgnoreCase ) ).Id,
							Destination = current.Destination,
							ExternalId = current.Id,
							Origin = current.Origin,
							Status = current.Status,
							Type = current.Type,
							TransactionDate = current.Date
						};
						await this.context.Transactions.AddAsync( intx, cancellationToken );
						count++;

						break;
					}
					case "OUT": {
						var outtx = new Transaction {
							Amount = current.OriginAmount * -1m,
							CryptoCurrencyId = assets.First( a => a.Symbol.Equals( current.OriginCurrency, StringComparison.OrdinalIgnoreCase ) ).Id,
							Destination = current.Destination,
							ExternalId = current.Id,
							Origin = current.Origin,
							Status = current.Status,
							Type = current.Type,
							TransactionDate = current.Date
						};
						await this.context.Transactions.AddAsync( outtx, cancellationToken );
						count++;

						break;
					}
				}
			}

			await this.context.SaveChangesAsync( cancellationToken );
			if( count > 0 )
				this.Changed = true;
			this.Logger.LogInformation( $"{count} transactions saved to database." );
		}

		private async Task<List<CryptoCurrency>> createAssetsAsync( IReadOnlyCollection<FileTransaction> transactions, CancellationToken cancellationToken ) {
			var assets = await this.context.CryptoCurrencies.ToListAsync( cancellationToken );
			var symbols = transactions.Select( t => new { Symbol = t.DestinationCurrency } ).Union( transactions.Select( t => new { Symbol = t.OriginCurrency } ) ).Distinct( );
			var newSymbols = symbols.Where( s => !assets.Any( a => a.Symbol.Equals( s.Symbol, StringComparison.OrdinalIgnoreCase ) ) ).Select( sym => sym.Symbol ).ToList( );
			if( !newSymbols.Any( ) )
				return assets;

			this.Logger.LogInformation( $"New symbols to retrieve: {string.Join( ",", newSymbols )}" );
			var parameters = new LatestQuoteParameters( );
			parameters.Symbols.AddRange( newSymbols );
			var response = await this.client.GetLatestQuoteAsync( parameters, cancellationToken );

			base.Logger.LogInformation( $"{response.Data.Count} symbols to add." );
			foreach( var crypto in from CryptocurrencyWithLatestQuote value in response.Data
			                       select new CryptoCurrency {
				                       Symbol = value.Symbol,
				                       AltSymbol = value.Symbol,
				                       AddedToExchange = value.DateAdded?.Date,
				                       ExternalId = value.Id,
				                       Slug = value.Slug,
				                       Name = value.Name
			                       } ) {
				await this.context.CryptoCurrencies.AddAsync( crypto, cancellationToken );
			}

			await this.context.SaveChangesAsync( cancellationToken );

			// reload and return assets
			return await this.context.CryptoCurrencies.ToListAsync( cancellationToken );
		}

		private List<FileTransaction> readFile( string fileName ) {
			using var reader = new StreamReader( fileName, Encoding.Default );
			using var csv = new CsvReader( reader, CultureInfo.InvariantCulture );
			csv.Context.RegisterClassMap<FileTransactionMap>( );

			return csv.GetRecords<FileTransaction>( ).ToList( );
		}

		#endregion

		#region IDisposable

		/// <inheritdoc />
		public void Dispose( ) {
			this.watcher?.Dispose( );
		}

		#endregion
	}
}