using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Oxide.Core;
using Oxide.Core.Libraries;
using Oxide.Core.Libraries.Covalence;
using Oxide.Core.Plugins;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("PoundBotConnector", "MrPoundsign", "0.2.2")]
    [Description("Communicate with PoundBot")]

    class PoundBotConnector : RustPlugin
    {
        [PluginReference]
        Plugin Clans, BetterChat;

        static int ApiRetrySeconds = 2;
        static int ApiRetryNotify = 20;

        static bool ApiInError = false;
        static bool ApiRetry = false;
        static uint ApiRetryAttempts = 0;
        static DateTime ApiErrorTime;
        static DateTime LastApiAttempt;
        // static Dictionary<string, string> headers;

        class ApiErrorResponse
        {
            public string Error;
        }

        class ChatMessage
        {
            public ulong SteamID { get; set; }
            public string DisplayName { get; set; }
            public string Message { get; set; }
            public string Source { get; set; }
            public string ClanTag { get; set; }
        }

        class EntityDeath
        {
            public string Name;
            public string GridPos;
            public ulong[] Owners;
            public DateTime CreatedAt;

            public EntityDeath(string name, string gridpos, ulong[] owners)
            {
                this.Name = name;
                this.GridPos = gridpos;
                this.Owners = owners;
                this.CreatedAt = DateTime.UtcNow;
            }
        }

        class DiscordAuth
        {
            public ulong SteamID;
            public string DisplayName;
            public string ClanTag;
            public string DiscordName;
            public int Pin;
            public DateTime CreatedAt;

            public DiscordAuth(string displayname, string discordName, ulong steamid, string clantag)
            {
                System.Random rnd = new System.Random();
                this.DisplayName = displayname;
                this.DiscordName = discordName;
                this.SteamID = steamid;
                this.Pin = rnd.Next(1, 9999);
                this.CreatedAt = DateTime.UtcNow;
                this.ClanTag = clantag;
            }
        }

        public Dictionary<string, string> headers()
        {
            return new Dictionary<string, string>
            {
                {
                    "Content-type",
                    "application/json"
                },
                {
                    "Authorization",
                    $"Token {Config["api_key"]}"
                }
            };
        }

        public bool ApiRequestOk()
        {
            if (ApiInError && !ApiRetry && (LastApiAttempt.AddSeconds(ApiRetrySeconds) < DateTime.Now))
            {
                ApiRetryAttempts++;
                if (ApiRetryAttempts == 1 || ApiRetryAttempts % ApiRetryNotify == 0)
                {
                    Puts($"Time in error: {DateTime.Now.Subtract(ApiErrorTime).ToShortString()}");
                }
                ApiRetry = true;
            }
            return (!ApiInError || ApiRetry);
        }

        private void ApiError(int code, string response)
        {
            string error;
            if (code == 0 || code == 400)
            {
                if (ApiInError)
                {
                    if (ApiRetry)
                    {
                        LastApiAttempt = DateTime.Now;
                        ApiRetry = false;
                    }

                    if (ApiRetryAttempts % ApiRetryNotify != 0)
                    {
                        return;
                    }

                }
                else
                {
                    ApiErrorTime = DateTime.Now;
                    LastApiAttempt = DateTime.Now;
                    ApiInError = true;
                    ApiRetry = false;
                }

                error = "Connection Failure!";
            }
            else
            {
                try
                {
                    var air = JsonConvert.DeserializeObject<ApiErrorResponse>(response);
                    error = air.Error;
                }
                catch
                {
                    error = response;
                }
            }
            Puts($"Error communicating with PoundBot: {code}/{error}");

        }

        private bool ApiSuccess(bool success)
        {
            // Reset retry varibles if we're successful
            if (ApiInError && success)
            {
                Puts("Reconnected with PoundBot!");
                Puts($"Total time in error: {DateTime.Now.Subtract(ApiErrorTime).ToShortString()}");
                ApiRetryAttempts = 0;
                ApiInError = false;
                ApiRetry = true;
            }
            return success;
        }

        private List<Timer> chat_runners = new List<Timer>();

        #region Configuration
        protected override void LoadDefaultConfig()
        {
            Config.Clear();
            Config["api_url"] = "http://localhost:9090/";
            Config["show_own_damage"] = false;
            Config["api_key"] = "API KEY HERE";
            SaveConfig();
        }
        #endregion

        void OnEntityDeath(DecayEntity victim, HitInfo info)
        {
            PrintDeath(victim, info?.Initiator);
        }

        void OnEntityDeath(StorageContainer victim, HitInfo info)
        {
            PrintDeath(victim, info?.Initiator);
        }

        void OnEntityDeath(BaseVehicle victim, HitInfo info)
        {
            PrintDeath(victim, info?.Initiator);
        }

        void PrintDeath(BaseEntity entity, BaseEntity initiator)
        {
            if (entity == null) return;
            if (initiator == null) return;
            if (initiator is Scientist) return;
            if (initiator is NPCMurderer) return;
            if (initiator is BasePlayer)
            {
                var player = (BasePlayer) initiator;
                if (entity.OwnerID == 0) return;
                if (!(bool) Config["show_own_damage"] && entity.OwnerID == player.userID) return;
                var priv = entity.GetBuildingPrivilege();
                ulong[] owners;
                if (priv != null)
                {
                    owners = priv.authorizedPlayers.Select(id => { return id.userid; }).ToArray();
                }
                else
                {
                    owners = new ulong[] { entity.OwnerID };
                }

                string[] words = entity.ShortPrefabName.Split('/');
                var name = words[words.Length - 1].Split('.') [0];

                var di = new EntityDeath(name, GridPos(entity), owners);
                var body = JsonConvert.SerializeObject(di);

                if (ApiRequestOk())
                {
                    webrequest.Enqueue(
                        $"{Config["api_url"]}entity_death",
                        body,
                        (code, response) =>
                        {
                            if (!ApiSuccess(code == 200)) { ApiError(code, response); }
                        },
                        this,
                        RequestMethod.PUT,
                        headers(),
                        100f
                    );
                }
            }
        }

        void OnServerInitialized()
        {
            if (Clans != null)
            {
                var clan_tags = (JArray) Clans?.Call("GetAllClans");
                List<JObject> clans = new List<JObject>();
                foreach (string ctag in clan_tags)
                {
                    clans.Add((JObject) Clans?.Call("GetClan", ctag));
                }
                var body = JsonConvert.SerializeObject(clans);

                if (ApiRequestOk())
                {
                    Puts("Sending clans data to Poundbot");
                    webrequest.Enqueue(
                        $"{Config["api_url"]}clans",
                        body,
                        (code, response) =>
                        {
                            if (!ApiSuccess(code == 200))
                            {
                                ApiError(code, response);
                            }

                        },
                        this, RequestMethod.PUT, headers(), 100f);
                }
            }

            StartChatRunners();
        }

        void Unload()
        {
            KillChatRunners();
        }

        void KillChatRunners()
        {
            foreach (var runner in chat_runners)
            {
                runner.Destroy();
            }
            chat_runners.Clear();
        }

        void StartChatRunners()
        {
            var runners_to_start = Enumerable.Range(1, 2);
            foreach (int i in runners_to_start)
            {
                chat_runners.Add(startChatRunner());
            }
        }

        void OnClanCreate(string tag)
        {
            var clan = (JObject) Clans?.Call("GetClan", tag);
            var body = JsonConvert.SerializeObject(clan);

            if (ApiRequestOk())
            {
                Puts($"Sending clan {tag} to Poundbot");
                webrequest.Enqueue(
                    $"{Config["api_url"]}clans/{tag}",
                    body,
                    (code, response) =>
                    {
                        if (!ApiSuccess(code == 200))
                        {
                            ApiError(code, response);
                        }

                    }, this, RequestMethod.PUT, headers(), 100f);
            }
        }

        void OnClanUpdate(string tag) { OnClanCreate(tag); }

        void OnClanDestroy(string tag)
        {
            if (ApiRequestOk())
            {
                Puts($"Sending clan delete for {tag} to Poundbot");
                webrequest.Enqueue(
                    $"{Config["api_url"]}clans/{tag}",
                    null,
                    (code, response) =>
                    {
                        if (!ApiSuccess(code == 200))
                        {
                            ApiError(code, response);
                        }

                    }, this, RequestMethod.DELETE, headers(), 100f);
            }
        }

        private Timer startChatRunner()
        {
            return timer.Repeat(1f, 0, () =>
            {
                if (ApiRequestOk())
                {
                    webrequest.Enqueue(
                        $"{Config["api_url"]}chat",
                        null,
                        (code, response) =>
                        {
                            switch (code)
                            {
                                case 200:
                                    ChatMessage message = JsonConvert.DeserializeObject<ChatMessage>(response);
                                    if (message != null)
                                    {
                                        Puts($"{{{{Discord}}}} {message?.DisplayName}: {message?.Message}");
                                        PrintToChat($"<color=red>{{DSCD}}</color> <color=orange>{message?.DisplayName}</color>: {message?.Message}");
                                    }
                                    ApiSuccess(true);
                                    break;
                                case 204:
                                    ApiSuccess(true);
                                    break;
                                default:
                                    ApiError(code, response);
                                    break;
                            }

                        }, this, RequestMethod.GET, headers(), 1000f
                    );
                }
            });
        }

        void OnBetterChat(Dictionary<string, object> data)
        {
            if (ApiRequestOk())
            {
                IPlayer player = (IPlayer) data["Player"];
                var cm = new ChatMessage { };
                cm.SteamID = (ulong) Convert.ToUInt64(player.Id);
                cm.DisplayName = player.Name;
                cm.Message = (string) data["Text"];
                if (Clans != null)
                {
                    cm.ClanTag = (string) Clans?.Call("GetClanOf", player.Id);
                }
                var body = JsonConvert.SerializeObject(cm);

                webrequest.Enqueue(
                    $"{Config["api_url"]}chat",
                    body,
                    (code, response) => { if (!ApiSuccess(code == 200)) { ApiError(code, response); } },
                    this, RequestMethod.POST, headers(), 100f
                );
            }
        }

        #region Commands
        [ChatCommand("discord")]
        private void cmdDiscord(BasePlayer player, string command, string[] args)
        {
            if (ApiRequestOk())
            {
                if (args.Count() != 1)
                {
                    PrintToChat(player, "Usage: /discord <discord name>\n Example: /discord FancyGuy#8080");
                    return;
                }

                var da = new DiscordAuth(player.displayName, args[0], player.userID, (string) Clans?.Call("GetClanOf", player.userID));

                var body = JsonConvert.SerializeObject(da);

                webrequest.Enqueue(
                    $"{Config["api_url"]}discord_auth",
                    body,
                    (code, response) =>
                    {
                        if (ApiSuccess(code == 200))
                        {
                            PrintToChat(player, $"Enter the following PIN to the bot in discord: {da.Pin.ToString("D4")}");
                        }
                        else
                        {
                            ApiError(code, response);
                        }

                    }, this, RequestMethod.PUT, headers(), 100f);
            }
            else
            {
                PrintToChat(player, "Cannot connect to PoundBot right now. Please alert the admins.");
            }
        }

        #endregion

        private string GridPos(BaseEntity entity)
        {
            var size = World.Size;
            var gridCellSize = 150;
            var num2 = (int) (entity.transform.position.x + (size / 2)) / gridCellSize;
            var index = Math.Abs((int) (entity.transform.position.z - (size / 2)) / gridCellSize);
            return $"{this.NumberToLetter(num2) + index.ToString()}";
        }

        public string NumberToLetter(int num)
        {
            int num1 = Mathf.FloorToInt((float) (num / 26));
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