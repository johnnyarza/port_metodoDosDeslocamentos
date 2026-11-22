# Propped cantilever, uniform load

Closed form: R at the roller = 3wL/8 = 36 kN, at the fixed end 5wL/8 = 60 kN; fixed-end moment wL²/8 = 96 kN·m (hogging); largest sagging moment 9wL²/128 = 54 kN·m at 3L/8 = 3 m from the roller (x = 5 m).

Section W250x49: A = 6260 mm², Ix = 70.6 × 10⁶ mm⁴, Iy = 15.1 × 10⁶ mm⁴; E = 200000 MPa. Axial deformation included, shear deformation not included (switch it off in the other program). Units: m, kN, kN·m; displacements mm and rad.

## Model

| Node | X (m) | Y (m) | Support |
| --- | --- | --- | --- |
| 1 | 0.000 | 0.000 | Fixed |
| 2 | 8.000 | 0.000 | Roller (vertical reaction) |

| Member | Node I | Node J | Length (m) | Bending axis |
| --- | --- | --- | --- | --- |
| 1 | 1 | 2 | 8.000 | Major (Ix) |

| Load case | Category | Self-weight |
| --- | --- | --- |
| D | D | no |

| Member | Case | qx (kN/m) | qy (kN/m) |
| --- | --- | --- | --- |
| 1 | D | 0.00 | -12.00 |

Loads are in global axes (+X right, +Y up, moments counter-clockwise positive); distributed loads act on the whole member, per metre of member length.

## Results

Internal forces at x along each member (x from node I), on the part towards node I: **N** tension positive; **M** positive when it stretches the face on the right-hand side of I→J (sagging for a beam drawn left to right; for a column drawn upwards, tension on its right face); **V = dM/dx**. Reactions are the forces from the supports on the frame, global axes.

### D

| Node | ux (mm) | uy (mm) | θ (rad) | Rx (kN) | Ry (kN) | Mz (kN·m) |
| --- | --- | --- | --- | --- | --- | --- |
| 1 | 0.0000 | 0.0000 | 0 | 0.000 | 60.000 | 96.000 |
| 2 | 0.0000 | 0.0000 | 0.009065 |  | 36.000 |  |

| Member | x (m) | N (kN) | V (kN) | M (kN·m) | v across (mm) |
| --- | --- | --- | --- | --- | --- |
| 1 | 0.000 (max \|M\|) | 0.000 | 60.000 | -96.000 | 0.0000 |
| 1 | 4.000 | 0.000 | 12.000 | 48.000 | -18.1303 |
| 1 | 5.000 (V = 0) | 0.000 | 0.000 | 54.000 | -18.5907 |
| 1 | 8.000 | 0.000 | -36.000 | 0.000 | 0.0000 |

