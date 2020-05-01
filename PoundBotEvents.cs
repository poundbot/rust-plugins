// Requires: PoundBot
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Oxide.Core.Libraries.Covalence;
using Oxide.Core.Plugins;

namespace Oxide.Plugins
{
  [Info("Pound Bot Events", "MrPoundsign", "2.0.3")]
  [Description("Relays events for PoundBot")]

  class PoundBotEvents : CovalencePlugin
  {
    [PluginReference]
    private Plugin PoundBot, Clans, DeathNotes; //, Inbound;

    protected string EventPlayerConnectionsChannel;
    protected string EventPlayerConnectionsColor;
    protected string EventDisconnectedChannel;
    protected string EventDisconnectedColor;
    protected string EventDeathNotesChannel;
    protected string EventDeathNotesColor;
    protected string EventBanKickChannel;
    protected string EventBanKickColor;
    protected string EventClanCreateChannel;
    protected string EventClanCreateColor;

    const string PermNoConnectEvents = "poundbotevents.noconnectevents";
    const string PermNoEvents = "poundbotevents.noevents";

    public static string StripTags(string input) => Regex.Replace(input, "</{0,1}(color|size|i|b)(=[^>]*|)>", string.Empty);

    #region Configuration
    protected override void LoadDefaultConfig()
    {
      LogWarning("Creating a new configuration file");
      Config["config.version"] = 0;

      Config["events.default.channel"] = "";

      Config["events.bankick.channel"] = "default";
      Config["events.bankick.color"] = "";

      Config["events.clancreate.channel"] = "default";
      Config["events.clancreate.color"] = "";

      Config["events.connected.channel"] = "default";
      Config["events.connected.color"] = "";

      Config["events.deathnotes.channel"] = "default";
      Config["events.deathnotes.color"] = "";

      Config["events.disconnected.channel"] = "default";
      Config["events.disconnected.color"] = "";
    }

    private string ChannelID(string channel_name)
    {
      string channelID = (string)Config[$"events.{channel_name}.channel"];

      if (channelID == null || channelID == "default")
      {
        return (string)Config["events.default.channel"];
      }

      return channelID;
    }

    protected string ChannelColor(string channel_name)
    {
      string channelColor = (string)Config[$"events.{channel_name}.color"];

      if (channelColor == null || channelColor.Length == 0)
      {
        return null;
      }

      return channelColor;
    }
    #endregion

    #region Oxide Hooks
    void Init()
    {
      permission.RegisterPermission(PermNoConnectEvents, this);
      permission.RegisterPermission(PermNoEvents, this);

      EventPlayerConnectionsChannel = ChannelID("connected");
      EventPlayerConnectionsColor = ChannelColor("connected");

      EventDisconnectedChannel = ChannelID("disconnected");
      EventDisconnectedColor = ChannelColor("disconnected");

      EventDeathNotesChannel = ChannelID("deathnotes");
      EventDeathNotesColor = ChannelColor("deathnotes");

      EventBanKickChannel = ChannelID("bankick");
      EventBanKickColor = ChannelColor("bankick");

      EventClanCreateChannel = ChannelID("clancreate");
      EventClanCreateColor = ChannelColor("clancreate");
    }

    protected override void LoadDefaultMessages()
    {
      lang.RegisterMessages(
        new Dictionary<string, string>
        {
          ["user.clan_tag"] = "[{0}]",
          ["dateformat"] = "[HH:mm]",
          ["events.banned.prefix"] = ":hammer: ",
          ["events.banned.message"] = " has been banned",
          ["events.clancreate.prefix"] = ":arrow_right::busts_in_silhouette: ",
          ["events.clancreate.message"] = "Clan {clan} ({clan_tag}) has been created by {player}",
          ["events.connected.prefix"] = ":arrow_right::video_game: ",
          ["events.connected.message"] = " has joined the server",
          ["events.deathnotes.prefix"] = ":skull_crossbones: ",
          ["events.disconnected.prefix"] = ":x::video_game: ",
          ["events.disconnected.message"] = " has left the server",
          ["events.kicked.prefix"] = ":boot: ",
          ["events.kicked.message"] = " has been kicked",
        }, this);
    }

    private bool DisabledNotification(string playerID, string perm)
    {
      return permission.UserHasPermission(playerID, perm);
    }

    private void OnUserConnected(IPlayer player)
    {
      if (EventPlayerConnectionsChannel.Length == 0 || DisabledNotification(player.Id, PermNoConnectEvents)) return;

      SendToPoundBot(player, "connected", EventPlayerConnectionsChannel, EventPlayerConnectionsColor);
    }

    private void OnUserDisconnected(IPlayer player)
    {
      if (EventPlayerConnectionsChannel.Length == 0 || DisabledNotification(player.Id, PermNoConnectEvents)) return;

      SendToPoundBot(player, "disconnected", EventPlayerConnectionsChannel, EventPlayerConnectionsColor);
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
    #endregion

    #region DeathNotes Hooks
    void OnDeathNotice(Dictionary<string, object> data, string message)
    {
      if (EventDeathNotesChannel.Length == 0) return;
      KeyValuePair<string, bool>[] message_parts = new KeyValuePair<string, bool>[2]
      {
        new KeyValuePair<string, bool>(lang.GetMessage("events.deathnotes.prefix", this), false),
        new KeyValuePair<string, bool>(StripTags(message), true),
      };

      PoundBot.Call(
        "API_SendChannelMessage",
        new object[] { this, EventDeathNotesChannel, message_parts, EventDeathNotesColor }
      );
    }
    #endregion

    #region Inbound Hooks
    //void OnInboundBroadcast(Dictionary<string, string> data)
    //{
    //  Puts($"OnInboundBroadcast: {data["type"]} {data["message"]}");
    //}
    #endregion

    #region Clan Hooks
    //void OnClanCreate(string player, string id, string address, string reason)
    //{
    //  if (EventClanCreateChannel.Length == 0) return;

    //  SendToPoundBot(player, "clancreate", EventClanCreateChannel, EventClanCreateColor);
    //}
    #endregion

    void SendToPoundBot(IPlayer player, string eventType, string chan, string color = null)
    {
      string playerName = player.Name;
      var clanTag = (string)Clans?.Call("GetClanOf", player);
      if (!string.IsNullOrEmpty(clanTag))
      {
        playerName = $"[{clanTag}]{playerName}";
      }

      SendToPoundBot(player.Id, playerName, eventType, chan, color);
    }

    void SendToPoundBot(string playerID, string playerName, string eventType, string chan, string color = null)
    {
      if (chan.Length == 0 || permission.UserHasPermission(playerID, PermNoEvents))
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
        new object[] { this, chan, message_parts, color }
      );
    }
  }
}