// Requires: PoundBot

using System;
using System.Collections.Generic;
using System.Linq;
using Oxide.Core.Plugins;

namespace Oxide.Plugins
{
  [Info("Pound Bot Raid Alerts", "MrPoundsign", "2.0.2")]
  [Description("Raid Alerts for use with PoundBot")]

  class PoundBotRaidAlerts : RustPlugin
  {
    [PluginReference]
    private Plugin PoundBot;

    private Dictionary<string, string> RequestHeaders;
    private bool ShowOwnDamage;
    private bool PermittedOnly;
    const string PermissionName = "poundbot.raidalerts";

    #region Language
    protected override void LoadDefaultMessages()
    {
      lang.RegisterMessages(new Dictionary<string, string>
      {
        ["config.upgrading"] = "Upgrading config to v{0}"
      }, this);
    }
    #endregion

    private void Init()
    {
      permission.RegisterPermission(PermissionName, this);
    }

    void OnServerInitialized()
    {
      UpgradeConfig();
      RequestHeaders = new Dictionary<string, string>
      {
        ["X-PoundRaidAlerts-Version"] = Version.ToString()
      };
      ShowOwnDamage = (bool)Config["debug.show_own_damage"];
      PermittedOnly = (bool)Config["permitted_only.enabled"];
    }

    #region Configuration
    protected override void LoadDefaultConfig()
    {
      Config["config.version"] = 3;
      Config["debug.show_own_damage"] = false;
      Config["permitted_only.enabled"] = false;
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

    void SendEntityDeath(BaseEntity entity, BaseEntity initiator)
    {
      if (entity == null) return;
      if (initiator == null) return;
      if (initiator is Scientist) return;
      if (initiator is NPCMurderer) return;
      if (initiator is BasePlayer)
      {
        if (entity.OwnerID == 0) return;
        BasePlayer player = (BasePlayer)initiator;

        if (!ShowOwnDamage && entity.OwnerID == player.userID) return;

        BuildingPrivlidge priv = entity.GetBuildingPrivilege();
        string[] ownerIDs;

        if (priv != null)
        {
          ownerIDs = priv.authorizedPlayers.Select(p => { return p.userid.ToString(); }).ToArray();
        }
        else
        {
          ownerIDs = new string[] { entity.OwnerID.ToString() };
        }

        // Filter Owners to those who are registered with PoundBot
        string[] registeredPlayerIDs = permission.GetUsersInGroup((string)PoundBot.Call("API_RegisteredUsersGroup"));

        for (int i = 0; i < registeredPlayerIDs.Length; i++)
        {
          registeredPlayerIDs[i] = registeredPlayerIDs[i].Substring(0, registeredPlayerIDs[i].IndexOf(' '));
        }

        ownerIDs = ownerIDs.Intersect(registeredPlayerIDs).ToArray();
        if (ownerIDs.Length == 0) return;

        if (PermittedOnly)
        {
          ownerIDs = ownerIDs.Where(ownerID => permission.UserHasPermission(ownerID, PermissionName)).ToArray();
        }

        if (ownerIDs.Length == 0) return;

        string[] words = entity.ShortPrefabName.Split('/');
        string name = words[words.Length - 1].Split('.')[0];

        Func<int, string, bool> callback = EntityDeathHandler;

        PoundBot.Call(
          "API_SendEntityDeath", new object[] { this, name, GridPos(entity), ownerIDs, callback }
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
  }
}