# 03 — Odhad křivosti

Zdroje: `src/MeshRegistration.Core/Numerics/Sym2x2.cs`, `Sym3x3Solver.cs`,
`src/MeshRegistration.Algorithms/Curvature/ShapeOperatorField.cs`

---

## 1. Co se počítá

V bodě plochy `p` s jednotkovou normálou `n` popisuje zakřivení **Weingartenův operátor** (operátor
tvaru) `S` — lineární zobrazení tečné roviny do sebe, které tečnému posunu přiřadí odpovídající
změnu normály. Je symetrický, takže má reálná vlastní čísla a ortogonální vlastní vektory:

- vlastní čísla `kMin ≤ kMax` = **hlavní křivosti**
- vlastní vektory = **hlavní směry**

Odvozené veličiny: střední křivost `H = (kMin + kMax)/2`, Gaussova křivost `K = kMin · kMax`.

**Znaménková konvence.** Zde je `S = dN`. S vnějšími normálami má tedy konvexní plocha kladnou
křivost a koule o poloměru `R` má `kMin = kMax = 1/R`. (Část literatury definuje `S = −dN`, kde by
totéž vyšlo záporně.) `MeshBuilder` orientuje uzavřené komponenty ven, takže je konvence dobře
definovaná.

## 2. Proložení metodou nejmenších čtverců

V tečném rámci `(e₁, e₂, n)` v bodě `p` se pro každého souseda `qᵢ` s normálou `nᵢ` a vahou `wᵢ`
zavede

```
uᵢ = (e₁·(qᵢ − p),  e₂·(qᵢ − p))     tangenciální posun
mᵢ = (e₁·nᵢ,        e₂·nᵢ)           průmět sousedovy normály do tečné roviny
```

Operátor `S = [[s₀, s₁], [s₁, s₂]]` má splňovat `S·uᵢ ≈ mᵢ`. Vážená úloha nejmenších čtverců

```
min  Σ wᵢ ‖S·uᵢ − mᵢ‖²
 S    i
```

má tři neznámé a každý soused dodá dvě rovnice. Derivace podle `s₀`, `s₁`, `s₂` dají normální
rovnice — symetrickou pásovou soustavu 3×3, kde `x`, `y` jsou složky `uᵢ` a `mx`, `my` složky `mᵢ`:

```
⎡ Σw x²      Σw xy         0     ⎤ ⎡s₀⎤   ⎡ Σw x·mx            ⎤
⎢ Σw xy   Σw(x² + y²)   Σw xy    ⎥ ⎢s₁⎥ = ⎢ Σw y·mx + Σw x·my  ⎥
⎣   0        Σw xy      Σw y²    ⎦ ⎣s₂⎦   ⎣ Σw y·my            ⎦
```

Prvek `(0,2)` je strukturálně nulový: `s₀` a `s₂` se nikdy nevyskytnou ve stejné rovnici.

Středový vrchol sám nepřispěje ničím — jeho posun je nulový a jeho normála se promítne na nulu.
To je důsledek, ne zvláštní případ.

> Sestavení soustavy je stejné jako v původním kódu; ten ji měl správně. Vyměněný je řešič
> a všechno kolem.

### Okolí

Sousedé se hledají záplavovým průchodem přes hrany, omezeným euklidovskou vzdáleností
`r = AverageEdgeLength × NeighbourhoodWidth` (výchozí `8`).

Rozšiřování **přes hrany**, ne prostorovým indexem, drží okolí na ploše: dva listy, které se
v prostoru míjejí těsně vedle sebe, zůstanou oddělené — a přesně to odhad křivosti potřebuje.

Návštěvy se značí **generačními razítky** (`int[] stamp` a čítač) místo pole `bool`. Nový dotaz
tak stojí inkrement, ne vynulování pole velikosti sítě; díky tomu je cena na vrchol nezávislá na
velikosti modelu. Každý pracovní oddíl má vlastní instanci.

### Váhy

```
wᵢ = plochaVrcholu(qᵢ) · exp(−dᵢ² / (2σ²)),    σ = r · WeightSigmaFraction  (výchozí 0.5)
```

Samotné vážení plochou — jediné, co dělal původní kód, přestože si nesl nevolané pomocné metody
přesně pro tohle — činí proložení citlivým na to, kde přesně bylo okolí uříznuto, protože vzdálený
soused váží stejně jako přilehlý. Gaussovský radiální člen to odstraní.

### Řešení soustavy

`Sym3x3Solver.Solve` provede **LDLᵀ rozklad** `M = L·D·Lᵀ` s jednotkovou dolní trojúhelníkovou `L`,
uzavřeně nad lokálními proměnnými — bez alokace a bez nepřímé adresace. Spustí se milionkrát na
síť.

Před rozkladem se matice normuje střední hodnotou diagonály, takže vrácený **poměr pivotů**

```
PivotRatio = min(d₀,d₁,d₂) / max(d₀,d₁,d₂) ∈ [0,1]
```

je čisté číslo a práh na něj (`MinimumPivotRatio`, výchozí `1e-6`) je bezrozměrný.

Nahrazuje Cramerovo pravidlo a ruční test singularity, který porovnával determinanty s pevnými
absolutními konstantami `1e-10` a `1e-6`. Ty nejsou invariantní vůči měřítku: táž geometrie
v milimetrech a v metrech dostávala různý verdikt.

## 3. Vlastní čísla 2×2 — oprava NaN

### Problém

Na **rovině** je `S = 0`. Na **kouli** je `S = (1/R)·I`. V obou případech platí `A == C` a `B == 0`.

Klasická větev pro vlastní vektor volí podle toho, který jmenovatel je větší:

```csharp
if (Math.Abs(b) < Math.Abs(a - e1))  v1 = -b / (a - e1);
else                                 v2 = (e1 - a) / b;
```

Pro `a == c, b == 0` vyjde `e1 == a`, takže podmínka je `0 < 0` → nepravda → jde se do větve
`(e1 − a) / b`, tedy `0 / 0` → **NaN**.

Ověřeno přepočtem větvení:

| vstup | výsledek `(e1, e2, v1, v2)` |
|---|---|
| rovina `S = 0` | `(0, 0, 1, NaN)` |
| koule `R = 10` | `(0.1, 0.1, 1, NaN)` |
| válec `R = 10` | `(0, 0.1, 0, 1)` ✓ |
| skoro-umbilický, `b = 1e-12` | směr skočí z `(1,0)` na `(−1,1)` |

Doprovodný test singularity to nezachytil, protože zkoumal **momentovou matici**, ne výsledný
operátor — a na rovině je momentová matice dokonale podmíněná. NaN pak propagoval do traceru,
jehož vlastní pojistka `moveVector.X == double.NaN` **nikdy nemůže zareagovat**, protože NaN se
nerovná sám sobě.

### Řešení, část 1: zůstat konečný

`Sym2x2.Eigen` používá vztah pro dvojnásobný úhel. Pro symetrickou 2×2 matici splňuje úhel
vlastního vektoru příslušného většímu vlastnímu číslu

```
tan(2θ) = 2B / (A − C)
```

odkud

```
mean = (A + C) / 2
r    = hypot((A − C)/2,  B)
kMax = mean + r
kMin = mean − r
θ    = ½ · atan2(2B,  A − C)
```

`atan2` je **totální**: `Atan2(0, 0)` je definováno a vrací `0`. Dělení ze vzorce zmizelo úplně.
Degenerovaný vstup tedy dá libovolný, ale **konečný a deterministický** směr místo NaN.
`hypot` navíc odstraní přetečení a podtečení v mezivýpočtu.

Ověření vůči stopě a determinantu, obojí testované:

```
kMax + kMin = A + C           (stopa)
kMax · kMin = A·C − B²        (determinant)
```

### Řešení, část 2: vědět, že odpověď nic neznamená

Konečné číslo **není** použitelný směr. V umbilickém bodě je hlavním směrem *každý* tečný směr,
takže žádný není vyznačený — to je vlastnost geometrie, ne aritmetiky. Žádný vzorec to nespraví;
správná odpověď je bod klasifikovat.

Křivost má rozměr `1/délka`, takže samotný práh na křivost skrytě předpokládá velikost modelu.
Vynásobením poloměrem okolí vzniknou čistá čísla:

```
aniso = (kMax − kMin)/2 · r     „o kolik radiánů se přes okolí liší zakřivení ve dvou směrech"
curv  = max(|kMax|,|kMin|) · r  „o kolik radiánů se plocha přes okolí stočí"
```

Klasifikace:

| podmínka | příznak | co platí |
|---|---|---|
| `curv < PlanarThreshold` (0.02) | `Planar` + `Umbilic` | rovina — ani hodnoty, ani směry nenesou informaci |
| jinak `aniso < UmbilicThreshold` (0.05) | `Umbilic` | koule — **hodnoty platí**, směr neexistuje |
| jinak | — | použitelné |

Rovina dostane oba příznaky, protože rovina *je* umbilická. Spotřebitel, kterého zajímá jen
„můžu použít směr?", tak testuje jediný příznak `Umbilic`; `Planar` je navíc informace, že ani
hodnoty nemají obsah.

Další příznaky ze samotného proložení: `Boundary`, `InsufficientNeighbours`, `IllConditioned`,
`Isolated`. Poslední tři tvoří masku `Unusable`.

**Proč na tom záleží.** Na reálných datech (viz [08](08-vysledky.md)):

| model | rovinné | umbilické |
|---|---|---|
| `dra1.obj` | 0.01 % | 2.4 % |
| `kac1.obj` | 0.31 % | 7.8 % |
| `hip1.obj` | 0.00 % | 29.1 % |
| `eie1.obj` | **43.7 %** | 29.7 % |

Na `eie1.obj` nemá **73 % vrcholů** definovaný hlavní směr. To jsou přesně místa, kde původní
verze vyráběla NaN.

### Spolehlivost

```
Confidence = neighbourFactor · conditionFactor · residualFactor · boundaryFactor  ∈ [0,1]
```

| činitel | vzorec | smysl |
|---|---|---|
| `neighbourFactor` | `clamp(počet / (2·MinimumNeighbours), 0, 1)` | proložení potřebuje redundanci |
| `conditionFactor` | `clamp(PivotRatio / 1e-3, 0, 1)` | podmíněnost soustavy |
| `residualFactor` | `sqrt(clamp(sᵀ·rhs / Σw‖mᵢ‖², 0, 1))` | podíl vysvětlené variace normál |
| `boundaryFactor` | `0.5` na okraji, jinak `1` | jednostranné okolí extrapoluje |

Reziduál je zdarma: pro **přesné** řešení normálních rovnic platí `sᵀMs = sᵀ·rhs`, takže

```
‖reziduum‖² = Σw‖mᵢ‖² − sᵀ·rhs
```

a stačí navíc akumulovat `Σw‖mᵢ‖²`. Není potřeba druhý průchod okolím. Podíl
`sᵀ·rhs / Σw‖mᵢ‖²` je obdoba koeficientu determinace. Na ploché záplatě je jmenovatel nulový —
není co vysvětlovat, proložení je triviálně přesné, činitel je 1.

## 4. Vyhodnocení mimo vrcholy

Tracer se ptá na křivost v **libovolném bodě plochy**, na každém kroku. Původní kód tam pokaždé
spustil nový záplavový průchod a nové proložení nejmenšími čtverci.

Místo toho se operátory předpočítají na vrcholech (paralelně) a v bodě plochy se interpolují:

1. `n_p` = barycentrická směs normál vrcholů trojúhelníka, normalizovaná.
2. Rámec `(e₁, e₂)` v `p`.
3. Pro každý vrchol `v` trojúhelníka se jeho `S_v` **přenese** z rámce `(e₁ᵥ, e₂ᵥ, nᵥ)` do rámce
   v `p`:
   - **paralelní přenos** báze minimální rotací `nᵥ → n_p` — složky operátoru se tím nemění, to
     je právě význam paralelního přenosu;
   - zbylá **rotace v rovině** o úhel `φ` se aplikuje jako kongruence `S′ = Rᵀ S R`.
4. `S_p = U·S′_v0 + V·S′_v1 + W·S′_v2`, pak vlastní čísla podle části 3.

Kongruence je v `Sym2x2.RotatedBy` zapsaná přes dvojnásobné úhly, což je levnější a lépe
podmíněné než roznásobení tří matic:

```
mean = (A + C)/2,   half = (A − C)/2
A′ = mean + half·cos2φ + B·sin2φ
B′ =        B·cos2φ − half·sin2φ
C′ = mean − half·cos2φ − B·sin2φ
```

**Rámce se musí srovnat, než se složky zprůměrují.** Bez toho by se sčítala čísla vyjádřená
v různých bázích, což nedává smysl.

**Degenerace se odvozuje ze smíšeného operátoru**, ne dědí z vrcholů: bod mezi umbilickým
a anizotropním vrcholem si zaslouží posouzení podle svého vlastního operátoru. Z vrcholů se dědí
jen příznaky kvality (`Unusable`, `Boundary`).

Složitost je O(1) na dotaz místo záplavového průchodu.

> **Pozor na past v API.** `ShapeOperatorField` **záměrně nemá** přístupový bod k uloženým
> příznakům. Uložené příznaky nesou jen to, co našlo *proložení*; `Planar` a `Umbilic` se odvozují
> z vlastních čísel, tedy až při dekompozici v `AtVertex` / `AtSurfacePoint`. Vystavení uložené
> podmnožiny by vybízelo k testování degenerace proti příznakům, které ji nikdy nemohou nést —
> na což jsem při implementaci sám naletěl a report tvrdil 0 % umbilických.

## 5. Paralelizace

`Parallel.For` přes souvislé rozsahy vrcholů, ne přes prokládané indexy: sousední vrcholy bývají
prostorově blízko, takže si každý pracovník udrží teplý výřez polí pozic a normál.

Výsledky se zapisují podle indexu, takže rozvrhování vláken neovlivní výstup.
