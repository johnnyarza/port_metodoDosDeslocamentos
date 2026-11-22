# Simply supported beam with a point load at mid-span

Loads within a span are not supported yet, so the beam is split at mid-span by node 2, which carries the load. Closed form: R = P/2 = 20 kN, M(L/2) = PL/4 = 60 kN·m, deflection PL³/48EI = 12.748 mm.

Section W250x49: A = 6260 mm², Ix = 70.6 × 10⁶ mm⁴, Iy = 15.1 × 10⁶ mm⁴; E = 200000 MPa. Axial deformation included, shear deformation not included (switch it off in the other program). Units: m, kN, kN·m; displacements mm and rad.

## Model

| Node | X (m) | Y (m) | Support |
| --- | --- | --- | --- |
| 1 | 0.000 | 0.000 | Pin |
| 2 | 3.000 | 0.000 | Free |
| 3 | 6.000 | 0.000 | Roller (vertical reaction) |

| Member | Node I | Node J | Length (m) | Bending axis |
| --- | --- | --- | --- | --- |
| 1 | 1 | 2 | 3.000 | Major (Ix) |
| 2 | 2 | 3 | 3.000 | Major (Ix) |

| Load case | Category | Self-weight |
| --- | --- | --- |
| D | D | no |

| Node | Case | Fx (kN) | Fy (kN) | Mz (kN·m) |
| --- | --- | --- | --- | --- |
| 2 | D | 0.00 | -40.00 | 0.00 |

Loads are in global axes (+X right, +Y up, moments counter-clockwise positive); distributed loads act on the whole member, per metre of member length.

## Results

Internal forces at x along each member (x from node I), on the part towards node I: **N** tension positive; **M** positive when it stretches the face on the right-hand side of I→J (sagging for a beam drawn left to right; for a column drawn upwards, tension on its right face); **V = dM/dx**. Reactions are the forces from the supports on the frame, global axes.

### D

| Node | ux (mm) | uy (mm) | θ (rad) | Rx (kN) | Ry (kN) | Mz (kN·m) |
| --- | --- | --- | --- | --- | --- | --- |
| 1 | 0.0000 | 0.0000 | -0.006374 | 0.000 | 20.000 |  |
| 2 | 0.0000 | -12.7479 | 0 |  |  |  |
| 3 | 0.0000 | 0.0000 | 0.006374 |  | 20.000 |  |

| Member | x (m) | N (kN) | V (kN) | M (kN·m) | v across (mm) |
| --- | --- | --- | --- | --- | --- |
| 1 | 0.000 | 0.000 | 20.000 | 0.000 | 0.0000 |
| 1 | 1.500 | 0.000 | 20.000 | 30.000 | -8.7642 |
| 1 | 3.000 (max \|M\|) | 0.000 | 20.000 | 60.000 | -12.7479 |
| 2 | 0.000 (max \|M\|) | 0.000 | -20.000 | 60.000 | -12.7479 |
| 2 | 1.500 | 0.000 | -20.000 | 30.000 | -8.7642 |
| 2 | 3.000 | 0.000 | -20.000 | 0.000 | 0.0000 |

