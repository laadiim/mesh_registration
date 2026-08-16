# 11 — Dávkové zpracování

Zdroje: `scripts/run-all.sh`, `scripts/summarise-runs.py`

```bash
./scripts/run-all.sh
```

Zpracuje všechny `.obj` ve složce, paralelně, a agreguje výsledky do jedné tabulky.

---

## Použití

| přepínač | výchozí | význam |
|---|---|---|
| `--data DIR` | `data` | vstupní složka |
| `--out DIR` | `out` | výstupní složka |
| `--jobs N`, `-j N` | počet jader | kolik sítí naráz |
| `--inspect` | — | jen `inspect`, nic se nezapisuje |
| `--` | — | vše za tím se předá příkazu `meshreg trace` |
| `--help`, `-h` | — | nápověda |

```bash
./scripts/run-all.sh --data skeny --out vysledky --jobs 4
./scripts/run-all.sh -- --lines 100 --seed-spacing 0.02 --nonmanifold pair-best
./scripts/run-all.sh --inspect              # jen kontrola topologie
```

Skript nejdřív provede Release build, pak spouští už zkompilovanou binárku
(`bin/Release/net10.0/meshreg`) — ne přes `dotnet run`, který by u každé sítě znovu kontroloval
projekt.

## Výstup

Kromě obvyklých souborů na síť vznikne:

```
out/
  logs/<název>.log     konzolový výstup jednoho běhu
  logs/<název>.exit    návratový kód
  logs/<název>.ms      doba běhu
  summary.csv          souhrnná tabulka
```

Souhrn na konzoli:

```
MODEL         VERTS     TRIS      DIAG   CMP  NMe  BOW  ISO  PLAN%   UMB%  LINES   SAMP  LEN/DIAG  NaN     ms
-------------------------------------------------------------------------------------------------------------
cha1         169862   314102     1.283  1258   32  221   23   2.80  26.24     28    550     0.027    .   7364
eie1         750742  1480240       352    47    0   13    0  43.65  29.72     44   3719     0.067    .  10808
...

  meshes                24
  triangles total       4,877,435
  elapsed               12.6 s  (sum of per-mesh: 91.9 s)
  scale span            0.1335 .. 391  (2928x)
  line length / diagonal 0.012 .. 0.379
  lines traced          940
  samples               45,123
  degenerate samples    1,725

  meshes with non-manifold edges  2
  meshes with bow-tie vertices    15
  meshes with isolated vertices   8
  meshes needing reorientation    2

  OK: every mesh processed, no non-finite values anywhere.
```

Řádek `line length / diagonal` je průběžná kontrola invariantu bezrozměrnosti: pásmo musí zůstat
úzké, přestože se modely liší 2 900×. Kdyby se rozevřelo o řády, někde se do kódu vloudil
absolutní práh.

Sloupec `NaN` je `.` nebo `!`.

## Proč se čte JSON, ne konzolový výstup

`summarise-runs.py` čte `<název>_report.json`, ne to, co příkaz vypsal. Rozbor textu by se rozešel
s nástrojem při první změně formátování — což se během vývoje reálně stalo, když jsem se
o statistiky degenerace pokusil přes `sed`.

Kvůli tomu byl do `RunReport` doplněn oddíl `Curvature` s počty rovinných, umbilických
a nepoužitelných vrcholů. Dřív se tato čísla jen vypisovala na konzoli, takže report — přestože
se prezentoval jako strojově čitelný — postrádal hlavní statistiku fáze odhadu křivosti.

## Detekce selhání

Skript končí nenulovým kódem, když:

1. některá síť neprodukovala report (spadla nebo byla odmítnuta),
2. některý `_samples.csv` obsahuje `NaN` nebo `Infinity`,
3. některý report nejde přečíst.

Kontrola na nekonečné hodnoty je **nezávislá**: skript nevěří poli `NonFiniteSamples` v reportu,
ale sám prohledá CSV. Tabulka ukazuje obojí (`non_finite_reported` a `non_finite_in_csv`
v `summary.csv`), takže případný nesoulad mezi tím, co nástroj tvrdí, a tím, co skutečně zapsal,
je vidět.

Návratové kódy:

| kód | význam |
|---|---|
| `0` | vše prošlo |
| `1` | některá síť selhala nebo obsahuje nekonečnou hodnotu |
| `64` | špatné použití (neznámý přepínač) |
| `66` | vstupní složka neexistuje nebo je prázdná |

Ověřeno na záměrně poškozených vstupech:

```
$ ./scripts/run-all.sh --data /tmp/badinput --out /tmp/badout
  brd1       ok         329 ms
  corrupt    FAILED     180 ms  (exit 1, see /tmp/badout/logs/corrupt.log)
  outofrange FAILED     175 ms  (exit 1, see /tmp/badout/logs/outofrange.log)
  ...
  FAILED: 2 mesh(es) produced no report:
    corrupt  exit 1
    outofrange  exit 1
$ echo $?
1
```

Selhání jedné sítě nezastaví ostatní — dávka doběhne a teprve pak se vyhodnotí.

## Samostatné použití agregátoru

`summarise-runs.py` jde spustit nad libovolnou existující výstupní složkou:

```bash
python3 scripts/summarise-runs.py --out out
python3 scripts/summarise-runs.py --out out --csv /tmp/tabulka.csv
```

Bez `--elapsed-ms` vypíše místo reálného času jen součet časů jednotlivých sítí — reálnou dobu
běhu zná jen ten, kdo dávku spouštěl.

## Sloupce v `summary.csv`

Kromě sloupců z tabulky výše obsahuje CSV navíc `avg_edge`, `degenerate_faces`, `reoriented`,
`unusable_pct`, `usable_pct`, `seeds`, `mean_len`, `degenerate_samples`, `non_finite_reported`,
`non_finite_in_csv`, `end_reasons` a `exit`.

Je vhodné jako vstup pro tabulkový procesor nebo pro sledování vývoje mezi verzemi — například
`diff` dvou takových CSV ukáže, co změna parametru udělala napříč celým datasetem.

## Použití v CI

```bash
./scripts/run-all.sh --jobs 2 || exit 1
```

Předpokládá rozbalená data. Návratový kód stačí jako brána; `out/summary.csv` je vhodné uschovat
jako artefakt.
