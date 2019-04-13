using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Oxide.Core.Libraries;
using Oxide.Core.Libraries.Covalence;
using Oxide.Core.Plugins;

namespace Oxide.Plugins
{
  [Info ("Pound Bot Better Chat", "MrPoundsign", "1.0.1")]
  [Description ("Better Chat relay for use with PoundBot")]

  class PoundBotBetterChat : RustPlugin
  {
    [PluginReference]
    private Plugin PoundBot;

    protected int ApiChatRunnersCount = 0;

    class ChatMessage
    {
      public ulong SteamID { get; set; }
      public string DisplayName { get; set; }
      public string Message { get; set; }
    }


    private List<Timer> chat_runners = new List<Timer> ();

    #region Oxide Hooks
    private void Init ()
    {
      lang.RegisterMessages (
        new Dictionary<string, string> {
          { "chat.console", "{{DSCD}} {0}: {1}" },
          { "chat.discord", "<color=red>{{DSCD}}</color> <color=orange>{0}</color>: {1}" }
        },
        this
      );
    }

    void OnServerInitialized ()
    {
      StartChatRunners ();
    }

    void Unload ()
    {
      KillChatRunners ();
    }
    #endregion

    void KillChatRunners ()
    {
      foreach (var runner in chat_runners)
      {
        runner.Destroy ();
      }
      chat_runners.Clear ();
    }

    void StartChatRunners ()
    {
      var runners_to_start = Enumerable.Range (1, 2);
      foreach (int i in runners_to_start)
      {
        chat_runners.Add (startChatRunner ());
      }
    }

    private Timer startChatRunner ()
    {
      return timer.Every (1f, () =>
      {
        if (ApiChatRunnersCount < 2)
        {
          if (ApiRequestOk())
          {
            ApiChatRunnersCount++;
            webrequest.Enqueue (
              $"{ApiBase()}/chat",
              null,
              (code, response) =>
              {
                ApiChatRunnersCount--;
                switch (code)
                {
                  case 200:
                    ChatMessage message = JsonConvert.DeserializeObject<ChatMessage> (response);
                    if (message != null)
                    {
                      Puts (string.Format (lang.GetMessage ("chat.console", this), message?.DisplayName, message?.Message));
                      PrintToChat (string.Format (lang.GetMessage ("chat.discord", this), message?.DisplayName, message?.Message));
                    }
                    ApiSuccess(true);
                    break;
                  case 204:
                    ApiSuccess(true);
                    break;
                  default:
                    ApiError(code, response);
                    break;
                }

              },
              this,
              RequestMethod.GET,
              Headers(),
              120000f
            );
          }
        }
      });
    }

    void OnBetterChat (Dictionary<string, object> data)
    {
      if (ApiRequestOk())
      {
        IPlayer player = (IPlayer) data["Player"];
        var cm = new ChatMessage { };
        cm.SteamID = (ulong) Convert.ToUInt64 (player.Id);
        cm.DisplayName = player.Name;
        cm.Message = (string) data["Text"];

        var body = JsonConvert.SerializeObject (cm);

        webrequest.Enqueue (
          $"{ApiBase()}/chat",
          body,
          (code, response) => { 
            if (!ApiSuccess (code == 200))
            {
              ApiError (code, response);
            }
          },
          this,
          RequestMethod.POST,
          Headers(),
          100f
        );
      }
    }

    private bool ApiRequestOk() {
      return (bool) PoundBot?.Call("ApiRequestOk");
    }

    private string ApiBase() {
      return (string)PoundBot?.Call("ApiBase");
    }

    private bool ApiSuccess(bool success) {
      return (bool) PoundBot?.Call("ApiSuccess", success);
    }

    private void ApiError(int code, string response) {
      PoundBot?.Call("ApiError", code, response);
    }

    private Dictionary<string, string> Headers()
    {
      var headers = (Dictionary<string, string>) PoundBot?.Call("Headers");
      headers["X-PoundBotBetterChat-Version"] = Version.ToString();
      return headers;
    }
  }
}