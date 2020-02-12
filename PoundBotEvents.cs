// Requires: PoundBot
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Oxide.Core.Libraries.Covalence;
using Oxide.Core.Plugins;

namespace Oxide.Plugins
{
  [Info("Pound Bot Events", "MrPoundsign", "2.0.2")]
  [Description("Relays events for PoundBot")]

  class PoundBotEvents : CovalencePlugin
  {
    [PluginReference]
    private Plugin PoundBot, Clans, DeathNotes;

    protected string EventPlayerConnectionsChannel;
    protected string EventPlayerConnectionsColor;
    protected string EventDisconnectedChannel;
    protected string EventDisconnectedColor;
    protected string EventDeathNotesChannel;
    protected string EventDeathNotesColor;
    protected string EventBanKickChannel;
    protected string EventBanKickColor;

    public static string StripColor(string input) => Regex.Replace(input, "</{0,1}color(=[^>]*|)>", string.Empty);


    #region Configuration
    protected override void LoadDefaultConfig()
    {
      LogWarning("Creating a new configuration file");
      Config["config.version"] = 0;
      Config["events.default.channel"] = "";

      Config["events.connected.channel"] = "default";
      Config["events.connected.color"] = "";

      Config["events.disconnected.channel"] = "default";
      Config["events.disconnected.color"] = "";

      Config["events.deathnotes.channel"] = "default";
      Config["events.deathnotes.color"] = "";

      Config["events.bankick.channel"] = "default";
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
          ["dateformat"] = "[HH:mm]",
          ["events.connected.prefix"] = ":arrow_right::video_game: ",
          ["events.connected.message"] = " has joined the server",
          ["events.disconnected.prefix"] = ":x::video_game: ",
          ["events.disconnected.message"] = " has left the server",
          ["events.deathnotes.prefix"] = ":skull_crossbones: ",
          ["events.banned.prefix"] = ":hammer: ",
          ["events.banned.message"] = " has been banned",
          ["events.kicked.prefix"] = ":boot: ",
          ["events.kicked.message"] = " has been kicked",
        }, this);
    }
    #endregion

    private string ChannelID(string channel_name)
    {
      string channelID = (string)Config[$"events.{channel_name}.channel"];

      if (channelID == "default")
      {
        return (string)Config["events.default.channel"];
      }

      return channelID;
    }

    protected string ChannelColor(string channel_name)
    {
      string channelColor = (string)Config[$"events.{channel_name}.color"];
      if (channelColor.Length == 0)
      {
        return null;
      }

      return channelColor;
    }

    void OnInboundBroadcast(Dictionary<string, string> data)
    {
      Puts($"OnInboundBroadcast: {data["type"]} {data["message"]}");
    }

    void Init()
    {
      EventPlayerConnectionsChannel = ChannelID("connected");
      EventPlayerConnectionsColor = ChannelColor("connected");

      EventDisconnectedChannel = ChannelID("disconnected");
      EventDisconnectedColor = ChannelColor("disconnected");

      EventDeathNotesChannel = ChannelID("deathnotes");
      EventDeathNotesColor = ChannelColor("deathnotes");

      EventBanKickChannel = ChannelID("bankick");
      EventBanKickColor = ChannelColor("bankick");
    }

    private void OnUserConnected(IPlayer player)
    {
      if (EventPlayerConnectionsChannel.Length == 0) return;

      SendToPoundBot(player, "connected", EventPlayerConnectionsChannel, EventPlayerConnectionsColor);
    }

    private void OnUserDisconnected(IPlayer player)
    {
      if (EventDisconnectedChannel.Length == 0) return;

      SendToPoundBot(player, "disconnected", EventPlayerConnectionsChannel, EventPlayerConnectionsColor);
    }

    void OnDeathNotice(Dictionary<string, object> data, string message)
    {
      if (EventDeathNotesChannel.Length == 0) return;
      KeyValuePair<string, bool>[] message_parts = new KeyValuePair<string, bool>[2]
      {
        new KeyValuePair<string, bool>(lang.GetMessage("events.deathnotes.prefix", this), false),
        new KeyValuePair<string, bool>(StripColor(message), true),
      };

      PoundBot.Call(
        "API_SendChannelMessage",
        new object[] { this, EventDeathNotesChannel, message_parts, EventDeathNotesColor }
      );
    }

    void OnUserBanned(string name, string id, string address, string reason)
    {
      if (EventBanKickChannel.Length == 0) return;

      SendToPoundBot(name, "banned", EventBanKickChannel, EventBanKickColor);
    }

    void OnUserKicked(IPlayer player, string reason)
    {
      if (EventBanKickChannel.Length == 0) return;

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
      if (channel.Length == 0)
      {
        Puts("Channel not defined. Please set your channel names in config/PoundBotEvents.json.");
        return;
      }

      KeyValuePair<string, bool>[] message_parts = new KeyValuePair<string, bool>[3];
      message_parts[0] = new KeyValuePair<string, bool>($"{lang.GetMessage($"events.{eventType}.prefix", this)}{DateTime.Now.ToString(lang.GetMessage("dateformat", this))}", false);
      message_parts[1] = new KeyValuePair<string, bool>(playerName, true);
      message_parts[2] = new KeyValuePair<string, bool>(lang.GetMessage($"events.{eventType}.message", this), false);
      PoundBot.Call(
        "API_SendChannelMessage",
        new object[] { this, channel, message_parts, embed_color }
      );
    }
  }
}