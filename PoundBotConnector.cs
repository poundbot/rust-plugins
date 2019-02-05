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
    [Info("Pound Bot Connector", "MrPoundsign", "0.2.6")]
    [Description("Connector for the PoundBot, with raid alerts and chat relaying to Discord.")]

    class PoundBotConnector : RustPlugin
    {
        [PluginReference]
        Plugin Clans;

        protected int ApiChatRunnersCount = 0;

        protected int ApiRetrySeconds = 2;
        protected int ApiRetryNotify = 50;

        protected bool ApiInError = false;
        protected bool ApiRetry = false;
        protected uint ApiRetryAttempts = 0;
        protected DateTime ApiErrorTime;
        protected DateTime LastApiAttempt;
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
                    Puts(string.Format(lang.GetMessage("connector.time_in_error", this), DateTime.Now.Subtract(ApiErrorTime).ToShortString()));
                }
                ApiRetry = true;
            }
            return (!ApiInError || ApiRetry);
        }

        private void ApiError(int code, string response)
        {
            string error;
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

            if (code == 0)
            {
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
            Puts(string.Format(lang.GetMessage("connector.error", this), code, error));
        }

        private bool ApiSuccess(bool success)
        {
            // Reset retry varibles if we're successful
            if (ApiInError && success)
            {
                Puts(lang.GetMessage("connector.reconnected", this));
                Puts(string.Format(lang.GetMessage("connector.time_in_error", this), DateTime.Now.Subtract(ApiErrorTime).ToShortString()));
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
            Config["api_url"] = "http://poundbot.mrpoundsign.com/";
            Config["show_own_damage"] = false;
            Config["api_key"] = "API KEY HERE";
        }
        #endregion

        #region Oxide Hooks
        private void Init()
        {
            var Messages = new Dictionary<string, string>
                {
                    { "connector.reconnected", "Reconnected with PoundBot" },
                    { "connector.time_in_error", "Total time in error: {0}" },
                    { "connector.sending_clans", "Sending clans data to PoundBot" },
                    { "connector.sending_clan", "Sending clan {0} to PoundBot" },
                    { "connector.sending_clan_delete", "Sending clan delete for {0} to PoundBot" },
                    { "connector.error", "Error communicating with PoundBot: {0}/{1}" },
                    { "connector.user_error", "Cannot connect to PoundBot right now. Please alert the admins." },
                    { "chat.discord", "<color=red>{{DSCD}}</color> <color=orange>{0}</color>: {1}" },
                    { "chat.console", "{{DSCD}} {0}: {1}" },
                    { "discord.pin", "Enter the following PIN to the bot in discord: {0}." },
                    { "discord.connected", "You are connected to discord." },
                    { "usage", "Usage: /discord \"<discord name>\"\n Example: /discord \"Fancy Guy#8080\"" },
                };
            lang.RegisterMessages(Messages, this);
        }

        void OnServerInitialized()
        {
            if (Clans != null)
            {
                var clan_tags = (JArray) Clans.Call("GetAllClans");
                List<JObject> clans = new List<JObject>();
                foreach (string ctag in clan_tags)
                {
                    clans.Add((JObject) Clans.Call("GetClan", ctag));
                }
                var body = JsonConvert.SerializeObject(clans);

                if (ApiRequestOk())
                {
                    Puts(lang.GetMessage("connector.sending_clans", this));
                    webrequest.Enqueue(
                        $"{Config["api_url"]}api/clans",
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

        #endregion

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
                        $"{Config["api_url"]}api/entity_death",
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
            if (Clans != null)
            {
                var clan = (JObject) Clans.Call("GetClan", tag);
                var body = JsonConvert.SerializeObject(clan);

                if (ApiRequestOk())
                {
                    Puts(string.Format(lang.GetMessage("connector.sending_clan", this), tag));
                    webrequest.Enqueue(
                        $"{Config["api_url"]}api/clans/{tag}",
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
        }

        void OnClanUpdate(string tag) { OnClanCreate(tag); }

        void OnClanDestroy(string tag)
        {
            if (ApiRequestOk())
            {
                Puts(string.Format(lang.GetMessage("connector.sending_clan_delete", this), tag));
                webrequest.Enqueue(
                    $"{Config["api_url"]}api/clans/{tag}",
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
            return timer.Every(1f, () =>
            {
                if (ApiChatRunnersCount < 2)
                {
                    ApiChatRunnersCount ++;
                    if (ApiRequestOk())
                    {
                        webrequest.Enqueue(
                            $"{Config["api_url"]}api/chat",
                            null,
                            (code, response) =>
                            {
                                ApiChatRunnersCount --;
                                switch (code)
                                {
                                    case 200:
                                        ChatMessage message = JsonConvert.DeserializeObject<ChatMessage>(response);
                                        if (message != null)
                                        {
                                            Puts(string.Format(lang.GetMessage("chat.console", this), message?.DisplayName, message?.Message));
                                            PrintToChat(string.Format(lang.GetMessage("chat.discord", this), message?.DisplayName, message?.Message));
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

                            }, this, RequestMethod.GET, headers(), 120000f
                        );
                    }
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
                cm.ClanTag = (string) Clans?.Call("GetClanOf", player.Id);

                var body = JsonConvert.SerializeObject(cm);

                webrequest.Enqueue(
                    $"{Config["api_url"]}api/chat",
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
                    PrintToChat(player, lang.GetMessage("usage", this, player.IPlayer.Id));
                    return;
                }

                var da = new DiscordAuth(player.displayName, args[0], player.userID, (string) Clans?.Call("GetClanOf", player.userID));

                var body = JsonConvert.SerializeObject(da);

                webrequest.Enqueue(
                    $"{Config["api_url"]}api/discord_auth",
                    body,
                    (code, response) =>
                    {
                        if (ApiSuccess(code == 200))
                        {
                            PrintToChat(player, string.Format(lang.GetMessage("discord.pin", this, player.IPlayer.Id), da.Pin.ToString("D4")));
                        }
                        else if (code == 405) // Method not allowed means we're already connected
                        {
                            PrintToChat(player, lang.GetMessage("discord.connected", this, player.IPlayer.Id), da.Pin.ToString("D4"));
                        }
                        else
                        {
                            ApiError(code, response);
                        }

                    }, this, RequestMethod.PUT, headers(), 100f);
            }
            else
            {
                PrintToChat(player, lang.GetMessage("connector.user_error", this, player.IPlayer.Id));
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
