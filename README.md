# Acer Caps Lock pop-up fix

Stops the on-screen pop-up that Acer laptops show every time you press **Caps Lock** or **Num Lock**, without breaking PredatorSense, performance modes or any other Acer feature.

The usual advice is to disable or uninstall Acer Quick Access. That breaks things: on Predator/Helios laptops, the Quick Access service is also what PredatorSense uses to switch operating modes (Eco / Balanced / Performance / Turbo). Your laptop gets stuck in one mode. Blocking only the pop-up process `AQAUserPS.exe` fails too, because the service then rejects every PredatorSense request. There is no setting for the pop-up.

This tool leaves Acer's software alone and just keeps the pop-up window from appearing for lock keys.

## Install

1. Download this repository (green **Code** button → **Download ZIP**) and unzip it.
2. Open **Terminal (Admin)** or **PowerShell (Admin)**: right-click the Start button.
3. Run, using the folder where you unzipped it:

   ```powershell
   cd "$env:USERPROFILE\Downloads\acer-capslock-popup-fix-main"
   powershell -ExecutionPolicy Bypass -File .\install.ps1
   ```

4. If Windows asks whether to install a certificate named **"LockOsdBlocker local signing"**, click **Yes** (see [Security](#security)).
5. Press Caps Lock. The pop-up should be gone. The helper starts automatically at every login.

Nothing to download besides this repo: the script compiles the helper from the included source with the C# compiler that ships with Windows.

## Uninstall

In an admin PowerShell, in the same folder:

```powershell
powershell -ExecutionPolicy Bypass -File .\uninstall.ps1
```

This stops the helper, removes the autostart entry, deletes `C:\Program Files\LockOsdBlocker` and removes the certificate.

## How it works

- Acer's pop-up is a window titled `QA OSD`, owned by `C:\Program Files\AcerQAAgent\AQAUserPS.exe`. Acer runs that process with **UIAccess**, a Windows privilege meant for screen readers. UIAccess shields its windows from every program that doesn't have UIAccess too, even ones running as administrator.
- So the helper is a UIAccess program as well. Windows only grants UIAccess to programs that are **signed with a trusted certificate** and installed under `Program Files`. That is why the installer creates a certificate.
- The helper watches for Caps Lock, Num Lock and Scroll Lock with a low-level keyboard hook. When one is pressed, it immediately clips the pop-up window to an empty region, before Acer's process even reacts. For the next 1.5 seconds it hides the window if Acer shows or moves it. After that the window is restored, so Acer's other pop-ups (mic mute, touchpad, mode switch) still work.
- About 150 lines of C#, in [`src/LockOsdBlocker.cs`](src/LockOsdBlocker.cs). Read it before you run it.

## Security

Please read this; you're installing something that sees key presses.

- **Keyboard hook:** the helper only checks whether the key was Caps Lock, Num Lock or Scroll Lock. It doesn't record, store or send keys anywhere and has no network code. Logging is off unless you start it with `--log`, and even then it only writes window and hide events, never keys you type.
- **Certificate:** the installer makes a new certificate on *your* PC, adds it to the trusted root store (needed for UIAccess), signs the helper and then **deletes the private key**. Without the key, nobody, including the installer, can use that certificate to sign anything else. Every install gets its own certificate; nothing is shared between PCs.
- **Windows Defender** may flag it: a program with a keyboard hook, UIAccess and a self-made signature looks like a keylogger to heuristics. The source is all here to check.
- Everything is removed by `uninstall.ps1`.

## Compatibility

Tested on an **Acer Predator Helios Neo 16 (PHN16-73)**, Windows 11 25H2, Acer Quick Access service 1.5.24 with PredatorSense 5.1. Other Acer models that run `AQAUserPS.exe` (check Task Manager → Details) will probably work. If the pop-up still shows, open an issue with your model and Quick Access version.

Diagnostics: start the helper with `--log` (writes `%TEMP%\LockOsdBlocker.log`). `--no-clip` and `--no-hide` turn off one of the two methods for testing.

## Disclaimer

Not affiliated with or endorsed by Acer. Use at your own risk. MIT licensed.
