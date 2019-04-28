// Requires: PoundBot

using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Oxide.Core.Libraries;
using Oxide.Core.Libraries.Covalence;
using Oxide.Core.Plugins;

namespace Oxide.Plugins
{
  [Info("Pound Bot Chat Relay", "MrPoundsign", "1.1.5")]
  [Description("Chat relay for use with PoundBot")]

  class PoundBotChatRelay : CovalencePlugin
  {
    [PluginReference]
    private Plugin PoundBot;

    protected int ApiChatRunnersCount;
    private Dictionary<string, string> RequestHeaders;
    private string ChatURI;
    private bool RelayDiscordChat;
    private bool RelayGiveNotices;
    private bool RelayServerChat;
    // Temporary work-around until I can figure out why Unsubscribe("OnBetterChat") is not working
    private bool UseBetterChat;

    class ChatMessage
    {
      public string PlayerID { get; set; }
      public string ClanTag { get; set; }
      public string DisplayName { get; set; }
      public string Message { get; set; }
    }

    private List<Timer> chat_runners = new List<Timer>();

    #region Configuration
    protected override void LoadDefaultConfig()
    {
      LogWarning("Creating a new configuration file");
      Config["version"] = "1.1.3";
      Config["relay.chat"] = true;
      Config["relay.serverchat"] = true;
      Config["relay.givenotices"] = true;
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
          ["chat.ClanTag"] = "[{0}] ",
          ["chat.Msg"] = "{{DSCD}} {0}: {1}",
          ["console.ClanTag"] = "[{0}] ",
          ["console.Msg"] = "{{DSCD}} {0}: {1}",
          ["console.DualChatWarning"] = "You have both relay.chat and relay.betterchat enabled. You may get duplicate messages. Please disable one or the other."
        }, this);
    }

    void OnServerInitialized()
    {
      UpgradeConfig();
      RequestHeaders = (Dictionary<string, string>)PoundBot?.Call("Headers");
      RequestHeaders["X-PoundBotChatRelay-Version"] = Version.ToString();

      ChatURI = $"{(string)PoundBot?.Call("ApiBase")}/chat";
      if (!(bool)Config["relay.chat"])
      {
        Unsubscribe("OnUserChat");
        Unsubscribe("OnBetterChat");
      }
      else
      {
        if ((bool)Config["relay.betterchat"])
        {
          Unsubscribe("OnUserChat");
          // Temporary work-around until I can figure out why Unsubscribe("OnBetterChat") is not working
          UseBetterChat = true;
        }
        else
        {
          Unsubscribe("OnBetterChat");
        }
      }

      RelayServerChat = (bool)Config["relay.serverchat"];
      RelayGiveNotices = (bool)Config["relay.givenotices"];
      
      if (!RelayServerChat && !RelayGiveNotices) Unsubscribe("OnServerMessage");

      RelayDiscordChat = (bool)Config["relay.discordchat"];
      
      StartChatRunners();
    }

    void UpgradeConfig()
    {
      if (Config["config.version"] == null || (string)Config["config.version"] != "1.1.3")
      {
        LogWarning("Upgrading config to 1.1.3");
        if ((bool)Config["relay.betterchat"])
        {
          Config["relay.chat"] = true;
        }
        Config["config.version"] = "1.1.3";
        Config["relay.givenotices"] = (bool)Config["relay.serverchat"];
        SaveConfig();
      }
    }

    void Unload() { KillChatRunners(); }
    #endregion

    void KillChatRunners()
    {
      if (!RelayDiscordChat) return;
      Puts("Killing chat runners");
      foreach (var runner in chat_runners)
      {
        runner.Destroy();
      }
      chat_runners.Clear();
    }

    // Chat runners connect to PoundBot and wait for new chat messages
    // from Discord to send to global chat
    void StartChatRunners()
    {
      if (!RelayDiscordChat || chat_runners.Count != 0) return;

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
      return timer.Every(1f, () =>
      {
        if (ApiChatRunnersCount < 2 && ApiRequestOk())
        {
          ApiChatRunnersCount++;
          webrequest.Enqueue(
            ChatURI, null,
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

                    var chatMessage = string.Format(lang.GetMessage("chat.Msg", this), chatName, message?.Message);

                    foreach (IPlayer p in players.Connected)
                    {
                      p.Message(chatMessage);
                    }
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
            this, RequestMethod.GET, RequestHeaders, 120000f
          );
        }
      });
    }

    void OnServerMessage(string message, string name)
    {
      var isGaveMessage = (message.Contains("gave") && name == "SERVER");
      if (!ApiRequestOk()) return;
      if (!RelayServerChat && !isGaveMessage) return;
      if (!RelayGiveNotices && isGaveMessage) return;

      var cm = new ChatMessage { };
      cm.DisplayName = name;
      cm.Message = message;

      SendToPoundBot(cm);
    }

    void OnUserChat(IPlayer player, string message)
    {
      if (!ApiRequestOk()) return;

      SendToPoundBot(IPlayerMessage(player, message));
    }

    void OnBetterChat(Dictionary<string, object> data)
    {
      // Temporary work-around until I can figure out why Unsubscribe("OnBetterChat") is not working
      if (!UseBetterChat) return;

      if (!ApiRequestOk()) return;
      SendToPoundBot(IPlayerMessage((IPlayer)data["Player"], (string)data["Text"]));
    }

    private ChatMessage IPlayerMessage(IPlayer player, string message)
    {
      var cm = new ChatMessage { };
      cm.PlayerID = player.Id;
      cm.DisplayName = player.Name;
      cm.Message = message;

      return cm;
    }

    void SendToPoundBot(ChatMessage cm)
    {
      var body = JsonConvert.SerializeObject(cm);

      webrequest.Enqueue(
        ChatURI, body,
        (code, response) => { if (!ApiSuccess(code == 200)) ApiError(code, response); },
        this, RequestMethod.POST, RequestHeaders, 100f
      );
    }

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