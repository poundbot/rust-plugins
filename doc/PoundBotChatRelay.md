**Important update! As of `2.0.0`, all PoundBot plugins must be updated to `v2`**

This plugin provides bidirectional chat relay to Discord via PoundBot

## Configuration

### Default Configuration

```json
{
  "config.version": 3,
  "chat.channel": "",
  "chat.prefix": ":radioactive:",
  "chat.server_channel": "",
  "chat.server_styled": true,
  "chat.server_styled_color": "darkred",
  "chat.styled": false,
  "chat.styled_color": "darkorange",
  "relay.betterchat": true,
  "relay.chat": true,
  "relay.discordchat": true,
  "relay.givenotices": true,
  "relay.serverchat": true
}
```

Game Server Chat -> Discord:
* **`chat.channel`** set this to the channel name or ID you wish to have server chat relayed to
* **`chat.styled`** set to false if you don't want the messages to have colored borders. Note: This requires the `Embed Links` permission to be enabled for PoundBot.
* **`chat.styled_color`** The default color for messages sent to discord for styling. You can use color names or hex codes. e.g. `red` or `#ff0000`.
* **`chat.prefix`** This will be prefixes onto the chat messages sent to Discord
* **`relay.chat`** relays chat messages to discord
* **`relay.betterchat`** enable to use Better Chat for `relay.chat`. Also if you're using styled chat, it will embed the messages and color them based upon the users Better Chat message color.

Server Console Chat -> Discord
* **`chat.server_channel`** set this to the channel name or ID you wish to have server chat relayed to
* **`chat.server_styled`** set to false if you don't want the messages to have colored borders. Note: This requires the `Embed Links` permission to be enabled for PoundBot.
* **`chat.server_styled_color`** The default color for messages sent to discord for styling. You can use color names or hex codes. e.g. `red` or `#ff0000`.
* **`relay.serverchat`** relays web console **say** messages to discord
* **`relay.givenotices`** relays item give notices to discord

Discord -> Game Server
* ** Note: You must run `!pb server chathere` in the channel you want to be sent to Discord. **
* **`relay.discordchat`** relays messages from discord to the game server

## Getting Help

You can use the command `!pb help` once PoundBot is in your server to get to our Discord server.