using Fluxer.Net;
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
}