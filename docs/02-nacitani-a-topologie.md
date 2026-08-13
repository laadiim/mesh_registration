# 02 — Načítání a topologie

Zdroje: `src/MeshRegistration.IO/ObjReader.cs`, `src/MeshRegistration.Core/Mesh/MeshBuilder.cs`

---

## Část A — Čtečka OBJ

### Formát vstupních dat

Všech 24 souborů v `Data.zip` má tvar:

```
v -19.493742 20.077261 695.802
vt 0.5 0.5
f 1/3 25/2 2/1
```

Tedy pozice, texturové souřadnice, trojúhelníky s indexy `vrchol/textura`. Normály (`vn`) se
v datech nevyskytují a stejně by se nepoužily — počítají se z opravené, konzistentně orientované
topologie, což je důvěryhodnější než to, co tvrdí soubor.

### Implementace

Parsuje se **syrové UTF-8 po bajtech**, ne dekódované řetězce. Původní čtečka volala
`StreamReader.ReadLine()` a `string.Split`, což alokuje několik řetězců na každý vrchol a stěnu;
na největším modelu (120 MB, 750 tisíc vrcholů) to dominuje běhu.

Dva průchody:

1. **Počítání** — jen sken bajtů. Zjistí přesný počet vrcholů a počet trojúhelníků, které vzniknou
   z n-úhelníků (`k` rohů dá `k − 2` trojúhelníků).
2. **Plnění** — do přesně naalokovaných polí. Čísla přes `Utf8Parser.TryParse`, řádky jako
   `ReadOnlySpan<byte>` do jednoho bufferu. Ustálená alokace je nulová.

`LineEnumerator` je `ref struct`, který dělí buffer na řádky, ořezává `\r` z CRLF a přeskakuje
úvodní bílé znaky (v OBJ legální a jinak by rozbily rozpoznání klíčového slova).

### Co je oproti původní čtečce jinak

| oprava | detail |
|---|---|
| **Neguje se Z** | Původní kód vždy ukládal `(x, y, −z)`. Negace jediné osy obrací orientaci soustavy, a tedy efektivní navinutí každého trojúhelníka a znaménko každé normály. Korektně vytvořený OBJ vyšel obrácený naruby, a protože křivost nese znaménko, i všechny křivosti. Nyní volba `--flip-z`, ve výchozím stavu vypnutá. |
| **n-úhelníky** | Trojúhelníkují se vějířem. Původní kód zvládal jen 3 a 4 vrcholy, a u čtyřúhelníku měl navíc mezi větvemi `flipNormals` nekonzistentní navinutí. |
| **Záporné indexy** | OBJ dovoluje `-1` jako naposledy deklarovaný vrchol. Původní kód by spočítal záporný index a spadl. |
| **Tvary rohů** | `f v`, `f v/vt`, `f v//vn`, `f v/vt/vn` |
| **Rozlišení `v` od `vt`/`vn`** | Testuje se oddělovač za klíčovým slovem |
| **BOM** | Přeskočí se |
| **Chyby** | `MeshParseException` **s číslem řádku** a s citovaným tokenem. Původní kód přehazoval holé `Exception(fnfe.Message)`. |

### Výkon

| model | velikost | vrcholů | trojúhelníků | načtení |
|---|---|---|---|---|
| `brd1.obj` | 0.5 MB | 2 656 | 5 001 | 16 ms |
| `kac1.obj` | 1.8 MB | 28 393 | 56 134 | 73 ms |
| `cha1.obj` | 11.4 MB | 169 608 | 318 387 | 366 ms |
| `eie1.obj` | 114 MB | 750 729 | 1 480 240 | 682 ms |

---

## Část B — Oprava topologie

### Proč vůbec

Původní `CornerTable.processEdge` házel výjimku `"Non-manifold mesh at the input."`, jakmile se
zopakovala orientovaná hrana. To je ta hlášená chyba načítání — v datech ji spouští `cha1.obj`
(32 nemanifoldních hran) a `cha2m.obj` (70).

Horší byly ale závady, které procházely **tiše**:

- **Motýlkový vrchol** — dvě jinak oddělené záplaty plochy, které se dotýkají v jediném bodě. Každá
  jeho *hrana* je dokonale manifoldní, takže kontroly na úrovni hran ho vůbec nevidí. Má ale dva
  disjunktní vějíře, a původní kód ukládal jeden libovolný přilehlý roh na vrchol
  (`incidentCorner[v]` přepsané posledním zapisujícím trojúhelníkem), takže obchůzka okolí
  pokryla jen jeden z vějířů a tiše vrátila neúplné okolí.
- **Izolovaný vrchol** — `incidentCorner` zůstalo `0`, což je platný roh trojúhelníka 0. Obchůzka
  tedy prošla vějíř úplně cizího vrcholu a vrátila nesmysl.

Obojí je v datech běžné: 221 motýlků v `cha1.obj`, 499 v `cha2m.obj`, 2 v `hip1.obj`; 23, 52 a 4
izolované vrcholy.

### Postup

Pořadí je dané tím, že každý krok může vytvořit práci pro následující.

```
1. svaření vrcholů (volitelné)
2. odstranění degenerovaných a duplicitních trojúhelníků
3. kbelíky neorientovaných hran
4. propagace orientace
5. orientace uzavřených komponent ven
6. ── přestavba kbelíků ──
7. řešení nemanifoldních hran podle politiky
8. štěpení motýlkových vrcholů
9. indexace: výchozí rohy, příznaky, CSR sousedství, komponenty
```

#### 1. Svaření vrcholů (volitelné, `--weld`)

Prostorový hash s velikostí buňky rovnou toleranci, sondují se okolní buňky (3×3×3), aby se
sloučila i dvojice ležící přes hranici buňky. Tolerance je podíl úhlopříčky bboxu, výchozí `1e-6`.

Nutné pro soubory, kde má každý trojúhelník vlastní kopii svých vrcholů. Bez svaření je taková
síť topologicky roztříštěná na samostatné trojúhelníky, každý vrchol je hraniční s dvouprvkovým
okolím, a odhad křivosti selže všude pro nedostatek sousedů.

#### 2. Degenerované a duplicitní trojúhelníky

Zahodí se trojúhelník s opakovaným indexem nebo s plochou pod `DegenerateAreaFraction`
(výchozí `1e-10`) násobkem **průměrné** plochy stěny — tedy práh je relativní, ne absolutní.
Duplicita se pozná podle seřazené trojice indexů, nezávisle na navinutí.

`cha1.obj` má 4 257 degenerovaných a 28 duplicitních stěn.

#### 3. Kbelíky neorientovaných hran

Pro každý roh se hrana zabalí do jediného `ulong`:

```
key = ((ulong)min(a,b) << 32) | max(a,b)
```

Pole dvojic (klíč, roh) se seřadí `Array.Sort` a kbelíky vzniknou skenem souvislých běhů stejných
klíčů.

Nahrazuje `Dictionary<Edge,int>`, jehož hash byl `v1 + 10000 * v2`. Ten nad deseti tisíci vrcholy
kolidoval katastrofálně (každá síť zde je větší) a jeho `Equals(object)` navíc boxoval při každé
sondě. Řazení zabalených klíčů skoro nealokuje, je přívětivé ke cache, a hlavně **odhalí kbelíky
libovolné velikosti** místo toho, aby kód musel po dvojicích rozhodovat, jestli je hrana legální.

#### 4. Propagace orientace

BFS přes duální graf po komponentách. Pro každou hranu se dvěma trojúhelníky se předem zjistí,
jestli si navinutí odpovídají (orientované hrany musí být opačné). Při průchodu platí

```
požadovanýPřeklop(soused) = souhlasí ? překlop(stěna) : ¬překlop(stěna)
```

Hrana, kterou po propagaci nelze uspokojit, znamená **neorientovatelnou** komponentu (Möbiova
konfigurace), ne selhání opravy — na takové ploše žádné přiřazení navinutí nevyhoví všem hranám.
Takové hrany se ohlásí a později rozříznou, protože spojité pole normál, které odhad křivosti
potřebuje, přes ně neexistuje. `cha2m.obj` má 2 takové hrany.

#### 5. Orientace uzavřených komponent ven

Znaménkový objem z Gaussovy–Ostrogradského věty:

```
6V = Σ  p₀ · (p₁ × p₂)
   stěny
```

je kladný právě tehdy, když je navinutí ven. Jen uzavřené komponenty mají kanonický vnějšek, takže
komponenty s hranicí — většina částečných skenů — zůstanou jak jsou. `cha1.obj` má 23 uzavřených
komponent, které se otočily.

#### 6. Přestavba kbelíků

Překlopení trojúhelníka `(V0,V1,V2) → (V0,V2,V1)` **permutuje rohy**, takže indexy rohů uložené
v kbelících už neukazují na hrany, pro které byly postavené. Členství *stěn* v kbelících se
překlopením nemění (množina neorientovaných hran trojúhelníka je stejná), proto kroky 4 a 5 smějí
kbelíky sdílet — ale krok 7 páruje jednotlivé rohy, a ten je potřebuje přestavěné.

> Tohle byla skutečná chyba v první verzi implementace; odhalil ji test orientace tetraedru.

#### 7. Politika pro nemanifoldní hranu

Kbelík velikosti ≥ 3. Tři možnosti, volba přes `--nonmanifold`:

| politika | chování |
|---|---|
| **`cut`** (výchozí) | Hrana se pro každý přilehlý roh označí jako okraj. Plocha se rozpadne na manifoldní záplaty. Odhad křivosti i trasování už okraje umí, takže zbytek pipeline nepotřebuje žádný zvláštní případ: čáry se u singularity zastaví přesně jako u skutečné hranice. **Nic se nezahazuje** — každý trojúhelník přežije, jen ne sousednost přes singulární hranu. |
| **`pair-best`** | Spáruje se dvojice rohů s nejplošším pokračováním (největší skalární součin normál stěn, tedy dihedrální úhel nejblíže π) a se souhlasným navinutím; zbytek se rozřízne. Zachová spojitost trasovaných čar přes singularitu, za cenu heuristické volby. |
| **`strict`** | Vyhodí `NonManifoldMeshException`. Reprodukuje původní chování, ale s výpisem konkrétních hran. |

Kbelík velikosti 2, který po propagaci orientace pořád nesouhlasí, se rozřízne rovněž.

#### 8. Štěpení motýlkových vrcholů

Union-find nad **rohy**. Dva rohy se spojí, když manifoldní hrana dovolí vějíři přejít z jednoho
na druhý:

```
pro každý roh c:
    across = Opposite(Next(c))
    je-li across ≥ 0:  union(c, Next(across))
```

Protože obchůzka vějíře nikdy neopustí vrchol, každá vzniklá množina je právě jeden vějíř. Vrchol
s více než jednou množinou se **duplikuje** — jednou na vějíř — a přeindexuje. Pozice se nemění
(kopie leží na sobě), takže geometrie, kterou vidí prohlížeč, je identická, zatímco každý dotaz na
okolí se stane dobře definovaným. Složitost O(V + F).

Tím je topologie **manifoldní z konstrukce**: každý neizolovaný vrchol má právě jeden vějíř. Kód
níže po proudu se na to smí spoléhat. Původní komentář „lets assume it is not complex" se stal
skutečností místo přání.

#### 9. Indexace

- **Výchozí roh na vrchol** preferuje roh na začátku otevřeného vějíře (`Opposite(Previous(c)) < 0`),
  takže obchůzka dopředu projde celý vějíř právě jednou a skončí. Izolované vrcholy dostanou `-1`,
  ne tiše `0`.
- **Příznaky vrcholů**: `Boundary`, `Isolated`, `SplitFromNonManifold`.
- **CSR sousedství** se postaví jednou obchůzkou každého vějíře, s odstraněním duplicit lineárním
  hledáním v zásobníkovém bufferu (vějíře mají typicky ~6 prvků).
- **Komponenty** se spočítají průchodem grafu jednookolí, izolované vrcholy se ignorují.

### Report

`MeshDiagnostics` je záznam se všemi počty (viz [08 — Naměřené výsledky](08-vysledky.md)).
Serializuje se do `<název>_report.json`.

### Ukázka: `cha1.obj`

```
$ meshreg inspect data/cha1.obj --nonmanifold strict
Topology error: The mesh has 32 non-manifold edge(s) and the policy is Strict.
First offenders (vertex pairs): (8992, 9016), (9963, 10763), (24486, 24487), ...
```

```
$ meshreg inspect data/cha1.obj
  vertices               169608 in -> 169862 out
  triangles              318387 in -> 314102 out
  components             1258
  non-manifold edges     32 (policy: Cut, 109 adjacencies cut)
  degenerate faces       4257
  duplicate faces        28
  reoriented faces       3
  outward-flipped        23 closed component(s)
  bow-tie vertices       221 found, 254 copies added
  isolated vertices      23
  repaired.
```

Poznámka: 221 motýlků dalo 254 kopií, protože některé vrcholy měly víc než dva vějíře.
