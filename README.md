# DDC/CI HA-Bridge

DDC/CI HA-Bridge is a small Windows tray app that adjusts the brightness of connected DDC/CI-capable monitors based on a Home Assistant lux sensor.

## Features

- Reads a Home Assistant sensor, for example `sensor.gw2000a_solar_lux`
- Maps lux values to monitor brightness from 1 to 100 percent
- Controls all detected DDC/CI monitors
- Runs in the Windows system tray
- Optional Windows startup
- Stores the Home Assistant token protected for the current Windows user

## Setup

1. Download or build `DDC-CI HA-Bridge.exe`.
2. Start the app and open `Settings > Home Assistant...`.
3. Enter your Home Assistant host as `IP:port`, for example `192.168.3.134:8123`.
4. Enter the sensor entity ID, for example `sensor.gw2000a_solar_lux`.
5. Create a Home Assistant long-lived access token and paste it into the token field.
6. Open `Settings > Brightness control...` and adjust the lux-to-brightness mapping.
7. Enable `Start with Windows` if the app should run automatically.

## Notes

Your monitors must support DDC/CI, and DDC/CI must be enabled in the monitor menu. Some laptop displays and docking setups may not expose brightness control through DDC/CI.

## License

MIT License. Copyright (c) 2026 [planetenexpress.de](https://planetenexpress.de).
