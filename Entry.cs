using System.Reflection;
using Fluxer.Net;
using Fluxify.Application.Entities.Channels;
using Fluxify.Application.Model.Messages;
using Fluxify.Bot;
using Fluxify.Commands.Model;
using Fluxify.Core;
using Fluxify.Core.Credentials;
using OpenRouter.NET;
using OpenRouter.NET.Models;
using Message = OpenRouter.NET.Models.Message;

namespace Fluxer_AI_Speak;

public class Entry
{
    public static OpenRouterClient AIClient;
    public static FluxerClient BotClient;
    public static string Model = "z-ai/glm-5.3-flash";
    public static int Context = 50;
    public static bool CurrentlyWritingMessage = false;
    
    public static async Task Main(string[] args)
    {
        if (args.Length == 0)
            throw new ArgumentException("Please specify a bot token!");
        var token = args[0];
        var key = args[1];
        var apiUrl = args.Length == 3 ? args [2] + "/api" : null;

        BotClient = new FluxerClient(token);
        AIClient = new OpenRouterClient(key);
        
        // BotClient.Commands.Command(new CommandMeta(Name: "model", Args: new []{new CommandArg("model")}), async ctx =>
        // {
        //     if (ctx.Reader.TryGetNext(out string model, true) && !model.IsWhiteSpace())
        //     {
        //         Model = model;
        //         await ctx.ReplyAsync($"Set model to {model}!");
        //     }
        //     else
        //     {
        //         await ctx.ReplyAsync($"No model to set :(");
        //     }
        // }, null);
        // BotClient.Commands.Command(new CommandMeta(Name: "context", Args: new []{new CommandArg("context")}), async ctx =>
        // {
        //     if (ctx.Reader.TryGetNext(out int context, true))
        //     {
        //         Context = context;
        //         await ctx.ReplyAsync($"Set context to {context} messages!");
        //     }
        //     else
        //     {
        //         await ctx.ReplyAsync($"No context to set :(");
        //     }
        // }, null);
        //
        // BotClient.MessageReceived += async args =>
        // {
        //     if (!CurrentlyWritingMessage 
        //         && !args.Message.Author.Username.Equals(BotClient.CurrentUser.Username) 
        //         && ((args.Message.ReferencedMessage?.Author.Username.Equals(BotClient.CurrentUser.Username) ?? false) || args.Message.MentionedUsers.Select(user => user.Username).Contains(BotClient.CurrentUser.Username)))
        //     {
        //         CurrentlyWritingMessage = true;
        //         
        //         var msgBatches = args.Channel.GetMessagesAsync(args.Message.Id, limit: Context);
        //         
        //         var allMsgs = new List<Message>();
        //
        //         await foreach (var msgBatch in msgBatches)
        //         {
        //             allMsgs.AddRange(msgBatch.Select(message => message.Author.Username.Equals(BotClient.CurrentUser.Username) ? Message.FromAssistant(message.Content) : FluxerMsgToAIUserMsg(message)));
        //         }
        //
        //         allMsgs.Add(Message.FromSystem(
        //             $"You are a helpful Discord like Fluxer chatbot whose display name is {BotClient.CurrentUser.DisplayName} and username is {BotClient.CurrentUser.Username}. Provide assistance, be humorous, helpful, a brat sometimes if you want, generally write 1-3 sentences. Generally act humanlike."));
        //         // allMsgs.Add(Message.FromSystem("The format to ping people is \"<@USER_ID>\", replacing USER_ID with their actual user id"));
        //         // allMsgs.Add(Message.FromSystem("The following list are user ids mapped to display names for people. Ping when you need to: " + String.Join(", ", )));
        //         allMsgs.Add(Message.FromSystem("The following messages are from users speaking in the channel."));
        //         
        //         allMsgs.Reverse();
        //         allMsgs.Add(FluxerMsgToAIUserMsg(args.Message));
        //         
        //         var request = new ChatCompletionRequest()
        //         {
        //             Model = Model,
        //             Messages = allMsgs
        //         };
        //         var response = await AIClient.CreateChatCompletionAsync(request);
        //         await args.Channel.SendMessageAsync(response.Choices[0].Message.Content?.ToString() ?? "I have nothing to say you goober.");
        //         
        //         CurrentlyWritingMessage = false;
        //     }
        // };
        
        await BotClient.LoginAsync(apiUrl);
        await BotClient.StartAsync();

        await Task.Delay(-1);

    }

    private static Message FluxerMsgToAIUserMsg(Fluxify.Application.Entities.Messages.Message message)
    {
        var content = message.Content;
        foreach (var mentionedUser in message.MentionedUsers)
        {
            content = content.Replace($"<@{mentionedUser.Id.ToString()}>", mentionedUser.DisplayName);
        }
        
        return Message.FromUser($"{message.Author.Username}: {content}");
    }
}