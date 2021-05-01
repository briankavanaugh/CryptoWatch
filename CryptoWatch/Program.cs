using System;
using System.Threading.Tasks;
using CryptoWatch.Core.Config;
using CryptoWatch.Entities.Contexts;
using CryptoWatch.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Events;
using Slack.Webhooks;

namespace CryptoWatch {
	static class Program {
		private static async Task Main( string[ ] args ) {
			using var host = AppStartup( args );
			try {
				await host.RunAsync( );
			} catch( Exception ex ) {
				Log.Logger.Error( ex, "Exiting application." );
			} finally {
				Log.Logger.Information( "Closing logs." );
				Log.CloseAndFlush( );
			}
		}

		private static IHost AppStartup( string[ ] args ) =>
			Host.CreateDefaultBuilder( args )
			    .UseSerilog( )
			    .ConfigureAppConfiguration( ( context, configuration ) => {
				    configuration.Sources.Clear( );
				    var env = context.HostingEnvironment;
				    configuration
					    .AddJsonFile( "appSettings.json", true, true )
					    .AddJsonFile( $"appSettings.{env.EnvironmentName}", true, true )
					    .AddEnvironmentVariables( "Crypto_" );
				    var configurationRoot = configuration.Build( );
				    Log.Logger = new LoggerConfiguration( )
				                 .ReadFrom.Configuration( configurationRoot )
				                 .Enrich.FromLogContext( )
				                 .WriteTo.EventLog( "CryptoWatch", manageEventSource: true, restrictedToMinimumLevel: LogEventLevel.Warning )
				                 .CreateLogger( );

				    Log.Logger.Information( "Application Starting" );
			    } )
			    .ConfigureServices( ( context, services ) => {
				    // load configuration sections
				    var general = new GeneralConfiguration( );
				    context.Configuration.Bind( "General", general );
				    services.AddSingleton( general );

				    var cma = new CoinMarketCapConfiguration( );
				    context.Configuration.Bind( "CoinMarketCap", cma );
				    services.AddSingleton( cma );

				    var integrations = new IntegrationsConfiguration( );
				    context.Configuration.Bind( "Integrations", integrations );
				    services.AddSingleton( integrations );
				    Log.Logger.Information( $"Slack notifications enabled: {integrations.SlackEnabled}" );
				    Log.Logger.Information( $"Pushbullet notifications enabled: {integrations.PushbulletEnabled}" );
				    Log.Logger.Information( $"Google Sheets update enabled: {integrations.GoogleSheetsEnabled}" );

				    var slack = new SlackClient( integrations.Slack );
				    services.AddSingleton( slack );

				    var contexts = new ConnectionStringsConfiguration( );
				    context.Configuration.Bind( "ConnectionStrings", contexts );
				    services.AddSingleton( contexts );

					// add database contexts
					services.AddDbContextPool<CryptoContext>( options => options.UseMySql( contexts.Crypto, ServerVersion.AutoDetect( contexts.Crypto ), m => { m.EnableStringComparisonTranslations( ); } ) );

				    // add services
				    services.AddSingleton<WatchService>( );
				    services.AddHostedService<CoinMarketCapService>( );
			    } )
			    .UseConsoleLifetime( )
			    .Build( );
	}
}