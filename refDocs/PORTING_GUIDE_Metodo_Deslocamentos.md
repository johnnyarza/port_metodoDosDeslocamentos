# Porting Guide: Displacement Method (Delphi → C#)

Handoff notes for porting the **plane-frame displacement method** (Método dos Deslocamentos) from Johnny's 2020 master's thesis app (Delphi + Firebird) to C#. The C# code will be integrated into another app that uses **local SQLite** (user note: "SQLite 10.0.14"; this is probably the `Microsoft.Data.Sqlite` / EF Core 10.0.x package, which would mean .NET 10. Confirm on the target PC).

---

## 1. Scope

**Port:**
- Linear-elastic analysis of a **2D frame** (3 DOF per node: u, v, θ) with nodal loads and uniformly distributed member loads.
- Outputs: nodal displacements, support reactions, member end forces, and internal forces/displacements at mid-span.

**Do not port:**
- Serviceability limit-state (ELS) calculations, ψ1/ψ2 load combinations, and Brazilian code (NBR 6118) checks. The user will design with the resulting forces under the **Canadian code**.
- Genetic algorithm, total search, cost functions, VCL forms, Firebird data module.

---

## 2. Source files (in `Projects.zip` → `Projects/`)

Files are **Windows-1252 (ANSI)** encoded with CRLF line endings. Open them as cp1252 or the accents in comments will show up as garbage.

| File | Status | Notes |
|---|---|---|
| `Unit_calculo_esforços.pas` | ✅ **PORT THIS ONE** (live code) | `procedure metodo_deslocamentos(Iz, Ax, D_out, AM_out, AMM_out, DM_out)`; body at lines ~259–850 |
| `Unit_calculo_esforços_ELS.pas` | ❌ Ignore | Same solver (about 27 lines differ). Only adds ψ factors on the variable loads via `tipo_els` |
| `Metodo_dos_deslocamentos.pas` | ❌ Ignore (old version) | Listed in `uses` but **never called**. Useful only as a readable reference; it has the same algorithm with older output layouts |
| `Metodo_dos_deslocamentos (Cópia em conflito…).pas` | ❌ Ignore | OneDrive conflict copy, older still |
| `Unit_DMDados.pas/.dfm` | Reference only | Firebird datasets/SQL (table names, field order) |
| `Unit_Menu_Otimo_AG.pas` | Reference only | The caller. See `p_Iz_Ax_porticos` (~line 930) for how `Iz`/`Ax` are built and line ~4817 for the call |
| `Unit_Menu_Ins_coord.pas`, `Unit_estrutura_inserir_barra.pas`, `Unit_menu_inserir_carregamento_no.pas`, `Unit_Menu_inserir_carregamento_barra.pas` | Reference only | Data entry; they confirm the field order of each table |

**External dependency:** `uDynLinAlg, uDynArrays, uMatTypes, uDynObjAlg` (a third-party Delphi matrix library, **not in the zip**). The only function used is `fastinverse(S_Sub, DET)`, a full matrix inversion. In C#, **replace it with a linear solve** (Cholesky, since K is symmetric positive-definite) rather than an inverse.

The variable names and step labels (`SCM1..4`, `SMD`, `RL`, `CRL`, `AML`, `AE`, `AC`, `AR`, steps "2.A.3…5.B.2") appear to follow the plane-frame program in **Gere & Weaver, *Matrix Analysis of Framed Structures***. That book is a good reference for the derivation. The thesis PDF only mentions that the method was used; it does not derive it.

---

## 3. Current data model (Firebird)

The solver reads the database directly through `DMDados` client datasets, **by field index**. Field order:

**`NOS`** (nodes), `SELECT * FROM NOS ORDER BY I`

| idx | field | meaning |
|---|---|---|
| 0 | I | node number (1-based, **must be contiguous 1..NJ**) |
| 1 | X | m |
| 2 | Y | m |
| 3 | RESTX | 1 = restrained, 0 = free |
| 4 | RESTY | 1/0 |
| 5 | RESTZ | 1/0 (rotation) |

**`BARRAS`** (members), `SELECT * FROM BARRAS ORDER BY N`

| idx | field | meaning |
|---|---|---|
| 0 | N | member number (1-based, **contiguous 1..M**) |
| 1 | J | start node |
| 2 | K | end node |
| … | TIPO | 'VIGA' (beam) / 'PILAR' (column), not used by the solver |

**`CARREGAMENTO_NO`** (nodal loads), `ORDER BY I`

| idx | field | meaning |
|---|---|---|
| 0 | I | node |
| 1,2,3 | FX, FY, MZ | permanent (dead) load, global axes |
| 4,5,6 | FX_AC, FY_AC, MZ_AC | variable ("acidental", live) load |

**`CARREGAMENTO_BARRA`** (member loads), `ORDER BY N`

| idx | field | meaning |
|---|---|---|
| 0 | N | member |
| 1 | QY | permanent distributed load, kN/m |
| 2 | QX | permanent distributed load, kN/m |
| 3 | QY_AC | variable load |
| 4 | QX_AC | variable load |

**`CONFIG`**: `Fields[9]` = `MOD_ELASTICIDADE` (MPa). The solver uses `E = value * 1000` (kPa), a single E for the whole frame.

**Section properties** are passed in rather than read from the database: `Ax[i] = b*h`, `Iz[i] = b*h³/12` (b, h stored in cm and divided by 100 to get m). Gross rectangular section only. In the old app the genetic algorithm chose the sizes.

> ✅ **Decision for the port:** **the user provides the material properties and member sizes.** Drop the `CONFIG` E lookup and the GA-driven `Iz`/`Ax` arrays. Each member gets its properties from user input (see §7).

> ⚠️ The live ELU solver **adds permanent and variable loads together without factors** (`FX + FX_AC`, `QY + QY_AC`). For the Canadian code, do **not** keep this. See §7.

---

## 3a. Input workflow (keep this order in the new app)

The original app had the user build the model in four steps. Keep the same flow; each step depends on the one before it.

**Step 1: Nodes**
- The user enters the coordinates (x, y) and picks a support type for the node.
- Original storage: three 0/1 flags `RESTX, RESTY, RESTZ` (set in `Unit_Menu_Ins_coord.pas`). Node numbers were assigned automatically as `MAX(I)+1`.
- Port: store a `SupportType` enum and derive the flags from it:

  | SupportType | RestX | RestY | RestRz | Restrained DOFs |
  |---|---|---|---|---|
  | Free | 0 | 0 | 0 | 0 |
  | Pin | 1 | 1 | 0 | 2 |
  | Roller (vertical reaction) | 0 | 1 | 0 | 1 |
  | Roller (horizontal reaction) | 1 | 0 | 0 | 1 |
  | Fixed | 1 | 1 | 1 | 3 |

  Confirm which roller direction(s) the UI should offer. Keeping the three raw flags in the table as well (or deriving them) makes the solver independent of the enum.
- Validation: there must be no two nodes at the same coordinates (within a tolerance).

**Step 2: Members**
- The user picks `node_i` and `node_j` from the existing nodes, plus the material and section (E, A, Iz).
- Original: `Unit_estrutura_inserir_barra.pas` showed combo boxes filled from `NOS`, assigned the member number automatically, and stored `TIPO` (VIGA/PILAR) from the orientation.
- Validation:
  - `node_i ≠ node_j`
  - both nodes exist
  - length > 0
  - no duplicate member between the same two nodes (see bug 6 in §6)
  - E, A, Iz > 0
- Member direction matters: local x runs from node_i to node_j. This decides the sign of local results and, in the old code, the direction of local distributed loads (§4). Suggestion: for horizontal members, normalize to left → right, or make loads global so the direction doesn't matter.

**Step 3: Loads**
- **Point loads on nodes:** Fx, Fy, Mz in global axes. The node must exist. Original table: `CARREGAMENTO_NO`.
- **Distributed loads on members:** a uniform load qx, qy over the full length. The member must exist. Original table: `CARREGAMENTO_BARRA`, one record per member.
- Port: tag every load with a **load case** (D, L, S, W, …) so the Canadian-code combinations can be made by superposition (§7). Allow several loads per member/node per case, and sum them.
- Not supported in the original (possible future additions): point loads within a member's span, partial or trapezoidal loads, support settlements, temperature.

**Step 4: Analyse**
Before solving, check the model:
- at least one support
- every node connected to a member
- every member has a section
- the structure is stable: Cholesky fails or the pivot is near zero → report "mechanism / unstable structure". The original app just caught the exception and showed "Erro ao calcular a estrutura".

**Editing and deleting:** deleting a node must delete (or block on) the members and loads attached to it, and deleting a member must delete its loads. The original had separate edit forms (`Unit_menu_editar_barra.pas`, `Unit_Menu_editar_carregamento_no.pas`, `unit_menu_editar_carregamento_barra.pas`). In SQLite, use foreign keys with `ON DELETE CASCADE` or `RESTRICT`, and note that SQLite needs `PRAGMA foreign_keys = ON` for each connection.

**Numbering:** the original solver required contiguous IDs 1..NJ and 1..M. After a deletion this would break. In the port, map IDs to solver indices (§7) so gaps are fine.

---

## 4. Units and sign conventions

- **Units:** kN, m, kPa (E in MPa × 1000). Displacements come out in m and rad, forces in kN, and moments in kN·m.
- **Global axes:** X right, Y up, θ counter-clockwise positive. DOF order per node: `[u, v, θ]`. Global index for node `n` (1-based): `3n-3, 3n-2, 3n-1`.
- **Member local axes:** x from J to K. `CX = (xK−xJ)/L`, `CY = (yK−yJ)/L`.
- **Distributed loads:** the code uses `QYI = −QY`, and the fixed-end actions are `AML = QYI·L/2`, `QYI·L²/12`. This means **QY is a signed load in the +y direction, so gravity on a beam drawn left→right is entered as a negative value** (e.g. −12 kN/m). Confirm this with an old database or the UI before relying on it.
- **Inconsistency to resolve:** for **inclined** members, QY is treated as a **global vertical** load per unit member length (decomposed with CX/CY). For **horizontal/vertical** members it is treated as a **local** load. On a beam defined right→left (CX = −1), the load direction therefore flips. In C#, pick one convention (recommended: global-direction loads, transformed explicitly) and document it.

---

## 5. Algorithm (live code, step by step)

Notation: NJ = number of nodes, M = number of members, NR = total restrained DOFs, N = 3·NJ − NR (free DOFs).

1. **Read geometry** (`p_coordenadas_nos`, `p_inclinacao_largura_barra`): X, Y per node; JJ, JK, L, CX, CY per member.
2. **Restraint list and renumbering** (`p_RL`, `p_crl`): `RL[3NJ]` holds 0/1 flags, and `CRL` is the cumulative sum of RL. A DOF `j` is mapped as follows:
   - free: `j − CRL[j]` (0..N−1)
   - restrained: `N + CRL[j] − 1` (N..N+NR−1)

   Free DOFs come first and restrained DOFs last, which partitions K into `Kff` and `Krf`.
3. **Member stiffness constants** (2.A.3): `SCM1 = EA/L`, `SCM2 = 4EI/L`, `SCM3 = 6EI/L²`, `SCM4 = 12EI/L³`.
4. **Member stiffness in global axes** (2.A.5): `SMD` is 6×6 and equals the standard `Tᵀ·k_local·T`, written out explicitly with CX, CY (lines ~455–491).
5. **Assembly** (2.A.6–2.A.11): columns are added only for free DOFs (`if RL[...] = 0`). Rows include the restrained DOFs, so `Krf` exists for reactions.
6. **Solve** (2.B, 5.a.1): `S_inv = inv(Kff)`, then `D_free = S_inv · AC_free`. → **C#: Cholesky solve.**
7. **Loads:**
   - (3.b) `A`: nodal loads in global axes.
   - (3.c) `AML[M,6]`: fixed-end actions in local axes from the distributed load (`±qL/2`, `±qL²/12`). The axial part is `QX·L/2` (see bugs).
   - (4.a) `AE`: equivalent nodal loads, `AE = −Tᵀ·AML`, added to J and K.
   - (4.b) `AC = A + AE`, renumbered with RL/CRL.
8. **Reactions** (5.a.2): `AR = −AC_r + Krf · D_free`, then mapped back to original DOF numbering (5.a.6). **Computed but never output; expose it in C#.**
9. **Expand D** (5.a.4): restrained DOFs = 0 → `D_out[3NJ]`.
10. **Member end actions** (5.B): `AM = AML + k_local · T · D_member`, in local axes. These are written out explicitly in lines ~850–870 → `AM_out[M,6]` = `[N_J, V_J, M_J, N_K, V_K, M_K]`.
11. **Mid-span** (lines ~873–1170), with `pos_s = L/2` only:
    - `AMM_out[M,3]` = internal forces `[N, V, M]` at mid-span, "viewed from the left of the section". Built from transfer matrices `TAXJ` (end actions), `TAQJ` (distributed load) and `TQ` (fixed-end reactions of the restrained member).
    - `DM_out[M,3]` = displacements at mid-span `[u, v, θ]` in local axes. Built from `TCR` (rigid-body), `TUX` and `TUXQ` (flexibility terms).

---

## 6. Bugs and limitations found in the live code

Fix these in the port and add a test for each:

1. **Axial distributed load reads the wrong table** (line ~680): `QX := −(barra.Fields[2] + carregamento_NO.Fields[4])`. This should be `carregamento_barra.Fields[4]` (QX_AC).
2. **Uninitialized index** (line ~719): `AML := (QX * L[AUX1]) / 2`. `aux1` is never assigned in this routine, so it should be `L[a_aux]`. It only matters when QX ≠ 0.
3. **QX ignored on inclined members**: the inclined-member branch uses only QYI.
4. **Mid-span results are wrong for inclined members**: the global→local transform (`T_DG_DL`) and `TAXJ` are only built for `CY = 0, +1, −1`. Any other angle leaves DM = 0. `CY = 0` also assumes CX = +1.
5. **Exact floating-point comparisons** (`abs(CX) <> 1`, `cy = 1`): use a tolerance, or better, use the general transform for all members.
6. **Off-diagonal assembly uses `:=` instead of `+=`** (e.g. `S[K1,J1] := SMD[3,0]`). This is wrong if two members connect the same pair of nodes.
7. **Only one distributed load per member**: AML is overwritten, AE accumulates, and the mid-span calculation uses `Locate` (first match only). These are inconsistent when a member has several load records.
8. **The mid-span loop indexes `JJ[J]`/`JK[J]` by loop counter** but uses bar number `I` elsewhere. This only works because bars are ordered by N with contiguous numbering.
9. **Oversized matrix**: `S` is allocated `3N × 3N`, but only `3NJ × 3NJ` is needed.
10. **Unnecessary database round-trips**: `QueryConsulta` re-reads J/K for each loaded member, although `JJ`/`JK` already hold them.
11. **Dead test code**: commented-out hard-coded `S_inv` and `AML` values. Ignore them.

---

## 7. Suggested C# design

```
FrameModel          Nodes, Members, Materials, Sections, LoadCases
  Node              Id, X, Y, SupportType (Free/Pin/RollerX/RollerY/Fixed) → RestraintX/Y/Rz
  Material          Id, Name, E                              (user input)
  Section           Id, Name, A, Iz                          (user input; optionally b, h → A, I helper)
  Member            Id, NodeJ, NodeK, MaterialId, SectionId
  NodalLoad         NodeId, LoadCaseId, Fx, Fy, Mz           (global)
  MemberUdl         MemberId, LoadCaseId, Qx, Qy, IsGlobal   (support several per member)
FrameSolver         Solve(model, loadCaseId) -> FrameResult   (pure, no DB access)
FrameResult         NodalDisplacements, Reactions, MemberEndForces (local),
                    ForcesAt(memberId, x), DisplacementsAt(memberId, x)
SqliteFrameRepository   loads/saves the model and results (Microsoft.Data.Sqlite)
```

Key points:
- **Material properties and member sizes come from the user.** Use E per member, through its material, instead of one global E, and A/Iz per member through its section. Validate the inputs: E, A, Iz > 0 and L > 0.
- Any cracked-section stiffness reduction (e.g. CSA A23.3 effective I) is the user's or design module's job. Pass in the I you want analysed.
- Units: store them explicitly (kN, m, kPa), or convert user units (MPa, mm, mm⁴) once at input.
- **Run one solve per load case** (D, L, S, W, …) and **combine results by superposition** in the host app using NBCC/CSA combinations (e.g. 1.25D + 1.5L). The analysis is linear, so this is exact and replaces the unfactored G+Q sum. Alternatively, factor K once and solve for several load vectors.
- Use the **general transformation matrix** for every member, so inclined members work everywhere, including mid-span.
- Replace the mid-span-only output with **`ForcesAt(x)`/`DisplacementsAt(x)`** for any x ∈ [0, L]. This allows envelopes and max-moment search, which a design check needs.
- Indexing: use 0-based internal arrays with a dictionary from node/member Id to index, so IDs don't need to be contiguous.
- Linear algebra: hand-written Cholesky (a few dozen lines) is enough for small frames, or use `MathNet.Numerics` if a dependency is acceptable.
- Keep `double` throughout (Delphi `real` = double).

---

## 8. Validation cases

**Case A: fixed-fixed beam** (sanity check): L = 6 m, w = 10 kN/m down. Expected end moments = wL²/12 = 30 kN·m, mid-span M = wL²/24 = 15 kN·m, end shears = 30 kN, and mid-span deflection = wL⁴/(384EI).

**Case B: thesis Example 1 portal frame** (thesis §5.1, Tables 5.1/Fig. 5.1). Nodes: 1 (0,0), 2 (4,0), 3 (0,3), 4 (4,3). Nodes 1 and 2 are fixed. The beam carries 12 kN/m downward.

The numbers below come from an independent numpy direct-stiffness run. In the port these values are user inputs; the run used the following test inputs:
- members 1: 1→3 and 2: 2→4 (columns 20×30 cm → A = 0.06 m², Iz = 4.5e−4 m⁴); member 3: 3→4 (beam 20×40 cm → A = 0.08 m², Iz = 1.0667e−3 m⁴)
- E = 30 000 MPa for all members
- the member numbering is assumed from the figure (not visible in the text extract)

| Result | Value |
|---|---|
| Reactions node 1 (Fx, Fy, Mz) | 4.2226 kN, 24.000 kN, −4.2120 kN·m |
| Reactions node 2 | −4.2226 kN, 24.000 kN, +4.2120 kN·m |
| Node 3 (u, v, θ) | 4.2e−6 m, −4.0e−5 m, −4.72e−4 rad |
| Beam end moments (local) | M_J = +8.4557, M_K = −8.4557 kN·m |
| Beam mid-span moment (sagging) | **15.544 kN·m** (= qL²/8 − 8.456) |
| Beam axial force | 4.2226 kN (compression) |
| Column top moment | 8.4557 kN·m |

Recommended approach: also **run the original Delphi app on the PC, if it still builds**, on Cases A and B. Record `D_out`, `AM_out`, `AMM_out` and `DM_out` as golden values, and confirm the sign conventions (especially the "viewed from the left" convention of `AMM_out`).

---

## 9. First-session checklist (on the PC)

- [ ] Open `Unit_calculo_esforços.pas` (cp1252) next to this guide.
- [ ] Confirm the target framework/package versions (.NET 10? `Microsoft.Data.Sqlite` 10.0.x?).
- [ ] Decide the load-direction convention (global vs local) and the output sign convention for internal forces.
- [ ] Define the SQLite tables (or map onto the host app's existing schema) for nodes, members, materials, sections, load cases, nodal loads and member loads. Material properties and member sizes are user input.
- [ ] Build the input flow nodes → members → loads → analyse (§3a), with validation at each step and cascade rules for deletes.
- [ ] Implement `FrameSolver` + unit tests for Cases A and B before wiring it to SQLite.
- [ ] Add tests for an inclined member and for a member with an axial distributed load (bugs 1–4).
- [ ] Wire load-case superposition into the Canadian-code design module.
