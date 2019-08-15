**Important update! As of `2.0.0`, all PoundBot plugins must be updated to `v2`**

This requires the **PoundBot** plugin is installed and configured.

## Setup

1. In Rust, type `/pbreg "Your Username#7263"`
2. `@PoundBot` should message you asking for the PIN number displayed in-game chat.
3. Respond to `PoundBot` with that PIN number and you should be connected!

## Configuration

### Default Configuration
```JSON
{
  "config.version": 2,
  "debug.show_own_damage": false,
  "permitted_only.enabled": false,
  "permitted_only.group": "vip"
}
```

* **`debug.show_own_damage`** Set to true will alert when you break your own entities. This is good for initial testing, but it's recommended you disable it to avoid spamming your players.
* **`permitted_only.enabled`** Set to try to make it so only users in a specified group will receive raid alerts. Useful for things like VIP systems.
* **`permitted_only.group`** The group to use for the `permitted_only.enabled` setting

## Getting Help

You can use the command `!pb help` once PoundBot is in your server to get to our Discord server.