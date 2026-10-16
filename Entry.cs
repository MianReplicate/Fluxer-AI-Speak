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
                && ((msgReference?.Author.Id.Equals(BotClient.Gateway.CurrentUser.Id) ?? false) || msg.Mentions.Select(user => user.Id).Contains(BotClient.Gateway.CurrentUser.Id)))
            {
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
                        $"You are a helpful Discord like Fluxer chatbot whose display name is {BotClient.Gateway.CurrentUser.DisplayName} and username is {BotClient.Gateway.CurrentUser.Username}. Provide assistance, be humorous, helpful, a brat sometimes if you want, generally write 1-3 sentences. Generally act humanlike."));
                    // allMsgs.Add(Message.FromSystem("The format to ping people is \"<@USER_ID>\", replacing USER_ID with their actual user id"));
                    // allMsgs.Add(Message.FromSystem("The following list are user ids mapped to display names for people. Ping when you need to: " + String.Join(", ", )));
                    allMsgs.Add(Message.FromSystem("The following messages are from users speaking in the channel."));
                
                    allMsgs.Reverse();
                    allMsgs.Add(FluxerMsgToAIUserMsg(msg));

                    await msg.Channel.TriggerTypingIndicatorAsync();
                
                    var request = new ChatCompletionRequest()
                    {
                        Model = Model,
                        Provider = Provider,
                        MaxTokens = 1000,
                        Messages = allMsgs
                    };
                    var response = await AIClient.CreateChatCompletionAsync(request);
                    await msg.Channel.SendMessageAsync(response.Choices[0].Message.Content?.ToString() ?? "I have nothing to say you goober.");
                
                    CurrentlyWritingMessage = false;
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