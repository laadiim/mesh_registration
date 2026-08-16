# 05 — Výstupy

Zdroje: `src/MeshRegistration.IO/Export/`

Příkaz `trace` zapíše pro vstup `<název>.obj` pět souborů (plus `.mtl`).

---

## Přehled

| soubor | formát | k čemu |
|---|---|---|
| `<n>_lines_tube.obj` + `.mtl` | OBJ trojúhelníky + materiály | **hlavní vizualizace** — čáry jako trubičky, vidět v každém režimu stínování |
| `<n>_lines.obj` | OBJ `v` + `l` | přesné polyčáry, malý soubor, vstup pro další nástroje |
| `<n>_curvature.obj` | OBJ s barvami vrcholů | vstupní síť obarvená podle skaláru |
| `<n>_samples.csv` | CSV | **rozhraní pro fázi 2** |
| `<n>_report.json` | JSON | strojově čitelný report |

---

## `_lines_tube.obj` — trubičky

Každá čára se převede na trubičku z trojúhelníků. Prstenec v každém vzorku leží v rovině kolmé
na čáru; protože je směr čáry tečný k ploše, je normála plochy na něj už kolmá a tvoří přirozenou
první osu prstence, která se podél čáry nekroutí:

```
osaA = normála plochy
osaB = směr × normála
bod_k = střed + poloměr · (cos θ_k · osaA + sin θ_k · osaB),   θ_k = 2πk/N
```

Sousední prstence se propojí čtyřúhelníky, každý jako dva trojúhelníky. Výchozí `N = 6`.

Střed se odsadí o `SurfaceOffset` (výchozí 0.25 násobku průměrné hrany) podél normály, aby
trubička nesoupeřila o hloubku se sítí, na které leží.

**Materiály.** Jeden `newmtl` na čáru, barva z `ColorRamp.Categorical`. Odstíny postupují
o **zlatý úhel** (137.508°), což drží sousední indexy daleko od sebe na barevném kruhu
a odstraňuje viditelné pásování rovnoměrně rozložené palety. Sytost a jas se navíc mírně střídají,
aby byly blízké odstíny rozlišitelné.

Při barvení podle skaláru (`--tube-color-by kappa-g` apod.) je materiál neutrálně šedý a barvu
nesou vrcholy.

Trubičkové sítě jsou samy o sobě korektní: test `TubeExport_ProducesValidGeometryAndAMaterialPerLine`
je načte zpět a ověří, že mají nula nemanifoldních hran a přesně tolik komponent, kolik je čar.

## `_lines.obj` — polyčáry

```
v x y z r g b
...
g line_0000
l 1 2 3 4 5 ...
```

Přesná geometrie, řádově menší soubor. MeshLab importuje OBJ prvky `l` jako hrany, které se ale
zobrazí jen v drátových režimech — proto je to doplněk, ne hlavní výstup.

## `_curvature.obj` — obarvená vstupní síť

Vstupní síť s barvou na vrchol jako `v x y z r g b`. Jde o široce podporované rozšíření OBJ, ne
o součást formátu; MeshLab ho čte, což je zde rozhodující. Barvy se zapínají ikonou
**Vertex Color** (nebo *Render → Color → Per Vertex*).

Skalár se volí přes `--color-by`.

### `--color-by flags` (výchozí)

Přímá vizuální kontrola ošetření degenerace:

| barva | RGB | význam |
|---|---|---|
| zelená | 70,180,90 | použitelný hlavní směr |
| modrá | 60,110,220 | `Umbilic` — kulová oblast, směr neexistuje |
| šedá | 150,150,150 | `Planar` — rovina |
| oranžová | 230,150,40 | `Boundary` — jednostranné okolí |
| červená | 200,40,40 | `Unusable` — proložení selhalo |

Modré a šedé oblasti jsou přesně ta místa, kde původní verze vyráběla NaN, a přesně ta, kde
`SeedSelector` nezakládá čáry. Selhání, které se dřív projevilo až jako NaN hluboko v traceru, je
tu vidět na první pohled.

### Skalární mapy

| druh | mapa | pro |
|---|---|---|
| znaménkové | divergentní modrá–bílá–červená, nula uprostřed | `kmin`, `kmax`, `mean`, `gauss` |
| neznaménkové | sekvenční tmavě modrá → tyrkysová → žlutá | `aniso`, `confidence` |

**Robustní rozsah.** Rozsah se bere mezi 2. a 98. percentilem, ne mezi skutečnými extrémy. Pole
křivostí běžně obsahuje hrstku obrovských odlehlých hodnot na tenkých trojúhelnících; škálování na
skutečné extrémy by všechno ostatní stlačilo do jediného odstínu. Nepoužitelné vzorky se do
rozsahu nepočítají a kreslí se pevně červeně.

Anizotropie se hlásí **bezrozměrně** (křivost × poloměr okolí), takže stejné barvy znamenají totéž
na modelu jakékoliv velikosti.

## `_samples.csv` — rozhraní pro fázi 2

```
lineId,sampleIndex,arcLength,x,y,z,nx,ny,nz,kMin,kMax,kappaG,confidence,flags,followed,triangle
0,0,0,52.0924277930,32.5496230697,57.1255159619,-0.7922722186,...,None,46717
```

| sloupec | obsah |
|---|---|
| `lineId` | identifikátor čáry, přidělený v pořadí seedů |
| `sampleIndex` | pořadí vzorku v rámci čáry |
| `arcLength` | délka oblouku od začátku čáry |
| `x,y,z` | poloha vzorku |
| `nx,ny,nz` | jednotková normála plochy |
| `kMin,kMax` | hlavní křivosti |
| `kappaG` | znaménková geodetická křivost |
| `confidence` | kvalita proložení, ⟨0,1⟩ |
| `flags` | textový výčet oddělený `|`, např. `Umbilic|Planar` |
| `followed` | které hlavní pole vzorek sledoval: `Max`, `Min`, nebo `Transported` |
| `triangle` | index trojúhelníka, ve kterém vzorek leží |

Trojice `kMin, kMax, kappaG` je podpis, který bude párovat fáze 2. Vzorky jsou ekvidistantní
v délce oblouku — právě to dělá ze dvou čar porovnatelné sekvence.

Zapisuje se invariantní kulturou (soubor je přenositelný) a formátem `"R"` (round-trip), takže
opětovné načtení nic neztratí. Bez BOM.

## `_report.json`

```json
{
  "Topology": {
    "InputVertexCount": 169608,
    "OutputVertexCount": 169862,
    "NonManifoldEdgeCount": 32,
    "NonManifoldEdgePolicy": "Cut",
    "NonManifoldVerticesFound": 221,
    "IsolatedVertexCount": 23,
    "ConnectedComponentCount": 1258,
    "AverageEdgeLength": 0.0025331,
    "DiagonalLength": 1.28256,
    "IsClean": false
  },
  "Curvature": {
    "VertexCount": 169862,
    "PlanarVertices": 4761,
    "UmbilicVertices": 44572,
    "UnusableVertices": 2455,
    "BoundaryVertices": 24990,
    "NeighbourhoodRadius": 0.020265,
    "UsableFraction": 0.6957
  },
  "Tracing": {
    "SeedCount": 50,
    "LineCount": 28,
    "TotalSamples": 550,
    "StepLength": 0.0025331,
    "MeanLineLength": 0.03419,
    "DegenerateSamples": 48,
    "EndReasons": { "SelfIntersection": 32, "Boundary": 18, "Degenerate": 6 },
    "NonFiniteSamples": 0,
    "SamplesOnMaxField": 402,
    "SamplesOnMinField": 100,
    "MaxFieldFraction": 0.8008
  }
}
```

Oddíl `Curvature` je hlavní statistika fáze odhadu křivosti: kolik plochy vůbec nese použitelný
hlavní směr. `SamplesOnMaxField` / `SamplesOnMinField` říkají, na kterém poli čáry reálně skončily
— `--field` určuje jen směr na seedu, dál rozhoduje spojitost křivky.

`NonFiniteSamples` je explicitní kontrola, ne jen statistika: pokud není nula, příkaz `trace`
skončí návratovým kódem **2**. Únik NaN z odhadu křivosti byl původní chyba, takže se nehlásí jako
poznámka, ale jako selhání běhu.

Výčty se serializují jmény, aby byl soubor čitelný bez zdrojového kódu. Metadata jsou generovaná
překladačem (`JsonSourceGenerationOptions`), takže se za běhu nepoužívá reflexe a nástroj by šel
publikovat trimovaný nebo AOT.

## Poznámky k zápisu

- **Bez BOM.** `UTF8Encoding(encoderShouldEmitUTF8Identifier: false)` u všech textových výstupů —
  některé parsery OBJ považují BOM za obsah.
- **Invariantní kultura, formát `"G9"`** u geometrie. OBJ zapsaný s desetinnou čárkou je
  nepoužitelný všude; `G9` round-tripuje jednoduchou přesnost a zůstane kompaktní.
- `TextWriter.WriteLine(IFormatProvider, ...)` **neexistuje**; používá se
  `writer.WriteLine(string.Create(CultureInfo.InvariantCulture, $"..."))`.
