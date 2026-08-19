# Alfred UI gap — handoff

Winfred is an Alfred-style launcher for Windows. PR
[#1](https://github.com/hollywoodj/winfred/pull/1) (`be38d73`) closed the first
round of search-results gaps: numbered shortcuts, real shell icons, bowler-hat
mark, empty-query chrome, type-ahead, modifier-key subtitles, and a preferences
cog.

This note is for the next agent. It lists **thirty remaining matches**: ten
that close the search-row visual gap, then twenty that look past the result
list — Alfred Powerpack surfaces, PowerToys Run / Flow Launcher habits, and
Windows-only opportunities. What is already shipped is listed so those
changes are not re-implemented or undone.

Visual QA has to happen on **Windows**. This cloud environment is Linux and
cannot run the WPF app. Rebuild with `.\build.ps1` and use Settings → General →
**Preview launcher**.

References (do not copy trademarked Alfred artwork):

- [Appearance & Theming](https://www.alfredapp.com/help/appearance/)
- [Cheatsheet](https://www.alfredapp.com/help/getting-started/cheatsheet/)
- [File Search & Navigation](https://www.alfredapp.com/help/features/file-search/)
- [Clipboard History](https://www.alfredapp.com/help/features/clipboard/)
- [PowerToys Run](https://learn.microsoft.com/en-us/windows/powertoys/run) (Window Walker, `>`, units)

---

## Already shipped — do not redo

| Alfred behaviour | Winfred today |
| --- | --- |
| Hat on the left of the search field, cog on the right | `MainWindow.xaml` 3-column grid + `WinHatMark` |
| Borderless large search field | Transparent `TextBox` with null style |
| Empty query = search bar only, no result dump | `SearchEngine.Query` returns `[]` when `text` is empty |
| Divider appears only once there are results | `Divider` collapsed until `RunQuery` has items |
| Result row: icon · title + gray subtitle · muted `1`–`9` | Item template; `AssignShortcuts` |
| ⌘1–9 analog | `Ctrl+1`…`9` |
| ⌘, analog | `Ctrl+,` opens Settings |
| Hold modifier → selected subtitle becomes that action | `ModifierState` + `ActionSubtitleConverter` |
| Tab ghost-completes | Overlay `Typeahead` TextBlock |
| Hover moves the selection | `Results_PreviewMouseMove` |
| Hidden scrollbar, no focus ring | ListBox styles |
| Real app/file icons | `IconExtractor` via `IShellItemImageFactory` |
| Glyph fallbacks for web/calc/settings/1Password | `Glyphs.cs` (Segoe MDL2) |

### Branding decisions — do not undo without a reason

- **Bowler**, not a stovepipe. Brim ≈ 1.3× crown. Front-view silhouette, not a
  3D dock icon.
- **No magnifying glass** on the mark (Alfred trademark).
- **Blue accent ribbon**, not Alfred purple. In-window hat uses `WinText`; the
  `.ico` keeps a rounded dark tile.
- Shortcut numbers are **plain digits**, not ⌘ badges.
- Default launcher width is still **700px** (Alfred is closer to 560–600).

Same geometry lives in `App.xaml` (`WinHatMark`), `tools/make-icon.ps1`, and
`Assets/winfred.ico`. If you change the hat, regenerate the ico with
`.\tools\make-icon.ps1` so they stay in step.

---

## Ten remaining matches

Each item is independently shippable. 1–4 are the highest visual return for
the least new machinery. 5–7 need new UI surfaces. 8–10 are polish.

### 1. Highlight query characters in result titles

**Alfred:** Matching letters in the title are emphasised (weight or underline)
as you type, including fuzzy/subsequence matches (`pps` → **P**ool **P**arty
**S**et List).

**Winfred:** Titles are a single `Text="{Binding Title}"` binding.
`FuzzyMatcher` scores subsequence matches but does not expose *where* they
landed.

**Do this:**

- Add `FuzzyMatcher.MatchSpans(text, query) → IReadOnlyList<(int Start, int Length)>`
  using the same walk as `Subsequence`, plus a contiguous span for
  `IndexOf` hits.
- Put the spans on `ResultItem` (or a small `TitleHighlight` DTO).
- Replace the title `TextBlock` with one that builds `Inlines`: unmatched
  `Run`s keep `WinText` / `WinSelectedText`; matched `Run`s use
  `FontWeight="SemiBold"` (and optionally `WinAccent` when unselected).
- Skip highlighting when the query is a keyword prefix (`open `, `en `, …)
  unless the remainder also matches the title.

**Files:** `FuzzyMatcher.cs`, `Models.cs`, `SearchEngine.cs`,
`MainWindow.xaml` (title currently cannot be `Inlines` from a binding —
use a small attached behaviour or a `TitlePresenter` in `LauncherUi.cs`).

**Watch:** WPF `DataTemplate` recycling; freeze brushes; do not highlight
inside the type-ahead overlay.

---

### 2. Appearance Options: hide hat, hide cog, subtext mode

**Alfred:** Appearance → Options can hide the hat, hide the cog, hide result
shortcuts (already a checkbox), hide the scrollbar (already hidden), and set
**Show result subtext** to Always / Never / Only for alternative actions.

**Winfred:** Only `ShowResultShortcuts` and `ShowActionHints`. Subtitles always
show. Hat and cog are always visible.

**Do this:**

```csharp
// AppearanceConfig
public bool ShowHat { get; set; } = true;
public bool ShowCog { get; set; } = true;
public string SubtextMode { get; set; } = "always"; // always | never | selected
```

- Bind hat/cog `Visibility` from `ApplyAppearance()`.
- Subtext: collapse unselected subtitles when mode is `selected`; collapse all
  when `never`; keep today's behaviour for `always`. Modifier-key rewrite
  (`ShowActionHints`) should still force the selected subtitle on, even in
  `never` / `selected` — that *is* Alfred's "only for alternative actions".
- Settings → General: three checkboxes + a combo for subtext. Live-apply like
  the other appearance fields.

**Files:** `Config.cs`, `MainWindow.xaml`, `MainWindow.xaml.cs`,
`SettingsWindow.xaml`, `SettingsWindow.xaml.cs`, `LauncherUi.cs`
(`ActionSubtitleConverter` should treat "no subtext unless selected +
modifier" as a first-class mode).

---

### 3. Typography, metrics, and selection colour

**Alfred (default-ish):** search field ~28–32pt light; hat ~48px; window
~560–600px; corner radius ~16; result icons ~40px; titles 17–18pt regular
(slightly heavier when selected); selected row is an inset rounded rect, often
with a cooler / accent wash rather than a flat grey slab; more padding around
the field.

**Winfred:** 24pt input, 40px hat, 700px wide, radius 12, icons 36px, selected
fill `#3D4666` (dark) / `#D6DDF0` (light). Outer `Grid Margin="20"` is the
drop-shadow gutter.

**Do this (defaults, all overridable):**

| Token | Now | Closer to Alfred |
| --- | --- | --- |
| `Appearance.WindowWidth` | 700 | 600 |
| Window `CornerRadius` | 12 | 16 |
| Selection `CornerRadius` | 6 | 8 |
| `WinInputSize` | 24 | 28, `FontWeight="Light"` |
| Hat | 40 | 48 (already scales with `FontScale`) |
| `WinResultIconSize` | 36 | 40 |
| Search-row `MinHeight` | 48 | 56 |
| Row padding | `8,6` | `10,8` |
| `WinSelected` (dark) | `#3D4666` | something closer to `#2F5DFF` @ ~35% or `#3A5080` |
| `WinSelected` (light) | `#D6DDF0` | system-blue wash `#C7D7FF` |

Bump `DropShadowEffect` BlurRadius (~40) and keep `ShadowDepth` modest so the
window floats the way Alfred's does.

Do **not** shrink width without a Settings default change — existing
`config.json` values persist.

**Files:** `App.xaml`, `Theme.cs`, `MainWindow.xaml`, `MainWindow.xaml.cs`
(`ApplyAppearance`), `Config.cs` default `WindowWidth`.

---

### 4. Frosted-glass background (window blur)

**Alfred:** Non-opaque themes blur the desktop behind the panel (Appearance →
Options, global blur).

**Winfred:** Solid-ish fills (`#F5202230` etc.) with a drop shadow. No DWM blur.

**Do this:**

- On Windows 11, enable [backdrop material](https://learn.microsoft.com/en-us/windows/apps/desktop/modernize/apply-mica-acrylic)
  (`Acrylic` or `MicaAlt`) on the launcher HWND after `SourceInitialized`.
  Keep `AllowsTransparency` **or** switch to a DWM path; mixing both is a
  common WPF footgun (black window, no rounded corners).
- Fallback on Windows 10: `DwmEnableBlurBehindWindow` / accent policy, or
  leave the current translucent fill.
- Theme palettes should then use **lower alpha** on `WinBg` so the blur reads.
  Light theme especially: near-white 70–85% over acrylic looks like Alfred's
  macOS theme.
- Settings: checkbox **Blur background** (default on when supported).

**Files:** new small `WindowBlur.cs` (P/Invoke), `MainWindow.xaml.cs`,
`Theme.cs`, `Config.cs`, Settings General.

**Watch:** `AllowsTransparency="True"` + acrylic often fights. Prototype on a
real Win11 box before committing to a strategy. Large Type (`LargeTypeWindow`)
should use the same treatment so it does not look like a different app.

---

### 5. Summon animation (fade + results unfold)

**Alfred:** The panel appears with a short opacity/scale ease; the result list
grows downward as matches arrive rather than popping a full-height slab.

**Winfred:** `Show()` / `Hide()` with no animation. `SizeToContent="Height"`
already shrinks to the search bar when empty, which is the right structure.

**Do this:**

- On `ShowLauncher`: opacity 0 → 1 over ~120ms (`QuadraticEase`), optional
  98% → 100% scale from the top centre. Skip animation if
  `SystemParameters.ClientAreaAnimation` is false (a11y).
- Keep `SizeToContent="Height"` so adding rows is the "unfold". If that
  jitters, animate `Results.MaxHeight` from 0 to the computed cap.
- On hide: 80ms fade out, then `Hide()`. Cancel in-flight storyboards if the
  hotkey is tapped again.

**Files:** `MainWindow.xaml.cs` primarily; optional `BeginStoryboard` in
`MainWindow.xaml`.

---

### 6. Quick Look for files

**Alfred:** `Shift` (press-and-hold) or `⌘Y` previews the selected file
(Quick Look).

**Winfred:** No preview. `Shift+Enter` is already bound to "copy path" /
calculator large type, so **do not steal Shift+Enter**. Use **Space** (hold or
toggle) and/or `Ctrl+Y`.

**Do this (v1, not a full Preview Handler host):**

- Space on a file/folder/`ResultKind` that has a path: open a topmost preview
  window to the right of the launcher (or centred overlay).
- Image extensions → `BitmapImage`; `.txt`/`.md`/`.json`/`.cs` → first N KB in
  a `TextBox`; folders → a few child names; anything else → large icon +
  metadata (size, modified, path) — still useful.
- Close on Space/Esc/selection change/launcher hide.
- Settings: **Quick Look with Space** (default on).

A later pass can host `IPreviewHandler` for PDF/Office; that is a project of
its own.

**Files:** new `QuickLookWindow.xaml` (+ code-behind), `MainWindow.xaml.cs`
key handling, `Models.cs` (a `PreviewPath` on `ResultItem` so the UI does not
parse subtitles), Settings.

**Watch:** `Window_Deactivated` currently **hides the launcher**. The preview
must either be owned by `MainWindow` (`Owner = this`) so it does not steal
activation, or `_openingSettings`-style guard must ignore deactivation while
Quick Look is up.

---

### 7. Actions panel for the selected result

**Alfred:** Right-arrow / `fn` / Action Panel shows Open, Reveal in Finder,
Copy Path, Email, … for the current item.

**Winfred:** Alternate actions exist only as `Ctrl`/`Shift`/`Alt`+Enter and a
subtitle rewrite. There is no list you can arrow through. Right-arrow is
unbound.

**Do this:**

- `→` (or `Ctrl+\`) on a result with extra actions replaces the result list
  with that item's actions (icon + label + shortcut). `←` / Esc returns.
- Seed the list from `Enter` / `CtrlEnter` / `ShiftEnter` / `AltEnter` so
  providers do not need a new API on day one. Add an optional
  `IReadOnlyList<ResultAction> Actions` later for "copy path", "open
  containing folder", etc. that are not already modifier-bound.
- Keep the search field showing the original query; do not treat `→` as
  autocomplete (Tab already does that).

This is the single biggest *interaction* gap left. It also makes modifier
hints discoverable without a cheatsheet.

**Files:** `MainWindow.xaml.cs` (panel state machine), `Models.cs`,
`SearchEngine.cs` (optional extra actions on files/apps), maybe a second
`ListBox` in `MainWindow.xaml` rather than overloading `Results`.

---

### 8. Optional default results on an empty query

**Alfred:** Can show default results (recent apps / fallbacks) before you type.
Winfred's empty field is intentionally chrome-only (PR #1), which matches
stock Alfred for many users.

**Do this as an opt-in**, default **off**:

```csharp
public bool ShowDefaultResults { get; set; } // false
```

When on and the query is empty, show up to `MaxResults` recents from `Usage`
(apps first), not the old keyword-hint dump. Placeholder still explains
calculator / keywords.

**Files:** `SearchEngine.cs` (`Query` early return), `Usage` ranking,
`Config.cs`, Settings General.

Do not bring back `Hints()` as the empty-state UI.

---

### 9. Pair light/dark with Windows, and make Light look like Alfred's macOS theme

**Alfred:** Users keep two themes and switch with OS appearance. The stock light
look is near-white panel, blue-tinted selection, graphite subtext.

**Winfred:** Theme is a sticky combo (`dark` / `midnight` / `light`). Light is
already the weakest of the three: selection is a periwinkle slab, not the
clean macOS Alfred row.

**Do this:**

- Add `theme = "system"` that follows
  `UISettings.ColorValuesChanged` / `SystemParameters` light/dark.
- Restyle **light**: `WinBg` ≈ `#F2FFFFFF`, `WinSelected` ≈ `#FF0A84FF` at
  ~22% or a solid `#D6E4FF`, `WinText` near-black, subtext `#6C6C70`, shortcut
  `#A1A1A6`. Selected title stays dark (Alfred does not invert to white on
  light themes).
- Dark can stay; optionally nudge it toward neutral charcoal and off the
  purple-navy if item 3's selection colour lands.
- Settings: Theme combo includes `system`.

**Files:** `Theme.cs`, `Config.cs`, `SettingsWindow`, `App.xaml.cs` (subscribe
to OS theme while the process lives).

---

### 10. Selected-row action chrome, Home/End, and a result context menu

Three small Alfred habits that should ship together because they share the
selected-row path:

1. **Idle action hint on the selected row** — even with no modifier held,
   Alfred often shows what Enter will do (or leaves subtext as the path).
   When `ShowActionHints` is on, append a muted `Enter  {Enter.Label}` on the
   selected subtitle (or a right-side label). Holding Ctrl/Shift/Alt still
   replaces it, as today.
2. **Home / End** move to first / last result. Alfred does this; Winfred
   leaves Home/End to the text box. If the caret is at the end of the input
   (the usual case), End should jump to the last result; Home to the first.
   Do not steal Home when the user is editing in the middle of the query.
3. **Right-click a row** → context menu of that row's actions (same list as
   item 7). Lets mouse users discover Ctrl/Shift/Alt+Enter. `PreviewMouseRightButtonUp`;
   do not run the default action.

**Files:** `LauncherUi.cs`, `MainWindow.xaml.cs`, `MainWindow.xaml` (optional
`ContextMenu` on `ListBoxItem`).

---

## Twenty more — look past the search row

Items 1–10 make the *panel* look like Alfred. These twenty make *using* it
feel like Alfred (and like a good Windows launcher). Sources mixed in: Alfred
Powerpack, PowerToys Run, Flow Launcher, Listary, and gaps in Winfred’s own
settings/tray/error surfaces.

| # | Gap | Closest analog |
| --- | --- | --- |
| 11 | Browse folders from `~` / `\` | Alfred file navigation |
| 12 | Multi-file buffer chips | Alfred file buffer |
| 13 | Searchable clipboard history | Alfred clipboard (opt-in) |
| 14 | Snippets | Alfred snippets |
| 15 | lock / sleep / shutdown / mute | Alfred system commands |
| 16 | `>` run in Terminal | Alfred `>` / PowerToys shell |
| 17 | Switch to (or kill) a running window | PowerToys Window Walker |
| 18 | `define` / `spell` | Alfred dictionary |
| 19 | ↑ query history | shells, most launchers |
| 20 | leading space, `in`, Ctrl+Enter web | Alfred keywords |
| 21 | Everything / Windows Search backend | Flow Launcher, Listary |
| 22 | Admin / folder buttons on the selected row | PowerToys Run |
| 23 | Status line instead of tray balloons | Alfred in-panel status |
| 24 | Search Settings + `?hotkeys` + live preview | Alfred Preferences |
| 25 | Which screen, DPI, high contrast | Alfred Appearance Options |
| 26 | IME, Narrator, reduced motion | platform-native |
| 27 | Now-playing mini-player | Alfred Music / SMTC |
| 28 | Contacts | Alfred Contacts |
| 29 | JSON keyword workflows | Alfred Workflows lite |
| 30 | Units / time / hash + Large Type on any row | PowerToys converter |

Each item is independently shippable. 11–12 assume the actions panel (7) exists
or they grow a tiny one of their own.

### 11. File-system navigation (`~`, `\`, drive letters)

**Elsewhere:** Alfred: type `~` or `/` and you are *in* a folder. Enter
descends, Backspace goes up, Tab completes like a shell, `.` toggles hidden
files. A small cog in that view sorts by name/date.

**Winfred:** `open` / `find` fuzzy-search an index. There is no browse mode.
Typing `C:\` or `~\Documents` is not special.

**Do this:** If the query is a path prefix (`~`, `\`, `/`, `X:\`, `\\server\`),
switch the list to the directory’s children (not the indexer). Enter on a
folder replaces the query with that path + `\`; Backspace at end-of-input
pops a segment. Tab still autocompletes the selected name. A footer chip
(“Name · Date · Folders first”) is enough; skip a full cog for v1.

**Files:** new `FileNavigator.cs`, `SearchEngine.Query` early path, `MainWindow`
Backspace handling (only when the caret is at the end and the query is a path).

---

### 12. File buffer (multi-select chips)

**Elsewhere:** Alfred Alt+↑ adds the selected file to a buffer; Alt+↓ adds and
moves on; Alt+← pops; Alt+→ actions the lot. A strip of icons sits under the
search field.

**Winfred:** One result, one action. No multi-file verb.

**Do this:** A `FileBuffer` (max ~20 paths) and a horizontal `ItemsControl`
under the divider. Chips: icon + truncated name + ×. Alt+Up/Down/Left/Right
as Alfred (on Windows Alt is already a modifier for “open folder” on Enter —
**buffer chords should be Alt+arrow, not Alt+Enter**, so they do not collide).
Alt+→ opens the actions panel (item 7) targeting the whole buffer (copy,
move, zip, attach to mail).

**Files:** `FileBuffer.cs`, `MainWindow.xaml` chip strip, `MainWindow.xaml.cs`.

---

### 13. Clipboard history viewer

**Elsewhere:** Alfred `⌥⌘C` — searchable clips (text, images, file lists),
Enter pastes into the previous app, `clear` pauses/wipes, Cmd+S saves a
snippet. Disabled until the user opts in.

**Winfred:** Clipboard is write-only (`ClipboardGuard`). Errors and copied
calculator answers never come back.

**Do this:** Opt-in Settings page **Clipboard**. Background watcher
(`Clipboard.Changed` / Win32 listener) stores text (+ optional file drops)
to `%APPDATA%\Winfred\clipboard.db`, skip if the foreground window is a
password box (`GetGUIThreadInfo` + ES_PASSWORD) or the clip came from
Winfred’s own secret copy. Keyword `clip` or a dedicated hotkey. Enter:
copy + optional `SendInput` Ctrl+V to the previous hwnd after hide
(remember hwnd in `ShowLauncher`). Images as thumbnails in the row.

**Files:** `ClipboardHistory.cs`, Settings panel, `SearchEngine` keyword,
`MainWindow` “last foreground hwnd”.

**Watch:** Privacy default **off**. Do not log 1Password copies.

---

### 14. Snippets and (optional) expansion

**Elsewhere:** Alfred `snip` keyword + automatic expansion of abbreviations.
Snippets can include `{date}`, `{clipboard}`.

**Winfred:** Nothing. Users retype signatures and issue templates.

**Do this:** Settings list (name, keyword, body, expand-in-place yes/no).
`snip foo` lists matches; Enter copies or pastes into the previous app.
Expansion v1 can be **launcher-only** (type the keyword in Winfred). Global
expansion needs the existing `KeyboardHook` — do it later, and never expand
in password fields.

Placeholders: `{date}`, `{time}`, `{clipboard}`, `{cursor}`.

**Files:** `SnippetStore.cs`, Settings **Snippets** nav item, `SearchEngine`.

---

### 15. System commands with a confirm row

**Elsewhere:** Alfred `lock`, `sleep`, `shutdown`, `restart`, `emptytrash`,
`mute`, `volup`, `screensaver`, `quit`. Destructive ones wait for a second
Enter.

**Winfred:** `BuiltInActions` is Settings / rebuild index / reload config.
No power or volume verbs.

**Do this:** Results for lock (`LockWorkStation`), sleep, hibernate, sign
out, restart, shutdown, empty Recycle Bin, mute/vol up/down, display off,
start screensaver. Match on the verb *and* fuzzy title. Shutdown/restart/
empty-trash: first Enter turns the row into **“Enter again to confirm”**
(`HidesWindow = false`); second Enter within ~3s runs it. Volume can call
`keybd_event` VK_VOLUME_*.

**Files:** `SystemCommands.cs`, `SearchEngine.BuiltInActions`.

---

### 16. Shell prefix `>`

**Elsewhere:** Alfred `>` runs in Terminal.app. PowerToys Run `> Shell:startup`
and arbitrary cmd.

**Winfred:** No shell. URLs and `ms-settings:` only.

**Do this:** Query starting with `>` (or `> `) shows “Run in Windows
Terminal” / “Run in cmd” / “Run in PowerShell”, plus `shell:` folder names.
Enter: `wt.exe` / `pwsh -NoExit -Command` with the remainder. Ctrl+Enter:
run hidden and copy stdout (timeout ~5s) as a result/notification.
Settings: default shell, “admin” as Shift+Enter (`runas`).

**Files:** `ShellRunner.cs`, `SearchEngine.Query` prefix, Settings Advanced.

**Watch:** Do not silently elevate. Quote the command you display.

---

### 17. Window switcher and process kill

**Elsewhere:** PowerToys Window Walker (`< outlook`). Alfred `quit` / `hide`
on the frontmost app. Flow Launcher kills by name.

**Winfred:** Can launch a second copy of an app; cannot jump to the one
already open. `ApplicationIndex.Launch` always `Process.Start`.

**Do this:** Enumerate visible top-level windows (`EnumWindows`), skip
toolwindows. Default results: if an app is running, prefer **“Switch to …”**
(score bump) over launching again; Ctrl+Enter launches a new instance.
Keyword `<` or `win` filters by title/process. Kill: Alt+Enter or an action
(confirm for elevated / explorer).

**Files:** `WindowIndex.cs`, `SearchEngine.ApplicationResults`,
`ApplicationIndex.Launch`.

---

### 18. Dictionary and spell

**Elsewhere:** Alfred `define word` / `spell`. PowerToys has a dictionary
plugin.

**Winfred:** Unknown words fall through to Google.

**Do this:** `define ` uses `System.Windows.Documents.Speller` if available,
else a small bundled word list, else Wiktionary URL as a result (not a silent
browser open). `spell ` shows Hunspell/OS suggestions as rows; Enter copies
the word. Optional: a definition subtitle under the exact match in default
results when the query is a single dictionary word and nothing else scored
higher.

**Files:** `DictionaryProvider.cs`, `SearchEngine.RouteKeyword`.

---

### 19. Query history (↑ in an empty-ish field)

**Elsewhere:** Shells and many launchers recall the last queries. Alfred
learns picks; users still want “what did I type two summons ago?”

**Winfred:** `Usage` boosts *results* for a query; the search field always
opens blank. There is no up-arrow history. (`Usage` is referenced from
`App` / `SearchEngine` / `SettingsWindow` — keep that store; this is a
separate ring buffer of raw strings.)

**Do this:** Persist last ~50 non-secret queries (`clip` / `1p` contents
excluded). When the field is empty, ↑ cycles history into the box (and
re-runs). When the field has text, ↑ stays “move selection” as today.
`Ctrl+R` can re-run the last query without cycling.

**Files:** `QueryHistory.cs`, `MainWindow` key routing, Settings Advanced
“clear history”.

---

### 20. Keyword completeness: leading space, `in`, `tags`, Ctrl+Enter web

**Elsewhere:** Alfred: leading space = file search; `in` = contents; `tags`
= Finder tags; **Ctrl+Enter always** searches the web for the typed query
even when an app is selected. Alt+Enter searches in Finder.

**Winfred:** `open` / `find` only. Ctrl+Enter is the *row’s* alternate
action, so you cannot force a Google search when Chrome is highlighted.
No content search.

**Do this:**

- Query starting with a space → file provider (same as `open`).
- Keyword `in` → Everything (item 21) or `findstr`/`Windows Search` over
  indexed folders; show a “searching contents…” row while async.
- Keyword `tag` → NTFS alternate-data / Explorer tags if cheap; otherwise
  skip and document it.
- **Global** Ctrl+Enter: if the selected row already defines CtrlEnter,
  keep it; add a fallback — when *no* CtrlEnter, or when Ctrl+Shift+Enter,
  open the default web search for the raw query. Surface this in the
  subtitle when Ctrl is held and the row has no Ctrl action.

**Files:** `SearchEngine`, `MainWindow.ExecuteSelected`, `Config` keywords.

---

### 21. Optional Everything / Windows Search backend

**Elsewhere:** Flow Launcher and Listary sit on [voidtools Everything](https://www.voidtools.com/)
and feel instant across the whole disk. Alfred has Spotlight. Winfred’s
own walker is capped and folder-scoped on purpose.

**Winfred:** `FileIndexer` walks Desktop/Documents/Downloads. No ES SDK, no
Windows Search ISearchQueryHelper.

**Do this:** Settings → Files: **Backend** = Internal (default) | Everything
| Windows Search. Everything: detect `Everything64.dll` / named pipe; if
the service is missing, one result “Install Everything to search the whole
PC”. Keep the internal index for people who do not want a third-party
service. UI: subtitle should say which backend hit (`Everything · C:\…`).

**Files:** `EverythingClient.cs`, `FileIndexer` (or a `IFileSource`
interface), Settings Files panel.

---

### 22. Selected-row accessory buttons

**Elsewhere:** PowerToys Run puts small buttons on the highlighted row
(run as admin, open folder). Alfred puts those in the action panel, but
the extra chrome is how Windows users discover them without item 7.

**Winfred:** The only extra chrome is the `1`–`9` digit. Mouse users who
never hold Ctrl never see “reveal”.

**Do this:** When a row is selected, fade in 1–3 22px buttons on the right
of the title (left of the shortcut number): folder, admin (apps), copy.
Click must not also fire `PreviewMouseLeftButtonDown` → Enter. Keyboard:
the buttons are the same as Ctrl/Shift/Alt+Enter, not a new focus trap.

**Files:** `MainWindow.xaml` item template, `Models.ResultItem` flags
`CanRunAsAdmin`, `CanReveal`.

**Watch:** Admin for `.exe` is `ProcessStartInfo.Verb = "runas"`; for Store
apps, hide the button.

---

### 23. In-window status instead of tray balloons

**Elsewhere:** Alfred shows “indexing…” / errors *in the results*.
PowerToys uses a progress line. Balloon tips on modern Windows are easy to
miss and feel like 2006.

**Winfred:** `Notifier` → `NotifyIcon.ShowBalloonTip`. File rebuild,
1Password failures, config errors all toast in the tray. `FileIndexer.IsBuilding`
is not shown in the launcher. Evernote has an info row; files do not.

**Do this:** A one-line status under the search field (or a sticky first
result) bound to a `LauncherStatus` singleton: indexing count, “Unlock
1Password”, last error, clipboard-cleared. Auto-hide after a few seconds
unless it is a blocking error. Keep the tray for when the launcher is
hidden. First-run balloon can stay.

**Files:** `LauncherStatus.cs`, `MainWindow.xaml`, `App.xaml.cs` Notifier
handler, `FileIndexer.IndexChanged`.

---

### 24. Settings that search themselves (and a live appearance preview)

**Elsewhere:** Alfred Preferences filter by typing; `?hotkeys` / `?keywords`
in the launcher jumps to the right pane. Appearance is edited against a
live preview of the panel.

**Winfred:** Nine sidebar emoji rows, no search. `?` does nothing. “Preview
launcher” is a separate button that hides Settings (`ShowSettings` calls
`HideLauncher` first, then the launcher’s `Deactivated` is guarded by
`_openingSettings` only on the cog path — preview is easy to get wrong).
Appearance sliders do not show the launcher until Apply.

**Do this:**

- Filter the sidebar and jump to the matching control as the user types in
  a Settings search box (Ctrl+F).
- Launcher queries `?` / `?hotkeys` / `?keywords` list features; Enter
  opens Settings on that page (`SearchEngine.OpenSettings` needs a section
  argument).
- Appearance: an embedded *non-activating* mini `MainWindow` (or a cloned
  visual) at the bottom of General, updated on every slider tick from the
  draft config — not only after Apply.

**Files:** `SettingsWindow.xaml`, `SearchEngine.OpenSettings`, `MainWindow`
preview owner flags.

---

### 25. Which screen, per-monitor DPI, high contrast

**Elsewhere:** Alfred Appearance Options: default / mouse / active screen,
plus a position grid. Themes can follow accessibility contrast.

**Winfred:** Always the monitor under the **cursor**, `TopOffsetPercent`
from the top. DPI is sampled once from the window. No high-contrast
palette. Mixed-DPI (laptop 150% + 100% monitor) can place the panel on
the wrong origin if the HWND is created on the other screen.

**Do this:** `Appearance.Screen` = `mouse` | `active` | `primary`.
Re-query DPI from the *target* screen (`HwndSource.CompositionTarget`).
Subscribe to `SystemEvents.DisplaySettingsChanged` and
`SystemParameters.HighContrast`. High contrast: skip acrylic (item 4),
use system `Window`/`Highlight` colours, 2px borders.

**Files:** `MainWindow.PositionOnActiveScreen`, `Theme.cs`, `Config`,
Settings General.

---

### 26. Keyboard, IME, and screen readers

**Elsewhere:** Launchers that feel native survive CJK composition, Narrator,
and “reduce motion”.

**Winfred:** `TextBox` has no `InputMethod` handling; `TextChanged` runs
the query on every composition update (flicker for IME). Rows are
`Focusable=False` with no `AutomationProperties.Name`. Settings DataGrids
are mouse-first. Animations (item 5) have no reduced-motion check yet.

**Do this:** Ignore `TextChanged` while `e.Changes` are composition, or
listen to `TextCompositionManager`. Set `AutomationProperties.Name` on
each row to `Title + Subtitle`. Settings: tab order, AccessKeys on
sidebar. Honour `SystemParameters.ClientAreaAnimation` and
`UISettings.AnimationsEnabled`. Optional: larger hit targets when
`Tablet.TabletDevices` is non-empty.

**Files:** `MainWindow.xaml.cs`, item template, `SettingsWindow`.

---

### 27. Media mini-player

**Elsewhere:** Alfred Music Mini Player. Windows has System Media Transport
Controls — one API for Spotify, Apple Music, Groove, browser tabs.

**Winfred:** No `play` / `pause` / `next` / `mute` beyond what the user
could type into Settings search.

**Do this:** Keyword `music` (or empty-query media when SMTC is active)
shows artwork + title + artist as a custom result (or a slim header above
the list). Enter play/pause; Ctrl/Shift next/prev. Volume verbs in item
15 can target SMTC when a session exists. Artwork from
`GlobalSystemMediaTransportControlsSession`.

**Files:** `MediaSession.cs`, `MainWindow` optional header, `SearchEngine`.

---

### 28. Contacts

**Elsewhere:** Alfred searches Contacts.app, actions a field (mail, call,
copy). Windows: People / Microsoft Graph / Outlook PST is a maze; the
practical source is the Windows Contacts folder (`*.contact` XML) plus
optional Graph if the user is signed in.

**Winfred:** No people provider.

**Do this:** Index `%USERPROFILE%\Contacts` and, if present, Outlook via
COM only when the user enables it (slow, opt-in). Results: name, email,
phone. Enter = mail (`mailto:`), Ctrl+Enter = copy, Shift+Enter = tel:
link. Keyword `con` / `email`. Do not require Graph for v1.

**Files:** `ContactIndex.cs`, Settings (enable Outlook), `SearchEngine`.

---

### 29. Lightweight workflows (keyword → command)

**Elsewhere:** Alfred Workflows and Flow’s plugin store are the reason
people stay. Winfred should not build a node editor first.

**Winfred:** Hard-coded providers. No extension point. `SearchShortcut` is
URL-only.

**Do this:** A `workflows\` folder of JSON:

```json
{ "keyword": "ghp", "title": "GitHub profile",
  "command": "https://github.com/{query}", "args": "url" }
```

`args`: `url` | `shell` | `clipboard`. List them in Settings, import a
file. Launcher `workflow` keyword lists installed ones. A later store can
wait; the JSON schema is the product. Sandbox: `shell` workflows prompt
the first time.

**Files:** `WorkflowLoader.cs`, Settings **Workflows**, `SearchEngine.RouteKeyword`.

---

### 30. Units, time, hashes — calculator adjacent — plus Large Type for any text

**Elsewhere:** PowerToys `%%` / `==` unit converter, time/date, GUID, hash.
Alfred Large Type is not calculator-only (`large type` action on any text).
Winfred Large Type is dark-only and calculator-only (`LargeTypeWindow`).

**Do this:**

- Parse `10 ft in m`, `100 usd in eur` (static table + optional ECB/cache
  later), `utc`, `unix 0`, `guid`, `md5 …` / `sha256 …`.
- Show as `ResultKind.Calculator` so Large Type / copy still work.
- Shift+Enter Large Type on **any** selected title (files, 1Password
  usernames, calc). Theme-follow the overlay (today colours are hard-coded
  dark).
- In-launcher: hex/bin already exist; add a third subtitle line only when
  those extras exist so rows do not grow for everyone.

**Files:** `Units.cs` / extend `Calculator.cs`, `LargeTypeWindow.cs`,
`MainWindow.ExecuteSelected` Shift+Enter fallback.

---

## Stretch (after 1–30)

- Rounded 4px clip on result icons (WPF `Clip` on a `Border`; skipped in PR #1).
- Bookmark favicons (local Chromium `Favicons` SQLite; no network).
- Store/UWP icons via `Windows.ApplicationModel` when `IconExtractor` misses.
- Drag a file result out of the list (`DoDragDrop`).
- `Glyphs.Zip` (`U+F012`) missing-glyph fallback.
- Full theme editor (every colour/font/padding) — Alfred Powerpack territory.
- Global snippet expansion (item 14 is launcher-only first).
- `IPreviewHandler` Quick Look (item 6 is images/text first).
- Microsoft Graph contacts (item 28 is local files first).
- Workflow store UI (item 29 is JSON files first).

---

## Suggested implementation order

**Visual, one PR:** **2 + 3 + 10** (appearance toggles, metrics, selected-row
chrome). Screenshot-friendly, no new windows.

**Then the search row:** **1** highlighting, **9** light/system theme, **5**
motion, **4** blur (needs a Win11 box), **8** empty-state opt-in, **22**
accessory buttons, **23** in-window status.

**Surfaces (own PRs):** **6** Quick Look and **7** actions panel share
`Deactivated` / owned-child issues — ship them before **12** (buffer) and
**11** (navigation), which depend on actions.

**Windows-native value:** **15** system commands, **17** window switcher,
**16** `>`, **21** Everything, **20** keyword completeness, **30** units.

**Powerpack-shaped:** **13** clipboard (opt-in), **14** snippets, **19**
history, **24** settings search, **25–26** screen/a11y, **27–29** media /
contacts / workflows.

Leave **18** dictionary until a data source is chosen so it does not become
a disguised web search.

---

## QA checklist (Windows)

- Empty query: hat + field (+ cog); no leftover divider.
- Type `10+25`, an app name, a file, `goog …`, `en …`: icons, numbers 1–9,
  Tab ghost, Ctrl+1.
- Hold Ctrl / Shift / Alt: only the **selected** subtitle changes.
- Settings → hide hat / cog / shortcuts / subtext modes (once item 2 exists).
- Light, dark, midnight, and (once item 9 exists) OS theme switch while the
  launcher is open.
- Full-screen game: hotkey still suppressed.
- Preview launcher from Settings does not lose the settings window
  (`_openingSettings` pattern).
- Any new overlay (Quick Look, actions, Large Type, clipboard viewer) must
  not dismiss the launcher via `Window_Deactivated`.
- Path query `~\` browses; Backspace climbs; buffer chips survive a new query
  until Esc.
- `clip` is empty until Clipboard is enabled; 1Password copies never appear.
- `< notepad` switches; Ctrl+Enter launches a second instance.
- `shutdown` requires a second Enter; `lock` does not.
- `?hotkeys` opens Settings on the right page.
- IME composition does not spam queries; Narrator reads the selected title.
- High contrast: no acrylic, visible 2px border.

---

## File map

| Path | Role |
| --- | --- |
| `MainWindow.xaml` / `.xaml.cs` | Launcher chrome, keys, type-ahead |
| `LauncherUi.cs` | Modifier state, subtitle converter, visibility converters |
| `Theme.cs` / `App.xaml` | Palettes, type sizes, hat geometry |
| `Config.cs` | `AppearanceConfig` |
| `SearchEngine.cs` | Query routing, empty-state, result DTOs |
| `FuzzyMatcher.cs` | Scores; needs span export for item 1 |
| `Models.cs` | `ResultItem`, `ResultAction` |
| `IconExtractor.cs` / `Glyphs.cs` | Icons |
| `LargeTypeWindow.cs` | Calculator-only overlay; hard-coded dark colours |
| `FileIndexer.cs` | Internal file index; Everything/WSearch would sit beside it |
| `App.xaml.cs` | Tray, balloons, single-instance, `Notifier` |
| `SettingsWindow.xaml` / `.xaml.cs` | Nine sections, no search, emoji sidebar |
| `KeyboardHook.cs` | Summon only; future snippet expansion / clipboard hotkey |
| `tools/make-icon.ps1` + `Assets/winfred.ico` | Mark |

Previous run (merged): [Alfred UI/logo design](https://cursor.com/agents/bc-65d65915-2726-4ded-8458-31367f9c06dd).
This handoff: [Winfred alfred ui gap](https://cursor.com/agents/bc-03464ec9-ff6b-4eb3-b3f3-8dd3a278628c).
