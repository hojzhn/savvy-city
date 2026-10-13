# You Will Date

A text-based dating sim. Every person the player can meet is a **pawn**: a
simulated person with a family, a life history, and adult traits that grow out
of that history. This repository holds the pawn generator, its data tables, and
a Godot debug app for inspecting and tuning pawns.

The game itself (the player, dates, dialogue) is not built yet.

## Quick start

Requirements: the .NET 8 SDK and Godot 4 (.NET edition).

```powershell
winget install --id Microsoft.DotNet.SDK.8 -e
winget install --id GodotEngine.GodotEngine.Mono -e
```

| What | Command |
| ----- | ----- |
| Run the debug app | `.\run.ps1` (or double-click `run.cmd`) |
| Run with a console for logs | `.\run.ps1 -Console` |
| Open the Godot editor | `.\run.ps1 -Editor` |
| Print a pawn | `dotnet run --project Tools/PawnCli -- 42` |
| Run the tests | `dotnet test` |

## How a pawn is made

Each pawn is generated in three parts, and each part reads only from the parts
before it:

1. **Starting point** (spec steps 1–3): birth year, birthplace, sex,
   temperament, the world it is born into, and its family and household.
2. **Life** (spec sections 4–5): a year-by-year simulation from birth to the
   pawn's current age. Events happen, choices are made, and the pawn reads each
   event in a way that depends on its temperament.
3. **Adult traits** (spec section 6): personality, attachment, physical health,
   beliefs and politics, sexuality, wants, inner life, social circle and mental
   health, computed from everything above.

The same seed and data files always produce the same pawn.

## Repository layout

| Path | Contents |
| ----- | ----- |
| [Data/](Data/) | YAML data tables that define every trait and rule. Designers edit these. |
| [Sim/](Sim/) | The generator: a plain C# library with no Godot code. |
| [Sim.Tests/](Sim.Tests/) | Unit, invariant and population tests. |
| [Game/](Game/) | Godot project: the debug app (pawn inspector and Traits tab). |
| [Tools/PawnCli/](Tools/PawnCli/) | Command-line tool for printing pawns and population statistics. |
| [docs/](docs/) | Design spec and project documentation. |

## Documentation

| Document | What it covers |
| ----- | ----- |
| [docs/spec/you-will-date.md](docs/spec/you-will-date.md) | The original design spec (also as .docx). |
| [docs/spec/social-circle-and-health.md](docs/spec/social-circle-and-health.md) | Spec addition: social circle, physical health, adult mental health. |
| [docs/architecture.md](docs/architecture.md) | How the generator works: the pipeline, facts, determinism, validation. |
| [docs/data-files.md](docs/data-files.md) | Reference for every YAML data file. |
| [docs/tools.md](docs/tools.md) | The debug app, the CLI, scripts and tests. |
| [docs/decisions.md](docs/decisions.md) | Design decisions and where the build departs from the spec. |
| [docs/status.md](docs/status.md) | What is built, what is placeholder, and what comes next. |
