# Cantilever with tip force, tip moment and uniform load

D: tip force P = 20 kN down and a counter-clockwise tip moment of 10 kN·m. L: 5 kN/m down. Closed form (D): fixed-end moment −PL + M₀ = −70 kN·m; tip deflection −PL³/3EI + M₀L²/2EI = −30.217 + 5.666 = −24.551 mm. Closed form (L): fixed-end moment −wL²/2 = −40 kN·m; tip deflection −wL⁴/8EI = −11.331 mm.

Section W250x49: A = 6260 mm², Ix = 70.6 × 10⁶ mm⁴, Iy = 15.1 × 10⁶ mm⁴; E = 200000 MPa. Axial deformation included, shear deformation not included (switch it off in the other program). Units: m, kN, kN·m; displacements mm and rad.

## Model

| Node | X (m) | Y (m) | Support |
| --- | --- | --- | --- |
| 1 | 0.000 | 0.000 | Fixed |
| 2 | 4.000 | 0.000 | Free |

| Member | Node I | Node J | Length (m) | Bending axis |
| --- | --- | --- | --- | --- |
| 1 | 1 | 2 | 4.000 | Major (Ix) |

| Load case | Category | Self-weight |
| --- | --- | --- |
| D | D | no |
| L | L | no |

| Node | Case | Fx (kN) | Fy (kN) | Mz (kN·m) |
| --- | --- | --- | --- | --- |
| 2 | D | 0.00 | -20.00 | 10.00 |

| Member | Case | qx (kN/m) | qy (kN/m) |
| --- | --- | --- | --- |
| 1 | L | 0.00 | -5.00 |

Loads are in global axes (+X right, +Y up, moments counter-clockwise positive); distributed loads act on the whole member, per metre of member length.

| Combination | D | L |
| --- | --- | --- |
| ULS 1.25D + 1.5L | 1.25 | 1.50 |

## Results

Internal forces at x along each member (x from node I), on the part towards node I: **N** tension positive; **M** positive when it stretches the face on the right-hand side of I→J (sagging for a beam drawn left to right; for a column drawn upwards, tension on its right face); **V = dM/dx**. Reactions are the forces from the supports on the frame, global axes.

### D

| Node | ux (mm) | uy (mm) | θ (rad) | Rx (kN) | Ry (kN) | Mz (kN·m) |
| --- | --- | --- | --- | --- | --- | --- |
| 1 | 0.0000 | 0.0000 | 0 | 0.000 | 20.000 | 70.000 |
| 2 | 0.0000 | -24.5515 | -0.008499 |  |  |  |

| Member | x (m) | N (kN) | V (kN) | M (kN·m) | v across (mm) |
| --- | --- | --- | --- | --- | --- |
| 1 | 0.000 (max \|M\|) | 0.000 | 20.000 | -70.000 | 0.0000 |
| 1 | 2.000 | 0.000 | 20.000 | -30.000 | -8.0264 |
| 1 | 4.000 | 0.000 | 20.000 | 10.000 | -24.5515 |

### L

| Node | ux (mm) | uy (mm) | θ (rad) | Rx (kN) | Ry (kN) | Mz (kN·m) |
| --- | --- | --- | --- | --- | --- | --- |
| 1 | 0.0000 | 0.0000 | 0 | 0.000 | 20.000 | 40.000 |
| 2 | 0.0000 | -11.3314 | -0.003777 |  |  |  |

| Member | x (m) | N (kN) | V (kN) | M (kN·m) | v across (mm) |
| --- | --- | --- | --- | --- | --- |
| 1 | 0.000 (max \|M\|) | 0.000 | 20.000 | -40.000 | 0.0000 |
| 1 | 2.000 | 0.000 | 10.000 | -10.000 | -4.0132 |
| 1 | 4.000 (V = 0) | 0.000 | 0.000 | 0.000 | -11.3314 |

### ULS 1.25D + 1.5L

| Node | ux (mm) | uy (mm) | θ (rad) | Rx (kN) | Ry (kN) | Mz (kN·m) |
| --- | --- | --- | --- | --- | --- | --- |
| 1 | 0.0000 | 0.0000 | 0 | 0.000 | 55.000 | 147.500 |
| 2 | 0.0000 | -47.6865 | -0.016289 |  |  |  |

| Member | x (m) | N (kN) | V (kN) | M (kN·m) | v across (mm) |
| --- | --- | --- | --- | --- | --- |
| 1 | 0.000 (max \|M\|) | 0.000 | 55.000 | -147.500 | 0.0000 |
| 1 | 2.000 | 0.000 | 40.000 | -52.500 | -16.0529 |
| 1 | 4.000 | 0.000 | 25.000 | 12.500 | -47.6865 |

