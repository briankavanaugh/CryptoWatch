using System;
using System.Threading.Tasks;
using Slack.Webhooks;

namespace CryptoWatch.Core.Utilities {
	public static class SlackExtensions {
		public static async Task SendMessageAsync( this SlackClient client, string msg ) {
#if DEBUG
			await Task.Delay( TimeSpan.FromMilliseconds( 50 ) );
#else
			var message = new SlackMessage { Text = msg, LinkNames = true };
			await client.PostAsync( message );
#endif
		}
	}
}