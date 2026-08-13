# 09 — Návrh fáze 2

Zbývající část pipeline. **Není implementovaná** — tento dokument shrnuje, co má vzniknout, na co
navazuje, a která místa původního řešení je potřeba změnit.

---

## Rozhraní mezi fázemi

Fáze 1 dodává:

- v kódu: `TracedLine[]` s polem `LineSample[]`, ekvidistantním v délce oblouku;
- na disku: `<n>_samples.csv` se sloupci `lineId, sampleIndex, arcLength, x,y,z, nx,ny,nz,
  kMin, kMax, kappaG, confidence, flags, triangle`.

Klíčové vlastnosti, na kterých fáze 2 staví:

| vlastnost | proč záleží |
|---|---|
| konstantní krok v délce oblouku | dvě čáry jsou porovnatelné jako sekvence |
| podpis `(kMin, kMax, κ_g)` je invariantní vůči tuhé transformaci | je to vlastnost plochy, ne polohy |
| `confidence` na vzorek | dovolí vážit, ne jen prahovat |
| `flags` na vzorek | označuje vzorky, které nic nerozlišují |
| poloha + normála na vzorek | vstup pro Kabsche |

---

## Kroky

### 1. Krájení podpisů (`Cutter`)

Z každé čáry se vyrobí okna — posuvná okna pevné délky, nebo celá čára. Ke každému oknu i jeho
**obrácená kopie**, protože orientace čáry je libovolná: tatáž křivka může být na druhé síti
trasovaná opačným směrem.

Při obrácení se `κ_g` **neguje** (úhel otočení mění znaménko), zatímco `kMin` a `kMax` zůstávají.

### 2. Párování (`Matcher`)

Lokální zarovnání sekvencí Smithovým–Watermanovým algoritmem: dynamické programování nad maticí
`|okno₁| × |okno₂|`, se skóre za shodu, neshodu a mezeru, a zpětným průchodem od maxima.

Substituční skóre porovnává dvojici vzorků. **Tady je potřeba změna oproti původnímu řešení.**

Původní `Matcher.HowSimilar` počítal euklidovskou vzdálenost nad syrovým `(k1, k2, κ_g)` a
porovnával ji s pevným prahem `treshold = 0.055`:

```csharp
double treshold = 0.055;
if (norm > treshold) return mismatchScore;
return matchScore - norm * (matchScore - mismatchScore) / treshold;
```

Tři problémy:

1. **Práh je absolutní.** Křivost má rozměr `1/délka`; na modelu s úhlopříčkou 0.14 a na modelu
   s úhlopříčkou 352 znamená `0.055` něco úplně jiného.
2. **Kanály se míchají v syrových jednotkách.** `kMin`, `kMax` a `κ_g` mají různý rozsah, takže
   euklidovská norma je fakticky váží náhodně.
3. **Nulová váha nemá kde vzniknout.** Dlouhý rovný nebo kulový úsek má skoro konstantní podpis,
   takže se zarovná stejně dobře **kdekoliv** — klasický zdroj falešných shod v zarovnávání
   sekvencí.

Návrh:

- **Standardizovat každý kanál** robustně přes celou síť (medián a MAD, ne průměr a směrodatná
  odchylka — pole křivostí má odlehlé hodnoty na tenkých trojúhelnících). Práh se pak vyjádří
  v jednotkách MAD, tedy bezrozměrně.
- **Vážit skóre spolehlivostí** obou vzorků.
- **Vzorkům s nízkou anizotropií dát váhu →0**, aby ploché a kulové úseky ani neodměňovaly, ani
  netrestaly. Příznak `Umbilic` už je v CSV.

### 3. Kandidátní transformace (Kabsch)

Z každé shody vznikne množina odpovídajících si bodů. Kabschův algoritmus je hledá optimální
rotaci: centrovat obě množiny, spočítat křížovou kovarianční matici `H = Pᵀ·Q`, provést SVD
`H = U·Σ·Vᵀ`, položit

```
R = V · diag(1, 1, det(V·Uᵀ)) · Uᵀ
t = těžiště_Q − R · těžiště_P
```

`det` v prostředním členu zabrání zrcadlení.

Body samotné čáry jsou skoro **kolineární**, takže rotace kolem osy čáry by zůstala neurčená.
Původní řešení to ošetřuje přidáním bodů odsazených podél normály — to je správně a stojí za
zachování. Normála každého vzorku už je v CSV.

**Doplnit test podmíněnosti.** Singulární čísla `H`: pokud je nejmenší vůči největšímu blízko nule,
je korespondence rank-deficientní a transformace je určena jen do **jednoparametrické rodiny**.
Nastává to, když matchované segmenty leží na kouli (rotace kolem středu) nebo v rovině (posun
v rovině), nebo když jsou body skoro kolineární. Kabsch v takovém případě vrátí *nějakou*
odpověď, ale ta je podél nulového směru libovolná. Takový kandidát se má zamítnout nebo dostat
nižší hlasovací váhu.

Pro 3×3 SVD stačí vlastní čísla `HᵀH` (Jacobiho metoda) nebo polární rozklad Newtonovou iterací —
oboje je pár desítek řádků a zapadá do pravidla „žádné knihovny na lineární algebru v horkých
smyčkách" ([01](01-architektura.md)).

### 4. Shlukování transformací (`Density`)

Kandidáti jsou body v `SE(3)`; hledá se nejhustší shluk. Rozumná metrika, kterou původní řešení
používalo a stojí za zachování, je **L2 vzdálenost mezi mračny** transformovanými dvěma kandidáty:

```
d(T₁, T₂) = (1/n) Σ ‖T₁(qᵢ) − T₂(qᵢ)‖²
                  i
```

To se dá spočítat v konstantním čase z předpočítaných sum tenzorových součinů bodů `Q`, bez
procházení mračna — to je na tom to chytré a je to v `Candidate.DistanceTo` původního kódu.

Jádrový odhad hustoty s exponenciálním jádrem, maximum přes kandidáty.

**Změnit:** ruční `ThreadedExecution` s magickou velikostí bloku 100 nahradit `Parallel.For`
s `Partitioner`.

### 5. Zpřesnění (ICP)

Iterativní hledání nejbližších bodů a přepočet transformace. Doporučené změny oproti původnímu:

- **point-to-plane** místo point-to-point (rychlejší konvergence na hladkých plochách);
- odmítání odlehlých párů podle percentilu vzdáleností, ne pevného prahu;
- KD-strom postavený jednou.

### 6. Detekce nejednoznačnosti

Je-li celý model rovinný nebo kulový, je pravá transformace **skutečně nejednoznačná** — existuje
spojitá grupa symetrií, která model zachovává. Správné chování je to ohlásit, ne vrátit libovolný
bod z hřebene.

Pozná se to na rozdělení hustoty v prostoru transformací: izolovaný vrchol znamená jednoznačné
řešení, spojitý hřeben znamená zbytkovou symetrii. Vzhledem k tomu, že `eie1.obj` má 44 %
rovinných vrcholů, to není teoretická obava.

---

## Doporučené pořadí implementace

1. `Cutter` + datové typy pro okna — malé, snadno testovatelné
2. Standardizace kanálů (medián/MAD přes síť) — samostatně testovatelné
3. `Matcher` (Smith–Waterman) — testovat na uměle posunutých kopiích téže čáry
4. Kabsch + test podmíněnosti — testovat na známé transformaci, round-trip
5. Shlukování hustotou
6. ICP
7. Detekce nejednoznačnosti

Testovací strategie zůstává stejná jako ve fázi 1: nejdřív analytické případy se známou odpovědí
(vezmi jednu síť, aplikuj známou transformaci, zaregistruj zpět, porovnej s inverzí), teprve pak
reálná data.

---

## Co si pohlídat

- **Bezrozměrnost prahů** platí i tady. Práh v `Matcher` v jednotkách MAD, práh v ICP jako podíl
  úhlopříčky, poloměr jádra hustoty vztažený k poloměru modelu.
- **Determinismus.** Fáze 1 nemá generátor náhody vůbec. Pokud fáze 2 bude vzorkovat, musí mít
  explicitní seed a per-worker generátory odvozené deterministicky — ne N generátorů se stejným
  seedem, jak to měl původní kód.
- **Krátké čáry.** Na roztříštěných modelech (`geb`, `cha`) dává fáze 1 čáry o 15–20 vzorcích.
  Smith–Waterman nad takovou sekvencí najde shodu snadno a často falešně. Minimální délka shody
  bude potřebovat rozvahu.
- **Podpis na degenerovaných úsecích.** Vzorky s příznakem `Umbilic` nesou platné `kMin`/`kMax`,
  ale `κ_g` je tam výsledkem paralelního přenosu, ne sledování směrového pole. Do skóre by měly
  vstupovat s malou vahou.
