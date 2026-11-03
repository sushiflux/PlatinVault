PlatinVault for Linux / PlatinVault für Linux
=============================================

English
-------
PlatinVault runs directly on the Linux PC where Steam runs (desktop Linux, Steam Deck desktop mode, Bazzite): it reads the
Steam files of this computer itself, so no bridge is needed. You use it in the web browser.

Install (no root, nothing outside your home folder):
    tar xzf PlatinVault-linux-x64-<version>.tar.gz
    cd PlatinVault-linux-x64-<version>
    ./install.sh

The installer starts PlatinVault and prints its address: http://127.0.0.1:5233/ (also in your application menu as "PlatinVault").
PlatinVault listens on this computer only. If port 5233 is taken, the installer stops and tells you; choose another one with
    ./install.sh --port 5300

Update:     extract the new package and run its install.sh again. Your data is kept.
Remove:     ~/.local/share/platinvault/app/uninstall.sh   (it asks whether to keep your data)
Settings:   ~/.config/platinvault/platinvault.env   (port, address, data folder)
Data:       ~/.local/share/platinvault/data         (database, automatic backups, encrypted API keys)
Control:    ~/.local/share/platinvault/app/ctl.sh start|stop|restart|status|open
Log:        journalctl --user -u platinvault

Requirements: 64-bit Linux (x86_64) with systemd (optional - without it the menu entry starts PlatinVault itself),
libicu (usually installed; without it dates and numbers use a neutral format), a web browser.

Licence: GNU AGPL v3 (docs/LICENSE in the program folder), third-party notices: docs/THIRD-PARTY-NOTICES.md, handbook: docs/handbuch/ (also in the app).


Deutsch
-------
PlatinVault läuft direkt auf dem Linux-PC, auf dem Steam läuft (Linux-Desktop, Steam Deck im Desktop-Modus, Bazzite): Es liest
die Steam-Dateien dieses Rechners selbst, eine Bridge ist nicht nötig. Bedient wird es im Browser.

Installation (ohne root, nichts außerhalb deines Home-Ordners):
    tar xzf PlatinVault-linux-x64-<Version>.tar.gz
    cd PlatinVault-linux-x64-<Version>
    ./install.sh

Der Installer startet PlatinVault und nennt die Adresse: http://127.0.0.1:5233/ (außerdem im Anwendungsmenü als "PlatinVault").
PlatinVault lauscht nur auf diesem Rechner. Ist der Port 5233 belegt, hält der Installer an und sagt es dir; einen anderen
Port wählst du mit
    ./install.sh --port 5300

Update:        das neue Paket entpacken und dessen install.sh erneut ausführen. Deine Daten bleiben erhalten.
Entfernen:     ~/.local/share/platinvault/app/uninstall.sh   (fragt, ob deine Daten behalten werden)
Einstellungen: ~/.config/platinvault/platinvault.env   (Port, Adresse, Datenordner)
Daten:         ~/.local/share/platinvault/data         (Datenbank, automatische Sicherungen, verschlüsselte API-Schlüssel)
Steuern:       ~/.local/share/platinvault/app/ctl.sh start|stop|restart|status|open
Protokoll:     journalctl --user -u platinvault

Voraussetzungen: 64-Bit-Linux (x86_64) mit systemd (optional - ohne startet das Menü-Symbol PlatinVault selbst),
libicu (meist vorhanden; ohne sie nutzen Datum und Zahlen ein neutrales Format), ein Webbrowser.

Lizenz: GNU AGPL v3 (docs/LICENSE im Programmordner), Drittanbieter-Hinweise: docs/THIRD-PARTY-NOTICES.md, Handbuch: docs/handbuch/ (auch in der App).
