# Winfred

An Alfred-style launcher for Windows. Summon it, type, hit Enter.

Web searches · calculator · indexed file search · Evernote · browser bookmarks ·
Windows settings · 1Password open-and-fill · a full settings UI.

## Run

```powershell
.\dist\Winfred.exe        # already built — or rebuild with .\build.ps1
```

Winfred lives in the system tray. Right-click the tray icon for **Settings…**,
**Rebuild file index**, **Edit config file**, **Reload config**, **Start with Windows**
and **Quit**. Typing `winfred` (or `settings`) in the launcher opens Settings too.

## Usage

| Type…            | Result                                                            |
| ---------------- | ----------------------------------------------------------------- |
| double-tap `Win` | open / close the launcher                                         |
| `10+25`          | **35** — Enter copies it                                          |
| `goog shoes`     | Google search for "shoes"                                         |
| `yt lofi`        | YouTube — also `gh`, `wiki`, `maps`, `amzn`, `ddg`, `img`, `so`, `tr` |
| `open report`    | search indexed files                                              |
| `find report`    | same, but Enter reveals the file in Explorer                      |
| `bm hacker`      | search browser bookmarks                                          |
| `en pedalboard`  | search Evernote notes                                             |
| `set bluetooth`  | jump to a Windows settings page                                   |
| `1p github`      | 1Password — Enter opens the site and fills the login              |
| `github.com`     | open the URL directly                                             |
| anything else    | fall back to your default search engine                           |

Applications, files, bookmarks and settings pages also appear without a keyword; each source can be
switched in or out of those unprefixed results in Settings.

### Keys

| Key | Does |
| --- | ---- |
| `↑` `↓` `PgUp` `PgDn` | move the selection |
| `Enter` | run the highlighted result |
| `Ctrl`/`Shift`/`Alt` + `Enter` | the alternate action (subtitle updates while the key is held) |
| `Ctrl`+`1`…`9` | run the result numbered on the right |
| `Ctrl`+`,` | open Settings |
| `Tab` | autocomplete a keyword or file name |
| `Esc` | dismiss |

## Settings

Nine sections, all live-applied on **Apply**:

- **General** — hotkey (double-tap modifier *or* a recorded key combination), start with
  Windows, theme (dark/midnight/light), result count, window width, vertical position,
  font scale, placeholder text, numbered result shortcuts, and modifier-key action hints.
- **Search Engines** — add, duplicate, remove and test keyword searches; pick the fallback
  engine. `{q}` in the URL is replaced with the URL-encoded query.
- **Files** — which folders to index and whether to recurse, folder/extension filters,
  hidden files, live watching, rebuild interval, index size cap, plus a manual rebuild.
- **Bookmarks** — which detected Chromium browsers to read, keyword, reload interval.
- **Evernote** — database path (auto-detected), content search, trashed notes, reload interval.
- **Windows Settings** — keyword, plus a browsable list of every page in the catalog.
- **Calculator** — decimals, thousands separators, `=`-prefix mode, large type, and a live
  test field.
- **1Password** — CLI path with a connection test, what Enter does for a login, autofill
  shortcut, timings, browser override, which categories copy instead of fill, clipboard
  clear delay.
- **Advanced** — open/export/import the config file, reset learning, restore defaults.

Winfred also learns which result you pick for a given query and floats it up next time
(General → *Learn from usage*; clear it under Advanced).

## Calculator

Type a sum anywhere in the launcher — the answer is the first result. `Enter` copies it,
`Ctrl+Enter` copies `expression = result`, `Shift+Enter` shows it in large type.

```
10+25            35
1,250 * 3        3,750
120 + 15%        138          (15% of 120, added)
20% of 300       60
17 mod 5         2
2^10             1,024        (also 2**10)
0x1f + 1         32           hex, 0b for binary
sqrt(16)         4
round(pi, 4)     3.1416
```

Also `min`/`max`/`sum`/`avg`, `ln`/`log`/`log2`/`exp`, the trig family, `fact`, `rad`/`deg`,
constants `pi`/`tau`/`e`/`phi`, and `10 x 5` for multiplication. Ordinary text never
triggers the calculator — an expression must parse completely, and a bare number needs a
leading `=` (`=42`). Integer answers also show their hex and binary forms.

## File search

Winfred keeps its own index of the folders you choose (Desktop, Documents and Downloads by
default) rather than relying on Windows Search. It is rebuilt on an interval, kept current
by `FileSystemWatcher` while running, and cached to `file-index.bin` so restarts are
instant. Matching is fuzzy, biased toward word starts, prefixes and recently modified files
— `pps` finds *Pool Party Set List*.

`Enter` opens · `Ctrl+Enter` reveals in Explorer · `Shift+Enter` copies the path ·
`Alt+Enter` opens the containing folder. With the `find` keyword the first two swap.

## Evernote

Reads the local SQLite graph the Evernote v10 desktop app keeps in sync, under
`%APPDATA%\Evernote\conduit-storage`. Evernote holds the file open, so Winfred snapshots it
before each read. Titles, notebooks and tags are always searched; note *text* is searched
for the notes Evernote has stored locally.

`Enter` opens the note in the Evernote app (`evernote:///view/…`) · `Ctrl+Enter` opens it on
the web · `Shift+Enter` copies the link.

## Bookmarks

Parses the `Bookmarks` JSON that Chrome, Edge, Brave, Vivaldi, Chromium and Opera each
write. Read-only, no extension required. **Brave** is the default source, reading its
**Default** profile only — Guest and secondary profiles are skipped unless you untick
*Only read each browser's Default profile* in Settings. That panel spells out exactly which
profile files are being read and which are ignored. Note that a browser only has a
`Bookmarks` file once you've saved a bookmark in it.

## Windows settings

148 entries covering the `ms-settings:` pages plus classic tools (Device Manager, Services,
Registry Editor, Environment Variables, God Mode…). `Enter` opens · `Ctrl+Enter` copies the
command.

## 1Password

Needs the `op` CLI and 1Password → **Settings** → **Developer** → **Integrate with
1Password CLI**. Use **Test connection** in Settings to check it.

What `Enter` does depends on the item:

- **Login** → opens its website and fills it. Two strategies, chosen in Settings:
  - *extension* (default) — after the page loads, Winfred presses the 1Password browser
    extension's autofill shortcut (`Ctrl+\` in Chrome by default), so the extension does the
    filling and the password never passes through Winfred's keystrokes.
  - *type* — Winfred types the username, `Tab`, then the password. Works without the
    extension.
  With **submit after fill** on, `Enter` is pressed afterwards to log you in.
- **Password, API credential, secure note, card, SSH key…** → copies the secret to the
  clipboard, which self-clears after 45 seconds (configurable, 0 disables). Which categories
  copy is a checklist in Settings.

Alternate actions: `Ctrl+Enter` copies the password · `Shift+Enter` copies the username ·
`Alt+Enter` opens the site without filling.

Before every synthetic keystroke Winfred re-checks that a browser still owns the foreground
window and aborts otherwise, so a password can't be typed into whatever else grabbed focus.

## The summon hotkey

The default is a double-tap of the **Windows key** (the Command-key position on a Mac
keyboard). Because a bare Win tap normally opens the Start menu, Winfred swallows the tap
and replays it ~380 ms later if no second tap follows — so the Start menu still works, just
with a small delay. `Win`+`L`, `Win`+`Tab` etc. are unaffected.

If the delay bothers you, switch to double-`Ctrl` (no side effects, no delay) or record a
key combination such as `Ctrl+Space` in **Settings → General**.

### Full-screen apps

The hotkey is ignored while a full-screen app owns the foreground. Taking focus from a game
makes most engines mute their audio, and over exclusive full-screen the launcher can't be
composited on top at all — so the summon would cost a second of silence and show nothing.
Turn it off with **Settings → General → "Ignore the hotkey while a full-screen app is in
front"** (`hotkey.suppressInFullscreen`) if you play in a window. Windowed and maximised
apps are unaffected, and the tray icon still opens the launcher either way.

## Build from source

```powershell
.\build.ps1    # publishes to .\dist (needs the .NET 8 SDK)
```

The app needs the .NET 8 Desktop Runtime, which ships on most systems.

## How it works

- `KeyboardHook.cs` — a `WH_KEYBOARD_LL` hook detects either two bare taps of a modifier or
  an exact chord. Win/Alt taps are masked with a dummy keystroke (`0xE8`) so the OS never
  sees a bare tap, and single taps are re-sent after the window expires.
- `FullscreenGuard.cs` — compares the foreground window's rect against its monitor bounds so
  the hook can stand down over full-screen games; cached for 500 ms to stay off the hook's
  hot path.
- `SearchEngine.cs` — routes the query: calculator → keyword trigger → merged default
  results → web fallback, with a usage-learned score nudge.
- `Calculator.cs` — a recursive-descent parser with contextual percentages.
- `FileIndexer.cs` / `BookmarkIndex.cs` / `EvernoteIndex.cs` / `SystemSettingsCatalog.cs` —
  the four content sources.
- `FuzzyMatcher.cs` — subsequence scoring with word-boundary, prefix and density rules.
- `OnePasswordProvider.cs` + `AutoFill.cs` — `op` for the data, guarded `SendInput` for the fill.
- `SettingsWindow.xaml` — the settings UI; edits a clone of the config so Cancel is real.
- `Branding.cs` — the bowler-hat mark. `Assets\winfred.ico` is the exe, taskbar and tray icon;
  the `WinHatMark` template in `App.xaml` draws the same hat as vector XAML for the launcher
  and the settings sidebar. Regenerate the .ico with `.\tools\make-icon.ps1` after changing
  the geometry — both copies are drawn in the same 256x256 space, so keep them in step.

State lives in `%APPDATA%\Winfred`: `config.json`, `usage.json`, `file-index.bin`,
`evernote-snapshot.db`, and `error.log` if anything goes wrong.
