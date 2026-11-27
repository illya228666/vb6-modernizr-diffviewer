# Modernizr DiffViewer

Modernizr DiffViewer is a read-only Windows viewer for a single file in a local Git worktree. It opens from the Staged and Unstaged lists in VB6 Modernizr and can also run directly from the command line.

| Mode | Left | Right |
| --- | --- | --- |
| Staged | HEAD | INDEX |
| Unstaged | INDEX | WORKING TREE |
| Uncommitted | HEAD | WORKING TREE |

WORKING TREE means the saved file on disk. Save a file in VB6 to include its editor changes. An untracked file has an empty comparison baseline. The viewer labels absent and empty versions separately.

## Build and run

On Windows, install the .NET 10 SDK and a current Git for Windows with `git.exe` on PATH. From this repository:

```powershell
dotnet test -c Release
.\publish.ps1
```

The script publishes a self-contained version of the application to `artifacts/publish/win-x64`. Use `-Runtime win-arm64` or `-Runtime win-x86` to target another Windows architecture. Keep the whole publish directory together. The published app requires Git and can run without an installed .NET runtime or VB6.

```powershell
.\artifacts\publish\win-x64\Modernizr.DiffViewer.exe --repo "C:\VB Projects\Demo" --file "Forms\Überblick.frm" --mode unstaged
```

`--repo` takes the absolute Git worktree root, including a UNC path. Git's normal repository trust checks apply. `--file` takes the exact repository-relative Git path with forward or backslashes. `--mode` takes `staged`, `unstaged` or `uncommitted`. Use `--encoding windows-1251` or `--encoding 1251` to set the fallback encoding for one launch.

View modes share the same change groups. Use **Next** or **F7** and **Previous** or **Shift+F7** to navigate. **Hide unchanged** keeps three context lines at each end of a folded section. The bottom scrollbar pans long lines while the line numbers stay visible.

## Embedded in VB6

The VB6 add-in launches the same executable with `--format inline-rtf` to render a comparison in the IDE. The process writes a UTF-8 response to `stdout` and diagnostics to `stderr`. Successful output starts with three LF-terminated lines:

1. `MODERNIZR-INLINE-RTF-1`
2. `light` or `dark`
3. A comma-separated list of zero-based UTF-16 positions for the starts of changed groups, or an empty line.

An ASCII RTF document follows. The offsets count each Rich Edit paragraph separator as one character. The export and the standalone window share change grouping and line order. An export can be run by adding `--format inline-rtf` to the standalone command and redirecting `stdout`.

DiffViewer 0.3.0 and later also accept `--format editor-hunks`. This export compares the code visible in VB6 after omitting the designer header and hidden attributes. Modernizr uses it to place Git markers and changed-word underlines in the active editor and show previous lines on demand.

With DiffViewer 0.5.0 or later, the editor uses `--mode uncommitted` so staging leaves its comparison intact. An unborn HEAD gives this mode an empty baseline.

In VB6 modules, diff views ignore capitalization in code and numeric labels at the start of a line, such as `105    Debug.Print value` and `105: Debug.Print value`. They compare string literals, comments, branch target numbers and designer metadata as written. The displayed lines keep their original capitalization and numbering.

The UTF-8 export starts with `MODERNIZR-EDITOR-HUNKS-1`, the theme and the hunk count on separate LF-terminated lines. Each hunk starts with four tab-separated counts for preceding context, new rows, following context and removed rows. The context rows are plain text. Each new row starts with a comma-separated list of zero-based UTF-16 `offset:length` spans and a tab. A single line of ASCII RTF containing the previous text completes the hunk.

## Configuration and limits

Optional defaults go in `%LOCALAPPDATA%\Modernizr\DiffViewer\settings.json`.

```json
{
  "fallbackEncoding": "windows-1251",
  "theme": "dark"
}
```

The decoder checks each version for a BOM, then strict UTF-8, then the selected fallback or the Windows ANSI code page. The standalone window shows line endings and final newline state in the version headers. A pure encoding or line-ending change has matching visible lines and an explanatory status. The viewer compares raw blob and disk contents. Repository filters and text conversion are outside that comparison.

The viewer recognizes staged renames when Git finds a matching source. Unstaged filesystem moves appear as deletion and addition. Binary resources, unresolved index stages, links and submodules display a message. Each side is limited to 4 MiB, 40,000 lines and 65,536 characters per line. Very long modified lines keep line highlighting and skip word highlighting.

## Design

The core reads Git objects and working tree bytes using Git CLI, decodes each side independently and delegates line and word differencing to DiffPlex. The WPF client draws both sides in one virtualized scrolling list and reuses the alignment model for inline view. The CLI and the core have no dependency on the VB6 add-in.

[DiffPlex](https://github.com/mmanela/diffplex) provides the Apache-2.0 diff engine. Its WPF controls were evaluated alongside [DiffViewer](https://github.com/geevensingh/DiffViewer), whose separate CLI, read, diff and navigation layers informed the design. [Ähnlich](https://github.com/Dirkster99/Aehnlich) informed the layout and theme review. [AvalonEdit](https://github.com/icsharpcode/AvalonEdit), [side-by-side-diffs](https://github.com/shiftkey/side-by-side-diffs) and [Diff.Net](https://github.com/menees/Diff.Net) were reviewed as alternatives.

The module is licensed under GPLv3. See [LICENSE](LICENSE).
## Editor merge

Modernizr uses `--editor-merge "<job path>" "<display path>" <owner HWND>`
to review external file changes against an unsaved VB6 buffer. The job
path has three input files with suffixes `.base`, `.editor` and `.disk`.
Each contains CRLF editor text as UTF-16LE without a BOM. The window
combines independent edits and requires an explicit choice for each
conflict.

On Apply, the viewer writes the chosen text to the job path with a
`MODERNIZR-MERGE-1` header followed by CRLF. Exit codes are 0 for Apply,
2 for Cancel and 1 on failure. The VB6 add-in owns the input snapshots,
checks that they still match and updates its editor buffer. The helper
only receives temporary files for this mode.
