using System.Reflection;
using Fluxer.Net;
using Fluxer.Net.Commands;

namespace Fluxer_AI_Speak;

public class Entry
{
    public static async Task Main(string[] args)
    {
        if (args.Length == 0)
            throw new ArgumentException("Please specify a bot token!");
        var token = "Bot " + args[0];
        var apiUrl = args.Length == 2 ? args [1] + "/api" : null;

        var client = new FluxerClient(token);
        await client.LoginAsync(apiUrl);

        var commands = new CommandService();

        await commands.AddModulesAsync(Assembly.GetExecutingAssembly());
        
        client.Gateway.MessageCreated += (data) =>
        {
            Console.WriteLine($"{data.Author.Username}: {data.Content}");
        };

        await Task.Delay(-1);
    }
}