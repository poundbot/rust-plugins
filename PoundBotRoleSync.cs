// Requires: PoundBot

using System.Collections.Generic;
using System;
using Oxide.Core.Plugins;
using Oxide.Core.Libraries.Covalence;

namespace Oxide.Plugins
{
  [Info("Pound Bot Role Sync", "MrPoundsign", "2.0.0")]
  [Description("Roles syncing for PoundBot")]

  class PoundBotRoleSync : CovalencePlugin
  {
    [PluginReference]
    private Plugin PoundBot;

    private class PluginConfig
    {
      public List<RoleSync> roles;
    }

    class RoleSync
    {
      public string role;
      public string group;
    }

    private PluginConfig config;

    #region Oxide Hooks
    private void Init()
    {
      config = Config.ReadObject<PluginConfig>();
    }

    protected override void LoadDefaultMessages()
    {
      lang.RegisterMessages(new Dictionary<string, string>
      {
        ["sending_role"] = "Sending role {0} from group {1} to PoundBot",
      }, this);
    }

    void OnUserGroupAdded(string id, string groupName)
    {
      timer.Once(1f, () =>
      {
        SendGroup(groupName);
      });
    }

    void OnUserGroupRemoved(string id, string groupName)
    {
      timer.Once(1f, () =>
      {
        SendGroup(groupName);
      });
    }
    #endregion

    #region Configuration
    protected override void LoadDefaultConfig()
    {
      Config.WriteObject(GetDefaultConfig(), true);
    }

    private PluginConfig GetDefaultConfig()
    {
      return new PluginConfig
      {
        roles = new List<RoleSync> { }
      };
    }

    private void SaveConfig() => Config.WriteObject(config, true);
    #endregion

    void OnServerInitialized()
    {
      timer.Every(15f, () =>
      {
        SendRoles();
      });
      SendRoles();
    }

    private bool AcceptedHandler(int code, string response) => (code == 202);

    void SendGroup(string group)
    {
      foreach (RoleSync rs in config.roles)
      {
        if (rs.group == group)
        {
          SendRole(rs.role, rs.group);
        }
      }
    }

    void SendRole(string group, string role)
    {
      Func<int, string, bool> callback = AcceptedHandler;

      string[] groupPlayerIDs;

      groupPlayerIDs = permission.GetUsersInGroup(group);

      for (int i = 0; i < groupPlayerIDs.Length; i++)
      {
        groupPlayerIDs[i] = groupPlayerIDs[i].Substring(0, groupPlayerIDs[i].IndexOf(' '));
      }

      PoundBot.Call("API_SendRoles", new object[] { this, groupPlayerIDs, role, callback });
    }

    private void EnsureRoleSync(string role, string group)
    {
      if (config.roles.Exists(x => x.role == role && x.group == group))
      {
        Puts("Role sync alrady exists");
        return;
      }

      Puts("Adding role sync");
      config.roles.Add(new RoleSync
      {
        role = role,
        group = group
      });

      SaveConfig();
    }

    private void RemoveRoleSync(string group, string role)
    {
      RoleSync remove = config.roles.Find(x => x.role == role && x.group == group);
      if (config.roles.Remove(remove))
      {
        SaveConfig();
        Puts("Removed role sync");
        return;
      }

      Puts("Could not find role sync");
    }

    [Command("pb.add_role_sync")]
    private void ConsoleCommandAddRoleSync(IPlayer player, string command, string[] args)
    {
      if (args.Length != 2)
      {
        Puts("usage: pb.add_role_sync <oxide_group> <discord_role>");
        return;
      }
      EnsureRoleSync(args[0], args[1]);

      SendRole(args[0], args[1]);
    }

    [Command("pb.remove_role_sync")]
    private void ConsoleCommandRemoveRoleSync(IPlayer player, string command, string[] args)
    {
      if (args.Length != 2)
      {
        Puts("usage: pb.remove_role_sync <oxide_group> <discord_role>");
        return;
      }
      RemoveRoleSync(args[0], args[1]);
    }

    [Command("pb.role_sync_list")]
    private void ConsoleCommandSyncList(IPlayer player, string command, string[] args)
    {
      foreach (RoleSync rs in config.roles)
      {
        Puts($"Group: {rs.group}, Role: {rs.role}");
      }
    }

    [Command("pb.send_role_sync")]
    private void ConsoleCommandSendRoles(IPlayer player, string command, string[] args)
    {
      if (args.Length != 1)
      {
        Puts("usage: pb.send_role_sync <role_or_group>");
        return;
      }

      foreach (RoleSync rs in config.roles)
      {
        if (rs.group == args[0] || rs.role == args[0])
        {
          SendRole(rs.group, rs.role);
          return;
        }
      }
    }

    void SendRoles()
    {
      foreach (RoleSync rs in config.roles)
      {
        SendRole(rs.group, rs.role);
      }
    }

    // Re-send all clans if PoundBot reconnects
    void OnPoundBotConnected() => SendRoles();
  }
}