# Changelog

Format based on [Keep a Changelog](https://keepachangelog.com/); versioning follows [SemVer](https://semver.org/).

## [Unreleased]
- Added watching friends' streams inside the app: "Assistir" in "Streams salvas" opens the stream in a new "Assistindo" tab (WebView2), and each extra stream is added side by side (auto grid, focus mode, per-tile volume, fullscreen, auto-reconnect, in-tile password prompt). The player can be popped out into its own window. Signaling goes through the app, so it works with hosts on older LanCast versions. The viewer name is asked on first use and can be changed in Configurações.
- Widened the sidebar so the nav badges are not clipped.

## [1.3.1] - 2026-10-17
- Fixed CPU usage growing every time the stream was stopped and started again: viewers' SCTP receive threads kept spinning at 100% after the connection was closed, and loopback audio captures could be left running after the mixer was disposed.

## [1.3.0] - 2026-10-11
- Added in-app GitHub Release updates for installed and portable builds, automatic checks, SHA-256 validation, and restart after installation.
- Added an eye icon to "Convide seus amigos" to hide/show the IP in the invite links (the Copiar buttons still copy the real link). The choice is remembered.
- Added a per-application volume slider (0-200%) in the "Áudio dos apps" tab, same style as the microphone volume, saved in `volumes.json`. Each app is now a single row (name, volume, speaker button); the Permitido/Silenciado button became a green speaker with waves (on) or a red speaker with an X (muted).
- Added a "Streams salvas" tab: save a friend's address (IP or IP:port) with a name and see whether their stream is active (green icon, viewer count) or offline. Rename, open in the browser and remove are supported; status is checked every 4 seconds while the tab is open.
- Added a static landing page in `website/` (not part of the Windows app).

## [1.2.1] - 2026-10-09
- Integrated the LanCast Figma redesign into the Windows host and web viewer, including Inter typography and exported icons.
- Added a persistent collapsible sidebar and six appearance themes with live previews and restore-default action.
- Added moderation and restart confirmations, settings dirty state, source recovery guidance, local microphone meter and audio preferences while stopped.
- Redesigned the viewer with Portuguese statistics, 80% initial volume, fullscreen, password feedback and safe reconnect/leave handling.
- Fixed the app opening with an empty Compartilhar page until the tab was clicked.

## [1.2.0] - 2026-10-08
- "AO VIVO" indicator while streaming: red dot on the tray icon and taskbar button, plus a pill at the top of the screen (hidden from the capture). Can be turned off in Settings ("Mostrar indicador AO VIVO").

## [1.1.0] - 2026-10-08
- Renamed the app from VideoStreaming to LanCast (executables, installer, namespaces, UI).
- Settings folder moved to `%AppData%\LanCast` (previous settings are not migrated).
- Firewall rule renamed to `LanCast`; env var `VIDEOSTREAMING_DATA` is now `LANCAST_DATA`.

## [1.0.0] - 2026-10-08
- First public release.
