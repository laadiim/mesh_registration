# 12 — Analytické tvary

Zdroj: `src/MeshRegistration.Core/Mesh/AnalyticShapes.cs`

Místo vstupního souboru lze nechat vygenerovat plochu, jejíž hlavní křivky známe v uzavřeném
tvaru. Pak je poznat, jestli trasování funguje.

```bash
meshreg trace --shape waves --out out --tube-color-by followed
meshreg inspect --shape torus
```

---

## Proč to existuje

Na reálném skenu není s čím výsledek porovnat. Čára vypadá věrohodně, ale nic neříká, jestli
opravdu sleduje hlavní směr. Na válci musí být čára maximální křivosti **kružnice kolmá k ose** —
jakákoliv odchylka je vidět okamžitě.

Program po doběhnutí vypíše, co má být vidět:

```
  expected:
    Concentric circles centred on the origin, or radial spokes running outward
    — nothing else. The two families swap roles at the umbilic rings between
    crest and trough, ...
```

## Dostupné tvary

| tvar | co má být vidět | rovinné | umbilické | čar |
|---|---|---|---|---|
| `plane` | **nic** — jakákoliv čára je chyba | 100 % | — | 0 |
| `sphere` | **nic** — jakákoliv čára je chyba | — | 100 % | 0 |
| `cylinder` | kružnice kolmé k ose (`max`), přímky podél osy (`min`) | 0 % | 0 % | 50 |
| `torus` | malé kružnice kolem tubusu (`max`), velké kolem osy (`min`) | 0 % | 0 % | 50 |
| `waves` | soustředné kružnice nebo radiální paprsky | 0 % | 5.4 % | 50 |
| `parabolic-cylinder` | dokonale přímé rulings (`max`) | 0 % | 0 % | 50 |
| `paraboloid` | kružnice/paprsky; střed umbilický, bez čar | 0 % | 2.3 % | 50 |
| `saddle` | dvě kolmé rodiny křivek | 0 % | 0 % | 50 |
| `monkey-saddle` | umbilický bod se třemi rameny | 0.7 % | 3.6 % | 50 |
| `ellipsoid` | uzavřené křivky, 4 umbilické body | 0 % | 0.8 % | 50 |

Čísla naměřená při `--shape-resolution 120`. Ve všech případech nula nekonečných hodnot.

## Tři návrhová rozhodnutí

### Kartézská mřížka, ne polární

`waves` a `paraboloid` jsou rotačně symetrické, takže jejich hlavní křivky jsou přesně soustředné
kružnice a přesně radiální paprsky. Kdyby se síťovaly na **polární** mřížce, hrany sítě by ležely
na těch křivkách — a tracer, který by jen kopíroval hrany, by vypadal správně.

Kartézská mřížka to zarovnání odstraní. Kružnice pak musí vzniknout z pole křivosti, ne z topologie.

### Kruhový výřez u rotačně symetrických ploch

Původně jsem `waves` generoval na čtverci. Z 42 čar jich 8 nepadlo ani do jedné rodiny — a nešlo
o chybu trasování: ty čáry měly **100 % vzorků u hranice čtverce**.

Čtvercový obrys rozbíjí právě tu symetrii, která se má ověřovat. U rohů je okolí jednostranné ve
směru, který s plochou nesouvisí. Rotačně symetrické tvary se proto ořezávají na **kruh**
(`HeightFieldDisc`) — mřížka zůstane kartézská, ale obrys je se symetrií slučitelný.

Po opravě: **38 kružnic, 12 paprsků, 0 nejasných.**

### Obdélníkový výřez u parabolického válce

Křivost paraboly klesá jako `(1 + a²x²)^(−3/2)`, takže široký čtvercový výřez je z větší části
skoro plochý — a tedy klasifikovaný jako umbilický. Naměřeno **59.5 %** umbilických vrcholů.

To je zrádné: v umbilické oblasti jde čára paralelním přenosem, což je **taky přímka**, takže
kontrola „rulings jsou přímé" by prošla z nesprávného důvodu. Zestrmení paraboly nepomůže (strmější
parabola plochne rychleji). Řešením je **obdélník** — úzký napříč parabolou, dlouhý podél rulings,
což je i přirozený tvar žlabu. Výsledek: **0 % umbilických**.

## Pozor na znaménko

U žlabu `z = a·x²/2` s kladným `a` a vnější normálou je parabolická křivost **záporná**, takže:

```
kMax = 0        podél rulings (přímé)
kMin = −a/(…)   napříč (parabola)
```

Přímé rulings jsou tedy **maximum**, ne minimum — opak intuice „plošší znamená menší". Napsal jsem
to nejdřív obráceně a odhalil to až test.

## Parametry

| přepínač | výchozí | popis |
|---|---|---|
| `--shape` | — | název tvaru |
| `--shape-resolution` | `120` | dělení mřížky; vyšší = jemnější trojúhelníky |
| `--save-shape` | `false` | zapíše i samotnou plochu jako `<název>_shape.obj` |

Protože jsou všechny prahy bezrozměrné, změna `--shape-resolution` mění délku hrany a s ní úměrně
i poloměr okolí a krok trasování — výsledek zůstane kvalitativně stejný.

Zadat naráz soubor i `--shape` je chyba a program ji ohlásí.

## Sdílení s testy

Generátory jsou v knihovně, ne v testech. `tests/…/AnalyticSurfaces.cs` je jen tenký adaptér.
Plochy, které testy ověřují, a plochy, které `--shape` kreslí, jsou tak doslova tentýž kód —
jinak by se to časem rozešlo.

Testy neověřují jen „nespadlo to". `Waves_EveryLineIsAConcentricCircleOrARadialSpoke` měří rozptyl
poloměru a opsaný úhel každé čáry a trvá na tom, aby padla do jedné z obou rodin;
`ParabolicCylinder_MaxFieldLinesAreStraight` kontroluje, že se čára neodchýlí napříč rulings;
`Cylinder_MaxFieldLinesStayAtConstantHeightAndRadius` hlídá konstantní poloměr i výšku.
