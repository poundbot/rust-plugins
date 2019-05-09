// Requires: PoundBot
// Requires: Clans

using System.Collections.Generic;
using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Oxide.Core.Plugins;

namespace Oxide.Plugins
{
  [Info("Pound Bot Clans", "MrPoundsign", "1.2.0")]
  [Description("Clans support for PoundBot")]

  class PoundBotClans : CovalencePlugin
  {
    [PluginReference]
    private Plugin PoundBot, Clans;

    const string ClansURI = "/clans";

    private Dictionary<string, string> RequestHeaders;

    #region Oxide Hooks
    protected override void LoadDefaultMessages()
    {
      lang.RegisterMessages(new Dictionary<string, string>
      {
        ["sending_clans"] = "Sending all clans to PoundBot",
        ["sending_clan"] = "Sending clan {0} to PoundBot",
        ["sending_clan_delete"] = "Sending clan delete for {0} to PoundBot",
      }, this);
    }
    #endregion

    void OnServerInitialized()
    {
      RequestHeaders = (Dictionary<string, string>)PoundBot?.Call("Headers");
      RequestHeaders["X-PoundBotClans-Version"] = Version.ToString();
      SendClans();
    }

    private bool AcceptedHandler(int code, string response)
    {
      return (code == 200);
    }

    void SendClans()
    {
      Puts(lang.GetMessage("sending_clans", this));

      var clan_tags = (JArray)Clans.Call("GetAllClans");
      List<JObject> clans = new List<JObject>();
      foreach (string ctag in clan_tags)
      {
        clans.Add((JObject)Clans.Call("GetClan", ctag));
      }

      Func<int, string, bool> callback = AcceptedHandler;

      PoundBot?.Call("API_RequestPut", new object[] { ClansURI, JsonConvert.SerializeObject(clans), callback, this, RequestHeaders, 100f });
    }

    #region Clans Hooks
    void OnClanCreate(string tag)
    {
      var clan = (JObject)Clans.Call("GetClan", tag);

      Puts(lang.GetMessage("sending_clans", this));

      Func<int, string, bool> callback = AcceptedHandler;

      PoundBot?.Call("API_RequestPut", new object[] { $"{ClansURI}/{tag}", JsonConvert.SerializeObject(clan), callback, this, RequestHeaders, 100f });
    }

    void OnClanUpdate(string tag) => OnClanCreate(tag);

    void OnClanDestroy(string tag)
    {
      Func<int, string, bool> callback = AcceptedHandler;

      Puts(string.Format(lang.GetMessage("sending_clan_delete", this), tag));

      PoundBot?.Call("API_RequestDelete", new object[] { $"{ClansURI}/{tag}", null, callback, this, RequestHeaders, 100f });
    }
    #endregion

    // Re-send all clans if PoundBot reconnects
    void OnPoundBotConnected() => SendClans();
  }
}