PlatinVault Bridge
==================

What is this?
The bridge runs on your gaming PC and reports unlocked Steam achievements to your
PlatinVault server in real time. It needs no .NET and no administrator rights, updates
itself and shows a small trophy icon in the tray.

How to set it up
1. Unpack the ZIP (right-click > "Extract all"). The location does not matter, e.g. the desktop.
2. Double-click PlatinVault.Bridge.exe.
   If Windows says "Windows protected your PC", choose "More info" and then "Run anyway".
3. The setup window opens. It shows server and token (from bridge.json) and tests the
   connection right away: "Connected to ... - Steam account ..." means everything is fine.
4. "Install & start" sets the bridge up for your user:
   - copied to %LOCALAPPDATA%\Programs\PlatinVault Bridge
   - starts with Windows (can be switched off)
   - Start menu entry (can be switched off)
   - entry under Windows "Apps" for uninstalling
   Afterwards you can delete the unpacked folder.

Language
The bridge speaks the language of your PlatinVault (German or English): the ZIP already
carries it, and once the bridge is connected it follows PlatinVault if you change the
language there. Before it has ever connected, it uses the Windows display language.
"--language de" or "--language en" forces a language.

Handbook
The ZIP contains the PlatinVault handbook as a PDF, in German ("Handbuch (Deutsch).pdf") and
in English ("Handbook (English).pdf"). The bridge opens the one in its language: from the setup
window ("Open handbook") and later by right-clicking the tray icon > "Open handbook".

Licences
PlatinVault is free software under the GNU Affero General Public License v3 (AGPL-3.0-only);
the source code is at https://github.com/sushiflux/PlatinVault.
The ZIP also contains LICENSE (the licence text) and THIRD-PARTY-NOTICES.md
(copyright notices and licence texts of the bundled open-source parts, e.g. the .NET runtime).
Both files open in any text editor (right-click > "Open with" > Notepad) and are copied into the
program folder during the installation. In the PlatinVault interface they are under
Settings > System > "Licences" and in the footer of the page.

How do I see that it is running?
At the bottom right next to the clock you see the bridge's trophy icon. Coloured = connected,
grey = not connected. Hover over it to see the status and the current game with progress,
e.g. "Game: Cyberpunk 2077 (12/44)".
- Double-click the icon: status window
- Right-click: Open PlatinVault, Open handbook, Open log, Start with Windows, Exit, Uninstall
Windows 11 likes to hide new icons behind the arrow "^" next to the clock. To show it
permanently: Settings > Personalization > Taskbar > "Other system tray icons" > switch
PlatinVault Bridge on.
If you start PlatinVault.Bridge.exe again while it is running, its status window opens.

Where are my credentials?
In the file bridge.json (server address and your personal token), after the installation in
the program folder. Do not pass it on. If you created a new token in PlatinVault, download the
package again and run the setup again.

Log
%LOCALAPPDATA%\PlatinVault\bridge.log   (Windows key + R, paste the path, Enter)
or right-click the tray icon > "Open log".

Updates
The bridge fetches new versions from your PlatinVault server by itself, you do not have to
do anything. The icon briefly reports when it was updated.

Uninstall
Windows Settings > Apps > Installed apps > "PlatinVault Bridge" > Uninstall, or right-click
the tray icon > "Uninstall ...". The bridge is ended, the autostart and the program folder
are removed.
