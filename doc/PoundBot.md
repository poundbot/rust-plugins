**Important update! As of `2.0.0`, all PoundBot plugins must be updated to `v2`**

[PoundBot](https://github.com/poundbot/poundbot) is a Discord bot for game servers.

This plugin is the base communication layer to the bot. 
You must intall other plugins to provide additional functionality. 

## Setup

1. Download `PoundBot.cs` and add it to your plugins directory.
2. Add the bot to your Discord server at [https://add.poundbot.com/](https://add.poundbot.com/) 
3. Command PoundBot with `!pb server add myservername` in the channel you want your chat relay to occur.
4. PoundBot will whisper you your API key and instructions on where to put it.

## Using PoundBot

Authenticating with PoundBot associates your stem account with Discord. This is necessary to determine
who to send messages to from their in-game identity.

1. In chat, type `/pbreg "Your Username#7263"`
2. PoundBot should message you asking for the PIN number displayed in chat.
3. Send a message in Discord to PoundBot with that PIN number and you will receive a registration confirmation

## Configuration

### Default Configuration

```json
{
  "api.key": "API KEY HERE",
  "api.url": "https://api.poundbot.com/",
  "config.version": 2,
  "players.registered.group": "poundbot.registered",
  "command.poundbot_register": "pbreg"
}
```

## Getting Help

You can use the command `!pb help` once PoundBot is in your server to get to our Discord server.