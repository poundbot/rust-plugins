using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Oxide.Core.Libraries;
using Oxide.Core.Libraries.Covalence;
using Oxide.Core.Plugins;
using System;
using System.Collections.Generic;

namespace Oxide.Plugins
{
  [Info("Pound Bot", "MrPoundsign", "1.0.4")]
  [Description("Connector for the Discord bot PoundBot.")]

  class PoundBot : CovalencePlugin
  {
    protected int ApiRetrySeconds = 1;
    protected int ApiRetryNotify = 10;

    protected bool ApiInError;
    protected bool ApiRetry;
    protected uint ApiRetryAttempts;
    protected DateTime ApiErrorTime;
    protected DateTime LastApiAttempt;

    class ApiErrorResponse
    {
      public string Error;
    }

    class DiscordAuth
    {
      public ulong SteamID;
      public string DisplayName;
      public string ClanTag;
      public string DiscordName;
      public int Pin;
      public DateTime CreatedAt;

      public DiscordAuth(string displayname, string discordName, ulong steamid)
      {
        System.Random rnd = new System.Random();
        this.DisplayName = displayname;
        this.DiscordName = discordName;
        this.SteamID = steamid;
        this.Pin = rnd.Next(1, 9999);
        this.CreatedAt = DateTime.UtcNow;
      }
    }

    #region Configuration
    protected override void LoadDefaultConfig()
    {
      Config["api_url"] = "http://poundbot.mrpoundsign.com/";
      Config["api_key"] = "API KEY HERE";
    }
    
    protected override void LoadDefaultMessages()
    {
      lang.RegisterMessages(new Dictionary<string, string>
      {
        ["connector.reconnected"] = "Reconnected with PoundBot",
        ["connector.time_in_error"] = "Total time in error: {0}",
        ["connector.error"] = "Error communicating with PoundBot: {0}/{1}",
        ["connector.user_error"] = "Cannot connect to PoundBot right now. Please alert the admins.",
        ["discord.pin"] = "Enter the following PIN to the bot in discord: {0}.",
        ["discord.connected"] = "You are connected to discord.",
        ["usage"] = "Usage: /pbreg \"<discord name>\"\n Example: /discord \"Fancy Guy#8080\"",
      }, this);
    }

    void Loaded()
    {
      Connected();
    }
    #endregion

    #region API

    private Dictionary<string, string> Headers()
    {
      return new Dictionary<string, string>
      {
        ["Content-type"] = "application/json",
        ["Authorization"] = $"Token {Config["api_key"]}",
        ["X-PoundBotConnector-Version"] = Version.ToString(),
        ["User-Agent"] = $"PoundBotConnector/{Version.ToString()}",
      };
    }

    private string ApiBase()
    {
      return $"{Config["api_url"]}api";
    }

    private bool ApiRequestOk()
    {
      if (ApiInError && !ApiRetry && (LastApiAttempt.AddSeconds(ApiRetrySeconds) < DateTime.Now))
      {
        ApiRetryAttempts++;
        if (ApiRetryAttempts == 1 || ApiRetryAttempts % ApiRetryNotify == 0)
        {
          Puts(string.Format(lang.GetMessage("connector.time_in_error", this), DateTime.Now.Subtract(ApiErrorTime).ToShortString()));
        }
        ApiRetry = true;
      }
      return (!ApiInError || ApiRetry);
    }

    private void ApiError(int code, string response)
    {
      string error;
      if (ApiInError)
      {
        if (ApiRetry)
        {
          LastApiAttempt = DateTime.Now;
          ApiRetry = false;
        }

        if (ApiRetryAttempts % ApiRetryNotify != 0)
        {
          return;
        }

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
      Puts(string.Format(lang.GetMessage("connector.error", this), code, error));
    }

    private bool ApiSuccess(bool success)
    {
      // Reset retry variables if we're successful
      if (ApiInError && success)
      {
        Puts(lang.GetMessage("connector.reconnected", this));
        Puts(string.Format(lang.GetMessage("connector.time_in_error", this), DateTime.Now.Subtract(ApiErrorTime).ToShortString()));
        ApiRetryAttempts = 0;
        ApiInError = false;
        ApiRetry = true;
        Connected();
      }
      return success;
    }

    private void Connected()
    {
      foreach (Plugin plugin in plugins.GetAll())
      {
        plugin.CallHook("OnPoundBotConnected");
      }
    }
    #endregion

    #region Commands
    [Command("pbreg")]
    private void CmdPoundBotRegister(IPlayer player, string command, string[] args)
    {
      if (!ApiRequestOk())
      {
        player.Message(lang.GetMessage("connector.user_error", this, player.Id));
        return;
      }
      if (args.Length != 1)
      {
        player.Message(lang.GetMessage("usage", this, player.Id));
        return;
      }

      var da = new DiscordAuth(player.Name, args[0], (ulong) Convert.ToUInt64(player.Id));

      var body = JsonConvert.SerializeObject(da);

      webrequest.Enqueue(
        $"{ApiBase()}/discord_auth",
        body,
        (code, response) =>
        {
          if (ApiSuccess(code == 200))
          {
            player.Message(string.Format(lang.GetMessage("discord.pin", this, player.Id), da.Pin.ToString("D4")));
          }
          else if (code == 405) // Method not allowed means we're already connected
          {
            player.Message(lang.GetMessage("discord.connected", this, player.Id));
          }
          else
          {
            ApiError(code, response);
          }

        }, this, RequestMethod.PUT, Headers(), 100f);
    }
  }
  #endregion
}