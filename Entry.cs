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
using Serilog.Core;
using Serilog.Events;
using Message = OpenRouter.NET.Models.Message;

namespace Fluxer_AI_Speak;

public class Entry
{
    private static OpenRouterClient AIClient;
    public static FluxerClient BotClient;
    private static CommandService CommandService;

    public static Brain Brain;

    public static string Model = Environment.GetEnvironmentVariable("OPENROUTER_MODEL");
    public static string Provider = Environment.GetEnvironmentVariable("OPENROUTER_PROVIDER");
    public static int Context;
    
    private static string _token = "Bot " + Environment.GetEnvironmentVariable("FLUXER_TOKEN");
    private static string _apiUrl = Environment.GetEnvironmentVariable("FLUXER_API_URL");
    private static string _routerKey = Environment.GetEnvironmentVariable("OPENROUTER_KEY");
    private static string _klipyKey = Environment.GetEnvironmentVariable("KLIPY_KEY");

    private static bool CurrentlyWritingMessage = false;

    private static HttpClient _httpClient;

    public static Logger Logger = new LoggerConfiguration()
        .MinimumLevel.Information()
        .WriteTo.Console()
        .WriteTo.File("logs/bot-.log", rollingInterval: RollingInterval.Day)
        .CreateLogger();
    
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

        BotClient = new FluxerClient(_token, new FluxerConfig
        {
            RestSerilog = Logger,
            GatewaySerilog = Logger
        });
        Brain = await GetOrCreateBrainAsync();
        CommandService = new CommandService();
        AIClient = new OpenRouterClient(_routerKey);
        
        AIClient.RegisterTool<Tools.AddReactionToMsg>();
        AIClient.RegisterTool<Tools.GetStickerUrl>();
        AIClient.RegisterTool<Tools.UpdateBotProfile>();
        AIClient.RegisterTool<Tools.UpdateBrain>();
        // AIClient.RegisterTool<Tools.SearchKlipyForGif>();

        _httpClient = new HttpClient();

        AppDomain.CurrentDomain.ProcessExit += (_, _) => SaveBrain();
        Console.CancelKeyPress += (_, _) => SaveBrain();

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

                        - Write 1-5 sentences as necessary
                        
                        - You may update your username, bio, status, pronouns and status.
                        
                        - You may alter your personality and mood based on current conversation context or appropriate consistent shifts to reflect how you feel. Your age should also modify part of your personality.
                        - Your text and actions are based on your personality. The mood should be a hard modifier on top of that personality.
                        - EXAMPLE: 
                            - Personality: (Playful, curious, helpful)
                            - Mood: Angry
                            - Text Input from User: Can you help me with something?
                            - Good Example Response: UGH i'm pissed right now but fine ill help you. After this, screw off.
                            - Good Example Response 2: no fuck off im angry
                            - Bad Example Response (does not reflect mood): Yeah sure! Why not :D
                        
                        Current Context:
                        - Bot Username: {BotClient.Gateway.CurrentUser.DisplayName}
                        - Bot Pronouns: {BotClient.Gateway.CurrentUser.Pronouns}
                        - Bot Status: {BotClient.Gateway.CurrentCustomStatus?.Text ?? "N/A"}
                        - Personality: {Brain.Personality}
                        - Mood: {Brain.Mood}
                        - Born: {Brain.DateFirstBoot.ToString("O")}
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

                    using (newMsg.Channel.EnterTypingState())
                    {
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

                        Logger.Information("AI Request:\n" + readableJson);
                        
                        var response = "";
                        var serverTools = new List<string>();
                        var attachments = new List<AttachmentRequest>();
                        
                        await foreach (var chunk in AIClient.StreamAsync(request))
                        {
                            response += chunk.Raw?.Choices?.FirstOrDefault()?.Delta?.Content;

                            if (chunk.ServerTool?.Result != null)
                            {
                                Logger.Information("Got result from server tool: " + chunk.ServerTool.Result);
                                if (chunk.ServerTool.Result.StartsWith("https://"))
                                {
                                    serverTools.Add(chunk.ServerTool.Result.Substring(1, chunk.ServerTool.Result.Length - 2));
                                }
                            }
                        }
                        
                        foreach (var result in serverTools)
                        {
                            try
                            {
                                Logger.Information("Downloading & adding new attachment: " + result);

                                var uri = new Uri(result);
                                var stream = await _httpClient.GetStreamAsync(uri, new CancellationTokenSource(TimeSpan.FromSeconds(10)).Token);
                                var attachmentRequest = new AttachmentRequest(stream, result);
                                
                                attachments.Add(attachmentRequest);
                            }
                            catch (Exception ex)
                            {
                                Logger.Error(ex, "Failed to download sticker image");
                            }
                        }
                        
                        Logger.Information("AI Response: " + response);

                        if (string.IsNullOrWhiteSpace(response))
                            response = "i have nothing to say :3";

                        response = Regex.Replace(response, @"<:([a-zA-Z0-9_]+):[0-9]+>", ":$1:");
                        response = Regex.Replace(response, @"<a:([a-zA-Z0-9_]+):[0-9]+>", ":$1:");

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
                }
                catch (Exception e)
                {
                    await newMsg.Channel.SendMessageAsync($"Error: {e.Message}");
                }
                
                SaveBrain();
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
        var content = Regex.Replace(message.Content, "<a:([a-zA-Z0-9_]+):[0-9]+>", ":$1:");
        content = Regex.Replace(content, "<:([a-zA-Z0-9_]+):[0-9]+>", ":$1:");
        
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

    private static async Task<Brain> GetOrCreateBrainAsync()
    {
        
        var filePath = "brain.json";

        var options = new JsonSerializerOptions 
        { 
            PropertyNameCaseInsensitive = true
        };

        Brain brain = null;
        try
        {
            await using FileStream readStream = File.OpenRead(filePath);
            brain = await JsonSerializer.DeserializeAsync<Brain>(readStream, options);
            Logger.Information("Loaded brain!");
        }
        catch (Exception e)
        {
            Logger.Warning(e, "Failed to get brain, this is okay if this is first boot");
        }
        
        if (brain == null)
            brain = new Brain();
        
        return brain;
    }
    
    private static void SaveBrain()
    {
        Logger.Information("Saving brain!");
        
        var filePath = "brain.json";

        var options = new JsonSerializerOptions 
        { 
            WriteIndented = true
        };

        var json = JsonSerializer.Serialize<Brain>(Brain, options);
        File.WriteAllText(filePath, json);
    }
}