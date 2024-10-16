﻿using System.Text;
using System.Text.Json;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Localization;
using CounterStrikeSharp.API.Modules.Cvars;

namespace NeedSystem;

public class BaseConfigs : BasePluginConfig
{
    [JsonPropertyName("WebhookUrl")]
    public string WebhookUrl { get; set; } = "";

    [JsonPropertyName("IPandPORT")]
    public string IPandPORT { get; set; } = "45.235.99.18:27025";

    [JsonPropertyName("CustomDomain")]
    public string CustomDomain { get; set; } = "https://crisisgamer.com/redirect/connect.php";

    [JsonPropertyName("EmbedThumbnailURL")]
    public string EmbedThumbnailURL { get; set; } = "https://image.gametracker.com/images/maps/160x120/csgo/{map}.jpg";

    [JsonPropertyName("EmbedColor")]
    public int EmbedColor { get; set; } = 9246975;

    [JsonPropertyName("MentionRoleID")]
    public string MentionRoleID { get; set; } = "";

    [JsonPropertyName("MaxServerPlayers")]
    public int MaxServerPlayers { get; set; } = 13;

    [JsonPropertyName("MinPlayers")]
    public int MinPlayers { get; set; } = 10;

    [JsonPropertyName("CommandCooldownSeconds")]
    public int CommandCooldownSeconds { get; set; } = 120;

    [JsonPropertyName("Command")]
    public List<string> Command { get; set; } = new List<string> { "css_need", "css_needplayers" };
}

public class NeedSystemBase : BasePlugin, IPluginConfig<BaseConfigs>
{
    private string _currentMap = "";
    private DateTime _lastCommandTime = DateTime.MinValue;
    private Translator _translator;

    public override string ModuleName => "NeedSystem";
    public override string ModuleVersion => "1.0.6";
    public override string ModuleAuthor => "luca.uy";
    public override string ModuleDescription => "Allows players to send a message to discord requesting players.";

    public NeedSystemBase(IStringLocalizer localizer)
    {
        _translator = new Translator(localizer);
    }

    public CounterStrikeSharp.API.Modules.Timers.Timer? intervalMessages;

    public override void Load(bool hotReload)
    {
        RegisterListener<Listeners.OnMapStart>(mapName =>
        {
            _currentMap = mapName;
        });

        foreach (var command in Config.Command)
        {
            AddCommand(command, "", (controller, info) =>
            {
                if (controller == null) return;

                int secondsRemaining;
                if (!CheckCommandCooldown(out secondsRemaining))
                {
                    controller.PrintToChat(_translator["Prefix"] + " " + _translator["CommandCooldownMessage", secondsRemaining]);
                    return;
                }

                int numberOfPlayers = GetNumberOfPlayers();

                if (numberOfPlayers >= MinPlayers())
                {
                    controller.PrintToChat(_translator["Prefix"] + " " + _translator["EnoughPlayersMessage"]);
                    return;
                }

                NeedCommand(controller, controller?.PlayerName ?? _translator["UnknownPlayer"]);

                _lastCommandTime = DateTime.Now;

                controller?.PrintToChat(_translator["Prefix"] + " " + _translator["NotifyPlayersMessage"]);
            });
        }

        if(hotReload)
        {
            _currentMap = Server.MapName;
        }
    }

    public required BaseConfigs Config { get; set; }

    public void OnConfigParsed(BaseConfigs config)
    {
        Config = config;
    }

    private bool CheckCommandCooldown(out int secondsRemaining)
    {
        var secondsSinceLastCommand = (int)(DateTime.Now - _lastCommandTime).TotalSeconds;
        secondsRemaining = Config.CommandCooldownSeconds - secondsSinceLastCommand;
        return secondsRemaining <= 0;
    }

    public int GetNumberOfPlayers()
    {
        var players = Utilities.GetPlayers();
        return players.Count(p => !p.IsBot && !p.IsHLTV);
    }

    public void NeedCommand(CCSPlayerController? caller, string clientName)
    {
        if (caller == null) return;

        clientName = clientName.Replace("[Ready]", "").Replace("[Not Ready]", "").Trim();
        string thumbnailUrl = Config.EmbedThumbnailURL.Replace("{map}", _currentMap, StringComparison.OrdinalIgnoreCase).Trim();

        var embed = new
        {
            title = _translator["EmbedTitle"],
            description = _translator["EmbedDescription"],
            color = Config.EmbedColor,
            thumbnail = new
            {
                url = thumbnailUrl
            },
            fields = new[]
            {
                new
                {
                    name = _translator["ServerFieldTitle"],
                    value = $"```{GetHostname()}```",
                    inline = false
                },
                new
                {
                    name = _translator["RequestFieldTitle"],
                    value = $"[{clientName}](https://steamcommunity.com/profiles/{caller.SteamID})",
                    inline = false
                },
                new
                {
                    name = _translator["MapFieldTitle"],
                    value = $"```{_currentMap}```",
                    inline = false
                },
                new
                {
                    name = _translator["PlayersFieldTitle"],
                    value = $"```{GetNumberOfPlayers()}/{MaxServerPlayers()}```",
                    inline = false
                },
                new
                {
                    name = _translator["ConnectionFieldTitle"],
                    value = $"```connect {GetIP()}```\n**{_translator["ClickToConnect"]}({GetCustomDomain()}?ip={GetIP()})**\n\n",
                    inline = false
                }
            }
        };

        Task.Run(() => SendEmbedToDiscord(embed));
    }

    private async Task SendEmbedToDiscord(object embed)
    {
        try
        {
            var webhookUrl = GetWebhook();

            if (string.IsNullOrEmpty(webhookUrl))
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("Webhook URL is null or empty, skipping Discord notification.");
                return;
            }

            var httpClient = new HttpClient();

            var payload = new
            {
                content = $"<@&{MentionRoleID()}> {_translator["NeedInServerMessage"]}",
                embeds = new[] { embed }
            };

            var json = JsonSerializer.Serialize(payload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await httpClient.PostAsync(webhookUrl, content);

            Console.ForegroundColor = response.IsSuccessStatusCode ? ConsoleColor.Green : ConsoleColor.Red;
            Console.WriteLine(response.IsSuccessStatusCode
                ? "Success"
                : $"Error: {response.StatusCode}");
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            throw;
        }
    }

    private string GetWebhook()
    {
        return Config.WebhookUrl;
    }
    private string GetCustomDomain()
    {
        return Config.CustomDomain;
    }
    private string GetIP()
    {
        return Config.IPandPORT;
    }
    private string GetHostname()
    {
        string hostname = ConVar.Find("hostname")?.StringValue ?? _translator["UnknownMap"];

        if(string.IsNullOrEmpty(hostname) || hostname.Length <= 3)
        {
            hostname = _translator["UnknownMap"];
        }
        
        return hostname;
    }
    private string MentionRoleID()
    {
        return Config.MentionRoleID;
    }
    private int MaxServerPlayers()
    {
        return Config.MaxServerPlayers;
    }
    private int MinPlayers()
    {
        return Config.MinPlayers;
    }
}