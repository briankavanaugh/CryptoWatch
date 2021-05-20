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
using CryptoWatch.Entities.Contexts;
using CryptoWatch.Entities.Domains;
using CryptoWatch.Services.Mappers;
using CryptoWatch.Services.Models;
using CsvHelper;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Services;
using Google.Apis.Sheets.v4;
using Google.Apis.Sheets.v4.Data;
using Google.Apis.Util.Store;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Slack.Webhooks;

namespace CryptoWatch.Services {
	public sealed class WatchService : HostedService, IDisposable {
		#region Member Variables

		private FileSystemWatcher watcher;
		private CoinMarketCapClient client;
		private readonly object processingLock = new( );
		private readonly object changedLock = new( );
		private readonly object sheetsLock = new( );
		private bool changed;

		static readonly string[ ] scopes = { SheetsService.Scope.Spreadsheets };

		#endregion

		#region Constructor

		public WatchService(
			ILogger<WatchService> logger,
			GeneralConfiguration generalConfig,
			CoinMarketCapConfiguration cmcConfig,
			IntegrationsConfiguration intConfig,
			CryptoContext context,
			SlackClient slack ) : base( logger, context, intConfig, generalConfig, cmcConfig, slack ) { }

		#endregion

		#region Properties

		/// <inheritdoc />
		protected override string ServiceName { get; } = nameof(WatchService);

		public bool Processing { get; private set; }

		public bool UpdatingSheets { get; private set; }

		public IPriceService PriceService { get; set; }

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
				var fileTransactions = await this.processFileAsync( e.FullPath, cancellationToken );
				await this.updateGoogleSheets( fileTransactions, cancellationToken );
				if( this.PriceService != null ) {
					this.PriceService.SkipNextUpdate = true;
					lock( this.processingLock )
						this.Processing = false;
					await this.PriceService.UpdateBalances( cancellationToken );
				}
			} catch( Exception ex ) {
				base.Logger.LogError( ex, "Failed to process file" );
			} finally {
				lock( this.processingLock )
					this.Processing = false;
				lock( this.sheetsLock )
					this.UpdatingSheets = false;
			}
		}

		#endregion

		#region Methods

		protected override async Task ExecuteAsync( CancellationToken cancellationToken ) {
			this.client = new CoinMarketCapClient( base.CoinMarketCapSettings.ApiKey );
			client.HttpClient.BaseAddress = new Uri( base.CoinMarketCapSettings.BaseUrl );
			base.Logger.LogInformation( $"Starting file watcher for {base.GeneralSettings.WatchDirectory}" );
			// create directory if it doesn't exist
			Directory.CreateDirectory( base.GeneralSettings.WatchDirectory );
			this.watcher = new FileSystemWatcher( base.GeneralSettings.WatchDirectory ) {
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

		private async Task<List<FileTransaction>> processFileAsync( string fileName, CancellationToken cancellationToken ) {
			var fileTransactions = this.readFile( fileName );
			var newTransactions = fileTransactions.Where( ft => !base.Context.Transactions.Any( t => t.ExternalId.Equals( ft.Id, StringComparison.OrdinalIgnoreCase ) ) ).ToList( );
			base.Logger.LogInformation( $"Transactions loaded: {fileTransactions.Count} transactions, {newTransactions.Count} new" );
			List<CryptoCurrency> assets;
			try {
				assets = await this.createAssetsAsync( newTransactions, cancellationToken );
			} catch( Exception ex ) {
				this.Logger.LogError( ex, "Failed to load assets" );
				return fileTransactions;
			}

			var @new = new List<Transaction>( );
			for( var i = 0; i < newTransactions.Count; i++ ) {
				// anything that is a transfer (buy/sell) creates two transactions (destination = buy, origin = sell)
				// anything that is an in (transfer in) creates a buy (destination)
				// anything that is an out (transfer out) creates a sell (origin)
				var current = newTransactions[ i ];
				switch( current.Type.ToUpperInvariant( ) ) {
					case "TRANSFER": {
						var intx = new Transaction {
							Amount = current.DestinationAmount,
							CryptoCurrency = assets.First( a => a.Symbol.Equals( current.DestinationCurrency, StringComparison.OrdinalIgnoreCase ) ),
							Destination = current.Destination,
							ExternalId = current.Id,
							Origin = current.Origin,
							Status = current.Status,
							Type = current.Type,
							TransactionDate = current.Date
						};
						@new.Add( intx );

						var outtx = new Transaction {
							Amount = current.OriginAmount * -1m,
							CryptoCurrency = assets.First( a => a.Symbol.Equals( current.OriginCurrency, StringComparison.OrdinalIgnoreCase ) ),
							Destination = current.Destination,
							ExternalId = current.Id,
							Origin = current.Origin,
							Status = current.Status,
							Type = current.Type,
							TransactionDate = current.Date
						};
						@new.Add( outtx );

						break;
					}
					case "IN": {
						var intx = new Transaction {
							Amount = current.DestinationAmount,
							CryptoCurrency = assets.First( a => a.Symbol.Equals( current.DestinationCurrency, StringComparison.OrdinalIgnoreCase ) ),
							Destination = current.Destination,
							ExternalId = current.Id,
							Origin = current.Origin,
							Status = current.Status,
							Type = current.Type,
							TransactionDate = current.Date
						};
						@new.Add( intx );

						break;
					}
					case "OUT": {
						var outtx = new Transaction {
							Amount = current.OriginAmount * -1m,
							CryptoCurrency = assets.First( a => a.Symbol.Equals( current.OriginCurrency, StringComparison.OrdinalIgnoreCase ) ),
							Destination = current.Destination,
							ExternalId = current.Id,
							Origin = current.Origin,
							Status = current.Status,
							Type = current.Type,
							TransactionDate = current.Date
						};
						@new.Add( outtx );

						break;
					}
				}
			}

			await base.Context.Transactions.AddRangeAsync( @new, cancellationToken );
			await base.Context.SaveChangesAsync( cancellationToken );
			if( @new.Count > 0 )
				this.Changed = true;
			this.Logger.LogInformation( $"{@new.Count} transactions saved to database:" );
			for( var i = 0; i < @new.Count; i++ ) {
				this.Logger.LogInformation( @new[ i ].CryptoCurrency.Symbol.Equals( base.GeneralSettings.CashSymbol, StringComparison.OrdinalIgnoreCase )
					                           ? $"\t\t{@new[ i ].CryptoCurrency.Symbol,-4} {@new[ i ].Amount:C}"
					                           : $"\t\t{@new[ i ].CryptoCurrency.Symbol,-4} {@new[ i ].Amount:N6}" );
			}

			return fileTransactions;
		}

		private async Task<List<CryptoCurrency>> createAssetsAsync( IReadOnlyCollection<FileTransaction> transactions, CancellationToken cancellationToken ) {
			var assets = await base.Context.CryptoCurrencies.ToListAsync( cancellationToken );
			var symbols = transactions.Select( t => new { Symbol = t.DestinationCurrency } ).Union( transactions.Select( t => new { Symbol = t.OriginCurrency } ) ).Distinct( );
			var newSymbols = symbols.Where( s => !assets.Any( a => a.Symbol.Equals( s.Symbol, StringComparison.OrdinalIgnoreCase ) ) ).Select( sym => sym.Symbol ).ToList( );
			if( !newSymbols.Any( ) )
				return assets;

			this.Logger.LogInformation( $"New symbols to retrieve: {string.Join( ",", newSymbols )}" );
			var parameters = new LatestQuoteParameters( );
			parameters.Symbols.AddRange( newSymbols );
			var response = await this.client.GetLatestQuoteAsync( parameters, cancellationToken );

			base.Logger.LogInformation( $"{response.Data.Count} symbols to add." );
			foreach( var (_, value) in response.Data ) {
				var crypto = new CryptoCurrency {
					Symbol = value.Symbol,
					AltSymbol = value.Symbol,
					AddedToExchange = value.DateAdded?.Date,
					ExternalId = value.Id,
					Slug = value.Slug,
					Name = value.Name
				};
				await base.Context.CryptoCurrencies.AddAsync( crypto, cancellationToken );
			}

			await base.Context.SaveChangesAsync( cancellationToken );

			// reload and return assets
			return await base.Context.CryptoCurrencies.ToListAsync( cancellationToken );
		}

		private List<FileTransaction> readFile( string fileName ) {
			using var reader = new StreamReader( fileName, Encoding.Default );
			using var csv = new CsvReader( reader, CultureInfo.InvariantCulture );
			csv.Context.RegisterClassMap<FileTransactionMap>( );

			return csv.GetRecords<FileTransaction>( ).ToList( );
		}

		private async Task updateGoogleSheets( List<FileTransaction> transactions, CancellationToken cancellationToken ) {
			if( !base.IntegrationSettings.GoogleSheetsEnabled ) {
				base.Logger.LogDebug( "Google Sheets not enabled." );
				return;
			}

			if( string.IsNullOrWhiteSpace( base.IntegrationSettings.GoogleSheetsId ) ) {
				base.Logger.LogError( "Google Sheets integration is enabled, but spreadsheet ID is not set." );
				return;
			}

			while( true ) {
				var updating = false;
				lock( this.sheetsLock ) {
					if( UpdatingSheets )
						updating = true;
				}

				if( !updating )
					break;
				this.Logger.LogInformation( "Google Sheets currently being updated. Sleeping five seconds." );
				await Task.Delay( TimeSpan.FromSeconds( 5 ), cancellationToken );
			}

			var service = await this.initializeSheetsAsync( cancellationToken );
			if( service == null )
				return;

			lock( this.sheetsLock )
				this.UpdatingSheets = true; // we don't want multiple updates running

			transactions = transactions.OrderBy( t => t.Date ).ToList( );
			var list = await this.processAsync( transactions, cancellationToken );
			// cash has different processing requirements than the others
			var cash = this.processCash(transactions );
			list.Add( cash );
			base.Logger.LogInformation( "Starting batch update" );
			var requestBody = new BatchUpdateValuesRequest { ValueInputOption = "USER_ENTERED", Data = list };
			var request = service.Spreadsheets.Values.BatchUpdate( requestBody, base.IntegrationSettings.GoogleSheetsId );
			await request.ExecuteAsync( cancellationToken );
			base.Logger.LogInformation( "Finished updating Google Sheets." );
		}

		private ValueRange processCash( IReadOnlyList<FileTransaction> transactions ) {
			var values = new List<IList<object>>( );
			var row = 1;
			// process cash sheet
			for( var i = 0; i < transactions.Count; i++ ) {
				var current = transactions[ i ];
				var formula = i == 0 ? $"=B{row}" : $"=B{row}+C{row - 1}";
				switch( current.Type.ToUpperInvariant( ) ) {
					case "TRANSFER": {
						// buy/sell
						values.Add( current.OriginCurrency.Equals( base.GeneralSettings.CashSymbol, StringComparison.OrdinalIgnoreCase )
							           ? new List<object> { current.Date.ToShortDateString( ), $"{current.OriginAmount * -1m:C}", formula, $"Buy {current.DestinationCurrency}" }
							           : new List<object> { current.Date.ToShortDateString( ), $"{current.DestinationAmount:C}", formula, $"Sell {current.OriginCurrency}" } );
						row++;
						break;
					}
					case "IN": {
						// transfer into Uphold
						// only care about cash - transfers will have same currency on both sides
						if( !current.OriginCurrency.Equals( base.GeneralSettings.CashSymbol, StringComparison.OrdinalIgnoreCase ) )
							continue;
						values.Add( new List<object> { current.Date.ToShortDateString( ), $"{current.DestinationAmount:C}", formula, $"In {current.OriginCurrency}" } );
						row++;
						break;
					}
					case "OUT": {
						// transfer out of Uphold
						// only care about cash - transfers will have same currency on both sides
						if( !current.OriginCurrency.Equals( base.GeneralSettings.CashSymbol, StringComparison.OrdinalIgnoreCase ) )
							continue;
						values.Add( new List<object> { current.Date.ToShortDateString( ), $"{current.DestinationAmount * -1m:C}", formula, $"Out {current.OriginCurrency}" } );
						row++;
						break;
					}
				}
			}

			base.Logger.LogInformation( $"{row - 1} {base.GeneralSettings.CashSymbol} transactions generated" );

			return new ValueRange { Range = $"{base.GeneralSettings.CashSymbol}!A1:D", Values = values };
		}

		private async Task<List<ValueRange>> processAsync( IReadOnlyCollection<FileTransaction> transactions, CancellationToken cancellationToken ) {
			// only process those with a balance
			var balances = await base.Context.Balances.Where( b => !b.Exclude ).ToListAsync( cancellationToken ); // cash is excluded and we handle that differently, anyway
			var data = new List<ValueRange>( );
			for( var i = 0; i < balances.Count; i++ ) {
				var asset = balances[ i ];
				var selected = transactions.Where( t => t.OriginCurrency.Equals( asset.Symbol, StringComparison.OrdinalIgnoreCase ) || t.DestinationCurrency.Equals( asset.Symbol, StringComparison.OrdinalIgnoreCase ) ).ToList( );
				var balanceValues = new List<IList<object>>( );
				var buyValues = new List<IList<object>>( );
				var sellValues = new List<IList<object>>( );
				int balanceCount = 1, buyCount = 1, sellCount = 1;
				base.Logger.LogInformation( $"Processing {selected.Count} transactions for {asset.Symbol}" );
				foreach( var current in selected ) {
					// header row, so things start on line 2
					var balancePrice = $"=C{balanceCount + 1}/D{balanceCount + 1}";
					var balanceShares = balanceCount == 1 ? "=D2" : $"=D{balanceCount + 1}+E{balanceCount}";
					var buyPrice = $"=I{buyCount + 1}/J{buyCount + 1}";
					var sellPrice = $"=N{sellCount + 1}/O{sellCount + 1}";
					switch( current.Type.ToUpperInvariant( ) ) {
						case "TRANSFER": {
							// buy/sell
							if( current.OriginCurrency.Equals( base.GeneralSettings.CashSymbol, StringComparison.OrdinalIgnoreCase ) ) {
								// selling cash, buying whatever
								balanceValues.Add( new List<object> { current.Date.ToShortDateString( ), balancePrice, $"={current.OriginAmount:N2}", current.DestinationAmount, balanceShares } );
								buyValues.Add( new List<object> { current.Date.ToShortDateString( ), buyPrice, $"={current.OriginAmount:N2}", current.DestinationAmount } );
								balanceCount++;
								buyCount++;

							} else if( current.DestinationCurrency.Equals( base.GeneralSettings.CashSymbol, StringComparison.OrdinalIgnoreCase ) ) {
								// raising cash, selling whatever
								balanceValues.Add( new List<object> { current.Date.ToShortDateString( ), balancePrice, $"={current.DestinationAmount * -1m:N2}", current.OriginAmount * -1m, balanceShares } );
								sellValues.Add( new List<object> { current.Date.ToShortDateString( ), sellPrice, $"={current.DestinationAmount * -1m:N2}", current.OriginAmount * -1m } );
								balanceCount++;
								sellCount++;
							} else if( current.OriginCurrency.Equals( asset.Symbol, StringComparison.OrdinalIgnoreCase ) ) {
								// selling whatever, buying non-cash (won't have an actual price I can calculate)
								balanceValues.Add( new List<object> { current.Date.ToShortDateString( ), string.Empty, string.Empty, current.OriginAmount * -1m, balanceShares } );
								balanceCount++;
							} else {
								// buying whatever, selling non-cash (won't have an actual price I can calculate)
								balanceValues.Add( new List<object> { current.Date.ToShortDateString( ), string.Empty, string.Empty, current.DestinationAmount, balanceShares } );
								balanceCount++;
							}

							break;
						}
						case "IN": {
							// transfer in from something else (probably BAT - Brave browser)
							balanceValues.Add( new List<object> { current.Date.ToShortDateString( ), string.Empty, string.Empty, current.DestinationAmount, balanceShares } );
							balanceCount++;
							break;
						}
						case "OUT": {
							// transfer out to something else (probably BAT - Brave browser)
							balanceValues.Add( new List<object> { current.Date.ToShortDateString( ), string.Empty, string.Empty, current.OriginAmount * -1, balanceShares } );
							balanceCount++;
							break;
						}
					}
				}

				ValueRange valueRange;
				if( balanceValues.Any( ) ) {
					// all transactions
					base.Logger.LogInformation( $"Writing {balanceValues.Count} transactions for {asset.Symbol}" );
					valueRange = new ValueRange { Range = $"{asset.Symbol}!A2:E", Values = balanceValues };
					data.Add( valueRange );
				}

				if( buyValues.Any( ) ) {
					base.Logger.LogInformation( $"Writing {buyValues.Count} buy transactions for {asset.Symbol}" );
					valueRange = new ValueRange { Range = $"{asset.Symbol}!G2:J", Values = buyValues };
					data.Add( valueRange );
				}

				if( !sellValues.Any( ) )
					continue;
				base.Logger.LogInformation( $"Writing {sellValues.Count} sell transactions for {asset.Symbol}" );
				valueRange = new ValueRange { Range = $"{asset.Symbol}!L2:O", Values = sellValues };
				data.Add( valueRange );
			}

			return data;
		}

		private async Task<SheetsService> initializeSheetsAsync( CancellationToken cancellationToken ) {
			if( !File.Exists( "credentials.json" ) ) {
				base.Logger.LogError( "The file credentials.json is missing from the executing directory. Google Sheets cannot be updated." );
				return null;
			}

			try {
				await using var stream = new FileStream( "credentials.json", FileMode.Open, FileAccess.Read );
				// The file token.json stores the user's access and refresh tokens, and is created
				// automatically when the authorization flow completes for the first time.
				const string credPath = "token.json";
				var credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(
				                                                                   GoogleClientSecrets.Load( stream ).Secrets,
				                                                                   scopes,
				                                                                   "user",
				                                                                   cancellationToken,
				                                                                   new FileDataStore( credPath, true ) );
				base.Logger.LogInformation( $"Credential file saved to: {credPath}" );

				return new SheetsService( new BaseClientService.Initializer {
					HttpClientInitializer = credential,
					ApplicationName = "CryptoWatch"
				} );
			} catch( Exception ex ) {
				base.Logger.LogError( ex, "Failed to initialize service." );
				return null;
			}
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