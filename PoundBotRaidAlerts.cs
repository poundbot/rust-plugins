// Requires: PoundBot

using System;
using System.Collections.Generic;
using System.Linq;
using Oxide.Core.Plugins;

namespace Oxide.Plugins
{
  [Info("Pound Bot Raid Alerts", "MrPoundsign", "2.0.1")]
  [Description("Raid Alerts for use with PoundBot")]

  class PoundBotRaidAlerts : RustPlugin
  {
    [PluginReference]
    private Plugin PoundBot;

    private Dictionary<string, string> RequestHeaders;
    private bool ShowOwnDamage;
    private bool PermittedOnly;
    private string PermittedGroup;

    //"Group {PermittedGroup} does not exist. Check permitted_only.group in config/PoundBotRaidAlerts.json or set permitted_only.enabled to false"
    #region Language
    protected override void LoadDefaultMessages()
    {
      lang.RegisterMessages(new Dictionary<string, string>
      {
        ["error.permitted_group_missing"] = "Group {0} does not exist. Check permitted_only.group in config/PoundBotRaidAlerts.json or set permitted_only.enabled to false",
        ["config.upgrading"] = "Upgrading config to v{0}"
      }, this);
    }
    #endregion

    void OnServerInitialized()
    {
      UpgradeConfig();
      RequestHeaders = new Dictionary<string, string>
      {
        ["X-PoundRaidAlerts-Version"] = Version.ToString()
      };
      ShowOwnDamage = (bool)Config["debug.show_own_damage"];
      PermittedOnly = (bool)Config["permitted_only.enabled"];
      PermittedGroup = (string)Config["permitted_only.group"];
    }

    #region Configuration
    protected override void LoadDefaultConfig()
    {
      Config["config.version"] = 2;
      Config["debug.show_own_damage"] = false;
      Config["permitted_only.enabled"] = false;
      Config["permitted_only.group"] = "vip";
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
          dirty = true;
        }
        catch (InvalidCastException) { } // testing if it can be converted to a string or not. No need to change it because it's not a string.
      }

      if ((int)Config["config.version"] < 2)
      {
        Puts(string.Format(lang.GetMessage("config.upgrading", this), 2));
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
        Config["permitted_only.group"] = "vip";
        Config["config.version"] = 2;
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
        string[] owners;

        if (priv != null)
        {
          owners = priv.authorizedPlayers.Select(p => { return p.userid.ToString(); }).ToArray();
        }
        else
        {
          owners = new string[] { entity.OwnerID.ToString() };
        }

        // Filter Owners to those who are registered with PoundBot
        string[] registeredPlayerIDs = permission.GetUsersInGroup((string)PoundBot.Call("API_RegisteredUsersGroup"));
        for (int i = 0; i < registeredPlayerIDs.Length; i++)
        {
          registeredPlayerIDs[i] = registeredPlayerIDs[i].Substring(0, registeredPlayerIDs[i].IndexOf(' '));
        }

        owners = owners.Intersect(registeredPlayerIDs).ToArray();
        if (owners.Length == 0) return;

        if (PermittedOnly)
        {
          if (!permission.GroupExists(PermittedGroup))
          {
            Puts(string.Format(lang.GetMessage("error.permitted_group_missing", this), PermittedGroup));
            return;
          }
          // Filter Owners to those in the permitted group

          string[] groupPlayerIDs = permission.GetUsersInGroup(PermittedGroup);
          for (int i = 0; i < groupPlayerIDs.Length; i++)
          {
            groupPlayerIDs[i] = groupPlayerIDs[i].Substring(0, groupPlayerIDs[i].IndexOf(' '));
          }

          owners = owners.Intersect(groupPlayerIDs).ToArray();
        }

        if (owners.Length == 0) return;

        string[] words = entity.ShortPrefabName.Split('/');
        string name = words[words.Length - 1].Split('.')[0];

        Func<int, string, bool> callback = EntityDeathHandler;

        PoundBot.Call(
          "API_SendEntityDeath", new object[] { this, name, GridPos(entity), owners, callback }
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