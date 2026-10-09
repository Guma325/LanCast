# Changelog

Format based on [Keep a Changelog](https://keepachangelog.com/); versioning follows [SemVer](https://semver.org/).

## [1.2.0] - 2026-10-08
- "AO VIVO" indicator while streaming: red dot on the tray icon and taskbar button, plus a pill at the top of the screen (hidden from the capture). Can be turned off in Settings ("Mostrar indicador AO VIVO").

## [1.1.0] - 2026-10-08
- Renamed the app from VideoStreaming to LanCast (executables, installer, namespaces, UI).
- Settings folder moved to `%AppData%\LanCast` (previous settings are not migrated).
- Firewall rule renamed to `LanCast`; env var `VIDEOSTREAMING_DATA` is now `LANCAST_DATA`.

## [1.0.0] - 2026-10-08
- First public release.
