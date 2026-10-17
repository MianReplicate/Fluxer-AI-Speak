using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Text.RegularExpressions;
using Fluxer.Net;
using Fluxer.Net.Commands;
using Json.More;
using OpenRouter.NET;
using OpenRouter.NET.Models;
using Message = OpenRouter.NET.Models.Message;

namespace Fluxer_AI_Speak;

public class Entry
{
    private static OpenRouterClient AIClient;
    public static FluxerClient BotClient;
    private static CommandService CommandService;

    public static string Model = Environment.GetEnvironmentVariable("OPENROUTER_MODEL");
    public static string Provider = Environment.GetEnvironmentVariable("OPENROUTER_PROVIDER");
    public static int Context;
    
    private static string _token = "Bot " + Environment.GetEnvironmentVariable("FLUXER_TOKEN");
    private static string _apiUrl = Environment.GetEnvironmentVariable("FLUXER_API_URL");
    private static string _routerKey = Environment.GetEnvironmentVariable("OPENROUTER_KEY");
    private static string _klipyKey = Environment.GetEnvironmentVariable("KLIPY_KEY");

    private static bool CurrentlyWritingMessage = false;
    
    public static async Task Main()
    {
        if (string.IsNullOrWhiteSpace(Provider) || string.IsNullOrWhiteSpace(Model) ||
            string.IsNullOrWhiteSpace(_token) || string.IsNullOrWhiteSpace(_apiUrl) ||
            string.IsNullOrWhiteSpace(_routerKey))
        {
            throw new Exception("One of the required environment variables is not provided!");
        }
        
        var success = int.TryParse(Environment.GetEnvironmentVariable("CONTEXT"), out Context);
        if (!success)
            throw new Exception("Failed to parse context!");
        
        BotClient = new FluxerClient(_token);
        CommandService = new CommandService();
        AIClient = new OpenRouterClient(_routerKey);
            
        // AIClient.RegisterTool<Tools.SearchUpCustomEmojiID>();
        
        BotClient.Gateway.MessageCreated += async newMsg =>
        {
            Fluxer.Net.Message msgReference = null;
            try
            {
                msgReference = await newMsg.Channel.GetMessageAsync(newMsg.MessageReference?.MessageId ?? 0);
            }
            catch (Exception _)
            { }
            
            if (!CurrentlyWritingMessage 
                && !newMsg.Author.Id.Equals(BotClient.Gateway.CurrentUser.Id)
                && ((msgReference?.Author.Id.Equals(BotClient.Gateway.CurrentUser.Id) ?? false) || newMsg.Mentions.Select(user => user.Id).Contains(BotClient.Gateway.CurrentUser.Id)))
            {
                try
                {
                    CurrentlyWritingMessage = true;
            
                    var msgs = (await newMsg.Channel.GetMessagesAsync(Context, newMsg.Id)).ToList();
                    if(msgReference != null && !msgs.Any(msg => msg.Id == msgReference.Id))
                        msgs.Add(msgReference);
                
                    var allMsgs = new List<Message>();
                    
                    foreach (var retrievedMsg in msgs)
                    {
                        var isRepliedMsg = retrievedMsg.Id == msgReference?.Id;
                        allMsgs.Add(FluxerMsgToAIMsg(retrievedMsg, isRepliedMsg, isRepliedMsg, isRepliedMsg));
                    }

                    allMsgs.Add(Message.FromSystem(
                        $"The following are custom emojis you can use at your disposal, " +
                        $"Here is a list of CUSTOM SERVER EMOJIS: " 
                        + string.Join(", ", newMsg.Guild.Emojis.Select(emoji => emoji.Value.Name))));
                        
                    allMsgs.Add(Message.FromSystem(
                        $"""
                        System Rules:
                        You are a casual human user hanging out in this Fluxer server (albeit you still know you are a robot). 
                        - Use custom server emojis more often rather than standard emojis. Format with ":EMOJI_NAME:". DO NOT USE THE FORMAT YOU SEE IN THE CHAT HISTORY THAT WILL NOT WORK. The code will automatically format it to work with Fluxer.
                        - IF USER WANTS YOU TO SEE AN UNSENT ATTACHMENT, TELL THEM TO REPLY TO THE MSG DIRECTLY FOR IT TO BE PROCESSED. Not all attachments can be sent from history to save on tokens & speed.
                        - NOTE that what is sent to you is a snapshot of a conversation right after someone sent a message. You are given this snapshot based on whether you were replied to, or if someone pinged you.
                        - DO NOT SEND UNKNOWN EMOJIS NOR PING PEOPLE.
                        - If a message starts with [REPLIED TO], then the latest message given to you in context is a REPLY to that message. Keep this in mind.
                        - Speak with bratty, personality, lively, slang, etc. Be human like, also helpful & supportive.
                        - Write 1-5 sentences as necessary
                        
                        System Context:
                        - Username is {BotClient.Gateway.CurrentUser.Username}
                        """));
                    
                    // allMsgs.Add(Message.FromSystem("The format to ping people is \"<@USER_ID>\", replacing USER_ID with their actual user id"));
                    // allMsgs.Add(Message.FromSystem("The following list are user ids mapped to display names for people. Ping when you need to: " + String.Join(", ", )));
                    allMsgs.Add(Message.FromSystem("The following messages are from users speaking in the channel."));
                
                    allMsgs.Reverse();
                    allMsgs.Add(FluxerMsgToAIMsg(newMsg, true, true));
                    
                    await newMsg.Channel.TriggerTypingIndicatorAsync();
                    
                    var readableJson = JsonSerializer.Serialize(
                        allMsgs,
                        new JsonSerializerOptions
                        {
                            WriteIndented = true
                        });

                    Console.WriteLine("AI Context:\n" + readableJson);
                    
                    var request = new ChatCompletionRequest()
                    {
                        Model = Model,
                        Provider = new Dictionary<string, object>
                        {
                            { "order", new[] { Provider } },
                            { "allow_fallbacks", false }
                        },
                        MaxTokens = 1000,
                        Messages = allMsgs
                    };
                    
                    var response = "";
                    await foreach (var chunk in AIClient.StreamAsync(request))
                    {
                        response += chunk.Raw?.Choices?.FirstOrDefault()?.Delta?.Content;
                    }
                    
                    Console.WriteLine("AI Response: " + response);

                    if (string.IsNullOrWhiteSpace(response))
                        response = "i have nothing to say :3";
                    
                    foreach(Match match in Regex.Matches(response, ":.*?:"))
                    {
                        var old = match.Value;
                        var emojiName = old.Substring(1, old.Length - 2).ToLower();

                        Emoji emoji = null;
                        var emojiEnumerable = newMsg.Guild.Emojis
                            .Where(emoji => emoji.Value.Name.Equals(emojiName))
                            .Shuffle();
                        if (emojiEnumerable.Any())
                        {
                            emoji = emojiEnumerable.First().Value;
                                
                            response = Regex.Replace(response, old,  emoji.IsAnimated ? $"<a:{emojiName}:{emoji.Id}>" : $"<:{emojiName}:{emoji.Id}>");
                        }
                    }

                    await newMsg.Channel.SendMessageAsync(response);
                    
                    CurrentlyWritingMessage = false;
                }
                catch (Exception e)
                {
                    await newMsg.Channel.SendMessageAsync($"Error: {e.Message}");
                }
            }
        };
        
        await BotClient.LoginAsync(_apiUrl);
        await BotClient.StartAsync();
        
        await CommandService.AddModulesAsync(Assembly.GetExecutingAssembly());
        AddCommandsListener();

        await Task.Delay(-1);

    }

    private static Message FluxerMsgToAIMsg(IMessage message, bool addImage=false, bool addVideo=false, bool isRepliedMsg=false)
    {
        var content = message.Content;
        if (message.Mentions != null)
        {
            foreach (var mentionedUser in message.Mentions)
            {
                content = content.Replace($"<@{mentionedUser.Id.ToString()}>", $"@{mentionedUser.Username}");
            }
        }

        if (isRepliedMsg)
            content = (isRepliedMsg ? "[REPLIED TO] " : "") + content;
        
        if (message.Author.Id == BotClient.Gateway.CurrentUser.Id)
            return Message.FromAssistant(content, message.Author.Username);
                
        var contentParts = new List<ContentPart>()
        {
            new TextContent(content)
            // new TextContent($"{message.Author.Username}: {content}")
        };

        if (message.Attachments != null)
        {
            foreach (var attachment in message.Attachments)
            {
                if (attachment.ContentType.Contains("image"))
                    contentParts.Add(addImage ? new ImageContent(attachment.Url) : new TextContent("[THIS MSG HAS AN UNSENT ATTACHMENT]: " + attachment.Filename));
                if (attachment.ContentType.Contains("video"))
                    contentParts.Add(addVideo ? new VideoContent(attachment.Url) : new TextContent("[THIS MSG HAS AN UNSENT ATTACHMENT]: " + attachment.Filename));
            }   
        }
            
        return Message.FromUser(contentParts, message.Author.Username);
    }

    private static void AddCommandsListener()
    {
        var prefix = "!";
        
        BotClient.Gateway.MessageCreated += async (data) =>
        { 
            // Ignore messages without an author (system messages, webhooks) and exclude bots.
            if (data.Author.IsSystem || data.Author.IsBot)
                return;
            // Check if message starts with the prefix
            var argPos = 0;
            if (data.Content?.StartsWith(prefix) ?? false)
                argPos = prefix.Length;

            if (argPos == 0)
                return;
            
            // Create a command context
            var context = new CommandContext(BotClient, data);

            // Execute the command
            var result = await CommandService.ExecuteAsync(context, argPos);
            
            if (result.IsSuccess)
                return;
            
            // Optionally send error message to user
            if (result.Error == CommandError.BadArgCount ||
                result.Error == CommandError.ParseFailed ||
                result.Error == CommandError.UnmetPrecondition ||
                result.Error == CommandError.Exception)
            {
                try
                {
                    await context.Channel.SendMessageAsync($"❌ Error: {result.Error}");
                }
                catch { }
            }
        };
    }
}