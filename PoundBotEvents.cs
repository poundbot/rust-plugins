// Requires: PoundBot
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Oxide.Core.Libraries.Covalence;
using Oxide.Core.Plugins;

namespace Oxide.Plugins
{
  [Info("Pound Bot Events", "MrPoundsign", "1.0.0")]
  [Description("Relays events for PoundBot")]

  class PoundBotEvents : CovalencePlugin
  {
    [PluginReference]
    private Plugin PoundBot, Clans, DeathNotes;

    protected string EventConnectedChannel;
    protected string EventConnectedColor;
    protected string EventDisconnectedChannel;
    protected string EventDisconnectedColor;
    protected string EventDeathNotesChannel;
    protected string EventDeathNotesColor;
    protected string EventBanKickChannel;
    protected string EventBanKickColor;

    public static string StrupColor(string input) => Regex.Replace(input, "</{0,1}color(=[^>]*|)>", string.Empty);


    #region Configuration
    protected override void LoadDefaultConfig()
    {
      LogWarning("Creating a new configuration file");
      Config["config.version"] = 0;
      Config["events.connected.channel"] = "";
      Config["events.connected.color"] = "";

      Config["events.disconnected.channel"] = "";
      Config["events.disconnected.color"] = "";

      Config["events.deathnotes.channel"] = "";
      Config["events.deathnotes.color"] = "";

      Config["events.bankick.channel"] = "";
      Config["events.bankick.color"] = "";
    }
    #endregion

    #region Oxide Hooks
    protected override void LoadDefaultMessages()
    {
      lang.RegisterMessages(
        new Dictionary<string, string>
        {
          ["user.clan_tag"] = "[{0}]",
          ["events.connected.prefix"] = ":arrow_right::video_game: ",
          ["events.connected.message"] = " has join the server",
          ["events.disconnected.prefix"] = ":arrow_left::video_game: ",
          ["events.disconnected.message"] = " has left the server",
          ["events.deathnotes.prefix"] = ":skull_crossbones: ",
          ["events.banned.prefix"] = ":hammer: ",
          ["events.banned.message"] = " has been banned",
          ["events.kicked.prefix"] = ":boot: ",
          ["events.kicked.message"] = " has been kicked",
        }, this);
    }
    #endregion

    protected void OnServerInitialized()
    {
      EventConnectedChannel = (string)Config["events.connected.channel"];
      if ((string)Config["events.connected.color"] != "")
      {
        EventConnectedColor = (string)Config["events.connected.color"];
      }
      
      EventDisconnectedChannel = (string)Config["events.disconnected.channel"];
      if ((string)Config["events.disconnected.color"] != "")
      {
        EventDisconnectedColor = (string)Config["events.disconnected.color"];
      }

      EventDeathNotesChannel = (string)Config["events.deathnotes.channel"];
      if ((string)Config["events.deathnotes.color"] != "")
      {
        EventDeathNotesColor = (string)Config["events.deathnotes.color"];
      }

      EventBanKickChannel = (string)Config["events.bankick.channel"];
      if ((string)Config["events.bankick.color"] != "")
      {
        EventBanKickColor = (string)Config["events.bankick.color"];
      }
    }

    private void OnUserConnected(IPlayer player)
    {
      if (EventConnectedChannel == "") return;

      SendToPoundBot(player, "connected", EventConnectedChannel, EventConnectedColor);
    }

    private void OnUserDisconnected(IPlayer player)
    {
      if (EventDisconnectedChannel == "") return;

      SendToPoundBot(player, "disconnected", EventDisconnectedChannel, EventDisconnectedColor);
    }

    void OnDeathNotice(Dictionary<string, object> data, string message)
    {
      if (EventDeathNotesChannel == "") return;
      KeyValuePair<string, bool>[] message_parts = new KeyValuePair<string, bool>[2]
      {
        new KeyValuePair<string, bool>(lang.GetMessage("events.deathnotes.prefix", this), false),
        new KeyValuePair<string, bool>(StrupColor(message), true),
      };

      PoundBot.Call(
        "API_SendChannelMessage",
        new object[] { this, EventDeathNotesChannel, message_parts, EventDeathNotesColor }
      );
    }

    void OnUserBanned(string name, string id, string address, string reason)
    {
      if (EventBanKickChannel == "") return;

      SendToPoundBot(name, "banned", EventBanKickChannel, EventBanKickColor);
    }

    void OnUserKicked(IPlayer player, string reason)
    {
      if (EventBanKickChannel == "") return;

      SendToPoundBot(player, "kicked", EventBanKickChannel, EventBanKickColor);
    }

    void SendToPoundBot(IPlayer player, string eventType, string channel, string embed_color = null)
    {

      string playerName = player.Name;
      var clanTag = (string)Clans?.Call("GetClanOf", player);
      if (!string.IsNullOrEmpty(clanTag))
      {
        playerName = $"[{clanTag}]{playerName}";
      }

      SendToPoundBot(playerName, eventType, channel, embed_color);

    }

    void SendToPoundBot(string playerName, string eventType, string channel, string embed_color = null)
    {
      if (channel == "")
      {
        Puts("Channel not defined. Please set your channel names in config/PoundBotEvents.json.");
        return;
      }

      KeyValuePair<string, bool>[] message_parts = new KeyValuePair<string, bool>[3];
      message_parts[0] = new KeyValuePair<string, bool>(lang.GetMessage($"events.{eventType}.prefix", this), false);
      message_parts[1] = new KeyValuePair<string, bool>(playerName, true);
      message_parts[2] = new KeyValuePair<string, bool>(lang.GetMessage($"events.{eventType}.message", this), false);
      PoundBot.Call(
        "API_SendChannelMessage",
        new object[] { this, channel, message_parts, embed_color }
      );
    }
  }
}