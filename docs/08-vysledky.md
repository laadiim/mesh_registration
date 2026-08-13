# 08 — Naměřené výsledky

Všechny hodnoty naměřené na 24 souborech z `Data.zip`, Release build, .NET 10.0.110, Linux.

---

## 1. Rozsah měřítek

Toto je nejdůležitější číslo pro celý návrh. Testovací data se **liší o tři řády**:

| model | úhlopříčka bboxu | průměrná hrana | poměr |
|---|---|---|---|
| `hea2m.obj` | **0.1335** | 0.000643 | 208 |
| `bud1.obj` | 0.1614 | 0.000605 | 267 |
| `hip1.obj` | 1.175 | 0.004192 | 280 |
| `cha1.obj` | 1.283 | 0.002533 | 506 |
| `bub2m.obj` | 176.3 | 1.343 | 131 |
| `kac1.obj` | 239.5 | 1.023 | 234 |
| `brd1.obj` | 256.6 | 2.908 | 88 |
| `eie1.obj` | **352.0** | 0.2831 | 1243 |
| `geb2m.obj` | **391.0** | 0.2364 | 1654 |

Poměr největší ku nejmenší úhlopříčce je **2 900×**, u průměrné hrany **4 500×**. Jakákoliv
absolutní konstanta v kódu je proto na části dat nesmyslná — odtud invariant „každý práh je
bezrozměrný".

Konkrétní dopad na původní verzi: `LineOnSurface.length = 30` znamená pro `brd1.obj` (hrana 2.908)
čáru o **11 krocích**, zatímco pro `hea1.obj` (hrana 0.000654) pokus o **43 000 kroků** přes model
214× menší, než je požadovaná délka.

## 2. Ověření bezrozměrnosti

Průměrná délka trasované čáry jako podíl úhlopříčky, výchozí parametry:

| model | úhlopříčka | průměrná délka čáry | **podíl** |
|---|---|---|---|
| `kac1` | 239.5 | 80.94 | 0.338 |
| `brd1` | 256.6 | 77.05 | 0.300 |
| `bub1` | 306.3 | 90.62 | 0.296 |
| `hea1` | 0.1359 | 0.03163 | 0.233 |
| `bud1` | 0.1614 | 0.03768 | 0.233 |
| `dra1` | 0.2578 | 0.04026 | 0.156 |
| `arm1` | 0.2502 | 0.03671 | 0.147 |
| `hip1` | 1.175 | 0.1365 | 0.116 |
| `coa1` | 280.2 | 28.00 | 0.100 |
| `eie1` | 352.0 | 23.63 | 0.067 |
| `cha1` | 1.283 | 0.03419 | 0.027 |
| `geb1` | 315.3 | 4.435 | 0.014 |

Podíl se drží v pásmu **0.014–0.379** napříč modely lišícími se o tři řády. Rozptyl uvnitř pásma
není chyba měřítka, ale skutečná vlastnost geometrie: modely s nízkým podílem (`geb`, `cha`, `eie`)
mají hodně komponent a hodně okrajů, takže čáry rychle narazí na hranici.

## 3. Klasifikace křivosti

Podíl vrcholů bez použitelného hlavního směru:

| model | rovinné | umbilické | **bez směru celkem** |
|---|---|---|---|
| `dra1` | 0.01 % | 2.35 % | 2.4 % |
| `arm1` | 0.00 % | 1.42 % | 1.4 % |
| `bub1` | 0.15 % | 3.90 % | 4.1 % |
| `bud1` | 0.02 % | 5.05 % | 5.1 % |
| `coa1` | 0.01 % | 7.28 % | 7.3 % |
| `kac1` | 0.31 % | 7.76 % | 8.1 % |
| `hea1` | 0.00 % | 13.80 % | 13.8 % |
| `brd1` | 0.00 % | 16.60 % | 16.6 % |
| `cha1` | 2.80 % | 26.24 % | 29.0 % |
| `geb1` | 5.38 % | 28.06 % | 33.4 % |
| `hip1` | 0.00 % | 29.08 % | 29.1 % |
| `eie1` | **43.65 %** | 29.72 % | **73.4 %** |
| `eie2m` | 43.95 % | 31.31 % | 75.3 % |

**To jsou přesně ta místa, kde původní verze vyráběla NaN.** Rozsah 1.4 % až 75 % vrcholů —
u `eie` (architektonický model s plochými panely) tři čtvrtiny.

Sloupec `Planar` roste tam, kde má model rovné plochy: `eie` 44 %, `geb` 5 %, `cha` 3 %. Organické
skeny (`dra`, `arm`, `bud`) mají rovinných skoro nula, ale umbilických pár procent — to jsou
lokálně kulové oblasti.

## 4. Topologické závady

| model | vrcholů | trojúhelníků | nemanifoldní hrany | motýlkové vrcholy | izolované | degenerované stěny | komponenty |
|---|---|---|---|---|---|---|---|
| `arm1` | 33 902 | 65 379 | 0 | 18 | 0 | 0 | 9 |
| `arm2m` | 36 107 | 69 985 | 0 | 11 | 0 | 0 | 4 |
| `brd1` | 2 656 | 5 001 | 0 | 0 | 0 | 0 | 2 |
| `bub1` | 10 912 | 21 285 | 0 | 0 | 71 | 0 | 1 |
| `bud1` | 59 544 | 116 804 | 0 | 4 | 0 | 0 | 7 |
| `bud2m` | 51 263 | 100 640 | 0 | 2 | 0 | 0 | 3 |
| **`cha1`** | 169 862 | 314 102 | **32** | **221** | 23 | **4 257** | 1 258 |
| **`cha2m`** | 211 425 | 366 512 | **70** | **499** | 52 | 4 581 | 2 848 |
| `coa1` | 28 120 | 53 715 | 0 | 13 | 58 | 0 | 5 |
| `coa2m` | 28 197 | 53 829 | 0 | 17 | 0 | 0 | 7 |
| `dra1` | 43 183 | 83 609 | 0 | 2 | 9 | 0 | 3 |
| `eie1` | 750 742 | 1 480 240 | 0 | 13 | 0 | 0 | 47 |
| `eie2m` | 761 901 | 1 501 582 | 0 | 14 | 0 | 0 | 38 |
| `geb1` | 57 033 | 106 930 | 0 | 9 | 0 | 0 | 47 |
| `geb2m` | 61 486 | 116 652 | 0 | 8 | 0 | 0 | 33 |
| `hea1` | 25 827 | 50 336 | 0 | 0 | **3** | 0 | 4 |
| `hea2m` | 24 273 | 47 316 | 0 | 0 | 1 | 0 | 4 |
| `hip1` | 30 521 | 59 166 | 0 | **2** | **4** | 0 | 7 |
| `hip2m` | 21 926 | 42 254 | 0 | 1 | 0 | 0 | 10 |
| `kac1` | 28 393 | 56 134 | 0 | 0 | 0 | 0 | 1 |
| `kac2m` | 28 142 | 55 583 | 0 | 0 | 0 | 0 | 1 |

Závěry:

- **Nemanifoldní hrany má jen `cha1` a `cha2m`.** Tyto dva soubory původní kód **odmítl načíst**.
  `cha1` má navíc 3 nekonzistentně navinuté stěny, `cha2m` má 2 neorientovatelné hrany (Möbiova
  konfigurace).
- **Motýlkové vrcholy jsou naopak všude** — 15 z 24 souborů, od 1 do 499. Ty původní kód zpracoval
  **tiše špatně**: obchůzka okolí pokryla jen jeden ze dvou vějířů.
- **Izolované vrcholy** v 8 souborech, od 1 do 71. Tam obchůzka startovala u cizího trojúhelníka.
- `cha1`/`cha2m` mají ~4 300 / ~4 600 degenerovaných stěn a rozpadají se na 1 258 / 2 848 komponent.

### `cha1.obj` — porovnání obou režimů

```
$ meshreg inspect data/cha1.obj --nonmanifold strict
Topology error: The mesh has 32 non-manifold edge(s) and the policy is Strict.
First offenders (vertex pairs): (8992, 9016), (9963, 10763), (24486, 24487), ...
                                                                       (exit 1)
```

```
$ meshreg inspect data/cha1.obj
  vertices        169608 in -> 169862 out
  triangles       318387 in -> 314102 out
  components      1258
  non-manifold edges  32 (policy: Cut, 109 adjacencies cut)
  degenerate faces    4257
  duplicate faces     28
  reoriented faces    3
  outward-flipped     23 closed component(s)
  bow-tie vertices    221 found, 254 copies added
  isolated vertices   23
  repaired.                                                            (exit 0)
```

## 5. Výkon

Časy jednotlivých fází v ms, Release, včetně JIT rozehřátí:

| model | vrcholů | trojúhelníků | načtení | topologie | křivosti | trasování | export |
|---|---|---|---|---|---|---|---|
| `brd1` | 2 656 | 5 001 | 16 | 52 | 44 | 21 | 107 |
| `kac1` | 28 393 | 56 134 | 73 | 203 | 232 | 43 | 181 |
| `hip1` | 30 519 | 59 166 | 81 | 172 | 253 | 37 | 149 |
| `cha1` | 169 608 | 318 387 | 366 | 758 | 646 | 75 | 303 |
| `eie1` | **750 729** | **1 480 240** | **682** | 2 435 | 1 490 | 141 | 893 |

`eie1.obj` je 114 MB. Celá pipeline nad 1.48 milionu trojúhelníků trvá **~5.6 s**, z toho načtení
0.68 s.

Trasování 50 čar je 21–141 ms napříč celým datasetem — po odstranění přestavby corner table na
každou čáru, kopií sítě, výpisů v horké smyčce a proložení nejmenšími čtverci na každý krok
(viz [04 §7](04-trasovani.md#7-paralelizace-a-výkon)).

## 6. Kontrola korektnosti

Nad všemi 24 soubory, výchozí parametry:

| kontrola | výsledek |
|---|---|
| běh skončil bez chyby | **24 / 24** |
| `NaN` nebo `Infinity` v `_samples.csv` | **0 výskytů** ve všech souborech |
| `LineEnd.Stuck` (pojistka pochodu) | **nikdy** |
| počet trasovaných čar | 7–50 (z limitu 50) |
| dva běhy bajtově shodné | **ano** |

```
$ diff -r /tmp/a /tmp/b && echo IDENTICAL
IDENTICAL
```

## 7. Kde je výsledek slabší

Poctivě: `geb1`/`geb2m` dají jen 11 a 7 čar, `cha1`/`cha2m` jen 28 a 31.

Není to závada výpočtu. Tyto modely jsou roztříštěné — `geb1` má 47 komponent a 7 058 hraničních
hran, `cha1` 1 258 komponent — takže záplaty jsou malé a čáry narazí na okraj po pár krocích.
U `geb1` je navíc 33 % vrcholů bez použitelného směru, takže je málo přípustných seedů a při
výchozím rozestupu 0.05 úhlopříčky se jich na model vejde jen 20.

Doporučení pro takové modely:

```bash
--lines 100 --seed-spacing 0.02
```

Na `hip1.obj` to zvedne výsledek ze 44 na 92 čar. Pro fázi 2 to bude podstatné: krátké čáry nesou
krátký podpis, a ten je méně specifický.
