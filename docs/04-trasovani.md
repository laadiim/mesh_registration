# 04 — Trasování čar

Zdroje: `src/MeshRegistration.Algorithms/Tracing/SurfaceWalker.cs`, `LineTracer.cs`,
`SeedSelector.cs`

---

## 1. Co se trasuje

Integrální křivka hlavního směrového pole: křivka, jejíž tečna je v každém bodě hlavním směrem
křivosti. Taková křivka je vlastností plochy samotné, nezávislou na jejím umístění v prostoru —
proto se na dvou skenech téhož objektu (v překryvu) jedná o tytéž křivky.

Podél nich se v konstantním kroku délky oblouku vzorkuje podpis `(kMin, kMax, κ_g)`.

## 2. Pochod po ploše (`SurfaceWalker`)

Trasa je **„nejpřímější geodetika"**: uvnitř každého trojúhelníka úsečka, na hraně se směr přenese
rotací, která jednu stěnu rozvine na druhou. Délka oblouku se zachovává přesně, což je právě to,
co dělá vzorky v pevném kroku porovnatelné mezi dvěma sítěmi.

### Hledání výstupní hrany

Barycentrické souřadnice jsou podél přímé trasy **afinní funkce**, takže paprsek opustí trojúhelník
přes hranu naproti vrcholu `i` právě když váha `i` klesne na nulu:

```
b(p + s·d) = b(p) + s·(b(p+d) − b(p))
             ╰─b─╯      ╰──── db ────╯

pro každé i s db[i] < 0:   sᵢ = b[i] / (−db[i])
výstup = argmin sᵢ
```

Tím se „kterou hranou projdu a kde" redukuje na tři lineární řešení, bez samostatného průsečíku
přímek, který by mohl zdegenerovat. Váhy, které podél paprsku neklesají, patří hranám, které
paprsek nikdy nepřekročí.

Test „opravdu klesá" je **relativní** k největší rychlosti změny (`db` se škáluje jako
`1/velikost trojúhelníka`), takže absolutní epsilon by byl závislý na měřítku.

> **Oprava.** Bere se **nejmenší** kladné `s`, tedy první zasažená hrana. Původní kód si držel
> **největší** platné `s` (`tryIt = s > sMax`), což vybere protilehlou stranu trojúhelníka pokaždé,
> když projde víc než jeden test hrany — a pošle pochod do špatného souseda.

### Přechod přes hranu

```
axis = normalize(v_b − v_a)                      osa sdílené hrany
φ    = signedAngle(n_stará, n_nová, axis)        dihedrální úhel
d′   = Rodrigues(d, axis, φ)
```

Jediná operace, prokazatelně zachovávající délku i úhel vůči sdílené hraně, bez jakékoliv analýzy
případů. Původní kód skládal směr zpět z hranového vektoru a úhlu získaného přes `acos`, a musel
znaménko hádat.

Osa se bere **z hrany**, ne z vektorového součinu normál. Pro dvě sousední stěny jsou tyto dvě věci
rovnoběžné, ale u zpět přeloženého případu (normály antiparalelní) dá součin nulu a žádnou
použitelnou osu, zatímco hrana je pořád dobře definovaná.

Po rotaci se směr znovu promítne do roviny nové stěny a normalizuje, aby se zaokrouhlovací chyba
nehromadila přes dlouhou trasu.

### Pojistky

- **Nejvýše 256 přechodů na krok** (`SurfaceWalker.MaxCrossingsPerStep`) → `WalkStatus.Stuck`.
  Krok o velikosti jedné hrany překročí jeden až dva trojúhelníky; přiblížení k mezi znamená, že
  se něco pokazilo. Původní smyčka byla `while (true)` bez úniku, takže konfigurace odrážející se
  mezi dvěma trojúhelníky zatuhla. Na celém datasetu tato pojistka nikdy nezasáhla.
- **Chybějící soused** (okraj sítě nebo rozříznutá nemanifoldní hrana) → `WalkStatus.HitBoundary`.

## 3. Volba směru (`LineTracer`)

Na každém novém vzorku se vyhodnotí křivost a aktualizuje směr.

### Hlavní směry jsou přímkové pole

Dvě komplikace, obě reálné:

1. Každý hlavní směr je určen jen **až na znaménko**.
2. Označení „minimální" a „maximální" se **prohazuje** všude, kde se obě křivosti protnou — tedy
   právě podél umbilických křivek.

Sledovat `DirMax` podle jména proto znamená skákat mezi různými integrálními křivkami. Tracer místo
toho vybírá ze **všech čtyř** kandidátů `±DirMin`, `±DirMax` ten, který nejlépe pokračuje
v paralelně přeneseném předchozím směru:

```
best = argmax |candidate · incoming|,   orientovaný tak, aby mířil dopředu
```

Na anizotropní ploše jsou obě pole kolmá a příchozí směr je už jednomu z nich blízký, takže tohle
samo od sebe drží jedno pole — a přitom zůstane správné tam, kde si označení vymění role.

#### Které pole čára reálně sleduje

`--field` určuje **jen směr na seedu**. Dál rozhoduje spojitost, takže čára může přejít na druhé
pole. Každý vzorek proto nese `FollowedDirection` (`Max` / `Min` / `Transported`) — je to
měření, ne předpoklad.

Naměřeno na reálných datech (podíl vzorků, které sledovaly maximální pole):

| model | `--field max` | `--field min` |
|---|---|---|
| `brd1` | 93 % | 14 % |
| `hip1` | 88 % | 9 % |
| `hea1` | 85 % | 9 % |
| `dra1` | 74 % | 13 % |
| `Head_2` | 64 % | 31 % |
| `kac1` | **52 %** | **51 %** |

Volba seedu tedy dominuje — čára většinou zůstane, kde začala. Míchání je ale znatelné (7–48 %)
a na `kac1` je poměr v obou režimech zhruba 50/50: ten model má hodně umbilických křivek, přes
které se označení vyměňuje.

Na válci, kde se křivosti nikdy neprotnou a žádná umbilická křivka neexistuje, je to 100/0 —
hlídají to testy `Cylinder_LineStaysOnTheFieldItWasSeededOn`.

**Proč se nedrží označení natvrdo.** Bylo by to geometricky špatně: v místě výměny označení by
čára odbočila o 90°, tedy přestala by být integrální křivkou. Navíc je poloha výměny citlivá na
šum, takže dva skeny téhož objektu by odbočily jinde a vytrasovaly různé křivky — přesně to, co by
fázi 2 rozbilo. Spojitost dá tutéž křivku bez ohledu na označení.

### Degenerované oblasti

Pokud vzorek **nemá použitelný směr** (`Umbilic` nebo `Planar`), tracer se na směrové pole přestane
ptát a pokračuje **čistým paralelním přenosem**, tedy trasuje geodetiku.

Odůvodnění: uvnitř takové oblasti směrové pole neexistuje, není co sledovat. Přenos:

- **přemostí** krátké přechody, čára i její parametrizace délkou oblouku zůstanou celistvé;
- podpis přes ten úsek poctivě zaznamená „rovina" nebo „koule", což je samo o sobě použitelná
  informace;
- po `MaxDegenerateRun` (výchozí 5) po sobě jdoucích vzorcích se čára **ukončí** — přes dlouhý běh
  už přenesený směr nemá s plochou nic společného a pokračování by bylo vymýšlení.

Seed, který sám nemá použitelný směr, čáru vůbec nezaloží.

> Kontroluje se `CurvatureSample.HasUsableDirection` (příznaky), **ne** to, jestli je vektor
> nenulový. Eigensolver vždy vrátí konečný vektor — to je jeho smysl — takže nenulovost není
> důkaz, že směr existuje.

### Ukončovací důvody

| `LineEnd` | kdy |
|---|---|
| `LengthReached` | vyčerpán délkový rozpočet |
| `Boundary` | okraj sítě nebo rozříznutá nemanifoldní hrana |
| `Degenerate` | příliš dlouhý běh bez definovaného směru |
| `SelfIntersection` | návrat na dřívější část sebe sama |
| `SampleLimit` | vyčerpán počet vzorků |
| `Stuck` | pojistka pochodu; nemá nastávat |

Self-intersection: vzdálenost pod `SelfIntersectionRadius × krok` (výchozí 0.5) od vzorku staršího
než `SelfIntersectionLookback` (výchozí 6). Odstup je nutný, protože po sobě jdoucí vzorky jsou
z definice vzdálené jeden krok.

## 4. Skládání čáry

Čára roste ze seedu **oběma směry**, každá půlka dostane polovinu rozpočtu. Výsledek se poskládá:

```
zpětná půlka obráceně  →  seed  →  dopředná půlka
```

Seed je prvním prvkem obou půlek, takže jedna kopie odpadne.

Teprve nad **hotovou uspořádanou** polyčárou se v jednom průchodu dopočítá délka oblouku
a geodetická křivost. To je jednodušší a méně chybové než akumulace během růstu — a hlavně
znaménko `κ_g` vyjde správně samo. Původní kód ho počítal při trasování a musel zpětnou půlku ručně
negovat (`firstDirection ? gc : -gc`).

## 5. Geodetická křivost

Křivost křivky ležící na ploše se rozpadá na dvě složky:

```
κ² = κ_n² + κ_g²
```

- `κ_n` — **normálová**, vynucená plochou samotnou, o křivce nic neříká;
- `κ_g` — **geodetická**, ta část ohybu, která leží uvnitř plochy. Nulová podél geodetiky.

Do podpisu patří `κ_g`, protože ta popisuje křivku.

Rozdělí se tím, že se obě tětivy **promítnou do tečné roviny** a teprve pak se změří jejich úhel:

```
v₁ = p[i]   − p[i−1],   v₁ ← v₁ − n·(v₁·n)
v₂ = p[i+1] − p[i],     v₂ ← v₂ − n·(v₂·n)

φ = ∠(v₁, v₂)                     úhel otočení uvnitř plochy
h = (|v₁| + |v₂|) / 2
κ_g = 2·sin(φ/2) / h  ·  sign((v₁ × v₂) · n)
```

Vzorec `2·sin(φ/2)/h` je přesná diskrétní verze: pro polyčáru vepsanou do kružnice o poloměru `ρ`
s tětivou `h` platí `h = 2ρ·sin(φ/2)`, tedy `1/ρ = 2·sin(φ/2)/h`.

> **Oprava.** Bez promítnutí vyjde křivost křivky **v prostoru**, což je jiná veličina. Na válci
> jsou hlavní kružnice geodetiky (válec je rozvinutelný — po rozvinutí do roviny se z kružnice
> stane přímka), takže správná odpověď je `κ_g = 0`, zatímco prostorová křivost je `1/R`. Původní
> kód bral úhel mezi syrovými trojrozměrnými tětivami, a počítal tedy to druhé.
>
> Tohle jsem měl špatně i ve své první verzi; odhalil to test na válci
> (`Cylinder_PrincipalLinesAreGeodesics`).

Koncové vzorky nemají otočení definované a zůstávají na nule.

`κ_g` je druhá diference poloh, tedy **nejšumnější kanál podpisu**. Krok pod `1.0` násobku
průměrné hrany nemá smysl.

## 6. Výběr seedů (`SeedSelector`)

Dvě pravidla:

1. Seed musí ležet tam, kde hlavní směr **skutečně existuje** — nikdy na ploché ani kulové
   záplatě, na nepoužitelném proložení nebo na okraji.
2. Seedy musí být rozprostřené po modelu, ne nahloučené na jeho jediném nejanizotropnějším místě.

> Původní kód seedoval rovnoměrně náhodně přes trojúhelníky. Na převážně hladkém skenu většina
> takových seedů padne tam, kde směrové pole není definované, takže většina čar byla nesmyslná už
> od nultého kroku.

Postup:

```
kandidáti = vrcholy s HasUsableDirection ∧ ¬Boundary ∧ ¬Isolated
skóre     = aniso · Confidence            (aniso je bezrozměrné)
seřadit sestupně, při shodě podle indexu vrcholu
hladově přijímat, dokud je vzdálenost k dosud přijatým ≥ SeedSpacing · úhlopříčka
```

**Determinismus bez generátoru náhody.** Úplné uspořádání (skóre, pak index) dělá výběr
reprodukovatelným triviálně. Původní kód měl v „deterministickém" režimu chybu: vytvořil N
generátorů, všechny se **stejným** seedem, takže každé vlákno generovalo shodnou posloupnost.

Seed se umístí kousek dovnitř přilehlého trojúhelníka (5 % k těžišti), ne přesně na vrchol — na
rohu trojúhelníka je test výstupní hrany nejednoznačný, protože je naráz nulová víc než jedna
barycentrická váha.

## 7. Paralelizace a výkon

`Parallel.ForEach` přes seedy, výsledky se zapisují podle indexu → nezávislé na rozvrhování.

Co bylo v původní verzi drahé a co se s tím stalo:

| původní chování | nyní |
|---|---|
| `new CornerTable(mesh)` a `new CurvatureOracle(mesh)` v konstruktoru čáry **a znovu** v `MoveInCurvatureDirection()` — 2 plné přestavby na čáru | postaveno jednou, sdíleno |
| `mesh.Clone()` a čtyři `ToList()` na čáru | žádná kopie; síť je immutable |
| tracer přidával vizualizační body do sítě, po které šel | vizualizace až v exportérech |
| `Console.WriteLine` na **každý krok** | žádný výpis v horké smyčce |
| `getCurvaturePoS` = záplavový průchod + proložení na každém kroku | interpolace předpočítaných operátorů, O(1) |
| `PointOnSurface` řešil barycentrika přes MathNet `Solve` na soustavě 4×3 | uzavřený vzorec, ~20 operací |

Výsledek: trasování 50 čar trvá 21–141 ms napříč celým datasetem (viz [08](08-vysledky.md)).
