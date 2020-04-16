## About
Allows Rust players to be notified when their base entities are dstroyed VIA Discord.

## Setup

Install the Pound Bot plugin and follow the instructions there.

## Permissions

- `poundbot.raidalerts` - Allows users to receive raid alerts i `permitted_only.enabled` is `true` in the config

## Configuration

### Default Configuration

```JSON
{
  "config.version": 3,
  "debug.show_own_damage": false,
  "permitted_only.enabled": false
}
```

* **`debug.show_own_damage`** Set to true will alert when you break your own entities. This is good for initial testing, but it's recommended you disable it to avoid spamming your players.
* **`permitted_only.enabled`** Set to `true` to use the groups permissions system.

## Getting Help

You can use the command `!pb help` once PoundBot is in your server to get to our Discord server.