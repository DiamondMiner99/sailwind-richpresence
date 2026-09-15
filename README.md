# Sailwind Rich Presence

Shows what you are doing in Sailwind on your Discord profile.

## Features

- The first line says where you are: the boat you are on and whether it is moored, anchored or at sea,
  or that you are ashore, swimming, asleep or at the shipyard.
- The port is named when you are moored in it or walking around it.
- At sea the line reads "At sea", names the region ("At sea in Aestrin"), or, soon after leaving port,
  names the port you left ("Out of Fort Aestrin"). The choice is made at random and changes every 4 to 8
  minutes.
- The status never shows your position, speed, heading, the nearest port or the in-game time.
- The second line says whether you are sailing solo or with a [Sailwind Co-op](https://github.com/DiamondMiner99/sailwind-coop)
  crew, and Discord shows the crew size next to it.
- A timer shows how long the game has been open, in real time.
- Friends looking at your profile see buttons linking to this mod and to Sailwind on Steam. Discord does
  not show you your own buttons.
- Modded boats such as HMS Leopard show their own name.

Example:

```
Brig, moored at Fort Aestrin
Sailing as captain (3 of 8)
```

## Installation

Requires BepInEx 5 (x64) and the Discord desktop app running on the same PC. Discord in a browser does
not work. "Share your detected activities with others" has to be on in Discord's Activity Privacy
settings.

Download the .zip from the latest [release](https://github.com/DiamondMiner99/sailwind-richpresence/releases)
and extract it into your Sailwind folder. The folder structure should look like:

```
BepInEx\
  plugins\
    SailwindRichPresence\
      SailwindRichPresence.dll
```

Built against Sailwind 0.38.1. Nothing else needs to be installed. If Discord is closed or restarts, the
status comes back within 20 seconds of Discord starting again.

## Sailwind Co-op

Works with or without Sailwind Co-op. Crew info needs Sailwind Co-op 0.4.0 or later; older versions show
you as sailing solo. It is client-side, so only the players who want it need to install it. Nothing in
the status can be used to find or join your crew.

## Config

Options are in `BepInEx/config/com.diamondminer99.richpresence.cfg` or the F1 menu if you have the
[BepInEx Configuration Manager](https://github.com/BepInEx/BepInEx.ConfigurationManager). Changes apply
without a restart.

- `Enabled`: master switch. Off clears the status.
- `ShowBoatAndPort`: off hides the boat, the port, the port you left and the region, leaving moored,
  anchored, at sea or ashore.
- `ShowCrew`: off hides the second line, including the captain's name and the crew size.
- `ShowElapsedTime`: off hides the timer.

## Building

```
dotnet build "src\SailwindRichPresence\SailwindRichPresence.csproj" -c Release -p:GameDir="C:\Program Files (x86)\Steam\steamapps\common\Sailwind"
```

The images in `art` are drawn by `tools/make_art.py`. They are uploaded to the Discord application under
Rich Presence, Art Assets, with the file name (without `.png`) as the asset name.

## License

MIT, see [LICENSE](LICENSE).
