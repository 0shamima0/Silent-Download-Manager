# Setup Guide

## Build the desktop app

From the project root:

```powershell
.\scripts\build-release.ps1
```

The app and native host will be published to:

```text
build\publish\Silent Download Manager
```

Run the app:

```powershell
.\build\publish\SDM\SDM.App.exe
```

## Load the browser extension

Chrome:

1. Open `chrome://extensions`.
2. Enable `Developer mode`.
3. Click `Load unpacked`.
4. Select `extensions\chromium`.
5. Copy the extension ID shown by Chrome.

Edge:

1. Open `edge://extensions`.
2. Enable `Developer mode`.
3. Click `Load unpacked`.
4. Select `extensions\chromium`.
5. Copy the extension ID shown by Edge.

## Register Native Messaging

After building and loading the extension, register the native host.

Chrome:

```powershell
.\scripts\register-native-host.ps1 -Browser Chrome -ExtensionId "paste-extension-id-here"
```

Edge:

```powershell
.\scripts\register-native-host.ps1 -Browser Edge -ExtensionId "paste-extension-id-here"
```

Restart the browser after registration.

## Test

Right-click a link in Chrome or Edge and choose:

```text
Download with Silent Download Manager
```

The link should appear in the desktop app and start downloading.

On normal pages with direct media/file links, the extension can also show a small:

```text
Download with SDM
```

button near the bottom-right of the page. This works for direct public links such as `.mp4`, `.webm`, `.mp3`, `.zip`, `.pdf`, `.exe`, and similar files.

YouTube and protected streaming sites do not provide normal direct file URLs to the page. Those URLs point to a video page or temporary segmented streams, so the extension does not add a YouTube-style capture button.

