using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Oxide.Core;
using Oxide.Core.Libraries;
using Oxide.Core.Libraries.Covalence;
using Oxide.Core.Plugins;
using System;
using System.Linq;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Oxide.Plugins
{
  [Info("Pound Bot", "MrPoundsign", "2.0.1")]
  [Description("Connector for the Discord bot PoundBot.")]

  class PoundBot : CovalencePlugin
  {
    const string EntityDeathURI = "/entity_death";
    const string ApiMessageBaseURI = "/messages";
    const string ApiRolesURI = "/roles";
    const string ApiChatURI = "/chat";
    const string ApiRegisteredPlayersURI = "/players/registered";
    const string ApiDiscordAuthURI = "/discord_auth";
    const string ApiClansURI = "/clans";

    private string ApiBaseURI;

    enum DebugLevels { TRACE, INFO };

    protected string DebugURI = "";
    protected int DebugLevel = (int)DebugLevels.INFO;
    protected int ApiRetrySeconds = 1;
    protected int ApiRetryNotify = 30;

    protected string RegisteredUsersGroup;
    protected bool ApiInError;
    protected bool ApiRetry;
    protected uint ApiRetryAttempts;
    protected DateTime ApiErrorTime;
    protected DateTime LastApiAttempt;
    private Dictionary<string, string> RequestHeaders;
    protected bool RegisteredUsersInFlight;
    private ChannelList KnownChannelList;

    class ApiErrorResponse
    {
      public string Error;
    }

    class ApiRequest
    {
      public RequestMethod Method;
      public string URI;
      public string Body;
      public Plugin Plugin;
      public string RequestUUID;

      public ApiRequest(RequestMethod method, string uri, string body, Plugin plugin)
      {
        Method = method;
        URI = uri;
        Body = body;
        Plugin = plugin;
        RequestUUID = Guid.NewGuid().ToString();
      }
    }

    #region PoundBot messages
    class Channel
    {
      public string ID;
      public string Name;
      public bool CanSend;
      public bool CanStyle;

      public bool IsAvailable()
      {
        return CanSend || CanStyle;
      }
    }

    class ChannelList
    {
      public Channel[] Channels;

      public bool CanSendTo(string channel)
      {
        foreach (Channel chan in Channels)
        {
          if (chan.Name == channel || chan.ID == channel)
          {
            return chan.CanSend;
          }
        }
        return false;
      }

      public bool CanStyleTo(string channel)
      {
        foreach (Channel chan in Channels)
        {
          if (chan.Name == channel || chan.ID == channel)
          {
            return chan.CanStyle;
          }
        }
        return false;
      }
    }

    class DiscordAuth
    {
      public string PlayerID;
      public string DisplayName;
      public string ClanTag;
      public string DiscordName;
      public int Pin;
      public DateTime CreatedAt;

      public DiscordAuth(string displayName, string discordName, string playerid)
      {
        Pin = new System.Random().Next(1, 9999);
        DisplayName = displayName;
        DiscordName = discordName;
        PlayerID = playerid;
        CreatedAt = DateTime.UtcNow;
      }
    }

    public class GameMessageEmbedStyle
    {
      public string Color { get; set; }
    }

    class GameMessagePart
    {
      public string Content { get; set; }
      public bool Escape { get; set; }
    }

    class GameMessage
    {
      public GameMessagePart[] MessageParts { get; set; }
      public int Type { get; set; } // 0 = plain, 1 = embedded
      public GameMessageEmbedStyle EmbedStyle { get; set; }
    }

    class RoleSyncRequest
    {
      public string GuildID;
      public string[] PlayerIDs;
    }

    class EntityDeath
    {
      public string Name;
      public string GridPos;
      public string[] OwnerIDs;
      public DateTime CreatedAt;

      public EntityDeath(string name, string gridpos, string[] ownerIDs)
      {
        Name = name;
        GridPos = gridpos;
        OwnerIDs = ownerIDs;
        CreatedAt = DateTime.UtcNow;
      }
    }
    #endregion

    #region Configuration
    protected override void LoadDefaultConfig()
    {
      Config["api.url"] = "https://api.poundbot.com/";
      Config["api.key"] = "API KEY HERE";
      Config["config.version"] = 2;
      Config["players.registered.group"] = "poundbot.registered";
    }

    void UpgradeConfig()
    {
      string configVersion = "config.version";
      bool dirty = false;

      if (Config[configVersion] == null)
      {
        Config[configVersion] = 1;
      }
      else
      {
        try
        {
          var foo = (string)Config[configVersion];
          Config[configVersion] = 2;
          dirty = true;
        }
        catch (InvalidCastException) { } // testing if it can be converted to a string or not. No need to change it because it's not a string.
      }

      if ((int)Config[configVersion] < 2)
      {
        LogWarning(string.Format(lang.GetMessage("config.upgrading", this), "1.1.2"));

        // Update the API endpoint
        string api_url = (string)Config["api_url"];
        if (api_url == "http://poundbot.mrpoundsign.com/" || api_url == "http://api.poundbot.com/")
        {
          Config["api.url"] = "https://api.poundbot.com/";
        }
        else
        {
          Config["api.url"] = (string)Config["api_url"];
        }
        Config["api.key"] = (string)Config["api_key"];
        Config.Remove("api_url");
        Config.Remove("api_key");
        Config["players.registered.group"] = "poundbot.registered";
        Config[configVersion] = 2;
        dirty = true;
      }

      if (dirty)
      {
        SaveConfig();
      }
    }
    #endregion

    #region Language
    protected override void LoadDefaultMessages()
    {
      lang.RegisterMessages(new Dictionary<string, string>
      {
        ["config.upgrading"] = "Upgrading config to v{0}",
        ["config.api_key_updated"] = "Your API key has been updated. Check config/PoundBot.json.",
        ["command.poundbot_register"] = "pbreg",
        ["connector.reconnected"] = "Reconnected with PoundBot",
        ["connector.time_in_error"] = "Total time in error: {0}",
        ["connector.error"] = "Error communicating with PoundBot: {0}:{1}",
        ["connector.error_with_rid"] = "Error communicating with PoundBot: [{0}] {1}:{2}",
        ["connector.user_error"] = "Cannot connect to PoundBot right now. Please alert the admins.",
        ["discord.pin"] = "Enter the following PIN to the bot in discord: {0}.",
        ["discord.connected"] = "You are registered.",
        ["discord.already_connected"] = "You are already registered.",
        ["discord.not_connected"] = "Registration not found.",
        ["discord.channels_updated"] = "Channel cache updated.",
        ["discord.channel_cannot_send"] = "Cannot send to channel {0}. Please check that the bot can send to this channel. Run pb.updatechannels after making Discord changes to update the cache.",
        ["discord.channel_cannot_style"] = "Cannot send to channel {0}. Please check that the bot can embed links to this channel. Run pb.updatechannels after making Discord changes to update the cache.",
        ["usage.pbreg"] = "Usage: {0} \"<discord name>\"\n Example: {0} \"Fancy Guy#8080\"",
        ["usage.set_api_key"] = "Usage:\n\tpb.set_api_key <api key>",
        ["discord.console.registration_attempt"] = "Player {0}({1}) attempting to register as {2}",
      }, this);
    }

    #endregion

    #region Oxide API
    void Init()
    {
      UpgradeConfig();
      ApiBaseURI = $"{Config["api.url"]}api";
      ApplyConfig();

      RegisteredUsersGroup = (string)Config["players.registered.group"];
      permission.RegisterPermission(RegisteredUsersGroup, this);
      if (!permission.GroupExists(RegisteredUsersGroup))
      {
        permission.CreateGroup(RegisteredUsersGroup, "PoundBot Registered Users", 0);
      }

      AddLocalizedCommand("command.poundbot_register", "CommandPoundBotRegister");

      Connected();

      timer.Every(5f, () =>
      {
        if (RegisteredUsersInFlight) return;
        RegisteredUsersInFlight = true;
        Request(new ApiRequest(RequestMethod.GET, ApiRegisteredPlayersURI, null, this),
        (code, response) =>
        {
          RegisteredUsersInFlight = false;
          if (code == 200)
          {
            string[] groupPlayerIDs = permission.GetUsersInGroup(RegisteredUsersGroup);
            string[] playerIDs = JsonConvert.DeserializeObject<string[]>(response);

            for (int i = 0; i < groupPlayerIDs.Length; i++)
            {
              groupPlayerIDs[i] = groupPlayerIDs[i].Substring(0, groupPlayerIDs[i].IndexOf(' '));
            }

            // hot group diff action
            string[] playersToRemove = groupPlayerIDs.Except(playerIDs).ToArray();
            string[] playersToAdd = playerIDs.Except(groupPlayerIDs).ToArray();

            if (playersToAdd.Length + playersToRemove.Length == 0)
            {
              return true;
            }

            foreach (string id in playersToRemove)
            {
              Puts($"Removing user {id} from {RegisteredUsersGroup}");
              permission.RemoveUserGroup(id, RegisteredUsersGroup);
            }

            foreach (string id in playersToAdd)
            {
              Puts($"Adding user {id} to {RegisteredUsersGroup}");
              permission.AddUserGroup(id, RegisteredUsersGroup);
            }
            return true;
          }

          Puts("Unuccessful registered players get");
          Puts(response);
          return false;
        });
      });
    }
    #endregion

    private void ApplyConfig()
    {
      RequestHeaders = new Dictionary<string, string>
      {
        ["Content-type"] = "application/json",
        ["Authorization"] = $"Token {Config["api.key"]}",
        ["X-PoundBotConnector-Version"] = Version.ToString(),
        ["User-Agent"] = $"PoundBotConnector/{Version.ToString()}",
        ["X-PoundBot-Game"] = covalence.Game.ToLower()
      };
    }

    #region API

    // private Dictionary<string, string> Headers() => RequestHeaders;

    private string API_RegisteredUsersGroup() => RegisteredUsersGroup;

    private string ApiBase() => ApiBaseURI;

    private bool ApiRequestOk()
    {
      if (ApiInError && !ApiRetry && (LastApiAttempt.AddSeconds(ApiRetrySeconds) < DateTime.Now))
      {
        ApiRetryAttempts++;
        if (ApiRetryAttempts == 1 || ApiRetryAttempts % ApiRetryNotify == 0)
        {
          Puts(string.Format(lang.GetMessage("connector.time_in_error", this), DateTime.Now.Subtract(ApiErrorTime)));
        }
        ApiRetry = true;
      }
      return (!ApiInError || ApiRetry);
    }

    private void ApiError(int code, string response, string requestID = null)
    {
      string error;
      if (ApiInError)
      {
        if (ApiRetry)
        {
          LastApiAttempt = DateTime.Now;
          ApiRetry = false;
        }

        if (ApiRetryAttempts % ApiRetryNotify != 0) return;
      }
      else
      {
        ApiErrorTime = DateTime.Now;
        LastApiAttempt = DateTime.Now;
        ApiInError = true;
        ApiRetry = false;
      }

      if (code == 0)
      {
        error = "Connection Failure!";
      }
      else
      {
        try
        {
          var air = JsonConvert.DeserializeObject<ApiErrorResponse>(response);
          error = air.Error;
        }
        catch
        {
          error = response;
        }
      }
      if (code == 0)
      {
        Puts(string.Format(lang.GetMessage("connector.error", this), code, error));
        return;
      }
      Puts(string.Format(lang.GetMessage("connector.error_with_rid", this), requestID, code, error));

    }

    private bool ApiSuccess(bool success)
    {
      // Reset retry variables if we're successful
      if (ApiInError && success)
      {
        Puts(lang.GetMessage("connector.reconnected", this));
        Puts(string.Format(lang.GetMessage("connector.time_in_error", this), DateTime.Now.Subtract(ApiErrorTime)));
        ApiRetryAttempts = 0;
        ApiInError = false;
        ApiRetry = true;
        Connected();
      }
      return success;
    }

    #region Requests

    private bool Request(ApiRequest api_request, Func<int, string, bool> callback)
    {
      if (!ApiRequestOk()) return false;

      if (DebugURI.Length > 0 && api_request.URI.StartsWith(DebugURI))
      {
        Puts($"Request from {api_request.Plugin.Name} to {api_request.URI} with RequestUUID {api_request.RequestUUID}\n{api_request.Body}");
      }

      if (api_request.Plugin == null)
      {
        api_request.Plugin = this;
      }

      Dictionary<string, string> rHeaders = new Dictionary<string, string>(RequestHeaders);
      rHeaders["X-Request-ID"] = api_request.RequestUUID;

      webrequest.Enqueue($"{ApiBaseURI}{api_request.URI}", api_request.Body,
      (code, response) =>
      {
        if (!ApiSuccess(callback(code, response)))
        {
          ApiError(code, response, rHeaders["X-Request-ID"]);
        }
      },
      api_request.Plugin, api_request.Method, rHeaders, 12000f);

      return true;
    }
    // Returns true if request was sent, false otherwise.
    private bool API_Request(string uri, string body, Func<int, string, bool> callback, Plugin owner, RequestMethod method = RequestMethod.GET, Dictionary<string, string> headers = null)
    {
      return Request(new ApiRequest(method, uri, body, this), callback);
    }
    #endregion

    #region Messages API
    private bool API_SendChannelMessage(Plugin owner, string channel, KeyValuePair<string, bool>[] message_parts, string embed_color = null, Func<int, string, bool> callback = null, Dictionary<string, string> headers = null, string type = "plain")
    {
      List<GameMessagePart> parts = new List<GameMessagePart>();

      foreach (KeyValuePair<string, bool> part in message_parts)
      {
        parts.Add(
          new GameMessagePart
          {
            Content = part.Key,
            Escape = part.Value
          }
        );
      }

      GameMessage sm = new GameMessage
      {
        MessageParts = parts.ToArray()
      };

      if (embed_color != null)
      {
        if (!KnownChannelList.CanStyleTo(channel))
        {
          Puts(string.Format(lang.GetMessage("discord.channel_cannot_style", this), channel));
          return false;
        }
        sm.Type = 1;
        sm.EmbedStyle = new GameMessageEmbedStyle
        {
          Color = embed_color
        };
      }
      else
      {
        if (!KnownChannelList.CanSendTo(channel))
        {
          Puts(string.Format(lang.GetMessage("discord.channel_cannot_send", this), channel));
          return false;
        }
      }

      if (channel[0] == '#')
      {
        channel = channel.Substring(1);
      }

      if (callback == null)
      {
        callback = (int code, string response) => (code == 200);
      }

      string body = JsonConvert.SerializeObject(sm);
      return Request(new ApiRequest(RequestMethod.POST, $"{ApiMessageBaseURI}/{channel}", body, owner), callback);
    }

    private bool API_GetChannelMessage(Plugin owner, string channel, Func<int, string, bool> callback = null)
    {
      return Request(new ApiRequest(RequestMethod.GET, ApiChatURI, null, owner), callback);
    }
    #endregion

    #region Roles API
    private bool API_SendRole(Plugin owner, string[] groupPlayerIDs, string role, Func<int, string, bool> callback)
    {
      RoleSyncRequest rsr = new RoleSyncRequest
      {
        PlayerIDs = groupPlayerIDs,
      };

      return Request(new ApiRequest(RequestMethod.POST, $"{ApiRolesURI}/{role}", JsonConvert.SerializeObject(rsr), owner), callback);
    }
    #endregion

    private bool API_SendEntityDeath(Plugin owner, string name, string gridPos, string[] owners, Func<int, string, bool> callback)
    {
      EntityDeath di = new EntityDeath(name, gridPos, owners);

      return Request(new ApiRequest(RequestMethod.PUT, EntityDeathURI, JsonConvert.SerializeObject(di), owner), callback);
    }

    private bool API_SendClans(Plugin owner, List<JObject> clans, Func<int, string, bool> callback)
    {
      return Request(new ApiRequest(RequestMethod.PUT, ApiClansURI, JsonConvert.SerializeObject(clans), owner), callback);
    }

    private bool API_SendClan(Plugin owner, string tag, JObject clan, Func<int, string, bool> callback)
    {
      return Request(new ApiRequest(RequestMethod.PUT, $"{ApiClansURI}/{tag}", JsonConvert.SerializeObject(clan), owner), callback);
    }

    private bool API_DeleteClan(Plugin owner, string tag, Func<int, string, bool> callback)
    {
      return Request(new ApiRequest(RequestMethod.DELETE, $"{ApiClansURI}/{tag}", null, owner), callback);
    }

    private void Connected()
    {
      UpdateChannels();
      Interface.Call("OnPoundBotConnected");
    }
    #endregion

    private void UpdateChannels()
    {
      Request(new ApiRequest(RequestMethod.GET, ApiMessageBaseURI, null, this),
          (code, response) =>
          {
            if (code == 200)
            {
              KnownChannelList = JsonConvert.DeserializeObject<ChannelList>(response);
              Puts(lang.GetMessage("discord.channels_updated", this));
              return true;
            }

            return false;
          });
    }

    public void PrintChannels(bool all = false)
    {
      foreach (Channel c in KnownChannelList.Channels)
      {
        if (all || c.IsAvailable())
        {
          Puts($"ID: {c.ID}, Name: {c.Name}, CanSend: {c.CanSend}, CanStyle: {c.CanStyle}");
        }
      }
    }

    #region Helpers
    private void AddLocalizedCommand(string key, string command)
    {
      foreach (var language in lang.GetLanguages(this))
      {
        var messages = lang.GetMessages(language, this);
        foreach (var message in messages.Where(m => m.Key.Equals(key)))
          if (!string.IsNullOrEmpty(message.Value)) AddCovalenceCommand(message.Value, command);
      }
    }
    #endregion

    #region Commands
    private void CommandPoundBotRegister(IPlayer player, string command, string[] args)
    {
      if (args.Length != 1)
      {
        player.Message(string.Format(lang.GetMessage("usage.pbreg", this, player.Id), lang.GetMessage("command.poundbot_register", this, player.Id)));
        return;
      }

      if (args[0] == "check")
      {
        Request(new ApiRequest(RequestMethod.GET, $"{ApiDiscordAuthURI}/check/{player.Id}", null, this),
        (code, response) =>
        {
          if (code == 200)
          {
            player.Message(lang.GetMessage("discord.connected", this, player.Id));
            return true;
          }

          if (code == 404) // Method not allowed means we're already connected
          {
            player.Message(lang.GetMessage("discord.not_connected", this, player.Id));
            return true;
          }

          return false;
        });

        return;
      }

      Regex r = new Regex(@"^[^#]+#[0-9]+$");
      if (!r.Match(args[0]).Success)
      {
        player.Message(lang.GetMessage("usage.pbreg", this, player.Id));
        return;
      }

      string.Format(lang.GetMessage("discord.console.registration_attempt", this), player.Name, player.Id, args[0]);

      var da = new DiscordAuth(player.Name, args[0], player.Id);

      Request(new ApiRequest(RequestMethod.PUT, ApiDiscordAuthURI, JsonConvert.SerializeObject(da), this),
        (code, response) =>
        {
          if (code == 200)
          {
            player.Message(string.Format(lang.GetMessage("discord.pin", this, player.Id), da.Pin.ToString("D4")));
            return true;
          }

          if (code == 409) // Conflict means we're already connected
          {
            player.Message(lang.GetMessage("discord.already_connected", this, player.Id));
            return true;
          }

          return false;
        });
    }

    [Command("pb.update_channels")]
    private void ConsoleCommandUpdateChannels(IPlayer player, string command, string[] args)
    {
      UpdateChannels();
    }

    [Command("pb.channels")]
    private void ConsoleCommandChannels(IPlayer player, string command, string[] args)
    {
      PrintChannels((args.Length != 0));
    }

    [Command("pb.set_api_key")]
    private void ConsoleCommandSetAPIKey(IPlayer player, string command, string[] args)
    {
      if (args.Count() != 1)
      {
        Puts(lang.GetMessage("usage.set_api_key", this));
        return;
      }

      Puts(lang.GetMessage("config.api_key_updated", this));
      Config["api.key"] = args[0];
      SaveConfig();
      ApplyConfig();
    }

    [Command("pb.set_debug_uri")]
    private void ConsoleCommandSetDebugURI(IPlayer player, string command, string[] args)
    {
      if (args.Count() != 1)
      {
        Puts(lang.GetMessage("usage.set_debug_uri", this));
        return;
      }

      DebugURI = args[0];
    }
    #endregion
  }
}