using System.Threading.Tasks;
using Slack.Webhooks;

namespace CryptoWatch.Core.Utilities {
	public static class SlackExtensions {
		public static async Task SendMessageAsync( this SlackClient client, string msg ) {
			var message = new SlackMessage { Text = msg, LinkNames = true };

			await client.PostAsync( message );
		}
	}
}