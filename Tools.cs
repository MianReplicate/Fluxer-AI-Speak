using Fluxer.Net;
using Fluxer.Net.Rest;
using OpenRouter.NET.Tools;

namespace Fluxer_AI_Speak;

public class Tools
{
    public class AddReactionToMsgParams
    {
        public string ChannelId { get; set; }
        public string MessageId { get; set; }
        public string EmojiName { get; set; }
    }
    
    public class AddReactionToMsg : VoidTool<AddReactionToMsgParams>
    {
        public override string Name => "add_reaction_to_message";
        public override string Description => "Add an appropriate reaction to a message when wanted.";

        protected override void HandleVoid(AddReactionToMsgParams parameters)
        {
            Task.Run(async () =>
            {
                var channel = Entry.BotClient.Gateway.GetChannel(ulong.Parse(parameters.ChannelId));
                var guild = Entry.BotClient.Gateway.GetGuild((ulong)channel.GuildId);
                var emojiEnumerable = guild.Emojis
                    .Where(emoji => emoji.Value.Name.Equals(parameters.EmojiName))
                    .Shuffle();

                var msg = await channel.GetMessageAsync(ulong.Parse(parameters.MessageId));

                if (emojiEnumerable.Any())
                    await msg.AddReactionAsync(emojiEnumerable.First().Value);
                else
                    await msg.AddReactionAsync(parameters.EmojiName);
            });
        }
    }
    
    public class GetStickerUrlParams
    {
        public string GuildId { get; set; }
        public string StickerName { get; set; }
        public string IntStickerSize { get; set; }
    }
    
    public class GetStickerUrl : Tool<GetStickerUrlParams, string>
    {
        public override string Name => "get_sticker_url";
        public override string Description => "Used to retrieve an url for a sticker which is then downloaded and used to send as an attachment into a message. This does not need to be put into the message, it is added automatically as an attachment when the tool runs.";

        protected override string Handle(GetStickerUrlParams parameters)
        {
            var guild = Entry.BotClient.Gateway.GetGuild(ulong.Parse(parameters.GuildId));
            var stickerEnumerable = guild.Stickers
                .Where(sticker => sticker.Value.Name.Equals(parameters.StickerName))
                .Shuffle();

            if (stickerEnumerable.Any())
                return stickerEnumerable.First().Value.GetStickerUrl(int.Parse(parameters.IntStickerSize));
            return "N/A";
        }
    }
    
    public class UpdateBrainParams
    {
        public string Personality { get; set; }
        public string Mood { get; set; }
    }
    
    public class UpdateBrain : VoidTool<UpdateBrainParams>
    {
        public override string Name => "update_brain";
        public override string Description => "Updates your personality (3-5 words separated by commas) and mood (one word) for future requests.";

        protected override void HandleVoid(UpdateBrainParams parameters)
        {
            Entry.Logger.Information($"Updating brain: \nPersonality -> {parameters.Personality}\nMood -> {parameters.Mood}");
            Entry.Brain.Personality = parameters.Personality;
            Entry.Brain.Mood = parameters.Mood;
        }
    }
    
    public class UpdateBotProfileParams
    {
        public string Username { get; set; }
        public string Bio { get; set; }
        public string Pronouns { get; set; }
    }
    
    public class UpdateBotProfile : VoidTool<UpdateBotProfileParams>
    {
        public override string Name => "update_bot_profile";
        public override string Description => "Updates your profile. You can change your username, bio, pronouns (example: she/they)";

        protected override void HandleVoid(UpdateBotProfileParams parameters)
        {
            Entry.Logger.Information($"Editing profile: \nNickname -> {parameters.Username}\nBio -> {parameters.Bio}\nPronouns -> {parameters.Pronouns}");
            
            Task.Run(async () =>
            {
                var guilds = await Entry.BotClient.Rest.GetCurrentUserGuildsAsync();

                foreach (var guild in guilds)
                {
                    await guild.UpdateCurrentMemberAsync(new UpdateCurrentMemberRequest()
                    {
                        Nickname =  parameters.Username,
                        Bio = parameters.Bio,
                        Pronouns = parameters.Pronouns,
                    });
                }
            });
        }
    }
    
    public class SearchKlipyForGifParams
    {
        public string Query { get; set; }
    }
    
    public class SearchKlipyForGif : Tool<SearchKlipyForGifParams, string>
    {
        public override string Name => "search_klipy_gif_url";
        public override string Description => "Queries Klipy for an appropriate gif given a search term and returns the first most relevant result in a url";

        protected override string Handle(SearchKlipyForGifParams parameters)
        {
            var result = Task.Run(() => Entry.BotClient.Rest.SearchGifsAsync(parameters.Query)).GetAwaiter().GetResult();
            return result.First().Url;
        }
    }
}