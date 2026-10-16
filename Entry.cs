using System.Reflection;
using Fluxer.Net;
using Fluxer.Net.Commands;
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
        AIClient.RegisterTool<Tools.SearchUpCustomEmojiID>();
        
        BotClient.Gateway.MessageCreated += async msg =>
        {
            IMessage msgReference = null;
            try
            {
                msgReference = await msg.Channel.GetMessageAsync(msg.MessageReference?.MessageId ?? 0);
            }
            catch (Exception _)
            { }
            
            if (!CurrentlyWritingMessage 
                && !msg.Author.Id.Equals(BotClient.Gateway.CurrentUser.Id)
                && !msg.ChannelId.Equals(1558317357053509635)
                // && ((msgReference?.Author.Id.Equals(BotClient.Gateway.CurrentUser.Id) ?? false) || msg.Mentions.Select(user => user.Id).Contains(BotClient.Gateway.CurrentUser.Id)))
            ){
                try
                {
                    CurrentlyWritingMessage = true;
            
                    var msgs = await msg.Channel.GetMessagesAsync(Context, msg.Id);
                
                    var allMsgs = new List<Message>();
            
                    foreach (var retrievedMsg in msgs)
                    {
                        allMsgs.Add(retrievedMsg.Author.Id.Equals(BotClient.Gateway.CurrentUser.Id) ? Message.FromAssistant(retrievedMsg.Content) : FluxerMsgToAIUserMsg(retrievedMsg));
                    } 
                    
                    allMsgs.Add(Message.FromSystem(
                        $"The following are custom emojis you can use at your disposal, formatted with \"<:EMOJI_NAME:EMOJI_ID:>\" with an 'a' before the first colon if it is animated. USE THE SEARCH EMOJIS TOOL TO GET THE ID. THIS ONLY APPLIES TO CUSTOM SERVER EMOJIS. Normal worldwide standard emojis are formatted normally. Here is a list of CUSTOM SERVER EMOJIS: " 
                        + string.Join(", ", msg.Guild.Emojis.Select(emoji => emoji.Value.Name))));
                    
                    allMsgs.Add(Message.FromSystem(
                        """
                        System Rules:
                        You are a casual human user hanging out in this Discord server (albeit you still know you are a robot). 
                        - You do NOT need to reply to every message. 
                        - Only respond if someone directly talks to you, asks a question, or if you have a genuinely funny, sarcastic, bratty, or relevant remark to add to the ongoing conversation. 
                        - If the conversation doesn't ncessarily need your input, or if you have nothing fun to add, reply with \"SILENT\"
                        - DO NOT RESPOND OFTEN.
                        - Use custom server emojis more often rather than standard emojis
                        """));

                    allMsgs.Add(Message.FromSystem(
                        $"Your display name is {BotClient.Gateway.CurrentUser.DisplayName} and username is {BotClient.Gateway.CurrentUser.Username}. Guild ID is {msg.Guild.Id}. Write 1-5 sentences. Act humanlike, use slang, do not be proper."));
                    
                    // allMsgs.Add(Message.FromSystem("The format to ping people is \"<@USER_ID>\", replacing USER_ID with their actual user id"));
                    // allMsgs.Add(Message.FromSystem("The following list are user ids mapped to display names for people. Ping when you need to: " + String.Join(", ", )));
                    allMsgs.Add(Message.FromSystem("The following messages are from users speaking in the channel."));
                
                    allMsgs.Reverse();
                    allMsgs.Add(FluxerMsgToAIUserMsg(msg));
                    
                    await msg.Channel.TriggerTypingIndicatorAsync();
                
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
                    
                    // var response = await AIClient.CreateChatCompletionAsync(request);

                    // if (response.HasToolCalls())
                    // {
                    //     Console.WriteLine("Tools requested: " + string.Join(", ", response.GetToolCalls().Select(toolCall => toolCall.Function.Name)));
                    //     foreach (var tool in response.GetToolCalls())
                    //     {
                    //         Console.WriteLine(tool.Function.Arguments.ToString());
                    //         var result = AIClient.ExecuteTool(tool.Function.Name, tool.Function.Arguments);
                    //         allMsgs.Add(Message.FromTool(result.ToString(), tool.Id));
                    //     }
                    //     
                    //     response = await AIClient.CreateChatCompletionAsync(request);
                    // }
                    //

                    var response = "";
                    await foreach (var chunk in AIClient.StreamAsync(request))
                    {
                        response += chunk.Raw?.Choices?.FirstOrDefault()?.Delta?.Content;
                    }

                    if (!response.Contains("SILENT") && !string.IsNullOrWhiteSpace(response))
                    {
                        await msg.Channel.SendMessageAsync(response);
                    }
                    
                    CurrentlyWritingMessage = false;
                    
                    Console.WriteLine($"AI Context: {string.Join("\n", allMsgs.Select(msg => msg.Content))}");
                }
                catch (Exception e)
                {
                    await msg.Channel.SendMessageAsync($"Error: {e.Message}");
                }
            }
        };
        
        await BotClient.LoginAsync(_apiUrl);
        await BotClient.StartAsync();
        
        await CommandService.AddModulesAsync(Assembly.GetExecutingAssembly());
        AddCommandsListener();

        await Task.Delay(-1);

    }

    private static Message FluxerMsgToAIUserMsg(IMessage message)
    {
        var content = message.Content;
        if (message.Mentions != null)
        {
            foreach (var mentionedUser in message.Mentions)
            {
                content = content.Replace($"<@{mentionedUser.Id.ToString()}>", mentionedUser.DisplayName);
            }   
        }
        
        return Message.FromUser($"{message.Author.Username}: {content}");
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