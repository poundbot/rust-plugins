// Requires: PoundBot

using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Oxide.Core.Libraries.Covalence;
using Oxide.Core.Plugins;

namespace Oxide.Plugins
{
  [Info("Pound Bot Chat Relay", "MrPoundsign", "2.0.0")]
  [Description("Chat relay for use with PoundBot")]

  class PoundBotChatRelay : CovalencePlugin
  {
    [PluginReference]
    private Plugin PoundBot, Clans;

    protected int ApiChatRunnersCount;
    private bool RelayDiscordChat;
    private bool RelayGiveNotices;
    private bool RelayServerChat;
    private bool UseBetterChat;
    private string RelayChatChannel;
    private string RelayChatColor;
    private string RelayServerChatColor;
    private string RelayServerChannel;

    class ChatMessage
    {
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
      Config["chat.styled"] = true;
      Config["chat.styled_color"] = "darkorange";
      Config["chat.server_styled"] = true;
      Config["chat.server_styled_color"] = "darkred";
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
          Config[configVersion] = 2;
        }
        catch (InvalidCastException) { } // testing if it can be converted to a string or not. No need to change it because it's not a string.
      }

      if ((int)Config[configVersion] < 2)
      {
        LogWarning(string.Format(lang.GetMessage("config.upgrading", this), 2));
        if ((bool)Config["relay.betterchat"])
        {
          Config["relay.chat"] = true;
        }
        Config["relay.givenotices"] = (bool)Config["relay.serverchat"];
        dirty = true;
      }

      if ((int)Config[configVersion] < 3)
      {
        LogWarning(string.Format(lang.GetMessage("config.upgrading", this), 3));
        Config[configVersion] = 3;
        Config["chat.channel"] = "";
        Config["chat.server_channel"] = "";
        Config["chat.styled"] = true;
        Config["chat.styled_color"] = "darkorange";
        Config["chat.server_styled"] = true;
        Config["chat.server_styled_color"] = "darkred";
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
          ["chat.Prefix"] = ":radioactive:",
          ["chat.DateFormat"] = "[HH:mm]",
          ["console.ClanTag"] = "[{0}] ",
          ["console.Msg"] = "{{DSCD}} {0}: {1}",
          ["config.upgrading"] = "Upgrading config to v{0}",
          ["config.chat_config_updated"] = "Config updated. Check oxide/config/PoundBotChatRelay.json",
          ["usage.chat_config"] = @"Usage:
pb.chat_config <channel|server_channel|styled|styled_color|server_styled|server_styled_color> [value]

If value is not supplied, prints the current value.

The values 'styled' and 'server_styled' are booleans, and must be set to '0', '1', 'true', or 'false'.

It is recommended you use the channel IDs rather than channel names for 'channel' and 'server_channel'.

See 'pb.channels' for information about your channels.",
          ["setting.is"] = "Setting {0} is {1}",
        }, this);
    }

    void OnServerInitialized()
    {
      UpgradeConfig();
      ApplyConfig();
      StartChatRunners();
    }

    void ApplyConfig()
    {
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
      RelayServerChannel = (string)Config["chat.server_channel"];

      if ((bool)Config["chat.styled"])
      {
        RelayChatColor = (string)Config["chat.styled_color"];
      }
      else
      {
        RelayChatColor = null;
      }

      if ((bool)Config["chat.server_styled"])
      {
        RelayServerChatColor = (string)Config["chat.server_styled_color"];
      }
      else
      {
        RelayServerChatColor = null;
      }
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
          if ((bool)PoundBot.Call("API_GetChannelMessage", new object[] { this, "chat", callback }))
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

      SendToPoundBot(name, message, RelayServerChannel, RelayServerChatColor);
    }

    void OnUserChat(IPlayer player, string message) => SendToPoundBot(player, message, RelayChatChannel, RelayChatColor);

    void OnBetterChat(Dictionary<string, object> data)
    {
      if (!UseBetterChat) return;

      string color = RelayChatColor;

      if (RelayChatColor != null)
      {
        Dictionary<string, object> m = (Dictionary<string, object>)data["MessageSettings"];

        color = (string)m["Color"];
      }

      IPlayer player = (IPlayer)data["Player"];

      SendToPoundBot(player, (string)data["Message"], RelayChatChannel, color);
    }

    void SendToPoundBot(IPlayer player, string message, string channel, string embed_color = null)
    {
      string playerName = player.Name;
      var clanTag = (string)Clans?.Call("GetClanOf", player);
      if (!string.IsNullOrEmpty(clanTag))
      {
        playerName = $"[{clanTag}]{playerName}";
      }

      SendToPoundBot(playerName, message, channel, embed_color);
    }

    void SendToPoundBot(string player, string message, string channel, string embed_color = null)
    {
      if (channel == "")
      {
        Puts("Channel not defined. Please set your channel names in config/PoundBotChatRelay.json.");
        return;
      }

      KeyValuePair<string, bool>[] message_parts = new KeyValuePair<string, bool>[4];
      message_parts[0] = new KeyValuePair<string, bool>($"{lang.GetMessage("chat.Prefix", this)}{DateTime.Now.ToString(lang.GetMessage("chat.DateFormat", this))} **", false);
      message_parts[1] = new KeyValuePair<string, bool>(player, true);
      message_parts[2] = new KeyValuePair<string, bool>("**: ", false);
      message_parts[3] = new KeyValuePair<string, bool>(message, true);
      PoundBot.Call(
        "API_SendChannelMessage",
        new object[] { this, channel, message_parts, embed_color, null }
      );
    }

    #region Commands
    //channel|server_channel|styled|styled_color|server_styled|server_styled_color
    [Command("pb.chat_config")]
    private void ConsoleCommandSetChatConfig(IPlayer player, string command, string[] args)
    {
      if (args.Count() < 1 || args.Count() > 2)
      {
        Puts(lang.GetMessage("usage.chat_config", this));
        return;
      }

      var configName = $"chat.{args[0]}";

      if (args.Count() == 1)
      {
        Puts(string.Format(lang.GetMessage("setting.is", this), configName, Config[configName]));
        return;
      }

      object configValue;

      switch (args[0])
      {
        // All the string cases
        case "channel":
        case "server_channel":
        case "styled_color":
        case "server_styled_color":
          configValue = args[1];
          break;

        // All the boolean cases
        case "styled":
        case "server_styled":
          configValue = (args[1] == "1" || args[1].ToLower() == "true");
          break;
        default:
          Puts(lang.GetMessage("usage.chat_config", this));
          return;
      }

      Puts(lang.GetMessage("config.chat_config_updated", this));
      Config[configName] = configValue;
      SaveConfig();
      ApplyConfig();
    }
    #endregion
  }
}