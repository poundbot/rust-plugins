using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Oxide.Core.Libraries;
using Oxide.Core.Libraries.Covalence;
using Oxide.Core.Plugins;

namespace Oxide.Plugins
{
  [Info("Pound Bot Chat Relay", "MrPoundsign", "1.0.7")]
  [Description("Chat relay for use with PoundBot")]

  class PoundBotChatRelay : RustPlugin
  {
    [PluginReference]
    private Plugin PoundBot;

    protected int ApiChatRunnersCount;

    class ChatMessage
    {
      public ulong SteamID { get; set; }
      public string ClanTag { get; set; }
      public string DisplayName { get; set; }
      public string Message { get; set; }
    }

    private List<Timer> chat_runners = new List<Timer>();

    #region Configuration
    protected override void LoadDefaultConfig()
    {
      Config["relay.chat"] = true;
      Config["relay.serverchat"] = true;
      Config["relay.discordchat"] = true;
      Config["relay.betterchat"] = false;
    }
    #endregion

    #region Oxide Hooks
    protected override void LoadDefaultMessages()
    {
      lang.RegisterMessages(
        new Dictionary<string, string>
        {
          ["chat.ClanTag"] = "<color=blue>[{0}]</color> ",
          ["chat.Msg"] = "<color=red>{{DSCD}}</color> <color=orange>{0}</color>: {1}",
          ["console.ClanTag"] = "[{0}] ",
          ["console.Msg"] = "{{DSCD}} {0}: {1}",
        }, this);
    }

    void Loaded()
    {
      if (PoundBot != null) StartChatRunners();
    }

    void Unload()
    {
      KillChatRunners();
    }

    void OnPluginLoaded(Plugin p)
    {
      if (!(bool) Config["relay.discordchat"]) return;
      if (p.Name == "PoundBot")
      {
        Puts($"Plugin '{p}' has been loaded");
        StartChatRunners();
      }
    }

    void OnPluginUnloaded(Plugin name)
    {
      if (!(bool) Config["relay.discordchat"]) return;
      if (name.Name == "PoundBot")
      {
        KillChatRunners();
      }
    }
    #endregion

    void KillChatRunners()
    {
      if (!(bool) Config["relay.discordchat"]) return;
      Puts("Killing chat runners");
      foreach (var runner in chat_runners)
      {
        runner.Destroy();
      }
      chat_runners.Clear();
    }

    void StartChatRunners()
    {
      if (!(bool) Config["relay.discordchat"]) return;
      if (chat_runners.Count != 0) { return; }
      if (PoundBot == null)
      {
        Puts("Waiting for PoundBot load before starting runners");
        return;
      }
      Puts("Starting chat runners");
      var runners_to_start = Enumerable.Range(1, 2);
      foreach (int i in runners_to_start)
      {
        Puts($"Started chat runner {i}");
        chat_runners.Add(StartChatRunner());
      }
    }

    private Timer StartChatRunner()
    {
      if (!(bool) Config["relay.discordchat"]) return null;
      return timer.Every(1f, () =>
      {
        if (ApiChatRunnersCount < 2)
        {
          if (ApiRequestOk())
          {
            ApiChatRunnersCount++;
            webrequest.Enqueue(
              $"{ApiBase()}/chat",
              null,
              (code, response) =>
              {
                ApiChatRunnersCount--;
                switch (code)
                {
                  case 200:
                    ChatMessage message = JsonConvert.DeserializeObject<ChatMessage>(response);
                    if (message != null)
                    {
                      var chatName = message?.DisplayName;
                      var consoleName = message?.DisplayName;
                      if ((chatName != null) && (message.ClanTag != String.Empty))
                      {
                        chatName = $"{string.Format(lang.GetMessage("chat.ClanTag", this), message.ClanTag)}{chatName}";
                        consoleName = $"{string.Format(lang.GetMessage("console.ClanTag", this), message.ClanTag)}{consoleName}";
                      }
                      Puts(string.Format(lang.GetMessage("console.Msg", this), consoleName, message?.Message));
                      PrintToChat(string.Format(lang.GetMessage("chat.Msg", this), chatName, message?.Message));
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

    object OnServerMessage(string message, string name, string color, ulong id)
    {
      if ((bool) Config["relay.serverchat"] || !ApiRequestOk()) return null;
      var cm = new ChatMessage { };
      cm.DisplayName = name;
      cm.Message = message;

      var body = JsonConvert.SerializeObject(cm);

      webrequest.Enqueue(
        $"{ApiBase()}/chat",
        body,
        (code, response) =>
        {
          if (!ApiSuccess(code == 200))
          {
            ApiError(code, response);
          }
        },
        this,
        RequestMethod.POST,
        Headers(),
        100f
      );
      return null;
    }

    object OnMessagePlayer(string message, BasePlayer player)
    {
      if (!(bool) Config["relay.chat"]) return null;

      Puts("OnMessagePlayer works!");
      return null;
    }

    void OnBetterChat(Dictionary<string, object> data)
    {
      if ((bool) Config["relay.betterchat"] && ApiRequestOk())
      {
        IPlayer player = (IPlayer) data["Player"];
        var cm = new ChatMessage { };
        cm.SteamID = (ulong) Convert.ToUInt64(player.Id);
        cm.DisplayName = player.Name;
        cm.Message = (string) data["Text"];

        var body = JsonConvert.SerializeObject(cm);

        webrequest.Enqueue(
          $"{ApiBase()}/chat",
          body,
          (code, response) =>
          {
            if (!ApiSuccess(code == 200))
            {
              ApiError(code, response);
            }
          },
          this,
          RequestMethod.POST,
          Headers(),
          100f
        );
      }
    }

    private bool ApiRequestOk()
    {
      if (PoundBot == null) return false;
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
      headers["X-PoundBotChatRelay-Version"] = Version.ToString();
      return headers;
    }
  }
}