# 01 — Architektura

## Projekty a závislosti

```
                    ┌──────────────────┐
                    │       Core       │  geometrie, numerika, síť, topologie
                    └────────┬─────────┘
                     ┌───────┴────────┐
                     ▼                ▼
            ┌────────────────┐  ┌──────────┐
            │   Algorithms   │  │          │  křivosti, seedy, trasování
            └────────┬───────┘  │          │
                     ▼          │          │
            ┌────────────────┐  │          │  OBJ čtečka, exportéry
            │       IO       │◄─┘          │
            └────────┬───────┘             │
                     ▼                     ▼
            ┌──────────────────────────────────┐
            │              Cli                 │  System.CommandLine
            └──────────────────────────────────┘
```

| projekt | odpovědnost | závisí na |
|---|---|---|
| `MeshRegistration.Core` | hodnotové typy geometrie, ruční numerika, reprezentace sítě a topologie, oprava | — |
| `MeshRegistration.Algorithms` | odhad křivosti, výběr seedů, trasování čar | Core |
| `MeshRegistration.IO` | čtení OBJ, zápis exportů pro MeshLab, CSV, JSON | Core, Algorithms |
| `MeshRegistration.Cli` | příkazy `inspect` a `trace` | vše |

**Proč IO závisí na Algorithms.** Exportéry serializují `TracedLine` a `CurvatureSample`, což jsou
typy z algoritmické vrstvy. Alternativou by bylo umístit exportéry do Algorithms, ale to by tam
zaneslo práci se soubory. Graf zůstává acyklický, tak je to v pořádku.

**Žádné knihovny na lineární algebru.** Vlastní řešiče 2×2 a 3×3 (viz [03](03-krivosti.md)). Původní
řešení volalo `MathNet` `DenseMatrix.Svd()` pro každého kandidáta v horké smyčce, což znamená
alokaci obecné matice na iteraci.

Jediné balíčky: `System.CommandLine` (CLI), `Microsoft.Extensions.Logging` (zatím nevyužito),
`xunit` (testy).

## Tok dat

```
soubor .obj
    │
    │  ObjReader.Read                      ← IO
    ▼
(Vec3[] Positions, Triangle[] Triangles)   syrová geometrie, indexy podle souboru
    │
    │  MeshBuilder.Build                   ← Core
    ▼
MeshBuildResult
  ├── TriangleMesh      pozice, trojúhelníky, normály, plochy vrcholů, měřítka
  ├── MeshTopology      corner table, CSR sousedství, příznaky vrcholů
  └── MeshDiagnostics   co bylo opraveno
    │
    │  ShapeOperatorField.Compute          ← Algorithms
    ▼
ShapeOperatorField      operátor tvaru na každém vrcholu + spolehlivost + příznaky
    │
    │  SeedSelector.Select
    ▼
List<SurfacePoint>      seedy seřazené podle anizotropie, s minimálním rozestupem
    │
    │  LineTracer.TraceAll
    ▼
TracedLine[]            čáry vzorkované ekvidistantně v délce oblouku
    │
    │  LineExporter, CurvatureMeshExporter,
    │  SampleCsvExporter, ReportExporter   ← IO
    ▼
soubory pro MeshLab + CSV pro fázi 2
```

Každý krok je čistá funkce svého vstupu. Žádná fáze nemění data, která dostala — to je jeden
z invariantů níže.

## Klíčové typy

### Core / Geometry

| typ | poznámka |
|---|---|
| `Vec3` | `readonly record struct` s `double` složkami. Dvojitá přesnost je záměr: křivost je veličina druhého řádu, odhad odečítá téměř shodné normály, a v jednoduché přesnosti by zajímavý signál na hladkém skenu ležel u zaokrouhlovací meze. |
| `BoundingBox` | `DiagonalLength` je kanonické měřítko modelu |
| `TangentFrame` | ortonormální `(E1, E2, Normal)`. Konstrukce je **totální** — nikdy nevrátí NaN ani nulovou osu, ať dostane jakoukoliv normálu. |

### Core / Numerics

| typ | poznámka |
|---|---|
| `Sym2x2` | symetrická 2×2 matice = operátor tvaru v tečném rámci. `Eigen()` je totální — viz [03](03-krivosti.md). |
| `Sym3x3Solver` | LDL řešič symetrické pozitivně definitní 3×3 soustavy, bez alokací, s bezrozměrným odhadem podmíněnosti |

### Core / Mesh

| typ | poznámka |
|---|---|
| `Triangle` | tři indexy vrcholů |
| `TriangleMesh` | **immutable**. Vystavuje `ReadOnlySpan`. V konstruktoru dopočítá normály stěn i vrcholů, plochy vrcholů, průměrnou délku hrany a bbox. |
| `SurfacePoint` | bod kdekoliv na ploše: index trojúhelníka + barycentrické souřadnice |
| `MeshTopology` | corner table + CSR sousedství. Po `MeshBuilder` je **manifoldní z konstrukce**. |
| `MeshBuilder` | oprava topologie, viz [02](02-nacitani-a-topologie.md) |
| `AnalyticShapes` | generátor ploch se známou křivostí (koule, válec, vlny, …), použitelný místo vstupního souboru |
| `MeshDiagnostics` | co se našlo a co se s tím udělalo |

### Algorithms

| typ | poznámka |
|---|---|
| `CurvatureSample` | křivosti, hlavní směry, spolehlivost, příznaky degenerace |
| `CurvatureFlags` | `Umbilic`, `Planar`, `Boundary`, `InsufficientNeighbours`, `IllConditioned`, `Isolated` |
| `ShapeOperatorField` | operátory na vrcholech + vyhodnocení v libovolném bodě plochy |
| `SurfaceWalker` | pochod po ploše s rozvíjením trojúhelníků |
| `LineTracer` | logika směrového pole a skládání čar |
| `SeedSelector` | deterministický výběr výchozích bodů |
| `TracedLine`, `LineSample` | výsledek, a zároveň rozhraní pro fázi 2 |
| `FollowedDirection` | které hlavní pole vzorek reálně sledoval — měření, ne předpoklad |

### Uspořádání dat

Vrcholy jsou uložené jako pole struktur (`Vec3[]`), ne jako tři paralelní pole souřadnic. Odhad
křivosti i tracer přistupují k vrcholům přes rozptýlené indexy sousedů, takže se všechny tři
souřadnice jednoho vrcholu chtějí naráz a přijdou na jedné cache-line. Struktura polí by se
vyplatila jen u průchodů celým polem, které jsou stejně omezené propustností paměti.

Sousedství je v CSR (compressed sparse row): jedno pole offsetů a jedno ploché pole sousedů.
Dotaz na okolí je souvislé čtení bez nepřímé adresace a bez alokace na vrchol.

## Invarianty řešení

Šest vlastností, kvůli kterým přepis vznikl. Jejich tiché porušení by práci zrušilo.

**1. Z odhadu křivosti nesmí odejít NaN ani nekonečno.** Na rovině je operátor tvaru nulový, na
kouli násobkem identity; klasická větev pro vlastní vektor pro obojí počítá `0/0`. Hlídají testy
`Plane_IsFlatAndFinite` a `Sphere_HasUniformCurvatureAndIsUmbilic`.

**2. Konečný směr není použitelný směr.** Protože eigensolver vždy vrátí číslo, musí volající
kontrolovat `CurvatureSample.HasUsableDirection` (tedy příznak `Umbilic`), ne jen to, že vektor
není nulový. Na reálných skenech je 2–30 % vrcholů umbilických a až 44 % rovinných.

**3. Načítání opravuje, neodmítá.** `MeshBuilder` zvládá degenerované i duplicitní trojúhelníky,
nekonzistentní navinutí, nemanifoldní hrany i motýlkové vrcholy, a všechno hlásí. Politika
`Strict` existuje jen pro volajícího, který si o původní chování výslovně řekne.

**4. Každý práh je bezrozměrný.** Délky jsou násobky `AverageEdgeLength` nebo podíly
`DiagonalLength`; prahy křivosti jsou křivost krát poloměr okolí. Testovací data mají úhlopříčky
od 0.14 do 352, takže absolutní konstanta je kdekoliv chyba.

**5. Determinismus.** Nikde není generátor náhodných čísel. Seedy se řadí deterministicky
s indexem vrcholu jako rozhodčím při shodě, paralelní práce zapisuje výsledky podle indexu. Dva
běhy musí dát bajtově shodný výstup (`TraceAll_IsDeterministic`).

**6. Síť je immutable.** Žádná fáze nesmí měnit síť, kterou čte. Původní tracer přidával
vizualizační body přímo do sítě, po které šel, čímž ji rozsynchronizoval s polem vah na vrchol.
Vizualizační geometrie patří do exportérů.

## Konvence

**Corner table.** Roh `c` patří trojúhelníku `c / 3` na lokálním indexu `c % 3`. Vrchol *v* rohu
`c` je vrchol `c % 3` daného trojúhelníka; hrana *naproti* rohu `c` spojuje vrcholy v `Next(c)`
a `Previous(c)`. `Opposite(c)` je roh hledící na tutéž hranu ze sousedního trojúhelníka, nebo `-1`.
Vějíř se obchází přes `Swing` / `Unswing`.

```
        Vertex(c)
           ╱╲
          ╱  ╲
   Prev(c)────Next(c)
      hrana naproti c
```

**Barycentrické souřadnice.** `SurfacePoint` používá `P = U·V0 + V·V1 + (1 − U − V)·V2`; třetí
váha je implicitní v `W`.

**Znaménko křivosti.** Operátor tvaru je bráno jako `dN`, takže s vnějšími normálami má konvexní
plocha kladnou křivost a koule o poloměru R má `kMin = kMax = 1/R`. `MeshBuilder` orientuje
uzavřené komponenty ven, takže je to dobře definované.

**Hlavní směry tvoří přímkové pole.** Každý je určen jen až na znaménko, a označení min/max se
prohazuje všude tam, kde se obě křivosti protnou. Sledovat `DirMax` podle jména proto znamená
skákat mezi různými integrálními křivkami.

## Nastavení překladu

`Directory.Build.props` platí pro všechny projekty:

| vlastnost | hodnota | proč |
|---|---|---|
| `TargetFramework` | `net10.0` | nejnovější nainstalované SDK (10.0.110) |
| `Nullable` | `enable` | |
| `TreatWarningsAsErrors` | `true` | s `AnalysisLevel latest-recommended` |
| `InvariantGlobalization` | `true` | soubory se parsují bajtově, kulturní data nejsou potřeba |
| `Deterministic` | `true` | reprodukovatelný překlad |
| `ServerGarbageCollection` | `true` (Release) | výpočetně vázaný konzolový nástroj |
| `TieredPGO` | `true` (Release) | |

`.editorconfig` potlačuje CA1711 (výčty s `[Flags]` mají legitimně končit na `Flags`) a v `tests/**`
CA1707 (konvence `Subject_Behaviour` potřebuje podtržítka).
