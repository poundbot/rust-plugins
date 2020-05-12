// Requires: PoundBot
// Requires: Clans

using System.Collections.Generic;
using System;
using Newtonsoft.Json.Linq;
using Oxide.Core.Plugins;

namespace Oxide.Plugins
{
  [Info("Pound Bot Clans", "MrPoundsign", "2.0.1")]
  [Description("Clans support for PoundBot")]

  class PoundBotClans : CovalencePlugin
  {
    [PluginReference]
    private Plugin PoundBot, Clans;

    #region Oxide Hooks
    protected override void LoadDefaultMessages()
    {
      lang.RegisterMessages(new Dictionary<string, string>
      {
        ["sending_clans"] = "Sending all clans to PoundBot",
        ["sending_clans_tag"] = " + [{0}]",
        ["sending_clan"] = "Sending clan {0} to PoundBot",
        ["sending_clan_delete"] = "Sending clan delete for {0} to PoundBot",
      }, this);
    }
    #endregion

    void OnServerInitialized()
    {
      SendClans();
    }

    private bool AcceptedHandler(int code, string response) => (code == 200);

    void SendClans()
    {
      Puts(lang.GetMessage("sending_clans", this));

      var clan_tags = (JArray)Clans.Call("GetAllClans");
      List<JObject> clans = new List<JObject>();
      foreach (string ctag in clan_tags)
      {
        Puts(string.Format(lang.GetMessage("sending_clans_tag", this), ctag));
        clans.Add((JObject)Clans.Call("GetClan", ctag));
      }

      Func<int, string, bool> callback = AcceptedHandler;

      PoundBot.Call("API_SendClans", new object[] { this, clans, callback });
    }

    #region Clans Hooks
    void OnClanCreate(string tag)
    {
      timer.Once(1f, () =>
      {
        SendClans();
        //var clan = (JObject)Clans.Call("GetClan", tag);

        //Puts(string.Format(lang.GetMessage("sending_clan", this), tag));

        //Func<int, string, bool> callback = AcceptedHandler;

        //PoundBot.Call("API_SendClan", new object[] { this, tag, clan, callback });
      });
    }

    void OnClanUpdate(string tag) => OnClanCreate(tag);

    void OnClanDestroy(string tag)
    {
      Func<int, string, bool> callback = AcceptedHandler;

      Puts(string.Format(lang.GetMessage("sending_clan_delete", this), tag));

      PoundBot.Call("API_DeleteClan", new object[] { this, tag, callback });
    }
    #endregion

    // Re-send all clans if PoundBot reconnects
    void OnPoundBotConnected() => SendClans();
  }
}