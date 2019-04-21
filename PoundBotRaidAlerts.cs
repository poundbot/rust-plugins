// Requires: PoundBot

using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Oxide.Core.Libraries;
using Oxide.Core.Plugins;
using UnityEngine;

namespace Oxide.Plugins
{
  [Info("Pound Bot Raid Alerts", "MrPoundsign", "1.1.0")]
  [Description("Raid Alerts for use with PoundBot")]

  class PoundBotRaidAlerts : RustPlugin
  {
    [PluginReference]
    private Plugin PoundBot;

    private Dictionary<string, string> RequestHeaders;
    private string EntityDeathURI;
    private bool ShowOwnDamage;

    class EntityDeath
    {
      public string Name;
      public string GridPos;
      public string[] OwnerIDs;
      public DateTime CreatedAt;

      public EntityDeath(string name, string gridpos, string[] ownerIDs)
      {
        Name = name;
        GridPos = gridpos;
        OwnerIDs = ownerIDs;
        CreatedAt = DateTime.UtcNow;
      }
    }

    #region Configuration
    protected override void LoadDefaultConfig()
    {
      Config["show_own_damage"] = false;
    }
    #endregion

    void OnServerInitialized()
    {
      RequestHeaders = (Dictionary<string, string>)PoundBot?.Call("Headers");
      RequestHeaders["X-PoundRaidAlerts-Version"] = Version.ToString();
      EntityDeathURI = $"{(string)PoundBot?.Call("ApiBase")}/entity_death";
      ShowOwnDamage = (bool)Config["show_own_damage"];
    }

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
        var player = (BasePlayer)initiator;

        if (!ShowOwnDamage && entity.OwnerID == player.userID) return;

        var priv = entity.GetBuildingPrivilege();
        string[] owners;

        if (priv != null)
        {
          owners = priv.authorizedPlayers.Select(p => { return p.userid.ToString(); }).ToArray();
        }
        else
        {
          owners = new string[] { entity.OwnerID.ToString() };
        }

        string[] words = entity.ShortPrefabName.Split('/');
        var name = words[words.Length - 1].Split('.')[0];

        var di = new EntityDeath(name, GridPos(entity), owners);
        var body = JsonConvert.SerializeObject(di);

        if (ApiRequestOk())
        {
          webrequest.Enqueue(
            EntityDeathURI, body,
            (code, response) => { if (!ApiSuccess(code == 200)) { ApiError(code, response); } },
            this, RequestMethod.PUT, RequestHeaders, 100f
          );
        }
      }
    }

    private string GridPos(BaseEntity entity)
    {
      var size = World.Size;
      var gridCellSize = 150;
      var num2 = (int)(entity.transform.position.x + (size / 2)) / gridCellSize;
      var index = Math.Abs((int)(entity.transform.position.z - (size / 2)) / gridCellSize);
      return $"{this.NumberToLetter(num2) + index.ToString()}";
    }

    public string NumberToLetter(int num)
    {
      int num1 = Mathf.FloorToInt(num / 26);
      int num2 = num % 26;
      string empty = string.Empty;
      if (num1 > 0)
      {
        for (int index = 0; index < num1; ++index)
          empty += Convert.ToChar(65 + index).ToString();
      }
      return empty + Convert.ToChar(65 + num2).ToString();
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