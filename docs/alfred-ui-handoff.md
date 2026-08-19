# Alfred UI gap — handoff

Winfred is an Alfred-style launcher for Windows. PR
[#1](https://github.com/hollywoodj/winfred/pull/1) (`be38d73`) closed the first
round of search-results gaps: numbered shortcuts, real shell icons, bowler-hat
mark, empty-query chrome, type-ahead, modifier-key subtitles, and a preferences
cog.

This note is for the next agent. It lists **ten remaining UI matches** against
Alfred, in the order they most change how the launcher *looks and feels*, plus
what is already done so those changes are not re-implemented or undone.

Visual QA has to happen on **Windows**. This cloud environment is Linux and
cannot run the WPF app. Rebuild with `.\build.ps1` and use Settings → General →
**Preview launcher**.

Official Alfred references (do not copy trademarked artwork):

- [Appearance & Theming](https://www.alfredapp.com/help/appearance/)
- [Cheatsheet](https://www.alfredapp.com/help/getting-started/cheatsheet/)

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

## Stretch (after the ten)

- Rounded 4px clip on result icons (WPF `Clip` on a `Border`; skipped in PR #1).
- Bookmark favicons (local Chromium `Favicons` SQLite; no network).
- Store/UWP icons via `Windows.ApplicationModel` instead of the generic app
  glyph when `IconExtractor` misses.
- Drag a file result out of the list (`DoDragDrop`).
- Search-in-progress spinner for Evernote / 1Password async rows.
- Large Type follows the active theme (today it hard-codes dark colours).
- `Glyphs.Zip` (`U+F012`) may render as a missing-glyph box on older Segoe
  MDL2; prefer a documented fallback.
- Full theme editor (fonts, paddings, every colour) — Alfred Powerpack
  territory; not required to *feel* like Alfred.

---

## Suggested implementation order

If the next change is a single PR, do **2 + 3 + 10** first: Appearance
toggles, metrics/selection, and selected-row chrome. That is visible in a
screenshot without new windows.

Then **1** (highlighting) and **9** (light/system theme).

Then **5** (motion), **4** (blur — needs a Windows box), **8** (empty-state
opt-in).

Leave **6** and **7** as their own PRs; they add surfaces and activation-edge
cases (`Deactivated` vs owned child windows).

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
- Any new overlay (Quick Look, actions, Large Type) must not dismiss the
  launcher via `Window_Deactivated`.

---

## File map

| Path | Role |
| --- | --- |
| `MainWindow.xaml` / `.xaml.cs` | Launcher chrome, keys, type-ahead |
| `LauncherUi.cs` | Modifier state, subtitle converter, visibility converters |
| `Theme.cs` / `App.xaml` | Palettes, type sizes, hat geometry |
| `Config.cs` | `AppearanceConfig` |
| `SettingsWindow.xaml` / `.xaml.cs` | General → Appearance |
| `SearchEngine.cs` | Query routing, empty-state, result DTOs |
| `FuzzyMatcher.cs` | Scores; needs span export for item 1 |
| `Models.cs` | `ResultItem`, `ResultAction` |
| `IconExtractor.cs` / `Glyphs.cs` | Icons |
| `LargeTypeWindow.cs` | Calculator overlay; should track theme |
| `tools/make-icon.ps1` + `Assets/winfred.ico` | Mark |

Previous run (merged): [Alfred UI/logo design](https://cursor.com/agents/bc-65d65915-2726-4ded-8458-31367f9c06dd).
