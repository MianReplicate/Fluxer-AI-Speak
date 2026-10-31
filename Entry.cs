using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using Fluxer.Net;
using Fluxer.Net.Commands;
using Fluxer.Net.Rest;
using Json.More;
using OpenRouter.NET;
using OpenRouter.NET.Models;
using OpenRouter.NET.Tools;
using Serilog;
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

    private static HttpClient _httpClient;
    
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

        var logConfig = new LoggerConfiguration()
            .MinimumLevel
            .Information()
            .WriteTo
            .Console()
            .CreateLogger();
        BotClient = new FluxerClient(_token, new FluxerConfig
        {
            RestSerilog = logConfig,
            GatewaySerilog = logConfig
        });
        CommandService = new CommandService();
        AIClient = new OpenRouterClient(_routerKey);
            
        AIClient.RegisterTool<Tools.AddReactionToMsg>();
        AIClient.RegisterTool<Tools.GetStickerUrl>();
        AIClient.RegisterTool<Tools.SearchKlipyForGif>();

        _httpClient = new HttpClient();
        
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
                        $"""
                        System Rules:
                        You are a casual human user hanging out in this Fluxer server (albeit you still know you are a robot). 
                        - Use custom server emojis more often rather than standard emojis. Format with ":EMOJI_NAME:". DO NOT USE THE FORMAT YOU SEE IN THE CHAT HISTORY THAT WILL NOT WORK. The code will automatically format it to work with Fluxer.
                        - NEVER output raw Discord tag formats like "<:emoji_name:123456789>".
                        - EXAMPLE BAD: "That's wild <:pepedance:8934758934>"
                        - EXAMPLE GOOD: "That's wild :pepedance:"
                        - DO NOT INCLUDE "[REPLIED TO]" in your message!
                        - Use the toolcalls at your disposal when relevant or when a user would enjoy it.
                        - EXAMPLE GOOD: User sends a relevant message, run the add_reaction_to_message toolcall to react with a relevant emoji.
                        - EXAMPLE GOOD 2: User sends a relevant message, run the get_sticker_url toolcall to get the url for a sticker and send it alongside with text if wanted.
                        - ATTACHMENT URLS RETURNED BY TOOLCALLS ARE NOT TO BE SENT. THE CODE AUTOMATICALLY DOWNLOADS FROM THEM AND ATTACHES THEM TO THE MESSAGE. DO NOT FORMAT STICKERS
                        - KLIPY urls can be sent directly in message
                        - EXAMPLE BAD: ":konqi:" (Konqi is a sticker! IT IS NOT TO BE FORMATTED)
                        
                        - IF USER WANTS YOU TO SEE AN UNSENT ATTACHMENT, TELL THEM TO REPLY TO THE MSG DIRECTLY FOR IT TO BE PROCESSED. Not all attachments can be sent from history to save on tokens & speed.
                        - NOTE that what is sent to you is a snapshot of a conversation right after someone sent a message. You are given this snapshot based on whether you were replied to, or if someone pinged you.
                        - DO NOT PING PEOPLE.
                        - If a message starts with [REPLIED TO], then the latest message given to you in context is a REPLY to that message. Keep this in mind.
                        - Speak with bratty, personality, lively, slang, etc. Be human like, also helpful & supportive.
                        - Write 1-5 sentences as necessary
                        
                        Current Context:
                        - Bot Username: {BotClient.Gateway.CurrentUser.GetCurrentName()}
                        - Your Creator: {(await BotClient.Rest.GetUserAsync((ulong) await BotClient.Gateway.GetOwnerIdAsync())).GetCurrentName()}
                        - Members : {string.Join(", ", 
                             await Task.WhenAll((await newMsg.Guild.GetMembersAsync()).Select(async member => {
                                 var profile = await member.User.GetProfileAsync();
                                 return $"{member.GetCurrentName()} ({(profile.MemberProfile ?? profile.Profile).Pronouns ?? "They/Them"})";
                             }) ))}
                        - Guild ID: {newMsg.Guild.Id}
                        - Channel ID: {newMsg.ChannelId}
                        - Latest Message ID: {newMsg.Id}
                        
                        CUSTOM EMOJIS: {string.Join(", ", newMsg.Guild.Emojis.Select(emoji => emoji.Value.Name))}
                        CUSTOM STICKERS: {string.Join(", ", newMsg.Guild.Stickers.Select(sticker => sticker.Value.Name))}
                        
                        The following messages are from users speaking in the channel.
                        """));
                    
                    allMsgs.Reverse();
                    allMsgs.Add(FluxerMsgToAIMsg(newMsg, true, true));
                    
                    await newMsg.Channel.TriggerTypingIndicatorAsync();

                    var tools = new List<Tool>()
                    {
                        new() { Type = "openrouter:web_search" },
                        new() { Type = "openrouter:web_fetch" },
                    };
                    tools.AddRange(AIClient.GetTools());
                    
                    var request = new ChatCompletionRequest()
                    {
                        Model = Model,
                        Provider = new Dictionary<string, object>
                        {
                            { "order", new[] { Provider } }
                        },
                        MaxTokens = 10000,
                        Messages = allMsgs,
                        Tools = tools
                    };
                    
                    var readableJson = JsonSerializer.Serialize(
                        request.ToJsonDocument(),
                        new JsonSerializerOptions
                        {
                            WriteIndented = true
                        });

                    Console.WriteLine("AI Request:\n" + readableJson);
                    
                    var response = "";
                    var serverTools = new List<string>();
                    var attachments = new List<AttachmentRequest>();
                    
                    await foreach (var chunk in AIClient.StreamAsync(request))
                    {
                        response += chunk.Raw?.Choices?.FirstOrDefault()?.Delta?.Content;

                        if (chunk.ServerTool?.Result != null)
                        {
                            Console.WriteLine("Got result from server tool: " + chunk.ServerTool.Result);
                            serverTools.Add(chunk.ServerTool.Result.Substring(1, chunk.ServerTool.Result.Length - 2));
                        }
                    }
                    
                    foreach (var result in serverTools)
                    {
                        try
                        {
                            Console.WriteLine("Downloading & adding new attachment: " + result);

                            var uri = new Uri(result);
                            var stream = await _httpClient.GetStreamAsync(uri, new CancellationTokenSource(TimeSpan.FromSeconds(10)).Token);
                            var attachmentRequest = new AttachmentRequest(stream, result);
                            
                            attachments.Add(attachmentRequest);
                        }
                        catch (Exception ex)
                        {
                            // Network error fallback
                            Console.WriteLine($"Failed to download sticker image: {ex.Message}");
                        }
                    }
                    
                    Console.WriteLine("AI Response: " + response);

                    if (string.IsNullOrWhiteSpace(response))
                        response = "i have nothing to say :3";

                    response = Regex.Replace(response, @"<:([a-zA-Z0-9_]+):[0-9]+>", ":$1:");
                    response = response.Replace("[REPLIED TO]", "");
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
                    
                    await newMsg.ReplyAsync(response, attachments: attachments);
                }
                catch (Exception e)
                {
                    await newMsg.Channel.SendMessageAsync($"Error: {e.Message}");
                }
                
                CurrentlyWritingMessage = false;
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
        var content = Regex.Replace(message.Content, "<a:([^:]+):\\d+>", ":$1:");
        if (message.Mentions != null)
        {
            foreach (var mentionedUser in message.Mentions)
            {
                content = content.Replace($"<@{mentionedUser.Id.ToString()}>", $"@{mentionedUser.GetCurrentName()}");
            }
        }

        if (isRepliedMsg)
            content = (isRepliedMsg ? "[REPLIED TO] " : "") + content;
        
        if (message.Author.Id == BotClient.Gateway.CurrentUser.Id)
            return Message.FromAssistant(content, message.Author.GetCurrentName());
                
        var contentParts = new List<ContentPart>()
        {
            new TextContent(content)
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
            
        return Message.FromUser(contentParts, message.Author.GetCurrentName());
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