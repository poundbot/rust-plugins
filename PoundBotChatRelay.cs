// Requires: PoundBot

using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Oxide.Core.Libraries.Covalence;
using Oxide.Core.Plugins;

namespace Oxide.Plugins
{
  [Info("Pound Bot Chat Relay", "MrPoundsign", "1.2.3")]
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
    private string RelayChatChannel;
    private string RelayServerChannel;

    class ChatMessage
    {
      public string PlayerID { get; set; }
      public string ClanTag { get; set; }
      public string DisplayName { get; set; }
      public string Message { get; set; }
    }

    class ChatRunner
    {
      public string ID { get; }
      public DateTime LastRun { get; set; }
      public bool Running { get; set; }
      public bool LastStartOK { get; set; }
      public Timer Timer { get; set; }

      public ChatRunner()
      {
        ID = Guid.NewGuid().ToString();
        LastRun = DateTime.UtcNow;
      }
    }

    private List<ChatRunner> ChatRunners = new List<ChatRunner>();

    #region Configuration
    protected override void LoadDefaultConfig()
    {
      LogWarning("Creating a new configuration file");
      Config["config.version"] = 3;
      Config["relay.chat"] = true;
      Config["relay.betterchat"] = false;
      Config["relay.serverchat"] = true;
      Config["relay.givenotices"] = true;
      Config["relay.discordchat"] = true;
      Config["chat.channel"] = "";
      Config["chat.server_channel"] = "";
    }
    
    void UpgradeConfig()
    {
      string configVersion = "config.version";
      bool dirty = false;
      if (Config[configVersion] == null)
      {
        Config[configVersion] = 1;
      }
      else
      {
        try
        {
          var foo = (string)Config[configVersion];
          Config["config.version"] = 2;
        }
        catch (InvalidCastException) { } // testing if it can be converted to a string or not. No need to change it because it's not a string.
      }

      if ((int)Config[configVersion] < 2) {
        LogWarning(string.Format(lang.GetMessage("config.upgrading", this), "2"));
        if ((bool)Config["relay.betterchat"])
        {
          Config["relay.chat"] = true;
        }
        Config["relay.givenotices"] = (bool)Config["relay.serverchat"];
        dirty = true;
      }

      if ((int)Config[configVersion] <3)
      {
        LogWarning(string.Format(lang.GetMessage("config.upgrading", this), "3"));
        Config[configVersion] = 3;
        Config["chat.channel"] = "";
        Config["chat.server_channel"] = "";
        dirty = true;
      }
      if (dirty) SaveConfig();
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

      RelayChatChannel = (string)Config["chat.channel"];
      RelayServerChannel = (string) Config["chat.server_channel"];

      StartChatRunners();
    }

    void Unload() => KillChatRunners();
    #endregion

    #region Chat Runners
    void KillChatRunners()
    {
      if (!RelayDiscordChat) return;
      Puts("Killing chat runners");
      foreach (var runner in ChatRunners)
      {
        runner.Timer.Destroy();
      }
      ChatRunners.Clear();
    }

    // Chat runners connect to PoundBot and wait for new chat messages
    // from Discord to send to global chat
    void StartChatRunners()
    {
      if (!RelayDiscordChat) return;

      Puts("Starting chat runners");
      var runners_to_start = Enumerable.Range(1, 2);
      foreach (int i in runners_to_start)
      {
        Puts($"Started chat runner {i}");
        ChatRunners.Add(StartChatRunner());
      }
    }

    void RestartChatRunner(string id)
    {
      int index = ChatRunners.FindIndex(x => x.ID == id);
      if (index < 0)
      {
        Puts($"Could not find ChatRunner with ID {id}");
        return;
      }
      ChatRunners[index].Timer.Destroy();
      ChatRunners.RemoveAt(index);
      ChatRunners.Add(StartChatRunner());
    }
    #endregion

    #region PoundBot Requests
    private ChatRunner StartChatRunner()
    {
      ChatRunner cr = new ChatRunner();
      Func<int, string, bool> callback = (int code, string response) =>
      {
        cr.Running = false;
        switch (code)
        {
          case 200:
            ChatMessage message;
            try
            {
              message = JsonConvert.DeserializeObject<ChatMessage>(response);
            }
            catch (JsonReaderException)
            {
              Puts($"Could not decode JSON message from body: {response}");
              return true;
            }
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
      };

      cr.Timer = timer.Every(1f, () =>
      {
        if (!cr.Running)
        {
          if ((bool)PoundBot.Call("API_RequestGet", new object[] { ChatURI, null, callback, this, RequestHeaders }))
          {
            cr.LastRun = DateTime.UtcNow;
            cr.Running = true;
            cr.LastStartOK = true;
          }
          else
          {
            // The API is down or could not start
            cr.LastStartOK = false;
          }
        }
        else if (cr.LastStartOK && cr.LastRun.AddSeconds(60) < DateTime.UtcNow)
        {
          cr.Running = false;
          RestartChatRunner(cr.ID);
        }
      });

      return cr;
    }
    #endregion

    void OnServerMessage(string message, string name)
    {
      var isGaveMessage = (message.Contains("gave") && name == "SERVER");
      if (!RelayServerChat && !isGaveMessage) return;
      if (!RelayGiveNotices && isGaveMessage) return;

      // var cm = new ChatMessage { };
      // cm.DisplayName = name;
      // cm.Message = message;
      // cm.ChannelName = RelayServerChannel;

      SendToPoundBot(name, RelayServerChannel, message);
    }

    void OnUserChat(IPlayer player, string message) => SendToPoundBot(player.Name, RelayChatChannel, message);

    void OnBetterChat(Dictionary<string, object> data)
    {
      if (!UseBetterChat) return;

      //SendToPoundBot((IPlayer)data["Player"], (string)data["Text"], RelayChatChannel);
    }

    void SendToPoundBot(string player, string channel_name, string message)
    {
      message = $":radioactive: [{DateTime.Now.ToString("HH:mm")}] {player} {message}";
      PoundBot.Call(
        "API_SendChannelMessage",
        new object[] { this, channel_name, message, null, RequestHeaders }
      );
    }
  }
}