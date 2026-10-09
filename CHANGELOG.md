# Changelog

Format based on [Keep a Changelog](https://keepachangelog.com/); versioning follows [SemVer](https://semver.org/).

## [Unreleased]
- Integrated the LanCast Figma redesign into the Windows host and web viewer, including Inter typography and exported icons.
- Added a persistent collapsible sidebar and six appearance themes with live previews and restore-default action.
- Added moderation and restart confirmations, settings dirty state, source recovery guidance, local microphone meter and audio preferences while stopped.
- Redesigned the viewer with Portuguese statistics, 80% initial volume, fullscreen, password feedback and safe reconnect/leave handling.

## [1.2.0] - 2026-10-08
- "AO VIVO" indicator while streaming: red dot on the tray icon and taskbar button, plus a pill at the top of the screen (hidden from the capture). Can be turned off in Settings ("Mostrar indicador AO VIVO").

## [1.1.0] - 2026-10-08
- Renamed the app from VideoStreaming to LanCast (executables, installer, namespaces, UI).
- Settings folder moved to `%AppData%\LanCast` (previous settings are not migrated).
- Firewall rule renamed to `LanCast`; env var `VIDEOSTREAMING_DATA` is now `LANCAST_DATA`.

## [1.0.0] - 2026-10-08
- First public release.
