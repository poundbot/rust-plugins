using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Oxide.Core.Libraries;
using Oxide.Core.Plugins;

namespace Oxide.Plugins
{
  [Info ("Pound Bot", "MrPoundsign", "0.4.0")]
  [Description ("Connector for PoundBot.")]

  class PoundBot : RustPlugin
  {
    protected int ApiRetrySeconds = 1;
    protected int ApiRetryNotify = 10;

    protected bool ApiInError = false;
    protected bool ApiRetry = false;
    protected uint ApiRetryAttempts = 0;
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

      public DiscordAuth (string displayname, string discordName, ulong steamid)
      {
        System.Random rnd = new System.Random ();
        this.DisplayName = displayname;
        this.DiscordName = discordName;
        this.SteamID = steamid;
        this.Pin = rnd.Next (1, 9999);
        this.CreatedAt = DateTime.UtcNow;
      }
    }

    #region API

    private Dictionary<string, string> headers ()
    {
      return new Dictionary<string, string>
      { { "Content-type", "application/json" },
        { "Authorization", $"Token {Config["api_key"]}" },
        { "X-PoundBotConnector-Version", Version.ToString () },
        { "User-Agent", $"PoundBotConnector/{Version.ToString()}" }
      };
    }

    private string ApiBase () {
      return $"{Config["api_url"]}api";
    }

    private bool ApiRequestOk ()
    {
      if (ApiInError && !ApiRetry && (LastApiAttempt.AddSeconds (ApiRetrySeconds) < DateTime.Now))
      {
        ApiRetryAttempts++;
        if (ApiRetryAttempts == 1 || ApiRetryAttempts % ApiRetryNotify == 0)
        {
          Puts (string.Format (lang.GetMessage ("connector.time_in_error", this), DateTime.Now.Subtract (ApiErrorTime).ToShortString ()));
        }
        ApiRetry = true;
      }
      return (!ApiInError || ApiRetry);
    }

    private void ApiError (int code, string response)
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
          var air = JsonConvert.DeserializeObject<ApiErrorResponse> (response);
          error = air.Error;
        }
        catch
        {
          error = response;
        }
      }
      Puts (string.Format (lang.GetMessage ("connector.error", this), code, error));
    }

    private bool ApiSuccess (bool success)
    {
      // Reset retry variables if we're successful
      if (ApiInError && success)
      {
        Puts (lang.GetMessage ("connector.reconnected", this));
        Puts (string.Format (lang.GetMessage ("connector.time_in_error", this), DateTime.Now.Subtract (ApiErrorTime).ToShortString ()));
        ApiRetryAttempts = 0;
        ApiInError = false;
        ApiRetry = true;
      }
      return success;
    }
    #endregion

    #region Configuration
    protected override void LoadDefaultConfig ()
    {
      Config["api_url"] = "http://poundbot.mrpoundsign.com/";
      Config["api_key"] = "API KEY HERE";
    }
    #endregion

    #region Oxide Hooks
    private void Init ()
    {
      lang.RegisterMessages (
        new Dictionary<string, string>
        { { "connector.reconnected", "Reconnected with PoundBot" },
          { "connector.time_in_error", "Total time in error: {0}" },
          { "connector.error", "Error communicating with PoundBot: {0}/{1}" },
          { "connector.user_error", "Cannot connect to PoundBot right now. Please alert the admins." },
          { "discord.pin", "Enter the following PIN to the bot in discord: {0}." },
          { "discord.connected", "You are connected to discord." },
          { "usage", "Usage: /pbreg \"<discord name>\"\n Example: /discord \"Fancy Guy#8080\"" },
        },
        this
      );
    }

    #endregion

    #region Commands
    [ChatCommand ("pbreg")]
    private void cmdPbreg (BasePlayer player, string command, string[] args)
    {
      if (ApiRequestOk ())
      {
        if (args.Count () != 1)
        {
          PrintToChat (player, lang.GetMessage ("usage", this, player.IPlayer.Id));
          return;
        }

        var da = new DiscordAuth (player.displayName, args[0], player.userID);

        var body = JsonConvert.SerializeObject (da);

        webrequest.Enqueue (
          $"{ApiBase()}/discord_auth",
          body,
          (code, response) =>
          {
            if (ApiSuccess (code == 200))
            {
              PrintToChat (player, string.Format (lang.GetMessage ("discord.pin", this, player.IPlayer.Id), da.Pin.ToString ("D4")));
            }
            else if (code == 405) // Method not allowed means we're already connected
            {
              PrintToChat (player, lang.GetMessage ("discord.connected", this, player.IPlayer.Id), da.Pin.ToString ("D4"));
            }
            else
            {
              ApiError (code, response);
            }

          }, this, RequestMethod.PUT, headers (), 100f);
      }
      else
      {
        PrintToChat (player, lang.GetMessage ("connector.user_error", this, player.IPlayer.Id));
      }
    }

    #endregion
  }
}