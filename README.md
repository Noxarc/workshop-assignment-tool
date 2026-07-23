# Workshop Assignment

Optimally assigns people to workshops based on their preference lists — respecting capacity limits, friend requests, and timeslot constraints.

## Features

- Constraint-based optimization for best-possible assignments
- Excel import/export with downloadable template
- PDF and Excel report generation (workshop leader + attendee reports)
- Bilingual interface (German / English)
- Friend group support — keep people together
- Flexible timeslots: full-day, half-day, or custom naming
- Configurable solver runtime (30 sec – 60 min)
- Cross-platform: macOS (Apple Silicon + Intel) and Windows

## Getting Started

Download the latest release for your platform from the [Releases](../../releases) page.

**Windows:** Double-click the `.exe`. SmartScreen may warn on first launch — click "More info", then "Run anyway".

**macOS (Apple Silicon):** Unzip, right-click the app, select "Open", confirm the security dialog. After that, normal double-click works.

**macOS (Intel):** Same as Apple Silicon, use the `osx-x64` download.

> Not sure which Mac you have? Apple menu → "About This Mac". Shows "Apple M…" = Apple Silicon. Shows "Intel…" = Intel.

## How It Works

1. **Get Template** — Download an Excel template with the correct column headers
2. **Import** — Load your filled-in Excel file (or click "Load Test Data" to try it out)
3. **Configure** — Adjust timeslot names, solver runtime, and export format in Settings
4. **Assign** — Run the optimizer. A progress bar shows the current status.
5. **Export** — Generate two reports: a workshop leader overview with statistics and attendance lists, and an attendee report in the same format as the import file

## Tech Stack

- [.NET 10](https://dotnet.microsoft.com/) + [Avalonia 11](https://avaloniaui.net/) (cross-platform UI)
- [Google OR-Tools](https://developers.google.com/optimization) (constraint optimization solver)
- [ClosedXML](https://github.com/ClosedXML/ClosedXML) (Excel I/O)
- [QuestPDF](https://www.questpdf.com/) (PDF generation)

## License

[MIT](LICENSE)
