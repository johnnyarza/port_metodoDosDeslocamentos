# Simply supported beam, uniform load

Closed form: R = wL/2 = 30 kN, M(L/2) = wL²/8 = 45 kN·m, mid-span deflection 5wL⁴/384EI = 11.951 mm,end rotation wL³/24EI = 0.006374 rad.

Section W250x49: A = 6260 mm², Ix = 70.6 × 10⁶ mm⁴, Iy = 15.1 × 10⁶ mm⁴; E = 200000 MPa. Axial deformation included, shear deformation not included (switch it off in the other program). Units: m, kN, kN·m; displacements mm and rad.

## Model

| Node | X (m) | Y (m) | Support |
| --- | --- | --- | --- |
| 1 | 0.000 | 0.000 | Pin |
| 2 | 6.000 | 0.000 | Roller (vertical reaction) |

| Member | Node I | Node J | Length (m) | Bending axis |
| --- | --- | --- | --- | --- |
| 1 | 1 | 2 | 6.000 | Major (Ix) |

| Load case | Category | Self-weight |
| --- | --- | --- |
| D | D | no |

| Member | Case | qx (kN/m) | qy (kN/m) |
| --- | --- | --- | --- |
| 1 | D | 0.00 | -10.00 |

Loads are in global axes (+X right, +Y up, moments counter-clockwise positive); distributed loads act on the whole member, per metre of member length.

## Results

Internal forces at x along each member (x from node I), on the part towards node I: **N** tension positive; **M** positive when it stretches the face on the right-hand side of I→J (sagging for a beam drawn left to right; for a column drawn upwards, tension on its right face); **V = dM/dx**. Reactions are the forces from the supports on the frame, global axes.

### D

| Node | ux (mm) | uy (mm) | θ (rad) | Rx (kN) | Ry (kN) | Mz (kN·m) |
| --- | --- | --- | --- | --- | --- | --- |
| 1 | 0.0000 | 0.0000 | -0.006374 | 0.000 | 30.000 |  |
| 2 | 0.0000 | 0.0000 | 0.006374 |  | 30.000 |  |

| Member | x (m) | N (kN) | V (kN) | M (kN·m) | v across (mm) |
| --- | --- | --- | --- | --- | --- |
| 1 | 0.000 | 0.000 | 30.000 | 0.000 | 0.0000 |
| 1 | 3.000 (V = 0, max \|M\|) | 0.000 | 0.000 | 45.000 | -11.9511 |
| 1 | 6.000 | 0.000 | -30.000 | 0.000 | 0.0000 |

