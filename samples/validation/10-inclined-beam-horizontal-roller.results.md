# Inclined member (3-4-5), pin at the bottom, horizontal-reaction roller at the top

Member is 5 m long. The top roller only resists horizontally (it is free to slide vertically, like a beam leaning on a wall). D: 10 kN/m down per metre of member length (50 kN in total; 12.5 kN/m per metre of horizontal projection). L: 10 kN down at the top node. Closed form (D): Rx at the top = −50·2/3 = −33.333 kN, pin Rx = +33.333 kN, pin Ry = 50 kN.

Section W250x49: A = 6260 mm², Ix = 70.6 × 10⁶ mm⁴, Iy = 15.1 × 10⁶ mm⁴; E = 200000 MPa. Axial deformation included, shear deformation not included (switch it off in the other program). Units: m, kN, kN·m; displacements mm and rad.

## Model

| Node | X (m) | Y (m) | Support |
| --- | --- | --- | --- |
| 1 | 0.000 | 0.000 | Pin |
| 2 | 4.000 | 3.000 | Roller (horizontal reaction) |

| Member | Node I | Node J | Length (m) | Bending axis |
| --- | --- | --- | --- | --- |
| 1 | 1 | 2 | 5.000 | Major (Ix) |

| Load case | Category | Self-weight |
| --- | --- | --- |
| D | D | no |
| L | L | no |

| Node | Case | Fx (kN) | Fy (kN) | Mz (kN·m) |
| --- | --- | --- | --- | --- |
| 2 | L | 0.00 | -10.00 | 0.00 |

| Member | Case | qx (kN/m) | qy (kN/m) |
| --- | --- | --- | --- |
| 1 | D | 0.00 | -10.00 |

Loads are in global axes (+X right, +Y up, moments counter-clockwise positive); distributed loads act on the whole member, per metre of member length.

| Combination | D | L |
| --- | --- | --- |
| ULS 1.25D + 1.5L | 1.25 | 1.50 |

## Results

Internal forces at x along each member (x from node I), on the part towards node I: **N** tension positive; **M** positive when it stretches the face on the right-hand side of I→J (sagging for a beam drawn left to right; for a column drawn upwards, tension on its right face); **V = dM/dx**. Reactions are the forces from the supports on the frame, global axes.

### D

| Node | ux (mm) | uy (mm) | θ (rad) | Rx (kN) | Ry (kN) | Mz (kN·m) |
| --- | --- | --- | --- | --- | --- | --- |
| 1 | 0.0000 | 0.0000 | -0.002995 | 33.333 | 50.000 |  |
| 2 | 0.0000 | -0.2773 | 0.002907 | -33.333 |  |  |

| Member | x (m) | N (kN) | V (kN) | M (kN·m) | v across (mm) |
| --- | --- | --- | --- | --- | --- |
| 1 | 0.000 | -56.667 | 20.000 | 0.000 | 0.0000 |
| 1 | 2.500 (V = 0, max \|M\|) | -41.667 | 0.000 | 25.000 | -4.7217 |
| 1 | 5.000 | -26.667 | -20.000 | 0.000 | -0.2219 |

### L

| Node | ux (mm) | uy (mm) | θ (rad) | Rx (kN) | Ry (kN) | Mz (kN·m) |
| --- | --- | --- | --- | --- | --- | --- |
| 1 | 0.0000 | 0.0000 | -0.000018 | 13.333 | 10.000 |  |
| 2 | 0.0000 | -0.1109 | -0.000018 | -13.333 |  |  |

| Member | x (m) | N (kN) | V (kN) | M (kN·m) | v across (mm) |
| --- | --- | --- | --- | --- | --- |
| 1 | 0.000 | -16.667 | 0.000 | 0.000 | 0.0000 |
| 1 | 2.500 | -16.667 | 0.000 | 0.000 | -0.0444 |
| 1 | 5.000 (max \|M\|) | -16.667 | 0.000 | 0.000 | -0.0887 |

### ULS 1.25D + 1.5L

| Node | ux (mm) | uy (mm) | θ (rad) | Rx (kN) | Ry (kN) | Mz (kN·m) |
| --- | --- | --- | --- | --- | --- | --- |
| 1 | 0.0000 | 0.0000 | -0.003771 | 61.667 | 77.500 |  |
| 2 | 0.0000 | -0.5131 | 0.003607 | -61.667 |  |  |

| Member | x (m) | N (kN) | V (kN) | M (kN·m) | v across (mm) |
| --- | --- | --- | --- | --- | --- |
| 1 | 0.000 | -95.833 | 25.000 | 0.000 | 0.0000 |
| 1 | 2.500 (V = 0, max \|M\|) | -77.083 | 0.000 | 31.250 | -5.9687 |
| 1 | 5.000 | -58.333 | -25.000 | 0.000 | -0.4105 |

