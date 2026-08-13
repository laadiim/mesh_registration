# 06 — Parametry

Úplná referenční tabulka. Kompletní seznam přepínačů dá `meshreg trace --help`.

**Všechny délkové parametry jsou bezrozměrné** — násobky průměrné délky hrany nebo podíly
úhlopříčky bboxu. Testovací data mají úhlopříčky od 0.14 do 352, takže absolutní konstanta je
kdekoliv chyba. Podrobněji v [01 — Invarianty](01-architektura.md#invarianty-řešení).

---

## Načítání — `ObjReadOptions`

| parametr | přepínač | výchozí | jednotka | popis |
|---|---|---|---|---|
| `FlipZ` | `--flip-z` | `false` | — | negace Z při načtení. Mění orientaci soustavy, a tedy efektivní navinutí a **znaménko všech křivostí**. Původní verze to dělala vždy a mlčky. |
| `FlipWinding` | — | `false` | — | obrácení navinutí trojúhelníků |

## Topologie — `MeshBuildOptions`

| parametr | přepínač | výchozí | jednotka | popis |
|---|---|---|---|---|
| `NonManifoldEdges` | `--nonmanifold` | `Cut` | — | `cut` / `pair-best` / `strict`, viz [02](02-nacitani-a-topologie.md#7-politika-pro-nemanifoldní-hranu) |
| `WeldVertices` | `--weld` | `false` | — | svaření koincidentních vrcholů |
| `WeldTolerance` | `--weld-tolerance` | `1e-6` | podíl úhlopříčky | vzdálenost pro svaření |
| `DegenerateAreaFraction` | — | `1e-10` | podíl **průměrné** plochy stěny | práh degenerace trojúhelníka |
| `RemoveDuplicateFaces` | — | `true` | — | odstranění stěn nad stejnou trojicí vrcholů |
| `RepairOrientation` | `--no-orientation-repair` (invertuje) | `true` | — | propagace navinutí + orientace uzavřených komponent ven |

## Křivost — `CurvatureOptions`

| parametr | přepínač | výchozí | jednotka | popis |
|---|---|---|---|---|
| `NeighbourhoodWidth` | `--nbhood` | `8.0` | násobek průměrné hrany | poloměr okolí pro proložení |
| `MinimumNeighbours` | — | `6` | počet | méně → `InsufficientNeighbours` |
| `PlanarThreshold` | `--planar-threshold` | `0.02` | bezrozměrné | `max(|kMin|,|kMax|) · r` pod touto hodnotou = rovina |
| `UmbilicThreshold` | `--umbilic-threshold` | `0.05` | bezrozměrné | `(kMax−kMin)/2 · r` pod touto hodnotou = umbilický |
| `MinimumPivotRatio` | — | `1e-6` | bezrozměrné | poměr pivotů LDL, pod tím `IllConditioned` |
| `WeightSigmaFraction` | — | `0.5` | podíl poloměru okolí | σ gaussovské radiální váhy |
| `DegreeOfParallelism` | — | `-1` | počet | `-1` = počet jader |

### Jak volit prahy degenerace

`aniso` a `curv` čtou jako „o kolik radiánů se plocha přes okolí stočí". Proto:

- **`--umbilic-threshold` výš** (např. `0.1`) → přísnější, zamítne víc hraničních směrů. Vhodné,
  když jsou trasované čáry na hladkých místech evidentně nesmyslné.
- **`--umbilic-threshold` níž** (např. `0.03`) → propustí víc směrů. **Pod ~0.02 nedoporučuji** —
  začnou procházet směry, které jsou jen šum, což je přesně to, čemu se práh vyhýbá.
- **`--planar-threshold`** je téměř vždy dobré nechat být. Vyšší hodnota označí za rovinu i mírně
  zakřivené oblasti.

### Jak volí `--nbhood`

Kompromis mezi šumem a rozlišením:

- **větší** okolí → hladší odhad, ale rozmazané skutečné rysy. Odhad je zkreslený směrem ke
  **střední** křivosti okolí, což je na plochách s proměnnou křivostí měřitelné (viz
  [07 — Testování](07-testovani.md)).
- **menší** okolí → ostřejší detail, citlivější na šum skenu a na nepravidelnou triangulaci.

Hodnota `8` je převzatá z původní implementace kvůli porovnatelnosti.

## Trasování — `TracingOptions`

| parametr | přepínač | výchozí | jednotka | popis |
|---|---|---|---|---|
| `StepLength` | `--step` | `1.0` | násobek průměrné hrany | délka oblouku mezi vzorky |
| `MaxLength` | `--length` | `0.5` | podíl úhlopříčky | maximální délka čáry; každá půlka dostane polovinu |
| `MaxSamples` | — | `4096` | počet | tvrdý strop na čáru |
| `MinSamples` | — | `8` | počet | kratší čáry se zahodí |
| `MaxDegenerateRun` | `--max-degenerate-run` | `5` | počet vzorků | délka přemostění degenerované oblasti paralelním přenosem |
| `Field` | `--field` | `Max` | — | `min` / `max` — na kterém poli se seeduje |
| `MaxLines` | `--lines` | `50` | počet | horní mez počtu čar |
| `SeedSpacing` | `--seed-spacing` | `0.05` | podíl úhlopříčky | minimální rozestup seedů |
| `SelfIntersectionRadius` | — | `0.5` | násobek kroku | vzdálenost, pod kterou se čára považuje za uzavřenou |
| `SelfIntersectionLookback` | — | `6` | počet vzorků | kolik posledních vzorků se při testu ignoruje |
| `DegreeOfParallelism` | — | `-1` | počet | `-1` = počet jader |

### `--step`

`κ_g` je druhá diference poloh, tedy nejšumnější kanál podpisu. **Pod `1.0` nemá smysl jít** —
signál se ztratí v triangulaci. Nahoru (`2.0`, `3.0`) se dá jít pro řidší, hladší podpis za cenu
detailu.

### `--length`

Delší čáry nesou delší podpis, tedy specifičtější otisk — ale mají větší šanci narazit na okraj
nebo na degenerovanou oblast. Na roztříštěných modelech je stejně omezuje `Boundary`.

## Export

| parametr | přepínač | výchozí | popis |
|---|---|---|---|
| `ColorBy` (síť) | `--color-by` | `flags` | `flags`, `aniso`, `kmin`, `kmax`, `mean`, `gauss`, `confidence`, `kappa-g`, `line` |
| `ColorBy` (trubičky) | `--tube-color-by` | `line` | totéž |
| `TubeRadius` | `--tube-radius` | `0.2` | násobek průměrné hrany |
| `SurfaceOffset` | — | `0.25` | násobek průměrné hrany; odsazení proti z-fightingu |
| `TubeSides` | — | `6` | rozlišení průřezu trubičky |
| výstupní adresář | `--out`, `-o` | `out` | |

U výčtových přepínačů se nerozlišují velká písmena a pomlčky ani podtržítka nevadí — projde
`pair-best`, `pair_best` i `PairBestContinuation`. Při překlepu program vypíše seznam platných
hodnot.

---

## Recepty

### Roztříštěný model dává málo čar

Typicky hodně komponent a hodně okrajů (`geb1.obj`: 47 komponent, `cha1.obj`: 1258).

```bash
--lines 100 --seed-spacing 0.02
```

Na `hip1.obj` to zvedne výsledek ze 44 na 92 čar.

### Čáry končí brzy na `Degenerate`

Model má hodně kulových nebo plochých oblastí.

```bash
--max-degenerate-run 12      # projde delší kulovou záplatou
--umbilic-threshold 0.03     # méně bodů se označí za umbilické
```

### Hrubší, méně šumný podpis

```bash
--step 2.0 --nbhood 12
```

### Ostřejší detail

```bash
--step 1.0 --nbhood 4
```

Pozor: menší okolí znamená citlivost na šum skenu.

### Síť se rozpadá na tisíce komponent

Soubor nejspíš ukládá vlastní kopii vrcholů pro každý trojúhelník.

```bash
--weld
```

`inspect` to potvrdí: bez `--weld` je počet komponent zhruba roven počtu trojúhelníků.

### Reprodukce původního chování při načítání

```bash
--nonmanifold strict --flip-z
```
