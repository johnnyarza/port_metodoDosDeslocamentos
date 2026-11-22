# Frame Analysis (port of Método dos Deslocamentos)

C# port of the plane-frame displacement method from the 2020 Delphi thesis app, to be integrated later into
WBeamS16App (a copy lives in `refDocs/WBeamS16App` for reference; same stack: .NET 10, WPF-UI 4.3.0,
CommunityToolkit.Mvvm 8.4.2, xUnit). Background and the original's bugs: `refDocs/PORTING_GUIDE_Metodo_Deslocamentos.md`.
The Delphi sources (`refDocs/Projects`) are Windows-1252.

## Layout
- `src/FrameAnalysis.Core`: model, solver, NBCC combinations, JSON. No dependencies, no UI, no database.
  `Drawing/FramePlot` gives drawing geometry in model coordinates (diagram ordinates, deformed shape, summed loads,
  sign runs) so it can be tested and reused by any UI.
- `src/FrameAnalysis.App`: minimal WPF test UI (one window) to enter data and see results. Not the final UI.
  `Views/FrameView` is the frame drawing (a `FrameworkElement` painting in `OnRender`): frame, supports, loads,
  deformed shape, N/V/M, reactions; wheel zoom, drag pan, double-click fit, click picks a node or member. It is meant
  to move into WBeamS16App with the editor. `FrameAnalysis.exe file.frame.json` opens a file.
- `tests/FrameAnalysis.Tests`: xUnit. `dotnet build FrameAnalysis.slnx`, `dotnet test tests/FrameAnalysis.Tests`.
- `samples/validation`: 12 frames (`.frame.json`) with result sheets (`.results.md`) for the user's cross-check against
  another program; see its README. Made by `tools/MakeSamples` (`dotnet run --project tools/MakeSamples`); rerun it
  after any solver change. `SampleFileTests` checks they load and solve.

## Planned integration (user's workflow)
User creates a task in WBeamS16App → picks the section → clicks a button → this frame editor opens with the task's
section and steel grade → user enters nodes, members and loads → user looks at all results and picks one member and one
result (load case or combination) → `S16DesignForces` fills the task (Tf/Cf, Vf, Mfx or Mfy, ω2's Mmax/Ma/Mb/Mc,
ω1's Mi/Mj and curvature). WBeamS16App receives the values without sign. The frame would be stored as JSON with the task.

## Decisions (from the user, 2026-11-22)
- **One section and one material for the whole frame**, from the task (`FrameModel.Section`/`Material`, handbook
  units: mm², 10⁶ mm⁴, kN/m, MPa). `FrameSolver.Analyze` takes an optional per-member properties function, used by
  the tests (thesis frame has two sections) and available if per-member sections are ever wanted.
- **Each member has a `BendingAxis`** (Major = Ix, web in the frame plane; Minor = Iy), because forces are referenced to
  the global X–Y plane.
- **Self-weight** (section dead load, downward per m of member) is on by default in the D case and can be switched off
  (`LoadCase.IncludeSelfWeight`, D cases only, at most one case).
- **Loads are global only.** Thesis sign convention: +Y up, so gravity is negative (Qy = −12). Uniform loads are per m of
  member length, full length only.
- **Supports:** free, pin, fixed, roller with vertical reaction, roller with horizontal reaction. No inclined rollers.
- **NBCC combinations are an editable table** (`LoadCombination.Factors` per load case), seeded by
  `NbccCombinations.CreateDefaults` with ULS (Table 4.1.3.2.-A without E, 1.25D and 0.9D variants) and SLS. D, L and S
  cases add up; W cases are alternatives. Only D, L, S, W for now. Each combination has a Run tick
  (`LoadCombination.IsActive`, saved, default true for older files); only ticked ones are solved, load cases always are.
  `ResultSummary` gives each result's largest T, C, V, M and displacement with where they occur ("Combination results" tab).
- **Deleting a node is blocked while a member uses it** (its point loads are deleted with it). Deleting a member deletes
  its loads; deleting a load case deletes its loads and factors.
- **Storage is JSON**; the links Firebird enforced are enforced by `FrameModel` edits and `FrameModel.Validate()`
  (run on load, save and solve). Ids need not be contiguous; new ids are max + 1.
- **Units:** internally N, mm, MPa (WBeamS16App's units). User-facing: m, kN, kN/m, kN·m; displacements mm and rad.

## Sign conventions
- Member-end actions (`MemberResult.EndForces`): forces on the member in member axes (Gere & Weaver, as the original's
  AM_out); counter-clockwise moments positive. Local x runs from node I to J.
- Internal forces (`ForcesAt(x)`), taken on the part towards node I as in the original ("vista à esquerda"):
  N tension positive, M positive when it stretches the local −y face (sagging for a beam drawn left to right).
  **V = dM/dx**: this differs from the original, whose mid-span shear had the opposite sign for horizontal members and
  orientation-specific flips for vertical ones; one rule now applies to every member. A beam drawn right to left gets
  negative sagging moments in member axes; `S16DesignForces` drops the signs anyway.
- Reactions: forces from the support on the structure, global axes.
- Diagrams: M is drawn on the tension side (towards local −y when positive); N and V towards local +y when positive.
  Blue = positive, red = negative in member axes, so a member drawn right to left shows sagging in red.

## Verification
- Closed-form tests: fixed-fixed (guide Case A), simply supported, cantilever, axial loads, inclined rafter.
- Thesis portal frame (guide Case B) against the guide's numpy values. **The guide's u₃ = 4.2e−6 m is inconsistent**
  with its own beam force (by symmetry u₃ = N·L/2EA = 3.52e−6 m); the test checks that identity instead.
- Rotating a frame and its loads must not change member-axis results (covers the original's inclined-member bugs).
- The user cross-checks forces with another program using `samples/validation`; no Delphi golden values are available.

## Status (2026-11-22) and next steps
- **Cross-check in progress (step 1 of the plan):** the user confirmed samples **01 and 02** match their other
  program; 03–12 still to check. When they report a difference, trace it with the sample, case, member and value.
- **Agreed order after the cross-check:**
  2. Gaps that matter for steel frames: member end releases (hinges) first (simple shear connections; today every
     joint is rigid), then point loads within a span, then partial/trapezoidal loads.
  3. S16 second-order effects (U2 / notional loads / P-Δ): needs the user's decision on the approach; this solver is
     first-order.
  4. Integration into WBeamS16App (button on the task, frame stored as JSON with the task, picked member and result
     fill the task's boxes).
  5. Smaller: adding nodes and members by clicking in the drawing, printing/exporting.
- Also not done: earthquake, settlements, temperature.
- `refDocs/Projects` (Delphi sources, 103 MB) and `refDocs/WBeamS16App` (copy of the other repo) are git-ignored and
  exist only on the original computer; the porting guide is committed. Get WBeamS16App from its own repo if needed.
- UI checks were done by screenshots driven through UI Automation (a PowerShell script in the session scratchpad,
  not kept); the user runs the app with F5 (`.vscode/launch.json`).
