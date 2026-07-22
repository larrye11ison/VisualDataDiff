# VisualDataDiff

**A fast, keyboard-driven, side-by-side data comparison tool for spreadsheets.**

Load two tabular data sources, match rows by key column(s), and see exactly what's different, what's missing, and where — rendered in two synchronized, virtualized grids that stay perfectly aligned even when the two sides don't have identical column widths, row counts, or row order.

![.NET 10](https://img.shields.io/badge/.NET-10-512BD4?logo=dotnet&logoColor=white)
![Avalonia UI](https://img.shields.io/badge/Avalonia_UI-12-8A2BE2)
![Platform](https://img.shields.io/badge/platform-Windows%20%7C%20macOS%20%7C%20Linux-informational)

---

## Why

Diffing two spreadsheets by eye is tedious and error-prone — rows get reordered, a handful of columns actually matter, and "different" isn't always what it looks like (trailing whitespace, case differences, reformatted numbers). VisualDataDiff handles the matching and highlighting so you can focus on what actually changed.

## Features

### Smart comparison
- **Key-based row matching** — designate one or more columns as the match key; rows are paired up regardless of order, with duplicates handled via first-in/first-out matching per key.
- **Per-column comparison rules** — mark columns as `Key`, `Ignored`, or `Normal`, independently toggle case-sensitivity and leading/trailing whitespace trimming.
- **Orphan detection** — rows that exist on only one side are clearly flagged rather than silently dropped or misaligned.

### Built to actually look at
- **Two synchronized grids** — scroll one side, the other follows. Column widths are computed once from *both* sides' content and shared between the grids, so column *N* lines up pixel-for-pixel no matter how different the underlying data is.
- **Consistent highlighting** — real differences, missing-side placeholders (hatched pattern), and identical cells are all styled from one central place, so the visual language stays consistent as the app evolves.
- **Row and column visibility filters** — show only rows with differences, only orphans, only left/right orphans; hide columns that are identical (optionally except key columns) or explicitly ignored.
- **Excel-style active cell navigation** — click a cell or arrow around with the keyboard (`↑ ↓ ← →`, `Home`, `End`, `Page Up`, `Page Down`) and the active cell is mirrored across both grids, auto-scrolling either grid as needed to keep it in view.

### Pivoted row view
Need to inspect one record closely instead of scanning a wide table? Double-click a row (or hit `Enter` with a cell focused) to flip that row 90 degrees into a full-window, column-per-line view with left/right values side by side. By default it only shows columns that actually differ plus your key columns — flip a switch to see everything. Navigate to the next/previous row with on-screen buttons or `↑`/`↓`, optionally skipping orphan rows so you only stop on rows that exist on both sides. `Esc` (or the row/column filters) puts you right back where you left off, focus and all.

### Performance
Tested comfortably with datasets in the thousands of rows and dozens of columns. Grid virtualization uses a measured, fixed row height so the UI stays responsive regardless of dataset size, and collection updates are batched to avoid the classic "thousands of individual UI notifications" slowdown.

## Getting started

**Prerequisites:** [.NET 10 SDK](https://dotnet.microsoft.com/download)

```bash
git clone https://github.com/larrye11ison/VisualDataDiff.git
cd VisualDataDiff
dotnet run
```

### Basic workflow

1. Pick a **Source Type** and **Setup Source** for both the Left and Right panes (currently supports Excel `.xls`/`.xlsx`, with a header-row toggle).
2. Hit **Load + Compare**.
3. Click a column to configure its role (`Key`/`Ignored`/`Normal`) and comparison rules, then **Recompare with current column options** — no need to reload the files.
4. Use the **Row Visibility** and **Column Visibility** controls to zero in on what matters.
5. Double-click any row for a focused, pivoted side-by-side view of just that record.

## Architecture

A straightforward MVVM app with a pluggable data-source abstraction — Excel is the first implementation, but the diff engine and UI don't know or care where rows come from.

```
Models/          Plain data types: TabularDataSet, DiffResult, DiffRow/DiffCell, comparison rules
Services/        ITabularDataSource + ExcelTabularDataSource, IDataDiffEngine + DataDiffEngine
ViewModels/      MainWindowViewModel and friends (CommunityToolkit.Mvvm)
Views/           MainWindow.axaml — the two-grid layout, pivot overlay, and TreeDataGrid wiring
Utilities/       Shared helpers (cell-state factory, batched observable collection, column naming)
```

Adding a new source type (CSV, a database query, an API) means implementing `ITabularDataSource` — the comparison engine and UI work unchanged.

## Tech stack

- [Avalonia UI](https://avaloniaui.net/) 12 — cross-platform .NET UI framework
- [Avalonia.Controls.TreeDataGrid](https://github.com/AvaloniaUI/Avalonia.Controls.TreeDataGrid) — virtualized grid control
- [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet) — MVVM source generators
- [ExcelDataReader](https://github.com/ExcelDataReader/ExcelDataReader) — `.xls`/`.xlsx` parsing
- .NET 10
