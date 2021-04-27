using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CryptoWatch.Core.Config;
using CryptoWatch.Core.Utilities;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PushBulletSharp.Core;
using PushBulletSharp.Core.Models.Requests;
using Slack.Webhooks;

namespace CryptoWatch.Services {
	public abstract class HostedService : IHostedService {
		#region Member Variables

		private readonly IntegrationsConfiguration config;
		private readonly SlackClient slack;
		private Task executingTask;
		private CancellationTokenSource cts;

		#endregion

		#region Constructor

		protected HostedService(
			ILogger logger,
			IntegrationsConfiguration config,
			SlackClient slack
		) {
			this.Logger = logger;
			this.config = config;
			this.slack = slack;
		}

		#endregion

		#region Properties

		/// <summary>
		/// Returns the name of the service (for logging purposes).
		/// </summary>
		protected abstract string ServiceName { get; }

		protected ILogger Logger { get; }

		protected IntegrationsConfiguration Integrations { get; }

		#endregion

		#region Implementation of IHostedService

		/// <inheritdoc />
		public Task StartAsync( CancellationToken cancellationToken ) {
			this.Logger.LogInformation( $"Starting {this.ServiceName}." );
			// Create a linked token so we can trigger cancellation outside of this token's cancellation
			this.cts = CancellationTokenSource.CreateLinkedTokenSource( cancellationToken );

			// Store the task we're executing
			this.executingTask = this.ExecuteAsync( this.cts.Token );

			this.Logger.LogInformation( $"{this.ServiceName} started." );

			// If the task is completed then return it, otherwise it's running
			return this.executingTask.IsCompleted ? this.executingTask : Task.CompletedTask;
		}

		/// <inheritdoc />
		public async Task StopAsync( CancellationToken cancellationToken ) {
			this.Logger.LogInformation( $"Stopping {this.ServiceName}." );
			if( this.executingTask == null )
				return;

			// signal cancellation to the executing method
			this.cts.Cancel( );

			// wait until the task completes or the stop token triggers
			await Task.WhenAny( this.executingTask, Task.Delay( -1, cancellationToken ) );

			// throw if cancellation triggered
			cancellationToken.ThrowIfCancellationRequested( );
		}

		#endregion

		#region Methods

		/// <summary>
		/// Derived classes should implement this to start the long-running method.
		/// </summary>
		protected abstract Task ExecuteAsync( CancellationToken cancellationToken );

		protected async Task SendNotificationAsync( string message, string title = "CryptoWatch" ) {
			if( this.config.SlackEnabled ) {
				await this.sendSlackNotificationAsync( message );
			}

			if( this.config.PushbulletEnabled ) {
				await this.sendPushbulletNotificationAsync( message, title );
			}
		}

		private async Task sendSlackNotificationAsync( string message ) {
			if( this.config.Slack.Contains( ".slack.com/", StringComparison.OrdinalIgnoreCase ) ) {
				try {
					await this.slack.SendMessageAsync( message );
				} catch( Exception ex ) {
					this.Logger.LogError( ex, "Failed to send Slack message." );
				}
			} else {
				this.Logger.LogError( "Slack notifications are enabled, but an invalid URL is specified. Expecting it to contain 'slack.com/'." );
			}
		}

		private async Task sendPushbulletNotificationAsync( string message, string title ) {
			if( string.IsNullOrWhiteSpace( this.config.PushbulletToken ) ) {
				this.Logger.LogError( "Pushbullet notifications are enabled, but no token was specified." );
				return;
			}

			var client = new PushBulletClient( this.config.PushbulletToken );
			try {
				var user = await client.CurrentUsersInformation( );
				if( user == null ) {
					this.Logger.LogError( "Failed to retrieve the Pushbullet current user." );
				}

				var request = new PushNoteRequest {
					Email = user.Email,
					Title = title,
					Body = message
				};
				await client.PushNote( request );
			} catch( Exception ex ) {
				this.Logger.LogError( ex, "Failed to send Pushbullet notification." );
			}
		}

		#endregion

	}
}