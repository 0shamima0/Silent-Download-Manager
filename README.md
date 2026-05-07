# Silent Download Manager

Silent Download Manager, short form SDM, is a lightweight Windows download manager built with C#/.NET and WPF, plus a Chromium browser extension scaffold for Chrome and Microsoft Edge.

## Current Features

- Direct HTTP/HTTPS download from URL
- Download queue list
- Progress bar, speed, file size, and status
- Pause and resume using HTTP range requests when the server supports it
- Cancel download
- Choose download folder
- Download history saved under `%LOCALAPPDATA%\SiS SDM`
- Chrome/Edge extension scaffold
- Native Messaging host bridge for sending browser links to the desktop app
- Direct media detector for normal public `.mp4`, `.webm`, `.mp3`, `.zip`, `.pdf`, `.exe`, and similar links

## Project Structure

```text
Silent Download ManagerDownloadManager
|-- src
|   |-- Silent.DownloadManager.App
|   |   |-- Models
|   |   |-- Services
|   |   |-- ViewModels
|   |   |-- MainWindow.xaml
|   |   `-- MainWindow.xaml.cs
|   `-- Silent.DownloadManager.NativeHost
|       `-- Program.cs
|-- extensions
|   |-- chromium
|   `-- native-messaging
|-- scripts
|-- docs
`-- build
```

## Requirements

- Windows 10/11
- Visual Studio Community with `.NET desktop development`
- .NET SDK 10 or later
- Chrome or Microsoft Edge

## Build

```powershell
.\scripts\build-release.ps1
```

More setup details are in `docs\SETUP.md`.

## Notes

This is a professional MVP foundation. It intentionally does not bypass DRM, paid streaming protections, encrypted media restrictions, or YouTube-style protected streaming flows.

