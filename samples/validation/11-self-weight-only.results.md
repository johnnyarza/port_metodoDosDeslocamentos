# Portal frame (sample 05 geometry) under self-weight only

The only load is the section's self-weight, 0.481 kN/m downward along every member (columns too: it acts along their axis). Total weight (4 + 4 + 6) × 0.481 = 6.734 kN, so the vertical reactions add up to 6.734 kN. If your program computes self-weight from a density, 0.481 kN/m corresponds to A·ρ·g with ρ ≈ 7833 kg/m³.

Section W250x49: A = 6260 mm², Ix = 70.6 × 10⁶ mm⁴, Iy = 15.1 × 10⁶ mm⁴; E = 200000 MPa. Axial deformation included, shear deformation not included (switch it off in the other program). Units: m, kN, kN·m; displacements mm and rad.

## Model

| Node | X (m) | Y (m) | Support |
| --- | --- | --- | --- |
| 1 | 0.000 | 0.000 | Fixed |
| 2 | 6.000 | 0.000 | Fixed |
| 3 | 0.000 | 4.000 | Free |
| 4 | 6.000 | 4.000 | Free |

| Member | Node I | Node J | Length (m) | Bending axis |
| --- | --- | --- | --- | --- |
| 1 | 1 | 3 | 4.000 | Major (Ix) |
| 2 | 2 | 4 | 4.000 | Major (Ix) |
| 3 | 3 | 4 | 6.000 | Major (Ix) |

| Load case | Category | Self-weight |
| --- | --- | --- |
| D | D | yes |

Loads are in global axes (+X right, +Y up, moments counter-clockwise positive); distributed loads act on the whole member, per metre of member length.

## Results

Internal forces at x along each member (x from node I), on the part towards node I: **N** tension positive; **M** positive when it stretches the face on the right-hand side of I→J (sagging for a beam drawn left to right; for a column drawn upwards, tension on its right face); **V = dM/dx**. Reactions are the forces from the supports on the frame, global axes.

### D

| Node | ux (mm) | uy (mm) | θ (rad) | Rx (kN) | Ry (kN) | Mz (kN·m) |
| --- | --- | --- | --- | --- | --- | --- |
| 1 | 0.0000 | 0.0000 | 0 | 0.405 | 3.367 | -0.538 |
| 2 | 0.0000 | 0.0000 | 0 | -0.405 | 3.367 | 0.538 |
| 3 | 0.0010 | -0.0077 | -0.000077 |  |  |  |
| 4 | -0.0010 | -0.0077 | 0.000077 |  |  |  |

| Member | x (m) | N (kN) | V (kN) | M (kN·m) | v across (mm) |
| --- | --- | --- | --- | --- | --- |
| 1 | 0.000 | -3.367 | -0.405 | 0.538 | 0.0000 |
| 1 | 2.000 | -2.405 | -0.405 | -0.272 | 0.0380 |
| 1 | 4.000 (max \|M\|) | -1.443 | -0.405 | -1.081 | -0.0010 |
| 2 | 0.000 | -3.367 | 0.405 | -0.538 | 0.0000 |
| 2 | 2.000 | -2.405 | 0.405 | 0.272 | -0.0380 |
| 2 | 4.000 (max \|M\|) | -1.443 | 0.405 | 1.081 | 0.0010 |
| 3 | 0.000 | -0.405 | 1.443 | -1.081 | -0.0077 |
| 3 | 3.000 (V = 0, max \|M\|) | -0.405 | 0.000 | 1.084 | -0.2380 |
| 3 | 6.000 | -0.405 | -1.443 | -1.081 | -0.0077 |

