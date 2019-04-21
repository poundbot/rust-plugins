// Requires: PoundBot
// Requires: Clans

using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Oxide.Core.Libraries;
using Oxide.Core.Plugins;

namespace Oxide.Plugins
{
  [Info("Pound Bot Clans", "MrPoundsign", "1.1.0")]
  [Description("Clans support for PoundBot")]

  class PoundBotClans : RustPlugin
  {
    [PluginReference]
    private Plugin PoundBot, Clans;

    private Dictionary<string, string> RequestHeaders;
    private string ClansURI;

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
      ClansURI = $"{(string)PoundBot?.Call("ApiBase")}/clans";
      SendClans();
    }

    void SendClans()
    {
      var clan_tags = (JArray)Clans.Call("GetAllClans");
      List<JObject> clans = new List<JObject>();
      foreach (string ctag in clan_tags)
      {
        clans.Add((JObject)Clans.Call("GetClan", ctag));
      }
      var body = JsonConvert.SerializeObject(clans);

      if (ApiRequestOk())
      {
        Puts(lang.GetMessage("sending_clans", this));
        webrequest.Enqueue(
          ClansURI,
          body,
          (code, response) =>
          {
            if (!ApiSuccess(code == 200)) { ApiError(code, response); }
          },
          this, RequestMethod.PUT, RequestHeaders, 100f);
      }
    }

    #region Clans Hooks
    void OnClanCreate(string tag)
    {
      var clan = (JObject)Clans.Call("GetClan", tag);
      var body = JsonConvert.SerializeObject(clan);

      if (ApiRequestOk())
      {
        Puts(string.Format(lang.GetMessage("sending_clan", this), tag));
        webrequest.Enqueue(
          $"{ClansURI}/{tag}",
          body,
          (code, response) =>
          {
            if (!ApiSuccess(code == 200)) { ApiError(code, response); }
          }, this, RequestMethod.PUT, RequestHeaders, 100f
        );
      }
    }

    void OnClanUpdate(string tag) { OnClanCreate(tag); }

    void OnClanDestroy(string tag)
    {
      if (!ApiRequestOk()) return;

      Puts(string.Format(lang.GetMessage("sending_clan_delete", this), tag));
      webrequest.Enqueue(
        $"{ClansURI}/{tag}", null,
        (code, response) => { if (!ApiSuccess(code == 200)) ApiError(code, response); },
        this, RequestMethod.DELETE, RequestHeaders, 100f);
    }
    #endregion

    // Re-send all clans if PoundBot reconnects
    void OnPoundBotConnected() { SendClans(); }

    private bool ApiRequestOk()
    {
      return (bool)PoundBot?.Call("ApiRequestOk");
    }

    private bool ApiSuccess(bool success)
    {
      return (bool)PoundBot?.Call("ApiSuccess", success);
    }

    private void ApiError(int code, string response)
    {
      PoundBot?.Call("ApiError", code, response);
    }
  }
}