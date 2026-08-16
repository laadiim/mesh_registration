# 07 — Testování

```bash
dotnet test
```

97 testů, běh ~3 s.

| projekt | testů | co pokrývá |
|---|---|---|
| `MeshRegistration.Core.Tests` | 41 | eigensolver 2×2, oprava topologie, čtečka OBJ |
| `MeshRegistration.Algorithms.Tests` | 56 | křivosti, trasování, analytické tvary, exporty, diagnostika |

---

## Strategie

Správnost se prokazuje proti **plochám se známou křivostí v uzavřeném tvaru**, ne proti reálným
skenům. U reálného skenu není s čím porovnávat, takže test může nanejvýš zachytit pád — u koule
se dá porovnat s `1/R`.

Generátory jsou v knihovně (`MeshRegistration.Core.Mesh.AnalyticShapes`), takže plochy, které
ověřují testy, a plochy, které kreslí `meshreg --shape`, jsou tentýž kód — viz
[12](12-analyticke-tvary.md).

| plocha | `kMin` | `kMax` | role |
|---|---|---|---|
| **rovina** | 0 | 0 | regresní test na NaN, degenerace |
| **koule** `R` | `1/R` | `1/R` | regresní test na NaN, degenerace, hodnota |
| **válec** `R` | `0` | `1/R` | hlavní **směry**, silně anizotropní |
| **torus** `(R, r)` | min z ↓ | max z ↓ | proměnná křivost |
| **kvadrika** `z = (a x² + c y²)/2` | `−a` nebo `−c` | druhá | sedlo, opačná znaménka |

Torus v bodě s úhlem tubusu `θ`:

```
k₁ = 1/r        k₂ = cos θ / (R + r·cos θ)
```

**Koule je icosphere**, ne zeměpisná síť: ta má u pólů degenerované trojúhelníky a divoce
proměnnou hustotu vrcholů, což by test křivosti zaměnilo za artefakt triangulace.

Sítě se generují navinuté tak, aby parametrická normála `∂P/∂u × ∂P/∂v` mířila ven — pod konvencí
`S = dN` je pak konvexní plocha kladně zakřivená. U koule se navíc na tuto orientaci spoléhá až
`MeshBuilder`, takže test koule mimochodem ověřuje i `OrientClosedComponentsOutward`.

---

## Klíčové testy

### Regrese původních chyb

| test | co hlídá |
|---|---|
| `Plane_IsFlatAndFinite` | na rovině `kMin = kMax = 0`, příznaky `Planar` i `Umbilic`, **žádný NaN** |
| `Sphere_HasUniformCurvatureAndIsUmbilic` | na kouli `kMin = kMax = 1/R` (±5 %), `Umbilic`, ne `Planar`, **žádný NaN** |
| `Eigen_OnZeroOperator_IsFiniteAndZero` | tentýž problém na úrovni numeriky |
| `Eigen_OnIsotropicOperator_IsFiniteAndEqual` | pro křivosti od `1e-9` do `1e9` |
| `Eigen_NearUmbilic_StaysFinite` | perturbace `0`, `±1e-18`, `1e-12`, `double.Epsilon` |
| `Plane_YieldsNoSeeds`, `Sphere_YieldsNoSeeds` | na degenerované ploše se nezaloží žádná čára |
| `Plane_TracedFromAForcedSeed_StopsImmediatelyWithoutNaN` | vnucený seed skončí `Degenerate` a prázdnou čárou |
| `DoesNotNegateZByDefault` | čtečka už tiše nemění orientaci soustavy |
| `IsolatedVertex_IsFlaggedRatherThanSilentlyMisindexed` | `IncidentCorner == −1`, ne `0` ukazující na cizí trojúhelník |
| `NonManifoldEdge_WithStrictPolicy_Throws` | zpráva **jmenuje konkrétní hranu** |

### Topologie

Ručně sestavené sítě, každá izolující jednu závadu:

| test | síť |
|---|---|
| `BowTieVertex_IsSplitIntoOneVertexPerFan` | dva trojúhelníky sdílející jediný vrchol → 1 motýlek, 1 kopie, žádný sdílený vrchol |
| `BowTieVertex_LeavesEveryVertexWithASingleFan` | po štěpení musí obchůzka vějíře dosáhnout **každého** rohu v daném vrcholu |
| `NonManifoldEdge_WithCutPolicy_KeepsEveryTriangle` | tři trojúhelníky na jedné hraně → všechny přežijí, tři rohy se stanou hraničními |
| `NonManifoldEdge_WithPairBestPolicy_KeepsTheFlattestContinuation` | přežije právě jedna dvojice rohů |
| `InconsistentWinding_IsRepaired` | dva trojúhelníky procházející sdílenou hranu stejným směrem |
| `ClosedComponent_IsOrientedOutward` | dovnitř navinutý tetraedr; a **navíc**, že už správně navinutý zůstane nedotčen |
| `DegenerateAndDuplicateFaces_AreRemoved` | opakovaný index + shodná trojice v jiném navinutí |
| `Welding_ReconnectsAMeshStoredWithPerFaceVertices` | bez `--weld` 2 komponenty a 6 hraničních hran; s ním 1 komponenta |
| `CornerTable_IsSymmetricAndFansClose` | oktaedr: `Opposite` symetrické, každý vějíř má 4 rohy a uzavře se |

> `ClosedComponent_IsOrientedOutward` odhalil skutečnou chybu v implementaci: po překlopení
> trojúhelníka se permutují rohy, takže indexy rohů v edge bucketech přestanou odpovídat hranám,
> pro které byly postavené. Kbelíky se proto po změně orientace přestavují.

### Trasování

| test | co hlídá |
|---|---|
| `Cylinder_MaxFieldLineIsACircleAroundTheAxis` | čára zůstane v konstantní vzdálenosti `R` od osy a v konstantním `z` — přímý test správnosti rozvíjení |
| `Cylinder_PrincipalLinesAreGeodesics` | `κ_g ≈ 0`; válec je rozvinutelný, hlavní kružnice jsou geodetiky |
| `Cylinder_LineClosesOnItself` | při dostatečném rozpočtu skončí `SelfIntersection` |
| `Cylinder_MinFieldLineRunsAlongTheAxisAndReachesTheRim` | směr minimální křivosti je osový, čára skončí `Boundary` |
| `Samples_AreEvenlySpacedInArcLength` | rozestupy v ⟨0.9·krok, 1.001·krok⟩ |
| `TraceAll_IsDeterministic` | dva běhy bajtově shodné |
| `Seeds_AreSpreadOutAndAvoidDegenerateRegions` | každý seed má použitelný směr, žádné dva blíž než `SeedSpacing` |

> `Cylinder_PrincipalLinesAreGeodesics` odhalil druhou skutečnou chybu: geodetická křivost se
> počítala z úhlu mezi syrovými trojrozměrnými tětivami, což je křivost křivky **v prostoru**.
> Očekáváno 0, naměřeno −1.05 ≈ −1/R. Oprava je promítnout obě tětivy do tečné roviny.

### Analytické tvary

`AnalyticShapeTracingTests` trasuje všech deset pojmenovaných tvarů a měří výsledek proti tomu, co
daný tvar zaručuje — ne jen že to nespadlo.

| test | co měří |
|---|---|
| `DegenerateShapes_YieldNoLines` | na rovině a kouli nesmí vzniknout ani jedna čára |
| `Waves_EveryLineIsAConcentricCircleOrARadialSpoke` | rozptyl poloměru a opsaný úhel každé čáry; musí padnout do jedné z obou rodin, a obě rodiny se musí objevit |
| `ParabolicCylinder_MaxFieldLinesAreStraight` | čára se nesmí odchýlit napříč rulings; `κ_g ≈ 0` |
| `Cylinder_MaxFieldLinesStayAtConstantHeightAndRadius` | konstantní poloměr i výška |
| `EveryShape_BuildsAValidManifoldMesh` | generované sítě jsou manifoldní a souvislé |

### Které pole čára sleduje

| test | co měří |
|---|---|
| `Cylinder_LineStaysOnTheFieldItWasSeededOn` | na válci se křivosti nikdy neprotnou, takže čára musí zůstat 100 % na svém poli |
| `Sphere_SamplesAreRecordedAsTransportedNotAsAField` | na kouli nesmí žádný vzorek tvrdit, že sledoval pole |

### Diagnostika běhu

`RunDiagnosisTests` ověřuje, že se krátký běh vysvětlí **tou příčinou, která opravdu nastala**.
Obsahuje explicitní kontrolu, že se nevrátí dřívější chybná hláška „flat or spherical" na
nesvařené síti.

### Exporty

`PolylineExport_…`, `TubeExport_…`, `CurvatureMeshExport_…`, `CsvExport_…`, `Report_…`

Trubičkový výstup se **načte zpět** vlastní čtečkou a ověří se, že má nula nemanifoldních hran
a přesně tolik komponent, kolik je čar. CSV se kontroluje na počet řádků, na absenci `NaN`
a `Infinity` a na to, že se každý číselný sloupec dá zparsovat zpět.

---

## Dvě jemnosti při utahování tolerancí

**1. Odhad průměruje přes okolí.** Na ploše s proměnnou křivostí je proložená hodnota zkreslená
směrem ke **střední** křivosti okolí. Konkrétně: `Saddle_IsAnisotropicWithOppositeSigns` má
v počátku dát `∓1`, ale s výchozím `NeighbourhoodWidth = 8` a sítí 60×60 přes rozsah 2.0 vychází
`−0.931`. Není to chyba odhadu — na `z = x²/2` klesá křivost jako `(1+x²)^(−3/2)` a na vzdálenosti
poloměru proložení (0.267) je to 0.902. Zprůměrováno přesně to dá naměřených ~0.93.

Řešení je **zúžit okolí** (test používá `NeighbourhoodWidth = 3` a jemnější síť), ne povolit
toleranci. Jinak test přestane měřit odhad a začne měřit vyhlazovací okno.

**2. Hraniční vrcholy mají jednostranné okolí** a jsou legitimně méně přesné. Pomocník `Analyse`
je z asercí vylučuje.

---

## Očekávání nad reálnými daty

Užitečná jako křížová kontrola; ověřeno nezávislou sondou v Pythonu:

| soubor | očekávání |
|---|---|
| `hip1.obj` | 2 motýlkové, 4 izolované vrcholy |
| `hea1.obj` | 3 izolované vrcholy |
| `cha1.obj` | 32 nemanifoldních hran, 221 motýlků, 23 izolovaných, 4257 degenerovaných stěn |
| `cha2m.obj` | 70 nemanifoldních hran, 499 motýlků, 2 neorientovatelné hrany |

Tyto hodnoty **nejsou** v automatizovaných testech, protože `data/` není v repozitáři. Ověřují se
příkazem `inspect`.

## Ruční kontroly

```bash
# nikde žádná nekonečná hodnota
grep -ciE "nan|infinity" out/*_samples.csv

# determinismus
meshreg trace data/kac1.obj --out /tmp/a
meshreg trace data/kac1.obj --out /tmp/b
diff -r /tmp/a /tmp/b && echo OK
```

Příkaz `trace` navíc sám vrátí kód **2**, pokud se ve výstupu objeví nekonečná hodnota.
