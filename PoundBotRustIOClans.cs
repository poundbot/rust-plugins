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
  [Info ("Pound Bot RustIO Clans", "MrPoundsign", "1.0.0")]
  [Description ("RustIO Clans for PoundBot")]

  class PoundBotRustIOClans : RustPlugin
  {
    [PluginReference]
    Plugin PoundBot;
    Plugin Clans;

    #region Oxide Hooks
    private void Init ()
    {
      lang.RegisterMessages (
        new Dictionary<string, string>
        {
          { "connector.sending_clans", "Sending clans data to PoundBot" },
          { "connector.sending_clan", "Sending clan {0} to PoundBot" },
          { "connector.sending_clan_delete", "Sending clan delete for {0} to PoundBot" },
        },
        this
      );
    }

    void OnServerInitialized ()
    {
      if (Clans != null)
      {
        var clan_tags = (JArray) Clans.Call ("GetAllClans");
        List<JObject> clans = new List<JObject> ();
        foreach (string ctag in clan_tags)
        {
          clans.Add ((JObject) Clans.Call ("GetClan", ctag));
        }
        var body = JsonConvert.SerializeObject (clans);

        if (ApiRequestOk ())
        {
          Puts (lang.GetMessage ("connector.sending_clans", this));
          webrequest.Enqueue (
            $"{ApiBase()}/clans",
            body,
            (code, response) =>
            {
              if (!ApiSuccess (code == 200))
              {
                ApiError (code, response);
              }

            },
            this, RequestMethod.PUT, headers (), 100f);
        }
      }
    }

    void OnClanCreate (string tag)
    {
      if (Clans != null)
      {
        var clan = (JObject) Clans.Call ("GetClan", tag);
        var body = JsonConvert.SerializeObject (clan);

        if (ApiRequestOk ())
        {
          Puts (string.Format (lang.GetMessage ("connector.sending_clan", this), tag));
          webrequest.Enqueue (
            $"{ApiBase()}/clans/{tag}",
            body,
            (code, response) =>
            {
              if (!ApiSuccess (code == 200))
              {
                ApiError (code, response);
              }

            }, this, RequestMethod.PUT, headers (), 100f);
        }
      }
    }

    void OnClanUpdate (string tag) { OnClanCreate (tag); }

    void OnClanDestroy (string tag)
    {
      if (ApiRequestOk ())
      {
        Puts (string.Format (lang.GetMessage ("connector.sending_clan_delete", this), tag));
        webrequest.Enqueue (
          $"{ApiBase()}/clans/{tag}",
          null,
          (code, response) =>
          {
            if (!ApiSuccess (code == 200))
            {
              ApiError (code, response);
            }

          }, this, RequestMethod.DELETE, headers (), 100f);
      }
    }
    #endregion

    private bool ApiRequestOk() {
      return (bool) PoundBot?.Call("ApiRequestOk");
    }

    private string ApiBase() {
      Puts((string)PoundBot?.Call("ApiBase"));
      return (string)PoundBot?.Call("ApiBase");
    }

    private bool ApiSuccess(bool success) {
      return (bool) PoundBot?.Call("ApiSuccess", success);
    }

    private void ApiError(int code, string response) {
      PoundBot?.Call("ApiError", code, response);
    }

    private Dictionary<string,string> headers() {
      return (Dictionary<string,string>) PoundBot?.Call("headers");
    }
  }
}