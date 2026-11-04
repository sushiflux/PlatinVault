---
title: PlatinVault
subtitle: Handbook
tagline: Live trophies for your Steam games and 100 % guides in one place – how to set up PlatinVault and use it every day.
version: v2026.11.17
date: 2026-11-03
status: Release
copyright: © 2026 SushiFlux
lang: en
---

# 1 What PlatinVault does for you

PlatinVault is your companion on the way to 100 % in Steam games:

- **Live trophies:** when you unlock a Steam achievement, PlatinVault shows its own trophy popup with animation and sound
  right away. Once you have every achievement of a game, the big **platinum moment** follows.
- **100 % guides:** for every game an AI researches a complete guide with chapters such as roadmap, collectibles,
  weapons, bosses or bestiary – with checklists you can tick off.
- **Your library at a glance:** trophies, playtime, ratings and your progress in one place.

Steam calls them "achievements"; PlatinVault calls them **trophies** and shows them as bronze, silver or gold – depending
on how rare they are among all players.

> [!NOTE] This handbook describes the current state of PlatinVault – it only shows what is already built in.

# 2 How PlatinVault runs

PlatinVault is a web app: you open it in the browser – on your PC, tablet or phone. Behind it runs a small server that
stores your games, guides and ticks in a database.

| Where does the server run? | Where do the live trophies come from? |
|---|---|
| On your gaming PC | PlatinVault reads Steam directly on that PC – nothing else needed. |
| On another machine (server, NAS, Unraid) | The small **PlatinVault Bridge** (Windows or Linux) runs on the gaming PC and reports new trophies to the server instantly (chapter 8). |

On the gaming PC, PlatinVault is also available as a **Windows program** with its own window, an overlay over your game and its own
database – see chapter 11. On a Linux gaming PC (desktop, Steam Deck in Desktop Mode, Bazzite) there is a **Linux package** that also
reads Steam directly – see chapter 13. **On Linux, trophy detection is still experimental** (chapter 13).

# 3 Installation on Unraid, Linux and NAS

PlatinVault comes as a container image. On Unraid you create it from the template; on a normal Linux machine (and on NAS systems that support Compose files) you start it with a ready-made Compose file. Sections 3.1 to 3.3 describe the three ways; Windows has its own program (chapter 11), and for the Linux PC where Steam runs there is a package of its own (chapter 13).

## 3.1 Unraid

PlatinVault is available as a container for Unraid. There is no database to set up: PlatinVault brings its own – one file in the
appdata folder.

1. Create the container from the template and fill it in:

   | Field | Meaning |
   |---|---|
   | WebUI Port | Port to open PlatinVault in the browser (default 5233). |
   | Appdata (database, backups, keys) | Folder for everything of PlatinVault: the database (`platinvault.db`), the automatic backups (`backups`), the encrypted API keys and caches. Default `/mnt/cache/appdata/platinvault` – directly on the cache pool, because users report damaged databases through the `/mnt/user/…` path. If your pool has another name, use `/mnt/<poolname>/appdata/platinvault`. **Never delete it.** |
   | PUID / PGID (advanced) | User and group that own the appdata folder and that PlatinVault runs as. Default 99 / 100 – the owner Unraid gives a new appdata folder; only change it if your folder belongs to another user. |

2. Start the container and open PlatinVault in the browser: `http://<IP of your Unraid>:5233`.
3. The first time, you first choose your **language** on a full-screen page (Deutsch or English; the language of your browser is preselected, and you can change it at any time later in the top bar). Then a short **tour** guides you through the app. Start it again any time with the **?** at the top right.

You get updates through Unraid like for any other container; your data in the appdata folder stays. Whether your container
gets only releases or betas too is decided by its image tag – see chapter 12.

## 3.2 Linux with Docker

On a Linux machine (or server) with Docker you need nothing but the Compose plugin (`docker compose version` prints a version). The
**database and the automatic backups are inside the container** – you do not set up a database and you do not make up a password.

1. Create a folder (for example `~/platinvault`) and download the files `docker-compose.yml` and `restore-db.sh` from
   the PlatinVault repository on GitHub (`github.com/sushiflux/PlatinVault`, folder `deploy/`) into it. If you want to change the port or the user,
   also download `.env.example` and save it as `.env`.
2. Start PlatinVault in that folder with one command:

   ```
   docker compose up -d
   ```

   On the first start Docker pulls the image and PlatinVault creates its database in the folder `data`. You do not have to enter anything.
3. Open `http://<address of your server>:5233` in the browser and continue with chapter 4.

Good to know:

- **No sign-in:** PlatinVault has no login. Run it in your own home network only and do not open the port to the internet.
- **Folder:** next to the Compose file you get `data` with everything: the database `platinvault.db`, the backups in `data/backups`,
  your encrypted API keys and settings – never delete it. It belongs on a local disk of the server (not a network share). Also copy it
  somewhere else regularly.
- **User id:** the files in `data` belong to the user with the numbers `PUID`/`PGID`; the default is 1000 / 1000, on most Linux systems
  the first normal user. The command `id` shows your numbers; enter other values (and a different port, default 5233) in the file `.env`.
- **Updating:** `docker compose pull && docker compose up -d` in the same folder. Your data stays. You get the beta version by changing
  the `image:` line in the Compose file to `ghcr.io/sushiflux/platinvault:beta` (chapter 12).
- **Backups:** PlatinVault backs up its database itself - on start, then every 6 hours and when the container stops - into the folder
  `data/backups` (the newest 20 and one per day for the last 60 days). A backup is only kept when something changed, it is checked first,
  and each one is a single file.
- **Restoring:** in the folder of the Compose file run `./restore-db.sh` (newest backup) or `./restore-db.sh data/backups/daily/<file>.db`.
  The script asks first and stops PlatinVault; PlatinVault saves the current state first, checks the chosen backup, then replaces the
  database, and the script starts PlatinVault again.
- **Uninstalling:** `docker compose down` removes the container, your data stays. Afterwards you can delete the folder `data` if you no
  longer need it.

## 3.3 NAS

A NAS whose container manager accepts Compose files can use the same `docker-compose.yml`. According to the vendors:

- **Synology (Container Manager):** under *Project* use *Create* to make a project, choose a name and a folder as the working directory
  and upload the `docker-compose.yml`. Put `restore-db.sh` into the same folder.
- **TrueNAS (Apps):** *Install via YAML* in the menu next to *Custom App* accepts Compose YAML. The file refers to the folder `data` next to
  it; use the absolute path of a dataset there (how TrueNAS treats relative paths has not been checked).
- **Other NAS:** check your vendor's manual for whether the container manager knows Compose files ("projects", "stacks"). Set `PUID` and
  `PGID` to the numbers of the user that owns your data folder.

PlatinVault was tested with Docker on Linux; we have not tried it on individual NAS models.

# 4 First steps

Open the **settings** with the gear at the top right (on every page). Settings that are
simply on or off have a switch: **green with a check mark** means on, **red with a cross** means off; click it (or the text
next to it) or flip it with the keyboard (space bar). Save with **Save settings**.

## 4.1 Connect Steam

- **Link with Steam:** in the **Steam** tab you sign in with Steam; Steam only tells PlatinVault your SteamID – never a
  password.
- **Steam Web API key:** lets PlatinVault read your library, playtimes and achievements. The key is free:
  1. Open `steamcommunity.com/dev/apikey` in the browser and sign in with Steam (Steam may ask for a Steam Guard code).
     If PlatinVault runs on your gaming PC, you can also open the page directly in the Steam client.
  2. Enter any name as "domain name", for example the address of your PlatinVault server.
  3. Agree to the terms and submit. Steam shows your key.
  4. Paste the key into PlatinVault and save.
- Your **Steam profile** must show game details publicly, otherwise Steam returns no library.
- **Steam folder (optional):** where PlatinVault runs directly on the gaming PC (Windows program or Linux package) it reads the
  Steam files of that computer. If it does not find Steam by itself, enter the folder that contains the subfolder `steamapps` in the
  **Steam** tab under **Steam folder (optional)** (for example `/home/<name>/.local/share/Steam`) and click **Apply**. PlatinVault checks
  the folder at once and says what is wrong. **Search automatically again** undoes the setting. Under Settings → **System** the row
  **Steam on this computer** shows which folder is used and how it was found; **Show the places searched** lists every place PlatinVault
  looked.

## 4.2 AI for the guides

In the **AI research** tab you add a key for **Google Gemini**, **Anthropic Claude** or **OpenAI ChatGPT**. After the check
you pick the model from a list.

> [!WARNING] Free key and web search: guide research uses Google Search. New Google accounts no longer get it on the free
> tier (as of October 2026) – the key's Google project needs billing for it. Settings → AI research → **"Test connection for real"**
> shows what your key may use.

A subscription (Gemini or Claude plan) does **not** cover API keys – they are billed separately. If you have no key but an AI subscription,
it still works – by hand, see **Guide with your AI subscription** in section 6.2.

## 4.3 Which AI does the work? The AI selection

At the top of the **AI research** tab you find the **AI selection**. It decides which AI takes over your actions: researching a new
guide, completing a guide, adding a chapter, Story & Lore and translating a guide. There are three ways:

| Mode | How PlatinVault works |
|---|---|
| **Default AI** | One AI writes the whole guide, every chapter in the same style. You choose which one in the list below – only AIs with a saved key can be chosen. |
| **Mixed** | The chapters of a guide are spread over every AI that has a key. That is faster, but the writing style may vary between chapters. For research PlatinVault only uses AIs with web search here (Gemini, Claude); OpenAI researches without web search and only joins in when it is the only key. |
| **Ask before every request** | Every AI action first opens a small window, **"Which AI do you want to use?"**. It lists only AIs with a key, with their state (for example "Quota used up since 10:12"); the AI you used last is preselected, and if it is exhausted right now you get a hint and a working AI is suggested. |

In the **Your AIs** list you see for every AI whether it is ready or has a problem: **Quota used up**, **Key rejected** or **Not
reachable**, each with the time the problem began. PlatinVault remembers this for a while, so that not every chapter asks the
exhausted AI again: for used-up quota as long as the AI names (otherwise one hour), for a rejected key until you replace the key, for
an outage two minutes. **Reset** makes PlatinVault forget the problem at once and try the AI again with the next request.

**Switch to the next AI automatically when there is a problem** (switch, on by default; applies to Default AI and Mixed): If the
quota of the chosen AI is used up, its key is invalid or it cannot be reached, PlatinVault carries on with the next AI that has a key.
You are **always** told – with a notice such as "Gemini quota used up – continuing with Claude.". Other errors, for example an
unreadable answer, do not cause a switch. With the switch off, the action stops at such a problem and you see the reason. In the mode
"Ask before every request" PlatinVault never switches on its own; only background tasks such as translating achievement texts or a
guide carry on quietly with the next AI in an emergency (and note it in the log).

**One AI for this one action only:** With Default AI and Mixed, the button of an AI action shows which AI will work, behind its name,
for example **Create chapter now · Claude ▾**. Clicking the part with the arrow lets you pick another AI for this action; your
setting stays unchanged. If an action fails, the notice offers **"Try again with Claude"** as soon as another AI has a key.

## 4.4 Research building blocks

Further down in the tab you find the **research building blocks**. No free text is needed: you click ready-made blocks that
PlatinVault builds into every AI research – the blueprint and every chapter, also for "Complete the guide" and "Add chapter". The
chapter layout of the app always applies on top.

- **Scope** (one choice): Compact · Detailed · Very detailed.
- **Focus** (several possible): Missables first · Collectibles with locations · Builds & equipment · Boss strategies · Trophy roadmap ·
  Farming routes · Difficulty tips · Online/co-op trophies.
- **Spoilers** (one choice): Spoiler-free · Light spoilers · Anything goes.
- **Presentation** (one choice): Bullet points · More running text · Prefer tables.

Clicking a chosen block again removes it; **Clear all** empties everything. **Nothing chosen** means the standard research – it asks
for everything a game can have (tips, builds, missables, collectibles, bestiary and more, only what fits the game). A guide is
written in its own language; the blocks work in that language. Blocks only apply to future research, existing guides do not change.

**The scope also decides how many chapters a guide gets.** Compact is about 5 to 8 chapters, **Detailed** (also when nothing is chosen) 8 to 14, Very
detailed 12 to 24. This is a ceiling, not a target: a small, simple game gets few chapters. A topic only becomes a chapter if the
research confirms that the game really has it – even when it normally belongs to this kind of game (a cosy building game without
combat gets no bestiary). The AI merges small related topics into one chapter. At the top of the guide builder (6.2) you see how many
chapters are suggested; other fitting topics are listed under **More topics**, not ticked.

# 5 The start page

## 5.1 Header

On the left the PlatinVault logo, next to it your **trophy progress**, your **Steam account** (name, avatar and – with a
Steam key – the number of your games; a click opens your profile) and the actions:

| Icon | Action |
|---|---|
| File | **Import guide** – load a guide file from another PlatinVault installation (creates the game together with its guide) |
| Trophy badge | **Trophy cabinet** – all games you completed to 100 % (5.5) |
| Aa | **Display size** – opens a small window: with − and + or directly through the steps 80 / 90 / 100 / 110 / 125 % fonts and boxes become smaller or larger while the page width stays the same; "Reset to 100 %" restores the standard size. The window stays where it is while the page changes, so you can click several times in a row. When the size is not 100 %, a small badge on the icon shows the value. Handy on 4K monitors with high Windows scaling. |
| Globe with code (DE / EN) | **Language** – opens a small list of all languages, each with its flag and its own name ("Deutsch", "English"); the one in use is highlighted and marked with a check. A click switches the whole app **at once**, without reloading, and remembers your choice. The language applies to the interface, date and number formats, the Steam achievement texts (Steam's own texts in that language) and the handbook, which opens in the chosen language. With the keyboard: arrow keys choose, Enter confirms, Esc closes. If saving fails, a message below the header names the reason and the previous language stays. |
| Sun/moon | Colour scheme: switches between light and dark (the first start follows your operating system) |
| Gear | Settings |
| ? | Help: shows the hints for the current page |
| Book | **Handbook** – this handbook right inside the app |

Hover over an icon and a short hint explains what it does.

## 5.2 Favourites and all games

At the top of the start page there is a slim **overview panel**: the title on the left, the figures as small badges on the right, and below them in one row the search, the view and the
slider for the tile size.

- **Favourites** are the games you marked with a **star** - they are the tiles on the start page.
- **All games** shows your complete Steam library (needs the Steam key).
- The figures at the top right (games, trophies, platinum games) always refer to the chosen view. For "All games" PlatinVault evaluates your library in the background; while that runs it says
  "Library is being evaluated".
- With the **Tile size** slider you choose between Large, Compact, Very compact, List and Text only.
- The **search** filters the current view; under the row "Showing x of y" says how many games you see right now, and **Reset filter** clears the search again. When you scroll down,
  a slim bar under the header takes over the search, the view and the slider.
- **Sorting:** to the right of the count line you choose the order of the tiles - in both views. You can pick **Completion** (unlocked Steam achievements in percent),
  **Alphabetical**, **Playtime**, **Last played**, **Release date**, **Size**, **Metacritic score** and **Steam reviews**; for the favourites **Own order** comes first.
  The button next to it reverses the direction; every order starts with the useful end first (highest completion, A to Z, newest, largest, best). Games that have no value
  (for example without a Metacritic score, or not installed) always stay at the end - in both directions. A line under the count line says in words how the tiles are sorted and
  how many games without a value are at the end; while PlatinVault is still loading values it says how far it is, and the list fills in by itself. Your choice is saved (per view) and is the same in the
  Windows program and in the browser. You can **drag** tiles only in **Own order** (favourites, no search filter) - in every other order dragging is off.
- **Where the values come from:** playtime, last played and completion come from Steam itself. Release date, Metacritic score, Steam reviews and the size on disk are read by
  PlatinVault, where it can, from the files of the Steam program on the gaming PC - directly in the Windows program, on an Unraid server through the bridge. Where that is missing,
  PlatinVault asks Steam's store slowly in the background for release date and reviews. **The size exists only with Steam on this PC or the bridge:**
  installed games with their real size, the others with the estimated install size (5.4). PlatinVault cannot sort by the date a game came into your library: Steam's public interfaces do not give it out, and the Steam files on the PC hold it only encrypted.

## 5.3 Bringing games onto the dashboard

Your games come **only from your Steam library** – there is no Steam Store search and no adding by hand. As long as no game is
on the start page, PlatinVault shows three steps instead of the tiles; they show the real state (tick = done) and their buttons
open the right place directly:

1. **Connect your Steam account:** "Sign in with Steam" (or in the settings, Steam tab).
2. **Enter your Steam Web API key:** you get the key from Steam for free (the link is right there), and you enter it in the
   settings, Steam tab. Without it PlatinVault cannot read your library.
3. **Bring a game onto the dashboard with the star:** "Open all games", sort by "last played", "most playtime" or name and tap
   the **star** on a game. The game appears as a tile right away – without AI costs. You create its guide afterwards on the game
   page (6.2) whenever you like.

The tour explains the same three steps; its checklist shows account and key with their real state.

- **Removing the star** takes the game off the start page; guide and ticks stay saved and come back when you star it again.
- There is no **delete button** on the tiles (any more): the star is enough to take a game off the start page. The interface cannot delete a whole game with its
  guide and ticks at the moment.

## 5.4 What a tile shows

Cover, name (up to two lines), below it the genres from Steam, your **trophies** with a progress bar and – in the sizes large and
compact – fixed lines: playtime, last played, rating (Steam reviews, Metacritic), release date, **size** and, for the favourites, the estimated time for 100 %.
Everything you can sort by (5.2) is also on the tile: the completion in the trophy bar, the other values in these lines. Every tile shows the same lines
with the same symbols, so that all tiles look alike: where a value is missing (not played yet, no data from Steam, no estimate), a grey dash "–" stands there,
and a hint on hover names the reason. Games with every trophy carry the **platinum** mark.

- **Size:** if the game is installed, the real **size on disk** stands there (written the way Steam writes it, for example "48.3 GB"). If it is not
  installed, you see the **install size** with "approx.": PlatinVault estimates it from the data Steam itself holds about the game (Windows, main
  language, without DLC). It can differ a little – for some games it is somewhat too high, never lower. If PlatinVault knows no size (no Steam on this PC,
  no bridge), the grey dash stands there.
- In the smaller sizes: **Very compact** shows title, trophies and the size on the cover; **List** and **Text only** show trophies and size in the row
  (List also last played and the bar); the other values are in the sizes Large and Compact.

Below the cover there is a slim bar of equally wide fields without divider lines: **Play**, **Favourite** and **Twitch** (only the
symbol on narrow tiles, a hint names the field). **Play** (green) starts the game (6.1) – also for the games under "All games".
That field is only there while starting can work, that is when the bridge is connected or PlatinVault runs directly on the
gaming PC; otherwise the other two share the bar. If you entered the credentials of your own Twitch application in the settings under **Twitch & IGDB** and
"Live streams" is on, **Twitch** shows a small number in its corner: how many streams are live for this game right now ("12"; from 300 on it says "300+").
At 0 there is no number, and the hint says that nobody is streaming it. The numbers arrive in the background and refresh every few minutes; without
credentials the field stays as it was.

## 5.5 Trophy cabinet

The trophy cabinet shows every game you brought to **100 %**, like a display case: behind dark smoked glass the games stand on
shelves, each with a golden plaque (name, date and time of the last trophy).

- **Opening:** the cabinet loads first (a thin golden line), then the glass doors slide aside and the lights come on.
- **New games:** if you brought a game to 100 % since your last visit, it does not simply appear: its place stays free first (a glassy card), then the game is set onto the shelf with a small flash. Several new games take their turn one after the other (faster when there are many, the whole show stays around five seconds). A golden **New** marks them until you close the cabinet again. After that they count as shown; on the same data (Windows program and browser) nothing is played twice. The first time you ever open the cabinet, games that are already at 100 % are put in without any animation. With "reduce animations" switched on in Windows the games just fade in calmly. Screen readers get one announcement, "New in the cabinet: …".
- **Doors:** the door icon at the top closes and opens the glass doors again; a click on the closed glass opens them.
- **View:** the sliders icon at the top opens a small window with two things PlatinVault remembers:
  - **Lighting:** **Spots** (one lamp above every place, with a soft cone of light) or **LED strip** (one continuous strip per
    shelf; the whole row switches on together, the shelves one after the other from top to bottom).
  - **Show empty places:** when the switch is on, the places that are still free hold grey, glassy placeholder cards with an
    empty plaque, so the cabinet looks like a display case waiting to be filled. Placeholders are decoration only (not
    clickable) and never push your games around. Off (the default), the free places stay empty.
- **Size:** the slider at the top makes the games smaller (more per shelf) or larger.
- **Many games:** if not all games fit at once, the shelves scroll sideways together - in every size. Scroll with the mouse wheel, swipe on the touchpad or touchscreen, step from game to game with the left and right arrow keys, or click the **arrow buttons** at the left and right edge (each click moves about one page). A dark fade at the edge shows where there is more; a dimmed arrow means there is nothing more in that direction. When everything fits at once, the arrows and fades are not shown.
- **Details:** a click on a game shows trophies, days to platinum, playtime and the rarest trophy – from the right in landscape,
  from the bottom in portrait. Esc closes the details first, then the cabinet.

# 6 The game page

## 6.1 Layout

At the top cover, title and the actions, below them a bar with your trophy progress and - on the right - the areas
**Live trophies**, **Statistics** and **Story & Lore** (the active area is highlighted).

The guide's **chapters** always stand in a **sidebar on the left**. It stays in place while you scroll and sorts the chapters
under small headings such as **Getting started**, **Story & world**, **Combat & tactics** or **Progression & unlocks** (a guide with only one group
has no headings). A number next to each chapter shows how much you have ticked off; the chosen chapter is highlighted in colour.
With the arrow at the top of the sidebar you collapse it to narrow numbers so the content has more room - PlatinVault remembers
that. The arrow keys (up/down, Home/End) jump through the chapters. On a phone you open the chapters with the **Chapters**
button in the trophy bar: they slide in over the page from the left and close again as soon as you have chosen a chapter (or
with Esc or a tap next to the panel).

At the very bottom of the chapter sidebar, under a fine dividing line, are the name of the app, the **version**, "Made with ♥", the copyright and the
**Licences** button (the start page shows the same details in its footer). In a collapsed sidebar (and in a very low window) this has become a small **info symbol** (an "i" in a circle): when you
move the mouse over it, tap it or reach it with the Tab key, a card with the same details opens. It stays open while the mouse pointer is on it or on the symbol, so you can
reach **Licences** comfortably, and it closes with Esc, a click next to it or when you move the mouse away.

The three areas of the bar each have their own colour: **Live trophies** gold, **Statistics** blue, **Story & Lore** purple. The area you are in is filled with its colour.

**Start a game.** The Play button starts the game in Steam without walking over to the gaming PC. On the game page the same spot reads **Game running** (a pulsing green dot, not clickable) while exactly this game is running, and goes back to **Play** afterwards. The button (and the "game is
running" hint) only shows while the bridge is connected or PlatinVault runs directly on the gaming PC, and it disappears by
itself when the connection drops. If the PlatinVault bridge on
the gaming PC is connected (chapter 8), the game starts there – even if you are using PlatinVault on a tablet or phone at the
moment; the bridge briefly shows "Launching …" next to its icon in the taskbar. Steam is started along with it if needed, and if
the game is not installed yet, Steam begins the installation. Without a bridge your browser opens the Steam address itself – that
only works if you opened PlatinVault in the browser of the machine that has Steam, and your browser then asks once whether it may
open Steam. If the start does not work, PlatinVault names the reason, for example "Steam was not found on the gaming PC" or "The
gaming PC did not pick up the launch command" (then the bridge is not running or has no connection). You can only start games
from your collection and your Steam library.

## 6.2 The guide

- **Chapters with checklists:** tick off what you have done; PlatinVault saves it immediately. The number next to each
  chapter shows how much of it is done. Ticks are your personal list – a game's progress follows its trophies.
- **Complete the guide:** the **guide builder** analyses the game and suggests suitable chapters (recommended ones are
  preselected; the top says how many there are and which scope that matches). You choose with a click, the AI researches only the
  missing chapters. The **Typical** tag on a topic means: this often belongs to this kind of game – it is only suggested if the analysis
  confirms that the game has it.
- **Guide with your AI subscription (no API key):** you copy a ready-made prompt into the chat of your subscription (for example ChatGPT, Claude or
  Gemini) and paste the answer back here – PlatinVault never accesses your subscription and sends nothing to an AI. This is allowed because you
  are the one sitting in the chat. You find it in the guide builder (**With your AI subscription (no API key)**), in the notice of a failed AI
  action and on the note of a chapter that could not be researched; without a stored key, **Complete the guide** opens it directly.
  - *Step 1, chapter list:* **Copy prompt**, paste it into the chat (ideally with web search switched on) and paste the answer into the field
    below – also with text before it or inside a json code block, or load it as a file. You see the suggested chapters as in the builder and
    tick what you want. If you do not want a blueprint prompt, choose the topics yourself.
  - *Step 2, approach:* at the top, **Approach** lets you choose between **All at once** and **One by one**. With many chapters, **All at once**
    is the more convenient way (see below).
  - *Step 2, one by one, chapter by chapter:* every chapter has a prompt of its own (**Copy prompt** or **Export as file**) and a field for the answer. The
    answer is checked **while you paste it**: does it belong to the right game (the Steam AppID is in every prompt and in the answer)? Is the
    JSON complete (not cut off)? Do sections, table rows and language fit? Errors are shown completely below it, together with a preview
    (title, sections, table rows). **Save chapter** creates the chapter exactly like an AI result; a tick shows what is saved already. For a
    chapter that could not be researched, the answer replaces the note.
  - *All at once:* PlatinVault makes **one** prompt for all chosen chapters (above it you see how many chapters there are and how big the prompt
    is). Proceed like this:
    1. Copy the prompt and paste it into the chat of your subscription, ideally with web search switched on.
    2. The chat answers chapter by chapter. Paste every answer into the field **Answers of the chat** – it is appended to what is already
       there – or drop the file your chat created (`.json`, `.md` or `.txt`, or **Choose file**). Write “continue” in the chat until its last line
       says **ALL CHAPTERS DONE**.
    3. Under **Chapters** every chapter shows its state: **Pending**, **Recognised**, **With notes**, **Error**, **Not in this game** or **Saved**.
       Click a chapter for the details. Answers that cannot be assigned to a chapter (the chat did not use the given chapter ID K01, K02 …) are
       listed separately and are not saved.
    4. **Save N chapters** creates all recognised chapters at once, exactly like AI results. If the chat did not finish or a chapter is faulty,
       copy **Copy prompt for the remaining X chapters**, paste it into the chat and carry on with it.
    5. When all chapters are saved, PlatinVault tells you that you can close the window.

    What you pasted is kept when you close the window in between (as long as you leave the browser tab open). **Clear pasted text** discards it after
    a confirmation; chapters that are already saved stay saved. If several chapters failed, **All with my AI subscription** in the notice starts
    this way for exactly those chapters.
  - **Warning for sentences in another language:** if a pasted answer contains something in a language other than the guide's, PlatinVault shows
    a warning with up to three example sentences. **Copy correction prompt** copies a prompt that makes the chat translate exactly those sentences and
    output the answer again. Paste it into the same chat and replace the answer here with the new one. You can also save the answer as it is – the
    warning does not stop you.
  - If your browser does not allow copying (for example on an http address in your home network), the prompt is shown and selected – then press
    Ctrl+C (Cmd+C on a Mac).
- **Progress of an AI action:** while the AI works (new guide, complete the guide, add a tab, Story & Lore, research chapters again), a
  notice shows **what is happening right now** ("3 of 8 chapters done – working on: Best weapons …") and how long it has been
  running. PlatinVault never promises a duration: depending on the game and the AI it can take a few minutes, and you can leave the
  page open. If a step stays the same for more than five minutes, a friendly hint says it takes longer than usual – that is not an error.
  Finished chapters are saved at once. If the server was restarted meanwhile, the notice says: "The server was restarted and the
  research was interrupted – chapters that were already finished are saved; starting again continues." ("Complete the guide" recognises
  the finished chapters and researches only the rest.)
- **A chapter that could not be researched:** such a chapter shows a calm note with the reason in one sentence (the AI's exact error
  message is folded under **Technical details**) and the button **Research this chapter again**. It researches exactly this chapter
  again – its topic is saved, you do not need to copy anything –, with the AI from your setting (the part with the arrow lets you pick
  another one; on quota problems PlatinVault switches by itself as usual) and replaces the note as soon as it works. If it fails again,
  the note names the new reason. If several chapters are affected, **Research all failed chapters again** appears at the top of the
  guide. If the game does not need the topic at all, **Remove chapter** removes it (after a confirmation), right in the note (every other
  chapter has the button too, see below).
- **Research chapter again (any chapter):** if a chapter is too thin or outdated, **Research chapter again** (top right above the content, next
  to **Remove chapter**) replaces it with a freshly researched version. The old version stays until the new one is ready. Your ticks stay where
  the entries are kept; Steam achievements and all other chapters are not affected. The AI uses your quota for this. Without a key, or with a
  subscription, use **Research again with my AI subscription**: you get a prompt for the chat, and the chapter is only replaced once you paste the
  answer and save. If the game does not have the topic at all, PlatinVault stops with a message and the chapter stays as it was. For entries that still exist, your ticks stay even if the AI renames them.
- **Remove chapter (any chapter):** a chapter you do not need – for example a topic that does not fit your game at all – is removed with
  **Remove chapter** at the top right above the chapter's content. A confirmation says what is deleted: the chapter, its translation and the
  check marks you set in its lists. Your Steam achievements and all other chapters are not affected; removing cannot be undone, but with
  **Complete the guide** the AI can research missing topics again later. Afterwards the next chapter opens. The guide's Steam achievement list
  (the live trophies read their tips from it) and **Story & Lore** (an area of its own; it is researched anew there) cannot be removed.
- **Walkthrough (always on top):** if a game has a reliable, complete source, the guide starts with the **walkthrough** chapter: the course of the
  game in the order of the game, built like a printed strategy guide. Next to the walkthrough there is a **Contents** you use to jump straight to a
  part or area (it can be shown and hidden). Every **part** (story chapter or mission) starts with a short orientation sentence and the box **At a
  glance**: recommended level, what there is to find, which Steam achievements are possible in this part, key items, missables and whether the part
  ends at a point of no return. Below follow the **areas** in the order you pass through them: the route in numbered steps (each step one or two
  full sentences), the area's **finds** as a tickable table (with a **Missable** column), side quests and bosses. Coloured hints with their own
  symbol appear exactly where something becomes important: **Important – take it along** (with its location), **Missable from here**, **Optional
  content available here**, **Achievement available here**, **Tip** and – shortly before the point of no return – **Point of no return** with the
  list **Do this first**. Puzzles appear as a block of their own (see below). It is written in its own words; the sources are listed at the end of
  the chapter under **Sources** (links open in the browser) and in the opening box. If there is no reliable source PlatinVault does not create the chapter – nothing is invented. The walkthrough does not count towards
  the chapter number of your scope; it is researched in several parts (the message names the part being worked on). An existing guide gets it
  through **Complete the guide** and it still sits on top afterwards.
- **Puzzles and “Show solution”:** every puzzle, code and sequence (“in the right order”) is a block of its own. It says openly **what** to do,
  **where** it is (area, landmark, route), **what is needed first** and whether it is **missable**. The solution stays hidden until you want it:
  **Show hint** reveals one hint after the other (vague to concrete; the same button shows the next one and finally **Hide hints**),
  **Show solution** the complete instructions – every step in the right order, with the source – and **Hide solution** hides it again. So you can try
  it yourself first. PlatinVault remembers in this browser what you have revealed. Everything works with the keyboard (Tab, Enter, Space). Some
  puzzles have a **Done** box. If the research could not verify the solution it says **Solution not documented** – PlatinVault does not invent
  codes or orders. New chapters contain puzzles this way; older chapters keep their text. To get the structured solutions for an existing game,
  pick the topic **Puzzles & hidden mechanisms** in **Complete the guide** (suggested only if the game has such puzzles) or add a chapter about
  a specific puzzle with **Add chapter**.
- **Side quests:** in the walkthrough, side quests appear only compactly in a hint **Side quests available here** (name, from when, missable until
  when, short reward). The complete solution is in the chapter of its own; **All in the chapter “…”** and the quest name lead there.
- **Bosses:** every boss fight has a card of its own, **Enemy**, with location, recommended level, **hit points** (only with a source – otherwise it
  says “not documented”, PlatinVault does not invent numbers), **weaknesses**, **resistances**, **immunities**, **dangerous attacks**, **phases**,
  recommended party and equipment, **tactics** and **rewards**. If the fight is missable or unlocks an achievement, that is shown there too.
- **Linked ticks:** if an entry appears in two chapters – for example an item the walkthrough mentions and that has a row of its own in a
  collection list – both share **the same tick**: tick the entry in one place and it is done in the other as well. The entry in the walkthrough
  carries the jump link **→ chapter name** to the original row, and the original row shows where else it appears (**Walkthrough · Part · Area**).
  Entries are linked only when the name matches exactly one entry of another chapter; when in doubt they stay separate – nothing is guessed. If
  you remove the chapter with the original row, the other entry keeps its tick and becomes a normal entry again.
- **Sources of the research:** the AI first looks for a wiki made for this very game and then adds other sources; numbers are checked against a second source where possible.
- **Language and titles:** a guide is written entirely in its own language – every sentence in its own words, not taken over from foreign-language
  sources. Names appear as they are called in that language's version of the game; the English original name appears at most once per chapter,
  in brackets at the first occurrence. Chapter titles are in the language of the guide only, without a translation in brackets.
- **Add a tab:** let the AI research a single chapter on a topic – pick a topic or describe it yourself. Which AI does it is shown on the button (see 4.3).
- **Export / PDF:** share the guide as a file (without your progress) or print it.
- **Reset progress:** removes all ticks of this game.
- **Guide in your language:** A guide is written in the language it was researched in. If you open it in the other language of the app,
  the AI from your AI settings translates it automatically – until then you see the original and a progress message at the bottom,
  then the content swaps by itself. The original is kept and your ticks count in both languages. If it does not work (no AI key,
  quota used up), the message names the reason and offers "AI settings" and "Try again"; new chapters are always written in the
  guide's own language and translated the next time you open it.
- New games with little information get the note "not documented yet" instead of made-up content.

## 6.3 Steam trophies (live)

All achievements of the game – in the texts Steam provides.

**Filter and view** sit in one box above the list, in two rows:

- **What is shown:** the status on the left (**All, Still needed, Unlocked, Secret**, each with its count), the tier on the
  right (**All, Gold, Silver, Bronze**).
- **How it is shown:** the search field (name, text or – while notes are switched on – note), the **sort order**, the **view** slider and, at the right end, an
  eye button for the **secret achievements** (revealed or covered). On narrow windows, such as a phone, the groups move neatly
  below each other; nothing in the box changes size or jumps, not even when the list becomes empty.

**Four views** (the "View" slider): **List** (one row per achievement), **Tiles** (icon only, the details are in the hint and,
on a click, in the detail window), **Compact** (a calm card with an icon strip on the left, name, rarity, description and unlock
date) and **Large** (a big card with a big icon). In all four views an unlocked achievement looks the same: a **green check**
in the corner of its icon; locked achievements have a grey icon.

- **Hidden trophies** are revealed and show what to do.
- **"How to get it":** tips from the guide right at the trophy.
- **Counter trophies** (for example "use 400 times …") show a bar like "244 / 10,000" when the game has a counter on Steam.
- **"x % of players"** is the rarity: that many Steam players have this trophy – not your progress.
- Your own **notes** per trophy: a small note button on the card (Compact, Large) and in the detail window opens the input
  field; the note then shows on the card (in List and Tiles in the hint) and is found by the search. If you do not need notes,
  switch them off under **Settings → Live & sound → "Notes on achievements"**: note buttons,
  note texts and the note search then disappear everywhere. Notes you have already written are **not deleted** – they are back
  as soon as you switch the option on again.

**How PlatinVault rates rarity:** Everywhere in the app – in the statistics and in the name a trophy popup carries – the same
scale with five steps applies:

- **Ultra rare:** under 5 % of players
- **Very rare:** 5 % to under 10 %
- **Rare:** 10 % to under 20 %
- **Uncommon:** 20 % to under 50 %
- **Common:** 50 % and above

(The app shows these names in the language you chose.)

The **colour** of a trophy (gold, silver, bronze) follows the same share, but with its own limits: **gold** is held by at
most 10 % of players, **silver** by at most 50 %, **bronze** by more than 50 %. **Platinum** is what you get when you own
every trophy of a game. If Steam provides no share for a trophy, PlatinVault names no rarity step for it and leaves it out of
the statistics.

## 6.4 Statistics

The **Statistics** tab shows your game in numbers and charts. Only what PlatinVault really knows appears: if something is
missing (for example because the game has no achievements or nothing has been recorded yet), that card is simply absent and
the others move up. Every chart has a **table icon** ("Show as table") that switches to the same numbers as a table – handy
for reading them off and for screen readers. Hover over a chart (or select it with the keyboard) to see the exact values.
**Refresh** at the bottom of the page reloads everything.

### The figures at the top

- **Trophies:** how many you have out of how many, with a progress bar.
- **How exclusive is platinum?** Shows the highest share of all players that can possibly own platinum: whoever has everything
  also has the rarest trophy, and only that many players worldwide have it. The real share can be lower – the figure is an
  **upper limit**.
- **Still to go for platinum:** a **rough estimate** of how many hours you still need. It extrapolates your pace so far
  (trophies per hour played) to the open trophies and only appears once you have at least three trophies and one hour of
  playtime. The rare trophies at the end often take longer – the real value tends to be higher. Once you have platinum, this
  tile shows after how much playtime and in how many days you got it instead.
- **Playtime:** your total playtime, the time of the last two weeks and when you last played.

### Progress

- **Trophies per day:** One bar per day shows how many trophies you unlocked on that day; the number stands above every bar.
  Only days with at least one trophy are drawn – days without an unlock do not appear, so the date labels jump from one unlock
  day to the next. With few days the bars fill the card (a single day is a single bar that is not too wide). If there are more
  days than fit comfortably side by side, the chart shows a window of the newest days; use the arrows "Earlier days" and
  "Later days" (or the arrow keys, on a phone by swiping) to page through the rest. Hover over a bar (or tap it) to see the date
  and the trophies of that day with their time. The chart appears from two trophies with a known time; trophies Steam has no
  time for are not part of it.
- **Trophies by tier:** platinum, gold, silver and bronze as four bars – how many of each you already have.

### Rarity

- **Rarity of your trophies:** How many trophies fall into which of the five steps (see 6.3) and how many of them you already
  have. Above it you can read how many of the very rare ones (under 10 %) you have. Trophies without a Steam share are not
  counted; if a game has no shares at all, the whole section is missing.
- **The easiest open trophies:** Your open trophies, most common first – the quickest wins. Ten at first, the rest with a click.

### Time

- **When you are most successful:** A grid of weekday and hour shows when you unlocked the most trophies (in your local time).
  It goes from green (few) to gold (the most) and appears from three trophies with a known time.
- **Playtime calendar:** One square per day. A **gold dot** marks every day on which you unlocked a trophy – even long before
  recording started. The square's **colour** shows how long you played once PlatinVault has recorded it (the stronger, the
  longer). Above it Steam's totals (overall and last two weeks), below it this week's and this month's playtime and your
  longest day – those only once minutes have been recorded.
- **Playtime compared** *(only with IGDB, see below)*: The usual playtime for "story only", "with extras" and "100 %" as bars –
  and your own time as a line across them. It also says what percentage of the usual time you are at. The values are reports
  from other players at IGDB, so they are guidelines.

### Community

- **Player count over time:** How many people play at the same time. You choose **7 days, 30 days, 90 days or 1 year**. Up to
  30 days you see the single measurements (every 30 minutes), beyond that the daily average with the range from the day's
  lowest to its highest value. Gaps in the line mean PlatinVault was not running at that time.
- **Reviews on Steam:** A split bar shows how many reviews are positive and how many negative (blue and orange instead of
  green and red, so they stay distinguishable with colour blindness).
- **Ratings compared** *(only with IGDB)*: IGDB critics, IGDB players, Steam reviews and Metacritic on one common scale from
  0 to 100, with the number of votes. It appears as soon as at least two sources are available.

### Series *(only with IGDB)*

If the game belongs to a series, you see all its parts in release order, with cover, year, whether you own them in your Steam
library or in PlatinVault, and your trophy progress. The game you are in is marked; a click on a part you have in PlatinVault
opens its game page. The card appears from two parts.

### About the game

Release date, developer, publisher, genres, number of achievements, Metacritic score, how many are playing right now, the
latest update and the newest news and patches from Steam.

### Good to know

- **The history fills up over time.** Steam only tells what is true *now*. So PlatinVault notes the player count every 30
  minutes and your playtime every day itself, **as long as it is running** – from the day it does so for the first time. There
  are no values for the time before. The player count appears from the first measurement (a dot first, a line from the
  second). The playtime per day is the difference of the total playtime between two recorded days, so it appears from the
  second day. A small note "Recorded since …" shows when measuring started.
- **IGDB is optional.** The three cards marked "only with IGDB" appear only if you entered the credentials of your own Twitch
  application (client ID and client secret) in the settings under **Twitch & IGDB**. IGDB is free for private, non-commercial
  use. Without these credentials exactly those cards are missing – everything else stays unchanged.
- **Estimates are labelled:** "Still to go for platinum" and "How exclusive is platinum?" are an estimate and an upper limit
  and explain themselves through the info icon on the card.

## 6.5 Story & lore

On request the AI researches a separate tab with story, characters, world and background. It **contains spoilers** and
stays covered until you open it on purpose; PlatinVault asks per game.

# 7 Trophy popups and sounds

In the **Live & Sound** tab of the settings:

- Turn popups on or off, choose their **position** on the screen: nine spots (top left to bottom right) or **Full screen**.
  Clicking a position shows a preview right away. With **Full screen** the whole window belongs to the new trophy for about five
  seconds: a large trophy in the colour of its tier, name, description, rarity and game – in landscape and in portrait. A click
  anywhere or the Esc key ends the popup early; the next trophy then follows.
- **Show trophy popup:** **Only in the app**, **Only in the game** or **Both** (default). "In the game" means: as an overlay over the
  game – only the Windows program has it (see 11.3). In the browser and on Unraid the two game choices are greyed out; there the popup
  always appears in the app. The sound plays **exactly once** per trophy, also with "Both".
- **Sound theme:** crystal, orchestra or music box – one click plays a preview. All sounds are made from real instrument
  recordings.
- Set the volume, **preview the sound** and **test the platinum moment**.
- Switch **notes on achievements** on or off ("Notes on achievements", on by default; see 6.3).
- Switch **Switch to the game when it starts** on or off (on by default; see below).

When you start a game from your collection, every open PlatinVault window jumps to that game's **live trophies**, so you do
not have to navigate there first. This works as soon as the bridge on the gaming PC is connected, or when PlatinVault itself
runs on the gaming PC. It only switches on a real start – not when you reload the page while the game is already running, and
not when the game ends. If a dialog is open, your cursor is in an input field or an AI task is running, you stay where you
are: a notice reports the start and offers **Switch to the game**. If the running game is one you have not added yet, a
notice only names it – "no guide yet".

Popups appear for every game that exists in PlatinVault. A popup names the share of players who have the trophy; its colour
(bronze, silver, gold) and its name follow the rarity (see 6.3).

# 8 PlatinVault Bridge (server on another machine)

If PlatinVault runs on a server, NAS or Unraid, the bridge reports your trophies from the gaming PC in real time. The bridge exists for
**Windows** and for **Linux** (desktop, Steam Deck, Bazzite); in the **PlatinVault Bridge** tab you pick your operating system under
**Gaming PC runs**. Steps 1 to 6 are for Windows; the Linux bridge is in section 8.1.

1. In the settings, tab **PlatinVault Bridge**, click **download**. The ZIP contains the program and is already connected
   to your server. Every new download creates a new key; the old one stops working. The ZIP also contains this handbook as
   a PDF (German and English); later you can open it any time by right-clicking the bridge icon and choosing **Open handbook**.
2. Unpack the ZIP on the gaming PC and start `PlatinVault.Bridge.exe`. The setup window checks the connection and installs
   the bridge (starts with Windows, start menu entry).
3. Afterwards the bridge sits as an icon at the bottom right of the taskbar. Right-click it for status and menu.
4. The bridge fetches new versions from the PlatinVault server by itself.
5. The bridge speaks the language of PlatinVault (German or English): the ZIP already carries it, and if you switch the
   language in PlatinVault the bridge follows – its menu, status window and handbook change at once, the setup windows the
   next time they open. Before it has connected for the first time it uses the Windows display language.
6. **Uninstall:** via Windows "Apps" or the bridge menu.

The bridge also starts games on request: when you tap Play in PlatinVault, the bridge on the gaming PC triggers the start in
Steam (see 6.1). To do that it asks the server for jobs every few seconds; a job it does not pick up expires after 30 seconds.

If PlatinVault runs directly on the gaming PC, you do not need the bridge.

## 8.1 The bridge on Linux (desktop, Steam Deck, Bazzite)

The Linux bridge is a small background service without a window that runs under your user. It needs neither root nor .NET and keeps
everything in your home folder (so it also works on SteamOS and Bazzite with their read-only system partition). It is
**experimental** – see the notes at the end of this section.

1. In the settings, **PlatinVault Bridge** tab, choose **Linux / Steam Deck** under **Gaming PC runs** and click **Download PlatinVault
   Bridge**. You get a `.tar.gz` that is already connected to your server; every new download creates a new key and the old one stops
   working. (The same file, `PlatinVault-Bridge-linux-x64.tar.gz`, is also among the releases on GitHub – there without your credentials.)
2. On the Linux machine open a terminal in the download folder and unpack the archive:

   ```
   tar -xzf PlatinVault-Bridge-linux-x64.tar.gz
   cd PlatinVault-Bridge
   ```

3. Install and start the bridge: `./install.sh`. It copies the program to `~/.local/share/platinvault-bridge/`, saves server and key
   (readable by you only) and sets up the systemd user service `platinvault-bridge`.
4. Check that it works:

   ```
   ~/.local/share/platinvault-bridge/platinvault-bridge status
   journalctl --user -u platinvault-bridge -f
   ```

   The **PlatinVault Bridge** tab then says **Connected**. The log is also in `~/.local/state/platinvault-bridge/bridge.log`.
5. **Without a ready package** (for example when you created the key by hand) set it up with
   `platinvault-bridge setup --server http://<server>:<port> --token <key>`; `platinvault-bridge run` starts it in the foreground to
   try it out. `platinvault-bridge --version` shows the version.
6. **Steam in an unusual place:** `platinvault-bridge setup --steam-dir /path/to/Steam` (the folder with `steamapps`) or the environment
   variable `PLATINVAULT_STEAM_DIR` or `STEAM_DIR`. The configuration is in `~/.config/platinvault-bridge/bridge.json`.
7. The bridge fetches new versions from the PlatinVault server by itself and speaks the language of PlatinVault (German or English).
8. **Uninstall:** `uninstall.sh` in the program folder `~/.local/share/platinvault-bridge/`.

As on Windows, the bridge also starts games on request (the **Play** button).

**Not verified on real devices yet:** reading achievements on Linux follows the Windows file format – whether Steam writes the stats file
at the moment of an unlock and how the signed-in Steam account is found is open. The bridge detects the running game through Steam's
process list. It is also open whether the service keeps running in the Steam Deck's Game Mode (it does in Desktop Mode).
**Docker on the same Linux PC** cannot see the running game – use the Linux package (chapter 13) there, or Docker together with the Linux
bridge. If `status` does not help, please tell us what it and the log show.

# 9 Data and privacy

- **Everything stays with you:** games, guides, ticks and settings live in your own database.
- **API keys** (Steam, AI) are stored encrypted in the appdata folder – not in the database – and only shown shortened.
  On Windows they are also bound to your Windows account. Do not share the appdata folder openly on your network: whoever
  has the whole folder also has the keys.
- **No accounts, no telemetry:** PlatinVault sends no usage data to the developers. On its own it only makes these connections:
  - **Update check at GitHub** (`api.github.com`): once a day by default; GitHub sees your IP address. Switch off: Settings →
    **System** → **Updates** → **Look for updates** set to **Never (by hand only)**. An update is only downloaded after you confirm.
  - **Microsoft WebView2:** if this runtime is missing on the Windows PC, the setup downloads it from Microsoft. WebView2 is Microsoft software with its own rules.
  - **Google Translate:** with a German interface, no AI key, and no German text from Steam, the English achievement texts are sent
    to Google to be translated. Switch off: Settings → **Live & Sound** → **Automatically translate English Steam achievements into German**.
- PlatinVault requests **Steam data** only for your own account and only when you ask for it – with the Web API key and account you
  enter yourself (`api.steampowered.com`, `store.steampowered.com`, `steamcommunity.com`, Steam's image servers).
- **AI providers** receive the game name and public game information for the research – no personal data. Only the provider whose
  key you entered is contacted, and only on an AI action (Google, Anthropic, OpenAI).
- **Twitch and IGDB** are only asked with your own credentials. The **bridge** (Windows and Linux) only talks to the PlatinVault server you entered.
- The **Linux package** makes no other connections than any PlatinVault server.
- PlatinVault deliberately has **no login** and is meant for one person. Run it in your home network, not openly on the
  internet.
- **Licence and source code:** PlatinVault is free software under the **GNU Affero General Public License, version 3**
  (AGPL-3.0-only). You may use, study, change and share it free of charge; whoever passes it on or offers a changed version
  to others over a network must offer the source code under the same licence. There is nothing to accept to use it. The
  source code is at `github.com/sushiflux/PlatinVault`.
- **Licences:** You can read PlatinVault's licence (with the **Source code** button) and the licence texts of the bundled open-source components in the
  app: on the start page click **Licences** at the bottom; on a game page at the bottom of the chapter sidebar (with a collapsed sidebar open the **info symbol** first); or open the settings, tab **System**, and click **Open licences**. The same texts are in the bridge ZIP, in the bridge's program folder and in the Windows program's folder (`docs`) as `LICENSE` and
  `THIRD-PARTY-NOTICES.md`; any text editor opens them.

# 10 Troubleshooting

| Problem | What helps |
|---|---|
| Something does not load or shows errors | Settings → **System**: shows whether backend, database, data folder, Steam, bridge and keys are fine. |
| "PlatinVault server not reachable – the browser got no answer" | The server does not answer at all. Is the program or container running (on Unraid: in the Docker list), and is the network connection to the server working? Then run the action again. |
| The library stays empty | Check the Steam Web API key; set game details to public in your Steam profile. |
| No live popups | Is the game in PlatinVault? If PlatinVault runs on a server: is the bridge connected (taskbar icon, or `platinvault-bridge status` on Linux, system check)? |
| On Linux: "PlatinVault found no Steam on this computer" | In the **Steam** tab enter the folder with `steamapps` under **Steam folder (optional)** (section 4.1). With Flatpak Steam it is usually under `~/.var/app/com.valvesoftware.Steam/.local/share/Steam`. Under Settings → **System**, **Show the places searched** shows where PlatinVault looked. If PlatinVault runs in Docker, it cannot see the computer's Steam folder – use the Linux package or the Linux bridge. |
| On Linux no live popups arrive | Detection on Linux is still **experimental** and not verified on every Steam installation (native, Flatpak, Snap, Steam Deck). Check in the system check whether Steam is found, and with the Linux bridge run `platinvault-bridge status`. If the service does not keep running in the Steam Deck's Game Mode, it stays active in Desktop Mode. Please tell us what you see – it helps us improve detection. |
| On Linux: "port 5233 is taken" during installation | Another program uses the port. Install with `./install.sh --port 5300` (or another free number). |
| The popup shows, but without sound | If the notice **"Sound blocked by the browser"** is shown, the browser held the sound back: browsers only play sounds after a click or key press on the page, for example after a reload. One click allows sound again, **Play the missed sound** plays it afterwards. Without that notice: raise the volume under Settings → **Live & sound** (quiet sounds get lost next to loud game audio) and check that the browser tab is not muted. |
| The AI reports a limit | In Settings → AI research the **Your AIs** list shows which AI had a problem and since when. With the automatic switch on, PlatinVault carries on with the next AI and tells you; otherwise you pick another AI yourself ("Try again with …"). For Gemini, "Test connection for real" also shows the reason: daily limit (try again later), model overloaded (wait a moment) or no web search on the free tier (a Google project with billing is needed). |
| A warning with foreign-language sentences appears in the AI subscription window | The chat's answer contains sentences in a language other than the guide's. **Copy correction prompt**, paste it into the same chat and insert the new answer (section 6.2). |
| With "All at once" a chapter stays **Pending** or appears under **Answers without a chapter** | The chat did not use the chapter ID (K01, K02 …) or did not finish. Write "continue" in the chat until it says **ALL CHAPTERS DONE**, or copy **Copy prompt for the remaining X chapters** and carry on with it. |
| A chapter is too thin or outdated | **Research chapter again** or **Research again with my AI subscription** above the chapter content (section 6.2). |
| A chapter says "could not be researched" | Click **Research this chapter again** (details in 6.2). The reason is in the note; for a quota or key problem first look in Settings → **AI research** or pick another AI with the arrow on the button. |
| "The server was restarted and the research was interrupted" | The server was briefly gone during the research (update, restart). Finished chapters are saved; start the same action again, it carries on from there. |
| Achievements in English | The game has no texts in your language on Steam; with an AI key PlatinVault translates them automatically. |
| No counter on a trophy | The game has no counter on Steam. |
| PlatinVault does not start: "This database has already been used by PlatinVault …" | The database comes from a newer version (for example a beta). So that nothing breaks, the older version does not start and changes nothing. Install the named version (or newer) again or wait for the next release (chapter 12). |

# 11 PlatinVault for Windows

On your gaming PC you can also install PlatinVault as a **Windows program**. It is the same app as in the browser – the same pages,
guides and settings – just in its own program window. Everything runs on your PC: the PlatinVault server, its database and the live
trophies. You don't need the bridge for this.

What the program adds:

- **Trophy popup over your game** (overlay), at the place you chose for popups under Settings → **Live & sound** – also full screen.
- **Sound without a click:** the trophy sound plays at once, even if you haven't clicked anything before.
- **Windows notifications** for every unlocked trophy (they stay in Windows' notification centre).
- **Icon next to the clock** and **start with Windows**, so trophies are noticed without an open window.
- **Ctrl+Alt+G** opens the guide for the running game over the game.
- **Start games without a question:** "Play" starts the game in Steam directly.

## 11.1 Installing

1. Download `PlatinVault-Setup-<version>.exe` from PlatinVault's releases on GitHub and start it with a double-click – **not** with
   "Run as administrator". PlatinVault is installed just for your Windows account and needs no administrator rights.
2. **Windows SmartScreen:** the setup file is not digitally signed yet. Windows therefore shows "Windows protected your PC" the first
   time. Click **More info** and then **Run anyway**. If your antivirus flags the file or the program, see section 11.9.
3. The setup guides you through six steps:

   | Step | What happens |
   |---|---|
   | Language | Deutsch or English – applies to the setup at once and is the app's preselection. |
   | Licence | The licence text (GNU AGPL v3) to read and the link to the source code – nothing to tick. |
   | Database | Just for information: there is nothing to set up. PlatinVault keeps everything in one file on your PC. You install and type nothing. |
   | Location & autostart | Program folder (suggested: `…\AppData\Local\Programs\PlatinVault`), **Start with Windows** (recommended), Start menu and desktop shortcut. Your data is kept apart from it. |
   | Installing | Copies the program, checks the window runtime (Microsoft Edge WebView2) and creates the database. If WebView2 is missing (rare, only older Windows 10 PCs), the setup downloads it from Microsoft – that needs an internet connection. |
   | Done | **Start PlatinVault now** is preselected. |

4. When you open PlatinVault the first time, choose the app's language and connect your Steam account (chapter 4).

The program runs on Windows 10 or 11 (64-bit).

**The program window.** On its first start PlatinVault opens **maximised**; after that it remembers how you left it (maximised or in
your size and position). If the screen it was on is no longer connected, it opens on the main screen. The bar at the top is
PlatinVault's own title bar in the app's style (light or dark like the app): trophy and name on the left, **Minimise**, **Maximise** or
**Restore Down** and **Close** (red when you point at it) on the right. It behaves like every Windows window: drag the bar to move it,
double-click it to maximise or restore, drag it to an edge or corner to snap it, on Windows 11 resting on the maximise button shows the
snap layouts, every edge and corner resizes, a right-click on the bar or **Alt+Space** opens the window menu, and **Windows key+arrow
keys** work as usual. On screens with different scaling (e.g. 4K at 250 % next to a 100 % screen) everything stays sharp and the same size.


## 11.2 The icon next to the clock

Closing the window does **not** quit PlatinVault – it keeps running next to the clock so live trophies and the overlay keep working.
A click on the icon opens the window, a right-click shows the menu:

| Entry | Meaning |
|---|---|
| Open PlatinVault | Brings the window to the front. |
| Guide for the running game | Opens the page of the game that is running, over the game (like Ctrl+Alt+G). |
| Trophy overlay over the game | Switches the overlay on or off (tick = on). The same as "Show trophy popup" in the app: on = "Both", off = "Only in the app". |
| Test the overlay | Shows a test trophy in the overlay (without sound) so you can see where the popup appears. |
| Windows notifications | Switches the notifications on or off. |
| Start with Windows | Autostart at sign-in on or off. |
| Open handbook | This handbook as a PDF. |
| Open backup folder / Open logs | For emergencies and for support questions. |
| Install update … | Appears when a new version is available. |
| Quit | Backs up the database and closes PlatinVault. |

If you start PlatinVault a second time, the existing window simply comes to the front.

## 11.3 Overlay over the game

When a trophy unlocks while you play, **exactly the same popup as in the app** appears over the game – the same card with the same
animation, the full-screen popup if you chose it, and the platinum moment. It appears on the monitor the game runs on, at the place
you chose under Settings → **Live & sound**. The overlay never takes the focus from the game, lets every click through (so a click
does not close it – it disappears by itself after a few seconds) and never shows up in Alt+Tab or the taskbar.

Where the popup appears is decided by the setting **Show trophy popup** (Settings → Live & sound):

| Choice | What happens |
|---|---|
| Both (default) | Overlay over the game; while you look at the PlatinVault window, only the app shows its popup – never twice on one screen. |
| Only in the game | Always the overlay, also over the PlatinVault window; the app itself shows no popup. |
| Only in the app | No overlay; the popup appears only in the PlatinVault window. |

The sound is always played by the PlatinVault window, once per trophy – also while it runs in the background or next to the clock.
The tick **Trophy overlay over the game** in the icon's menu is the same setting (on = "Both", off = "Only in the app").

> [!NOTE]
> The overlay works when the game runs in **windowed** or **borderless full screen** mode. In **exclusive full screen** the screen
> belongs to the game alone – no window is visible on top of it, the overlay included. The Windows notification and the sound still come.

## 11.4 Ctrl+Alt+G: the guide over your game

**Ctrl+Alt+G** opens PlatinVault over the game with the page of the game that is running (if it was open already, your chapter stays open). Press
Ctrl+Alt+G again to get back to the game. If another program already uses these keys, PlatinVault takes **Ctrl+Alt+Shift+G**; if that
is taken too, there is no shortcut – then open the guide from the icon next to the clock.

## 11.5 Your data and backups

- Program and data are kept apart: the program in the program folder, your data under `…\AppData\Local\PlatinVault` (database,
  backups, encrypted API keys, logs). An update only replaces the program.
- **Why is the program in the user folder?** PlatinVault is installed per user: program folder
  `%LOCALAPPDATA%\Programs\PlatinVault`, data `%LOCALAPPDATA%\PlatinVault`. So the installation needs no administrator rights, and
  updates run without an admin confirmation. Windows hides the `AppData` folder by default – so there are shortcuts: in the menu of
  the icon next to the clock **Open program folder**, **Open data folder** and **Open backup folder**, and in Settings → **System** →
  **Folders** an **Open** button for each (plus **Copy path**). Explorer then shows the folder.
- The database is a single file (`database\platinvault.db`). No separate database service runs, and nothing can be reached from outside.
- **Backups** are made automatically: on start, every 6 hours and on quitting (so also before every update) – but only when something
  changed. The 20 newest are in `backups\recent`, plus one per day for 60 days in `backups\daily`.
- **Restoring a backup:** quit PlatinVault from the icon, then run `"<program folder>\PlatinVault.exe" --restore "<path to the backup>.db"`
  in a command prompt. PlatinVault checks the chosen backup, first backs up the current state (it is then the newest backup in
  `backups\recent`) and then replaces the database. Afterwards start PlatinVault normally.

## 11.6 Updates

PlatinVault checks by itself whether a new version exists – as often as set under **Look for updates** (default: daily) –, releases only, or betas too if you switched on **Receive beta versions**
under Settings → **System** → **Updates** (chapter 12). If there is one, the icon's menu shows **Install update …** and a notification
appears. To look right away, use **Check for updates** in the icon's menu or **Check for updates now** in the settings; there you can
also install a version that was found with **Install update**. After you confirm, PlatinVault downloads the setup, checks its checksum,
quits (with a backup) and the setup updates the program. Your data stays unchanged. Once the Windows files are digitally signed,
PlatinVault also checks the signature of the downloaded setup and starts it only if it is valid. If you have an older, unsigned
PlatinVault installed, this check applies only after the update to a signed version.

**Reading the release notes:** in the settings under **System** → **Updates** (and in the header's update icon), **View release
notes** opens a window of its own, **What’s new**, with the changes of that version – without opening the GitHub page. **View on
GitHub** opens the release page in the browser; **Esc** closes the window.

## 11.7 Uninstalling

Windows Settings → **Apps** → **PlatinVault** → **Uninstall**. A window asks **"Keep your data?"**:

- **Keep data (recommended):** database, backups and API keys stay. If you install PlatinVault again later, it carries on right there.
- **Delete everything:** permanently deletes the database, all backups, stored API keys and logs.

Then PlatinVault closes, removes autostart, shortcuts and the "Apps" entry and deletes the program folder.

## 11.8 Problems on Windows

| Problem | What helps |
|---|---|
| "Windows protected your PC" | **More info** → **Run anyway** (the file is not signed yet). |
| The setup says it "is running with administrator rights" | Start the file with a double-click, not with "Run as administrator". |
| The window stays empty or says WebView2 is missing | Install the "Microsoft Edge WebView2 Runtime" from Microsoft's website (Evergreen) or run the setup again. |
| PlatinVault does not start and shows a database error | The complete reason is in the window. **Open logs** shows the logs (for support). |
| "The database file … is damaged" | PlatinVault does not change the file. Restore the latest backup (section 11.5); the damaged file is kept as a safety copy. |
| No overlay in the game | Does the game run in exclusive full screen? Switch to borderless full screen. Is **Show trophy popup** set to "Only in the app" (no tick at **Trophy overlay** in the icon's menu)? With "Both" the overlay stays away while the PlatinVault window is in front. **Test the overlay** shows a test card; if it says "not available", the log names the reason. |
| Ctrl+Alt+G does nothing | Another program uses the keys – PlatinVault then takes Ctrl+Alt+Shift+G or none; the guide always opens from the icon next to the clock. |
| No live trophies after restarting the PC | Is **Start with Windows** ticked in the icon's menu? |
| Antivirus flags PlatinVault | See section 11.9. |

## 11.9 Antivirus flags PlatinVault

**Why can this happen?** PlatinVault's Windows files are not digitally signed yet. At the same time, on an update the program
downloads an installer from the internet, starts it and quits itself – a sequence that the behaviour monitoring of some antivirus
programs rates as suspicious for an unknown, unsigned program. It is a false alarm; the source code is
publicly available. Some programs move files to quarantine in this case.

**What you can do:**

1. Add **exceptions** in your antivirus for the program folder `%LOCALAPPDATA%\Programs\PlatinVault` and the data folder
   `%LOCALAPPDATA%\PlatinVault`.
2. If files were moved to quarantine: restore the **key files** (your encrypted API keys in the data folder, subfolders
   `server\secrets` and `server\dp-keys`). Do **not** restore the database helper files (`platinvault.db-wal` and
   `platinvault.db-shm`) – they belong to an earlier state and can confuse the database. The database itself (`platinvault.db`)
   and the backups are fine if needed; if necessary, restore a backup (section 11.5).
3. **Run the setup again** (same or newer version). It detects a damaged program folder, for example one where `PlatinVault.exe` is
   missing, repairs the installation and leaves your data untouched.
4. If the antivirus keeps blocking file names after you have allowed them: remove the PlatinVault entries from the quarantine or the
   block history, or restart Windows. Usually the entry is forgotten then. You can also report the file to your antivirus vendor as a false alarm.

# 12 Updates and beta versions

PlatinVault comes in two kinds of versions:

- **Releases** – the normal, finished versions. This is the default and the right choice for most people.
- **Betas** – early versions with the newest features. They are fully built and tested but may still have bugs. A beta shows a **β**
  after the version number, for example `v2026.11.12 (13.557 β)`.

## 12.1 The switch "Receive beta versions"

Under Settings → **System** → **Updates** – on every platform – there is the switch **Receive beta versions**. It starts **off**.

- **Off:** you only get releases.
- **On:** you get betas and releases – always the newest of both.

There is **never a step back**: if you switch it off while a beta is running, that beta stays until a release comes out that is at
least as new. That release is then offered to you automatically. If you switch it on, you get the newest beta as soon as it is newer
than your version.

Below it you see your installed version and, with **Check for updates now**, the result: "You are up to date", "New beta … available" or
"New release … available" – with a link to the release notes. PlatinVault asks GitHub for the published versions (without signing in;
only a few times a day, a check by hand at most once a minute). If that does not work, the complete reason is shown there, for example no
internet connection or a used-up request quota at GitHub.

If a newer version exists for your installation, a small **update icon** (arrow up with a green dot) appears in the header of every page. A click shows the new version, a link to the release notes and - in the Windows program - **Install update**; on Unraid, Docker and NAS it briefly explains how to update the container. **All update settings** jumps to this section. If everything is up to date, the check is not possible or this is a dev build, no icon is shown; problems with the check are only reported here in the settings.

How often PlatinVault looks for updates by itself is set in this section under **Look for updates**: **At every start**, **Every 4 hours**, **Daily** (default), **Weekly**, **Monthly** or **Never (by hand only)**. It applies to every platform, including the Windows program. You check by hand with **Check for updates now** here - or at the bottom of every page: a click on the version number opens **Version & updates** with the button **Check for updates**, the time of the last check and the result. If a new version is found, the update icon appears in the header at once.

## 12.2 Windows program

The program updates itself (chapter 11.6): **Install update** in the settings or in the menu of the icon next to the clock downloads,
checks and installs the version that was found.

## 12.3 Unraid, NAS and Docker

A container cannot exchange its own program – your server does that. PlatinVault remembers your choice, reports new versions and shows
you in the settings exactly what to do. Which versions your container gets is decided by its **image tag**:

| Image | You get |
|---|---|
| `ghcr.io/sushiflux/platinvault:latest` | releases only |
| `ghcr.io/sushiflux/platinvault:beta` | betas and releases (always the newest of both) |

Set the switch in the app to match the tag. This is how you change the tag – your data in the database and appdata stays:

- **Unraid:** Docker tab → click the PlatinVault container → **Edit** → change the tag under **Repository** → **Apply**. Afterwards you
  get new versions as usual with **Check for Updates** and **Update**. In Community Applications you can pick the tag right at installation.
- **Docker Compose:** change the `image:` line in `docker-compose.yml` and run `docker compose pull && docker compose up -d` in the same
  folder.
- **Other NAS:** in the container management of your NAS, change the image of the PlatinVault container to the tag you want and recreate
  the container with the new image.

Go back from `:beta` to `:latest` only once a release is out that is at least as new as your beta – before that it would be a step back to
an older version. The settings tell you when that is the case.

## 12.4 Linux package

The Linux package does not update itself. PlatinVault reports new versions as everywhere (update icon in the header, Settings →
**System** → **Updates**) and shows you the three commands for updating there; step by step it is in section 13.5.

## 12.5 Protecting your data

If you start an older PlatinVault version with a database that a newer version (for example a beta) has already used, PlatinVault does
not start and changes **nothing** in the database. The message names the version you need (in the container log in German and English,
in the Windows program in its own window). Install that version (or a newer one) again or wait for the next release.

# 13 PlatinVault for Linux

On the Linux PC where Steam runs (desktop Linux, Steam Deck in Desktop Mode, Bazzite) you install PlatinVault as the **Linux package**.
It is the same app as in the browser, with its own database, and reads Steam on this computer itself – you do not need a bridge. You use
it in the web browser. Requirements: 64-bit Linux (x86_64), a web browser, preferably systemd (without it the menu entry starts
PlatinVault itself) and `libicu` (usually present; without it dates and numbers use a neutral format).

> [!NOTE]
> **Experimental:** reading the Steam files on Linux (stats file, detecting the Steam account, seeing Flatpak processes) has not been
> verified on a real Linux Steam yet, and whether the service keeps running in the Steam Deck's Game Mode is open (in Desktop Mode it
> does). If something does not work, see 13.6, and please tell us what happens for you.

## 13.1 Installing

1. Download `PlatinVault-linux-x64-<version>.tar.gz` from **Releases** on GitHub (`github.com/sushiflux/PlatinVault`).
2. Open a terminal in the download folder and run – no root, nothing outside your home folder:

   ```
   tar xzf PlatinVault-linux-x64-<version>.tar.gz
   cd PlatinVault-linux-x64-<version>
   ./install.sh
   ```

3. The installer starts PlatinVault and prints the address `http://127.0.0.1:5233/`. There is also a **PlatinVault** entry in your
   application menu that opens the page in the browser. Carry on with chapter 4.
4. If port 5233 is taken, the installer stops and tells you. Choose another one with `./install.sh --port 5300`.
   `./install.sh --no-systemd` sets up no service; the menu entry then starts PlatinVault itself.

## 13.2 Where things are

| What | Where |
|---|---|
| Program | `~/.local/share/platinvault/app/` |
| Data (database, automatic backups, encrypted keys) | `~/.local/share/platinvault/data/` |
| Settings (port, address, data folder) | `~/.config/platinvault/platinvault.env` (`PLATINVAULT_PORT`, `PLATINVAULT_BIND`, `PLATINVAULT_DATA_DIR`) |
| Service | systemd user service `platinvault` |
| Log | `journalctl --user -u platinvault` or `~/.local/share/platinvault/platinvault.log` |

Do not share the `data` folder and back it up as a whole: whoever has it also has your API keys. PlatinVault backs up the database
itself (start, every 6 hours, stop), as described in chapter 3.2.

## 13.3 Start, stop, open

`~/.local/share/platinvault/app/ctl.sh start|stop|restart|status|url|open` controls PlatinVault: `status` shows whether it runs, `url`
prints the address, `open` opens it in the browser.

## 13.4 Reachable on this computer only

By default PlatinVault listens on `127.0.0.1` only, that is on this computer. If you put a network address into `PLATINVAULT_BIND` in
`platinvault.env`, PlatinVault is reachable for the whole network – **there is no sign-in**, everyone there can see and change
everything. Do that only in a trusted home network and never expose the port to the internet.

## 13.5 Updating and uninstalling

- **Update:** download the new package, unpack it and run its `install.sh` again. The old version is stopped (with a last backup) and
  replaced, your data is kept. When an update is available, the app shows you these steps and the file name.
- **Uninstall:** `~/.local/share/platinvault/app/uninstall.sh`. It asks whether to keep your data – the default is to keep it.

## 13.6 Problems on Linux

| Problem | What helps |
|---|---|
| Steam is not found | In the **Steam** tab enter the folder with `steamapps` under **Steam folder (optional)** (4.1). The system check lists the places searched. |
| No live popups | Detection is experimental. Check that Steam is found (system check), that the game is in PlatinVault, and with Flatpak Steam that the Steam folder is right. Tell us what you see. |
| Port 5233 is taken | `./install.sh --port 5300` or another free port. |
| The page does not open | `ctl.sh status` shows whether PlatinVault runs; the log with `journalctl --user -u platinvault`. |
| After switching to the Steam Deck's Game Mode PlatinVault is gone | Whether the user service keeps running there is not verified. Start it again in Desktop Mode with `ctl.sh start`. |
