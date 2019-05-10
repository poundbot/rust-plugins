// Requires: PoundBot

using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Oxide.Core.Libraries.Covalence;
using Oxide.Core.Plugins;

namespace Oxide.Plugins
{
  [Info("Pound Bot Chat Relay", "MrPoundsign", "1.2.2")]
  [Description("Chat relay for use with PoundBot")]

  class PoundBotChatRelay : CovalencePlugin
  {
    [PluginReference]
    private Plugin PoundBot;

    const string ChatURI = "/chat";

    protected int ApiChatRunnersCount;
    private Dictionary<string, string> RequestHeaders;
    private bool RelayDiscordChat;
    private bool RelayGiveNotices;
    private bool RelayServerChat;
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
      Config["config.version"] = "1.1.3";
      Config["relay.chat"] = true;
      Config["relay.betterchat"] = false;
      Config["relay.serverchat"] = true;
      Config["relay.givenotices"] = true;
      Config["relay.discordchat"] = true;
    }

    void UpgradeConfig()
    {
      if (Config["config.version"] == null || (string)Config["config.version"] != "1.1.3")
      {
        LogWarning(string.Format(lang.GetMessage("config.upgrading", this), "1.1.3"));
        if ((bool)Config["relay.betterchat"])
        {
          Config["relay.chat"] = true;
        }
        Config["config.version"] = "1.1.3";
        Config["relay.givenotices"] = (bool)Config["relay.serverchat"];
        SaveConfig();
      }
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
          ["config.upgrading"] = "Upgrading config to v{0}"
        }, this);
    }

    void OnServerInitialized()
    {
      UpgradeConfig();
      RequestHeaders = new Dictionary<string, string>
      {
        ["X-PoundBotChatRelay-Version"] = Version.ToString()
      };

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

    void Unload() => KillChatRunners();
    #endregion

    #region Chat Runners
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
    #endregion

    #region PoundBot Requests
    private bool ChatReceiveHandler(int code, string response)
    {
      ApiChatRunnersCount--;
      switch (code)
      {
        case 200:
          ChatMessage message = JsonConvert.DeserializeObject<ChatMessage>(response);
          if (message != null)
          {
            string chatName = message?.DisplayName;
            string consoleName = message?.DisplayName;

            if ((chatName != null) && (message.ClanTag != string.Empty))
            {
              chatName = $"{string.Format(lang.GetMessage("chat.ClanTag", this), message.ClanTag)}{chatName}";
              consoleName = $"{string.Format(lang.GetMessage("console.ClanTag", this), message.ClanTag)}{consoleName}";
            }

            Puts(string.Format(lang.GetMessage("console.Msg", this), consoleName, message?.Message));

            string chatMessage = string.Format(lang.GetMessage("chat.Msg", this), chatName, message?.Message);
            foreach (IPlayer p in players.Connected)
            {
              p.Message(chatMessage);
            }
          }
          return true;
        case 204: // Status No Content
          return true;
      }

      return false;
    }

    private bool ChatSendHandler(int code, string response) => code == 200;

    private Timer StartChatRunner()
    {
      Func<int, string, bool> callback = ChatReceiveHandler;

      return timer.Every(1f, () =>
        {
          if (ApiChatRunnersCount < 2 &&
              (bool)PoundBot.Call("API_RequestGet", new object[] { ChatURI, null, callback, this, RequestHeaders })
          ) ApiChatRunnersCount++;
        }
      );
    }
    #endregion

    void OnServerMessage(string message, string name)
    {
      var isGaveMessage = (message.Contains("gave") && name == "SERVER");
      if (!RelayServerChat && !isGaveMessage) return;
      if (!RelayGiveNotices && isGaveMessage) return;

      var cm = new ChatMessage { };
      cm.DisplayName = name;
      cm.Message = message;

      SendToPoundBot(cm);
    }

    void OnUserChat(IPlayer player, string message) => SendToPoundBot(IPlayerMessage(player, message));

    void OnBetterChat(Dictionary<string, object> data)
    {
      if (!UseBetterChat) return;

      SendToPoundBot(IPlayerMessage((IPlayer)data["Player"], (string)data["Text"]));
    }

    private ChatMessage IPlayerMessage(IPlayer player, string message)
    {
      return new ChatMessage
      {
        PlayerID = player.Id,
        DisplayName = player.Name,
        Message = message
      };
    }

    void SendToPoundBot(ChatMessage cm)
    {
      var body = JsonConvert.SerializeObject(cm);

      Func<int, string, bool> callback = ChatSendHandler;

      PoundBot.Call(
        "API_RequestPost",
        new object[] { ChatURI, body, callback, this, RequestHeaders }
      );
    }
  }
}