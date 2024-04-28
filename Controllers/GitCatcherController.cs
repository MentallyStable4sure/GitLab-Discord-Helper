using Newtonsoft.Json;
using DSharpPlus.Entities;
using Microsoft.AspNetCore.Mvc;
using MentallyStable.GitHelper.Helpers;
using MentallyStable.GitHelper.Data.Database;
using MentallyStable.GitHelper.Data.Git.Gitlab;
using MentallyStable.GitHelper.Services.Parsers;
using MentallyStable.GitHelper.Data.Development;
using MentallyStable.GitHelper.Services.Discord;
using MentallyStable.GitHelper.Services.Development;

namespace MentallyStable.GitHelper.Controllers
{
    [ApiController]
    [Route("git-catcher")]
    public class GitCatcherController : ControllerBase
    {
        private readonly IDebugger _debugger;
        private readonly BroadcastDataService _broadcastService;
        private readonly DiscordConfig _discordConfig;
        private readonly IResponseParser<GitlabResponse> _gitResponseParser;
        private readonly IThreadWatcher _threadWatcher;
        private readonly PrettyViewWrapService _PrettyViewWrapService;

        public GitCatcherController(IDebugger logger,
            BroadcastDataService broadcastService, DiscordConfig config,
            IResponseParser<GitlabResponse> responseParser,
            IThreadWatcher threadWatcher,
            PrettyViewWrapService prettyViewWrapService) : base()
        {
            _debugger = logger;
            _broadcastService = broadcastService;
            _discordConfig = config;
            _gitResponseParser = responseParser;
            _threadWatcher = threadWatcher;
            _PrettyViewWrapService = prettyViewWrapService;
        }

        [HttpPost("ping")]
        public string Ping() => "monke flip";

        [HttpPost("webhook-raw")]
        public async Task<string> Catch([FromBody] object body)
        {
            var response = JsonConvert.DeserializeObject<GitlabResponse>(body.ToString());
            Console.WriteLine(body);

            //parse action type if possible (if not parse prefixes) and if its not a comment create a new thread
            _debugger.Log(response.ObjectKind, new DebugOptions(this, "[webhook-raw]"));
            string[] lookupKeys = response.ObjectKind.ToLookupKeys(response);

            //catch all implementation if we've set a channel id (CatchAllAPI_ID) in discordconfig
            await CatchAll(await _PrettyViewWrapService.WrapResponseInEmbed(response, response.ObjectKind, lookupKeys));

            //parse all out prefixes and see if it even needed to be tracked
            var prefixesFound = _gitResponseParser.ParsePrefixes(response, _broadcastService.GetAllPrefixes());
            if (prefixesFound.Length <= 0) return $"<h4>We have not found any prefixes tracked in your response, if this problem persist check if you have any prefixes you track in configs/{Endpoints.DISCORD_BROADCASTERS_CONFIG}</h4>";

            var channelsTracked = _broadcastService.GetChannels(prefixesFound);
            var threadedMessage = await _PrettyViewWrapService.WrapResponseInEmbed(response, response.ObjectKind, lookupKeys);

            string title = lookupKeys.ToTitle();
            foreach (var channel in channelsTracked)
            {
                if (!_threadWatcher.IsThreadCreated(channel, lookupKeys)) //response.ObjectAttributes.Title)
                {
                    await _threadWatcher.CreateThread(channel, title, threadedMessage);
                    _debugger.Log($"Created a thread named: '{title}'.", new DebugOptions(this, "[THREAD CREATED]"));
                }
                else
                {
                    var threadChannel = _threadWatcher.FindThread(channel, lookupKeys); //response.ObjectAttributes.Title);
                    if (threadChannel != null)
                    {
                        await _threadWatcher.Post(threadChannel, threadedMessage);
                        if (response.ObjectAttributes.State.Contains("closed")) await threadChannel.DeleteAsync();
                    }
                    else _debugger.Log($"Couldn't find a thread '{title}'.", new DebugOptions(this, "[THREAD NOT FOUND]"));
                }
            }

            return "Data sent to a discord model successfully";
        }

        private async Task CatchAll(string response)
        {
            if (_discordConfig.CatchAllAPI_ID <= 0) return;

            await _broadcastService.BroadcastMessageTo(_discordConfig.CatchAllAPI_ID, response);
        }

        private async Task CatchAll(DiscordMessageBuilder message)
        {
            if (_discordConfig.CatchAllAPI_ID <= 0) return;

            await _broadcastService.BroadcastMessageTo(_discordConfig.CatchAllAPI_ID, message);
        }
    }
}