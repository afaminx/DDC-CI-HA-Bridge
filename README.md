# DDC/CI HA-Bridge

DDC/CI HA-Bridge is a small Windows tray application that adjusts the brightness of connected DDC/CI-capable monitors based on any numeric Home Assistant sensor, typically an illuminance sensor.

Adaptive screen brightness is common on nearly all modern mobile devices, but it is still uncommon on desktop computers, despite its potential benefits: lower energy consumption, reduced display wear, improved visual comfort, and better sleep quality.

This tool provides an easy way to automatically adjust your monitor brightness based on the illuminance of your environment.

<img src="/screenshots/main.png" alt="main-window" width="50%">

## Features

- Reads a numeric Home Assistant sensor, for example `sensor.office_illuminance`
- Maps numeric (lux) values to monitor specific brightness from 1 to 100 percent
- Controls all detected DDC/CI-capable monitors
- Runs in the Windows system tray
- Supports optional startup with Windows
- Stores the Home Assistant token securely for the current Windows user
- Supports Dark/Light Theme based on global Windows Setting
- Optional auto update check

## Setup

1. Download the latest release from the [Releases page](https://github.com/afaminx/DDC-CI-HA-Bridge/releases), or build `DDC-CI HA-Bridge.exe` yourself.
2. Start the app and open `Settings > Home Assistant...`.
3. Enter your Home Assistant host as `IP:port`, for example `192.168.3.134:8123`.
4. Enter the sensor entity ID, for example `sensor.office_illuminance`.
5. Create a Home Assistant long-lived access token and paste it into the token field.
6. Open `Settings > Brightness control...` and adjust the lux-to-brightness mapping.
7. Enable `Start with Windows` if the app should run automatically.

## Notes

Your monitors must support DDC/CI, and DDC/CI must be enabled in the monitor menu. Some laptop displays, docking stations, adapters, or GPU/driver combinations may not expose brightness control through DDC/CI.

If HDR is enabled, changing the screen brightness through DDC/CI may cause contrast, tone mapping, or perceived brightness issues. Depending on your monitor model, disabling HDR while using this tool may provide more consistent results.

## License

MIT License. Copyright (c) 2026 [planetenexpress.de](https://planetenexpress.de).
