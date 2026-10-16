using Fluxer.Net;
using OpenRouter.NET;

namespace Fluxer_AI_Speak;

public class Tools
{
    public class SearchUpEmojiParams
    {
        public string GuildId { get; set; }
        public string SearchId { get; set; }
    }
    
    public class SearchUpCustomEmojiID : Tool<SearchUpEmojiParams, ulong>
    {
        public override string Name => "search_custom_emoji_id";
        public override string Description => "Search custom emojis with their name to find its ID so it can be formatted properly.";

        // Handle the execution logic using your local database/Discord cache
        protected override ulong Handle(SearchUpEmojiParams parameters)
        {
            var guild = Entry.BotClient.Gateway.GetGuild(ulong.Parse(parameters.GuildId));

            var emoji = guild.Emojis
                .Where(emoji => emoji.Value.Name.Equals(parameters.SearchId))
                .Shuffle()
                .First()
                .Value
                .Id;

            return emoji;
        }
    }
}