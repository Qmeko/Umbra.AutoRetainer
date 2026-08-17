# Umbra AutoRetainer

[日本語](README.ja.md)

Umbra toolbar widget that shows AutoRetainer retainer and submarine venture status.

Example: `R2|5　M0|4`

- **R** = retainers (ready to collect | currently on a venture)
- **M** = submarines (ready to collect | currently on a voyage)
- Click opens AutoRetainer with `/ays`
- Colors and included characters/retainers can be changed in the widget settings

## Install

1. Build with `.\build.ps1` and `.\install-dev.ps1`
2. Enable **Custom Plugins** in Umbra settings
3. Add `Umbra.AutoRetainer.dll`
4. Add the **AutoRetainer** widget to the toolbar

Requires [Umbra](https://github.com/una-xiv/umbra) and [AutoRetainer](https://github.com/PunishXIV/AutoRetainer).
