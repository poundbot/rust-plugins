using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Oxide.Core.Libraries;
using Oxide.Core.Plugins;
using UnityEngine;

namespace Oxide.Plugins
{
  [Info("Pound Bot Clans", "MrPoundsign", "1.0.1")]
  [Description("Clans support for PoundBot")]

  class PoundBotClans : RustPlugin
  {
    [PluginReference]
    Plugin PoundBot;
    [PluginReference]
    Plugin Clans;

    #region Oxide Hooks
    protected override void LoadDefaultMessages()
    {
      lang.RegisterMessages(new Dictionary<string, string>
      {
        ["sending_clans"] = "Sending clans data to PoundBot",
        ["sending_clan"] = "Sending clan {0} to PoundBot",
        ["sending_clan_delete"] = "Sending clan delete for {0} to PoundBot",
      }, this);
    }

    void Loaded()
    {
      SendClans();
    }
    #endregion

    void OnPluginLoaded(Plugin name)
    {
      if (name == Clans || name == PoundBot)
      {
        Puts($"Plugin '{name}' has been loaded");
        SendClans();
      }
    }

    void SendClans()
    {
      if (Clans == null)
      {
        Puts("RustIO Clans not yet loaded.");
        return;
      }
      if (PoundBot == null)
      {
        Puts("PoundBot not yet loaded.");
        return;
      }
      if (Clans != null && PoundBot != null)
      {
        var clan_tags = (JArray) Clans.Call("GetAllClans");
        List<JObject> clans = new List<JObject>();
        foreach (string ctag in clan_tags)
        {
          clans.Add((JObject) Clans.Call("GetClan", ctag));
        }
        var body = JsonConvert.SerializeObject(clans);

        if (ApiRequestOk())
        {
          Puts(lang.GetMessage("sending_clans", this));
          webrequest.Enqueue(
            $"{ApiBase()}/clans",
            body,
            (code, response) =>
            {
              if (!ApiSuccess(code == 200))
              {
                ApiError(code, response);
              }

            },
            this, RequestMethod.PUT, Headers(), 100f);
        }
        return;
      }
      Puts("Clans not loaded!");
    }

    #region Clans Hooks
    void OnClanCreate(string tag)
    {
      if (Clans != null)
      {
        var clan = (JObject) Clans.Call("GetClan", tag);
        var body = JsonConvert.SerializeObject(clan);

        if (ApiRequestOk())
        {
          Puts(string.Format(lang.GetMessage("sending_clan", this), tag));
          webrequest.Enqueue(
            $"{ApiBase()}/clans/{tag}",
            body,
            (code, response) =>
            {
              if (!ApiSuccess(code == 200))
              {
                ApiError(code, response);
              }

            }, this, RequestMethod.PUT, Headers(), 100f);
        }
      }
    }

    void OnClanUpdate(string tag) { OnClanCreate(tag); }

    void OnClanDestroy(string tag)
    {
      if (ApiRequestOk())
      {
        Puts(string.Format(lang.GetMessage("sending_clan_delete", this), tag));
        webrequest.Enqueue(
          $"{ApiBase()}/clans/{tag}",
          null,
          (code, response) =>
          {
            if (!ApiSuccess(code == 200))
            {
              ApiError(code, response);
            }

          }, this, RequestMethod.DELETE, Headers(), 100f);
      }
    }
    #endregion

    void OnPoundBotConnected()
    {
      SendClans();
    }

    private bool ApiRequestOk()
    {
      return (bool) PoundBot?.Call("ApiRequestOk");
    }

    private string ApiBase()
    {
      return (string) PoundBot?.Call("ApiBase");
    }

    private bool ApiSuccess(bool success)
    {
      return (bool) PoundBot?.Call("ApiSuccess", success);
    }

    private void ApiError(int code, string response)
    {
      PoundBot?.Call("ApiError", code, response);
    }

    private Dictionary<string, string> Headers()
    {
      var headers = (Dictionary<string, string>) PoundBot?.Call("Headers");
      headers["X-PoundBotClans-Version"] = Version.ToString();
      return headers;
    }
  }
}