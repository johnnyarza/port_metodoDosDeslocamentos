# Validation frames

Twelve frames for checking this solver against another analysis program. For each one:

- `NN-name.frame.json`: open it in the app (**Open**, or `FrameAnalysis.exe path\to\file.frame.json`) and press **Solve**.
- `NN-name.results.md`: the model's inputs (to enter in the other program) and this solver's results for every load
  case and combination: node displacements, reactions, and N, V, M and the deflection along each member.

All frames use W250x49 (A = 6260 mm², Ix = 70.6 × 10⁶ mm⁴, Iy = 15.1 × 10⁶ mm⁴) and E = 200 000 MPa.

| # | Frame | What it checks |
| --- | --- | --- |
| 01 | Simply supported beam, uniform load | Closed form (wL²/8, 5wL⁴/384EI); pin + vertical roller |
| 02 | Cantilever: tip force, tip moment, uniform load | Closed form; nodal moment; a combination |
| 03 | Propped cantilever, uniform load | Closed form (3wL/8, wL²/8, 9wL²/128) |
| 04 | Continuous beam, 3 spans, pattern live load | Statically indeterminate beam; a member drawn right to left |
| 05 | Portal, fixed bases: gravity and lateral | Frame action, sway; four combinations |
| 06 | Portal, pinned bases: same loads | Same as 05 with pins (larger sway, no base moments) |
| 07 | Gable frame, fixed bases | Inclined rafters; global loads on inclined members; uplift |
| 08 | Two-storey, two-bay frame | Bigger frame; minor-axis columns (Iy); pattern load |
| 09 | Portal with a rigid diagonal, pinned bases | Inclined member carrying axial force |
| 10 | Inclined member, pin + horizontal-reaction roller | Horizontal roller; load per metre of member length |
| 11 | Portal under self-weight only | Self-weight 0.481 kN/m along every member, columns included |
| 12 | Point load at mid-span (beam split by a node) | Closed form (PL/4, PL³/48EI) |

## Setting up the other program to match

- **Shear deformation off.** This solver uses Euler–Bernoulli members (bending and axial deformation only). Programs
  that include shear deformation by default (through a shear area) will give slightly larger deflections.
- **Axial deformation on.** Columns shorten here; programs or options that treat members as axially rigid will differ
  slightly in sway and in force distribution.
- **First-order (linear) analysis.** No P-Δ, no notional loads, no rigid end offsets, no automatic self-weight except in
  sample 11.
- **Distributed loads are per metre of member length** and in global directions. On inclined members (samples 07, 09,
  10) a program that takes gravity loads per metre of horizontal projection needs them scaled by L/Lₓ (the notes give the
  numbers).
- **Combinations are plain factored sums** of the load cases, as listed in each sheet.

## Reading the signs

- Global axes: +X right, +Y up, moments counter-clockwise positive. Gravity loads are negative.
- Reactions: forces from the supports on the frame, global axes.
- Internal forces at x along a member (x from node I), on the part towards node I: N tension positive; M positive when
  it stretches the face on the right-hand side of I → J (sagging for a beam drawn left to right; for a column drawn
  upwards, tension on its right face); V = dM/dx. A member drawn right to left (member 2 in sample 04, member 4 in
  sample 07) therefore shows sagging as negative. Compare magnitudes and which face is in tension rather than raw signs.
- "v across (mm)" is the displacement perpendicular to the member at that point (positive towards the left-hand side
  of I → J).

## Regenerating

The frames and sheets are produced by `tools/MakeSamples` from the current solver:

```
dotnet run --project tools/MakeSamples
```

The test `SampleFileTests` checks that every file here still loads and solves.
