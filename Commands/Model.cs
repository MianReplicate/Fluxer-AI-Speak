using Fluxer.Net.Commands;

namespace Fluxer_AI_Speak.Commands;

public class Model : ModuleBase
{
    [Command("test")]
    public async Task Test()
    {
        // This will send a message in the current channel.
        await ReplyAsync("Hello World!");
    }
}