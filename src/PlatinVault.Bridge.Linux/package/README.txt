PlatinVault Bridge for Linux
============================

What is this?
The bridge runs on your Linux PC (desktop, Steam Deck, Bazzite) and reports unlocked Steam achievements to your PlatinVault
server in real time. The server may run on another device (Unraid, NAS) or in Docker on this PC. It has no window: it runs as a
background service of your user, needs no .NET and no root rights, and updates itself from your PlatinVault server.
It can also start games when you press "Play" in PlatinVault.

How to set it up
1. Unpack the archive:      tar -xzf PlatinVault-Bridge-linux-x64.tar.gz
2. Go into the folder:      cd PlatinVault-Bridge
3. Install and start:      ./install.sh

install.sh copies the program to ~/.local/share/platinvault-bridge/, saves server and token from the bridge.json of this
package (readable by you only) and then deletes that copy from the download folder (it holds your token), sets up the systemd user service "platinvault-bridge" and starts it. Everything stays in
your home folder, so it also works on SteamOS and Bazzite with their read-only system partition.

Without the ready bridge.json (a package that is not from your own server) set it up by hand:
    echo '<token>' | ~/.local/share/platinvault-bridge/platinvault-bridge setup --server http://<server>:<port> --token-stdin
(The token comes from standard input so it does not show in the process list. --token <token> works as well.)

Is it working?
    ~/.local/share/platinvault-bridge/platinvault-bridge status
    journalctl --user -u platinvault-bridge -f
    The log file: ~/.local/state/platinvault-bridge/bridge.log

Where is Steam?
The bridge searches the usual places (~/.steam/steam, ~/.local/share/Steam, Flatpak, Snap). If yours is somewhere else:
    platinvault-bridge setup --steam-dir /path/to/Steam      (the folder that contains "steamapps")
or set PLATINVAULT_STEAM_DIR / STEAM_DIR.

Important - what is not verified yet
Reading achievements on Linux follows the Windows file format and has not been verified on real Linux Steam devices yet:
whether the local stats file is written at the moment of an unlock, and how the signed-in Steam account is found. The game
is detected through Steam's process list. Whether the systemd user service keeps running in the Steam Deck's Game Mode is also
not verified (Desktop Mode: yes). If something does not work, "status" and the log say what the bridge sees.

Language
The bridge speaks the language of your PlatinVault (German or English). Before the first connection it uses the system language.
"--language de" or "--language en" forces a language.

Uninstall
    ~/.local/share/platinvault-bridge/uninstall.sh            (keeps server and token)
    ~/.local/share/platinvault-bridge/uninstall.sh --purge    (removes everything)

Handbook and licences
The archive contains the PlatinVault handbook as PDF (German and English), LICENSE (GNU AGPL v3) and THIRD-PARTY-NOTICES.md
(licences of the bundled open-source parts, e.g. the .NET runtime). The source code is at https://github.com/sushiflux/PlatinVault.
