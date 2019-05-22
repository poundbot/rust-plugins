using Newtonsoft.Json;
using Oxide.Core.Libraries;
using Oxide.Core.Libraries.Covalence;
using Oxide.Core.Plugins;
using System;
using System.Linq;
using System.Collections.Generic;

namespace Oxide.Plugins
{
  [Info("Pound Bot", "MrPoundsign", "1.3.0")]
  [Description("Connector for the Discord bot PoundBot.")]

  class PoundBot : CovalencePlugin
  {
    private string ApiBaseURI;
    private string ApiMessageBaseURI;
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

    class ApiErrorResponse
    {
      public string Error;
    }

    class DiscordAuth
    {
      public string PlayerID;
      public string DisplayName;
      public string ClanTag;
      public string DiscordName;
      public int Pin;
      public DateTime CreatedAt;

      public DiscordAuth(string displayname, string discordName, string playerid)
      {
        Random rnd = new Random();
        DisplayName = displayname;
        DiscordName = discordName;
        PlayerID = playerid;
        Pin = rnd.Next(1, 9999);
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
        ["command.poundbot_register"] = "pbreg",
        ["connector.reconnected"] = "Reconnected with PoundBot",
        ["connector.time_in_error"] = "Total time in error: {0}",
        ["connector.error"] = "Error communicating with PoundBot: {0}:{1}",
        ["connector.error_with_rid"] = "Error communicating with PoundBot: [{0}] {1}:{2}",
        ["connector.user_error"] = "Cannot connect to PoundBot right now. Please alert the admins.",
        ["discord.pin"] = "Enter the following PIN to the bot in discord: {0}.",
        ["discord.connected"] = "You are connected to discord.",
        ["usage"] = "Usage: /pbreg \"<discord name>\"\n Example: /discord \"Fancy Guy#8080\"",
      }, this);
    }

    #endregion

    #region Oxide API
    void Init()
    {
      UpgradeConfig();
      ApiBaseURI = $"{Config["api.url"]}api";
      ApiMessageBaseURI = $"/messages";
      RequestHeaders = new Dictionary<string, string>
      {
        ["Content-type"] = "application/json",
        ["Authorization"] = $"Token {Config["api.key"]}",
        ["X-PoundBotConnector-Version"] = Version.ToString(),
        ["User-Agent"] = $"PoundBotConnector/{Version.ToString()}",
        ["X-PoundBot-Game"] = covalence.Game.ToLower()
      };

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
        API_RequestGet("/players/registered", null,
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
        }, this);
      });
    }
    #endregion

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

    // Returns true if request was sent, false otherwise.
    private bool API_Request(string uri, string body, Func<int, string, bool> callback, Plugin owner, RequestMethod method = RequestMethod.GET, Dictionary<string, string> headers = null)
    {
      if (!ApiRequestOk()) return false;

      Dictionary<string, string> rHeaders = new Dictionary<string, string>(RequestHeaders);
      rHeaders["X-Request-ID"] = Guid.NewGuid().ToString();

      if (headers != null)
      {
        foreach (var pairs in headers)
        {
          rHeaders[pairs.Key] = pairs.Value;
        }
      }

      webrequest.Enqueue($"{ApiBaseURI}{uri}", body,
      (code, response) =>
      {
        if (!ApiSuccess(callback(code, response)))
        {
          ApiError(code, response, rHeaders["X-Request-ID"]);
        }
      },
      owner, method, rHeaders, 12000f);
      return true;
    }

    private bool API_RequestGet(string uri, string body, Func<int, string, bool> callback, Plugin owner, Dictionary<string, string> headers = null, float timeout = 0)
    {
      return API_Request(uri, body, callback, owner, RequestMethod.GET, headers);
    }

    private bool API_RequestPost(string uri, string body, Func<int, string, bool> callback, Plugin owner, Dictionary<string, string> headers = null, float timeout = 0)
    {
      return API_Request(uri, body, callback, owner, RequestMethod.POST, headers);
    }

    private bool API_RequestPut(string uri, string body, Func<int, string, bool> callback, Plugin owner, Dictionary<string, string> headers = null, float timeout = 0)
    {
      return API_Request(uri, body, callback, owner, RequestMethod.PUT, headers);
    }

    private bool API_RequestDelete(string uri, string body, Func<int, string, bool> callback, Plugin owner, Dictionary<string, string> headers = null, float timeout = 0)
    {
      return API_Request(uri, body, callback, owner, RequestMethod.DELETE, headers);
    }

    private bool API_SendChannelMessage(Plugin owner, string channel, KeyValuePair<string, bool>[] message_parts, string embed_color = null, Func<int, string, bool> callback = null, Dictionary<string, string> headers = null, string type = "plain")
    {
      List<GameMessagePart> parts = new List<GameMessagePart>();

      foreach(KeyValuePair<string, bool> part in message_parts)
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
        sm.Type = 1;
        sm.EmbedStyle = new GameMessageEmbedStyle
        {
          Color = embed_color
        };
      }

      if (channel[0] == '#')
      {
        channel = channel.Substring(1);
      }

      if (callback == null) {
        callback = (int code, string response) => (code == 200);
      }

      string body = JsonConvert.SerializeObject(sm);
      API_RequestPost($"{ApiMessageBaseURI}/{channel}", body, callback, owner, headers);
      
      return true;
    }

    private void Connected()
    {
      foreach (Plugin plugin in plugins.GetAll())
      {
        plugin.CallHook("OnPoundBotConnected");
      }
    }
    #endregion

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
        player.Message(lang.GetMessage("usage", this, player.Id));
        return;
      }

      var da = new DiscordAuth(player.Name, args[0], player.Id);

      API_RequestPut("/discord_auth", JsonConvert.SerializeObject(da),
        (code, response) =>
        {
          if (code == 200)
          {
            player.Message(string.Format(lang.GetMessage("discord.pin", this, player.Id), da.Pin.ToString("D4")));
            return true;
          }
          else if (code == 405) // Method not allowed means we're already connected
          {
            player.Message(lang.GetMessage("discord.connected", this, player.Id));
            return true;
          }
          return false;
        }, this);
    }
  }
  #endregion
}