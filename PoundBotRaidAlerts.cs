// Requires: PoundBot

using System;
using System.Collections.Generic;
using System.Linq;
using Oxide.Core.Libraries.Covalence;
using Oxide.Core.Plugins;
using UnityEngine;

namespace Oxide.Plugins
{
  [Info("Pound Bot Raid Alerts", "MrPoundsign", "2.0.4")]
  [Description("Raid Alerts for use with PoundBot")]

  class PoundBotRaidAlerts : CovalencePlugin
  {
    [PluginReference]
    private Plugin PoundBot;

    private bool ShowOwnDamage;
    private bool PermittedOnly;
    const string RaidAlertsPermission = "poundbotraidalerts.alert";
    const string RaidAlertsTestPermission = "poundbotraidalerts.test";

    #region Language
    protected override void LoadDefaultMessages()
    {
      lang.RegisterMessages(new Dictionary<string, string>
      {
        ["config.upgrading"] = "Upgrading config to v{0}",
        ["config.show_own_damage"] = "debug.show_own_damage is {0}",
        ["config.permitted_only"] = "permitted_only.enabled is {0}",
        ["command.raid_alerts_test"] = "rat",
      }, this);
    }
    #endregion

    private void Init()
    {
      permission.RegisterPermission(RaidAlertsPermission, this);
      permission.RegisterPermission(RaidAlertsTestPermission, this);

      AddLocalizedCommand("command.raid_alerts_test", "CommandRaidAlertsTest", RaidAlertsTestPermission);
    }

    void OnServerInitialized()
    {
      UpgradeConfig();
      ApplyConfig();
    }

    #region Configuration
    protected override void LoadDefaultConfig()
    {
      Config["config.version"] = 3;
      Config["debug.show_own_damage"] = false;
      Config["permitted_only.enabled"] = false;
    }

    private void ApplyConfig()
    {
      ShowOwnDamage = (bool)Config["debug.show_own_damage"];
      PermittedOnly = (bool)Config["permitted_only.enabled"];
    }

    void UpgradeConfig()
    {
      string cvKey = "config.version";
      bool dirty = false;

      if (Config[cvKey] == null)
      {
        Config[cvKey] = 1;
      }
      else
      {
        try
        {
          var foo = (string)Config[cvKey];
          Config[cvKey] = 2;
          dirty = true;
        }
        catch (InvalidCastException) { } // testing if it can be converted to a string or not. No need to change it because it's not a string.
      }

      int currentVersion = (int)Config[cvKey];

      if (currentVersion < 2)
      {
        if (Config["show_own_damage"] != null)
        {
          Config["debug.show_own_damage"] = (bool)Config["show_own_damage"];
        }
        else
        {
          Config["debug.show_own_damage"] = false;
        }
        Config.Remove("show_own_damage");
        Config["permitted_only.enabled"] = false;
        dirty = true;
      }

      if (currentVersion < 3)
      {
        Puts(string.Format(lang.GetMessage("config.upgrading", this), 3));
        Config.Remove("permitted_only.group");
        Config[cvKey] = 3;
        dirty = true;
      }

      if (dirty)
      {
        SaveConfig();
      }
    }
    #endregion

    void OnEntityDeath(BaseEntity victim, HitInfo info)
    {
      if (!(victim is DecayEntity) &&
        !(victim is StorageContainer) &&
        !(victim is BaseVehicle)
      ) return;

      SendEntityDeath(victim, info?.Initiator);
    }

    void SendEntityDeath(BaseEntity entity, BaseEntity initiator, bool test = false)
    {
      if (entity == null) return;
      if (initiator == null) return;
      if (initiator is Scientist) return;
      if (initiator is NPCMurderer) return;
      if (initiator is BasePlayer)
      {
        string[] w = entity.ShortPrefabName.Split('/');
        string n = w[w.Length - 1].Split('.')[0];

        int eid = entity.GetInstanceID();

        if (test) n = $"[TEST_{n}:{eid}]";

        if (!test && entity.OwnerID == 0)
        {
          if (test) Puts($"{n} Could not find owner. Not sending alert.");
          return;
        }
        BasePlayer player = (BasePlayer)initiator;

        if (!test && !ShowOwnDamage && entity.OwnerID == player.userID)
        {
          if (test) Puts($"{n} Show own damage is false. Not sending alert.");
          return;
        }

        BuildingPrivlidge priv = entity.GetBuildingPrivilege();
        string[] ownerIDs;

        if (priv != null)
        {
          if (test) Puts($"{n} Checking building privs");
          ownerIDs = priv.authorizedPlayers.Select(p => { return p.userid.ToString(); }).ToArray();
        }
        else
        {
          if (test)
          {
            Puts($"{n} setting owners IDs to testing user");
            ownerIDs = new string[] { player.userID.ToString() };
          }
          else
          {
            ownerIDs = new string[] { entity.OwnerID.ToString() };
          }
        }

        if (test) Puts($"{n} ownerIDs are {String.Join(",", ownerIDs)}");

        if (test) Puts($"{n} Registered Players Group is {(string)PoundBot.Call("API_RegisteredUsersGroup")}");

        // Filter Owners to those who are registered with PoundBot
        string[] registeredPlayerIDs = permission.GetUsersInGroup((string)PoundBot.Call("API_RegisteredUsersGroup"));

        for (int i = 0; i < registeredPlayerIDs.Length; i++)
        {
          registeredPlayerIDs[i] = registeredPlayerIDs[i].Substring(0, registeredPlayerIDs[i].IndexOf(' '));
        }

        if (test) Puts($"{n} rgisteredPlayersIDs are {String.Join(",", registeredPlayerIDs)}");

        ownerIDs = ownerIDs.Intersect(registeredPlayerIDs).ToArray();

        if (test) Puts($"{n} intersecting ownerIDs are {String.Join(",", ownerIDs)}");

        if (PermittedOnly)
        {
          if (test) Puts($"{n} PermittedOnly is true. Checking perms.");
          ownerIDs = ownerIDs.Where(ownerID => permission.UserHasPermission(ownerID, RaidAlertsPermission)).ToArray();
        }

        if (ownerIDs.Length == 0)
        {
          if (test) Puts($"{n} Owners length is 0. Not sending alert.");
          return;
        }

        if (test) Puts($"{n} Sending entity death to PoundBot");

        Func<int, string, bool> callback = EntityDeathHandler;

        PoundBot.Call(
          "API_SendEntityDeath", new object[] { this, n, GridPos(entity), ownerIDs, callback }
        );
      }
    }

    private bool EntityDeathHandler(int code, string response) => (code == 200);

    private string GridPos(BaseEntity entity)
    {
      var size = World.Size / 2;
      var gridCellSize = 150;
      var num2 = (int)(entity.transform.position.x + size) / gridCellSize;
      var index = Math.Abs((int)(entity.transform.position.z - size) / gridCellSize);
      return NumberToLetter(num2) + index.ToString();
    }

    public string NumberToLetter(int num)
    {
      int num1 = (int)Math.Floor((double)(num / 26));
      int num2 = num % 26;
      string empty = string.Empty;
      if (num1 > 0)
      {
        for (int index = 0; index < num1; ++index)
          empty += Convert.ToChar(65 + index).ToString();
      }
      return empty + Convert.ToChar(65 + num2).ToString();
    }

    #region Helpers
    private void AddLocalizedCommand(string key, string command, string perm = null)
    {
      foreach (var language in lang.GetLanguages(this))
      {
        var messages = lang.GetMessages(language, this);
        foreach (var message in messages.Where(m => m.Key.Equals(key)))
          if (!string.IsNullOrEmpty(message.Value)) AddCovalenceCommand(message.Value, command, perm);
      }
    }
    #endregion

    #region Commands
    //[Command("rat"), Permission(RaidAlertsTestPermission)]
    private void CommandRaidAlertsTest(IPlayer iplayer, string command, string[] args)
    {
      BasePlayer p = iplayer.Object as BasePlayer;
      // RaidAlertsTestPermission

      RaycastHit raycastHit;
      BaseEntity targetEntity;

      bool flag = Physics.Raycast(p.eyes.HeadRay(), out raycastHit, 500f, Rust.Layers.Solid);
      targetEntity = flag ? raycastHit.GetEntity() : null;

      Puts($"targetEntity is {targetEntity}");

      if (targetEntity != null) SendEntityDeath(targetEntity, p, true);

    }

    [Command("pb.ra.show_own_damage")]
    private void ConsoleCommandShowOwnDamage(IPlayer player, string command, string[] args)
    {
      if (args.Length == 1)
      {
        Config["debug.show_own_damage"] = (args[0] == "1" || args[0] == "true");
        SaveConfig();
        ApplyConfig();
      }
      Puts(string.Format(lang.GetMessage("config.show_own_damage", this), ShowOwnDamage));
    }

    [Command("pb.ra.permitted_only")]
    private void ConsoleCommandPermittedOnly(IPlayer player, string command, string[] args)
    {
      if (args.Length == 1)
      {
        Config["permitted_only.enabled"] = (args[0] == "1" || args[0] == "true");
        SaveConfig();
        ApplyConfig();
      }

      Puts(string.Format(lang.GetMessage("config.permitted_only", this), PermittedOnly));
    }
    #endregion
  }
}