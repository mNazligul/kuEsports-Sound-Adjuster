# kuEsporSoundApp

# kuEsports sound Adjuster

A small, lightweight Windows app that lists every application currently producing sound and lets you **lower or restore the volume of a single app with one hotkey** (for example: drop Discord to 10% while you focus on the game, press the key again to bring it back).

---

> ## ⚠️ WARNING — PLEASE READ BEFORE USE
>
> **What this app uses**
> - [.NET 8](https://dotnet.microsoft.com/) with Windows Forms (user interface)
> - [NAudio 2.2.1](https://github.com/naudio/NAudio) (reads and sets per-app volume through the official Windows Core Audio API)
> - `user32.dll` (`RegisterHotKey` / `UnregisterHotKey`, Windows' standard global hotkey functions)
>
> **What this app does NOT do**
> - It does **not** access, read, modify, or inject anything into any game or game files, game memory, or game processes.
> - It does **not** read or modify any of your personal or system files. The only file it writes is its own settings file at `%AppData%\KuESportsSoundMixer\settings.json` (volume targets and hotkeys).
> - It does **not** use keyboard hooks, simulate keyboard/mouse input, draw overlays, or connect to the internet.
>
> **What it does**
> - It only asks **Windows** for audio information (which apps are playing sound, their names, and their volume levels) and then asks Windows to change an app's volume. This is the same thing the built-in Windows Volume Mixer does.
>
> **Anti-cheat**
> - The app is designed so that it does not interact with games or anti-cheat software, and we are not aware of any anti-cheat issues. However, it is **not an officially approved or whitelisted tool** by any anti-cheat vendor or game publisher, so we cannot give a guarantee.
>
> **Tournaments**
> - Because this is not a widely used application, **if you plan to use it in tournaments or organized competitive play, you may need to ask the tournament organizers for permission first.**
> - **Responsibility for any tournament bans, disqualifications, or penalties lies entirely with you.**
>
> This project is provided "as is", without warranty of any kind. It is not affiliated with or endorsed by Microsoft, Ubisoft, BattlEye, Discord, or any game publisher.

---

## Features

- Automatically lists every app that plays sound on any active output device.
- A horizontal slider per app sets the **target volume** (0–100%).
- Assign a **global hotkey** per app. Press it once to drop the app to the target volume, press it again to restore the previous volume.
- Restores all lowered volumes automatically when you exit the app.
- The window's **X button minimizes to the system tray**. Right-click the tray icon and choose **Çıkış** (Exit) to really quit.
- Remembers your sliders and hotkeys between runs.
- Standard, plain Windows Forms look. Small and fast.

## How to use

1. Start the app, then start the apps you want to control (Discord, game, music, browser...). They appear in the list.
2. Drag the slider of an app to the volume you want when the hotkey is pressed (for example 10%).
3. Click **Bind key** (Assign key), then press the key or key combination you want (for example `Ctrl+9`).
4. Press the hotkey to lower the app's volume. Press it again to restore the previous volume.
5. Click **Remove Key** to remove the hotkey of an app. Pressing `Delete` or `Backspace` while assigning also clears it, and `Esc` cancels.

> Tip: assigning a single key such as `9` with no modifier means that key is captured system-wide and will no longer type a `9` in other programs while the mixer is running. A combination like `Ctrl+9` avoids this.
>
> ## Download

**[⬇️ Download the latest version](https://github.com/mNazligul/kuEsports-Sound-Adjuster/releases/latest)**

1. Open the link above and download the `.exe` file under **Assets**.
2. Run it. No installation needed.

The `.exe` is not code-signed, so Windows SmartScreen may show an "Unknown publisher" warning the first time. Choose **More info → Run anyway**. Some antivirus programs may also give a false alarm for unsigned executables. You can always build the app yourself from the source code in this repository.


## How it connects to Windows

```
 Your hotkey press
        │
        ▼
 Windows (user32.dll: RegisterHotKey)  ──►  sends a WM_HOTKEY message to this app's window
        │
        ▼
 kuE-sports sound mixer
        │   asks for the list of audio sessions / changes a session's volume (via NAudio)
        ▼
 Windows Core Audio service  ──►  changes the volume of that app's audio session
```

- **Reading audio information:** the app asks Windows' Core Audio service for the list of *audio sessions*. Every app that plays sound gets one session. For each session, the app reads the process ID (to get the app's name, like Task Manager shows) and the session's volume.
- **Changing volume:** the app sets the session's volume through Windows' `SimpleAudioVolume` control. The game or app itself is never touched. Windows applies the volume change on its side.
- **Hotkeys:** the app registers the hotkey with Windows using `RegisterHotKey`. When you press it, Windows sends a message to the app window. No keyboard hook is installed and no keystrokes are recorded.

## Libraries and technologies explained

| Name | What it is | Why we use it |
|---|---|---|
| **.NET 8** | Microsoft's application platform (runtime + base libraries). | Runs the app. |
| **Windows Forms** (`System.Windows.Forms`) | .NET's classic desktop UI toolkit (windows, buttons, sliders). | All of the user interface. `NotifyIcon` provides the tray icon, `TrackBar` is the slider. |
| **NAudio** (`NAudio.CoreAudioApi`) | A popular open-source .NET audio library. Its `CoreAudioApi` part wraps Windows' Core Audio API for C#. | Lets C# list audio devices and read/set per-app volume without writing low-level COM code. |
| **Windows Core Audio API** (also called **WASAPI**, *Windows Audio Session API*) | Windows' built-in audio system, the same one the Windows Volume Mixer uses. | The actual source of the audio-session and volume information. |
| **`user32.dll`** | A core Windows system library. | `RegisterHotKey` / `UnregisterHotKey` register system-wide hotkeys. Called through P/Invoke (`DllImport`). |
| **`System.Diagnostics.Process`** | .NET class for process information. | Converts a process ID into the app name (for example `Discord`). Reads the name only. |
| **`System.Text.Json`** | .NET's built-in JSON library. | Saves and loads the settings file. |

### NAudio classes used

| Class / value | Meaning |
|---|---|
| `MMDeviceEnumerator` | Lists the audio devices of the PC. (*MM* = multimedia.) |
| `MMDevice` | One audio device (speakers, headset, virtual cable...). |
| `DataFlow.Render` | Output devices (where sound is played). The opposite, `Capture`, would be microphones. |
| `DeviceState.Active` | Only devices that are currently enabled and plugged in. |
| `AudioSessionManager` | Provides the list of audio sessions of a device. |
| `AudioSessionControl` | One audio session = one app that plays (or played) sound. |
| `SimpleAudioVolume` | The volume control of a single session (this is the slider in the Windows Volume Mixer). |

### NAudio version note

NAudio **3.x requires .NET 9**. This project targets **.NET 8**, so use **NAudio 2.2.1**. If you move the project to .NET 9 you can use the newer NAudio.

## Requirements

- Windows 10 or Windows 11
- To run a framework-dependent build: [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0)
  (not needed if you publish as *self-contained*)

## Settings

Settings are stored per user at:

```
%AppData%\KuESportsSoundMixer\settings.json
```

## Build from source

<details>
<summary>Click to expand (for developers)</summary>

1. Install **Visual Studio 2022** with the **.NET desktop development** workload.
2. Clone this repository and open the solution.
3. Restore NuGet packages (NAudio **2.2.1**) and press **F5**.

</details>


Delete this file to reset all sliders and hotkeys.

## Troubleshooting

- **An app does not appear in the list:** the app must have produced sound at least once and must play on an active output device. Start playback and wait 2 seconds.
- **The hotkey button says "Kullanımda!" (In use):** another program already uses that hotkey. Pick another combination.
- **The volume does not change:** make sure the title of the app's box shows "(kısıldı)" after pressing the hotkey. If it does and the volume still does not change, the app itself may be resetting its own volume.
- **Different headsets / virtual audio devices (Sonar, Voicemeeter, etc.):** the app scans all active output devices, so apps routed to virtual devices should also appear.

## Project structure

```
Form1.cs         – the whole application (UI, audio sessions, hotkeys, tray, settings)
Form1.Designer.cs, Program.cs – standard files generated by the Windows Forms template
.gitignore       – Visual Studio default (keeps bin/ and obj/ out of the repository)
LICENSE          – MIT license
README.md        – this file
```

## License

This project is licensed under the MIT License. See the [`LICENSE`](LICENSE) file for details.

[NAudio](https://github.com/naudio/NAudio) is a separate project, also released under the MIT License.

## Disclaimer

Use at your own risk. See the warning at the top of this file. The authors are not responsible for any bans, penalties, disqualifications, data loss, or other damage resulting from the use of this software.
