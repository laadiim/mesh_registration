# Manuál — fáze 1 (načtení, topologie, křivosti, trasování)

Praktický návod k obsluze.

- Zdůvodnění návrhu a rozbor obou opravených chyb: [README.md](README.md)
- Podrobná technická dokumentace (matematika, algoritmy, naměřené výsledky): [docs/](docs/README.md)

---

## 1. Příprava

Potřeba je .NET 10 SDK (`dotnet --list-sdks` musí ukázat 10.x).

```bash
cd /home/code/Projects/mesh_registration && dotnet build -c Release
```

Testovací data (24 souborů `.obj`) se rozbalí ze starého projektu:

```bash
mkdir -p data && unzip -o /home/code/Projects/vasa-projekt/Data.zip -d data
```

Složka `data/` i `out/` jsou v `.gitignore`, do repozitáře se nedostanou.

---

## 2. Dva příkazy

Program se jmenuje `meshreg` a má zatím dva příkazy.

### `inspect` — co je v síti za problémy

Načte síť, opraví topologii a vypíše report. Nic nepočítá, nic nezapisuje. Tohle spusťte jako
první, když se nová data chovají divně.

```bash
dotnet run -c Release --project src/MeshRegistration.Cli -- inspect data/hip1.obj
```

Výstup čtěte odshora: rozsah (počty vrcholů a trojúhelníků na vstupu a výstupu), měřítko, počty
hran podle typu, a nakonec seznam provedených oprav. Poslední řádek říká buď `clean: no repairs
were needed`, nebo `repaired.`

### `trace` — trasování čar a export

Celá pipeline plus zápis souborů pro MeshLab.

```bash
dotnet run -c Release --project src/MeshRegistration.Cli -- trace data/kac1.obj --out out
```

Vypíše čtyři fáze s časy (`read`, `topology`, `curvature`, `tracing`, `export`) a pak souhrn.

Návratový kód: `0` v pořádku, `1` chyba načtení nebo topologie, `2` v datech se objevila
nekonečná hodnota (nemá nastat — je to pojistka proti návratu původní chyby).

---

## 3. Jak číst výstup

### Řádek `topology`

```
topology  715 ms  169862 vertices, 314102 triangles, 1258 component(s), diagonal 1.283,
                  avg edge 0.002533; 4257 degenerate faces removed; 32 non-manifold edges;
                  221 bow-tie vertices split; 23 isolated vertices
```

Cokoliv za středníkem je oprava. Nejde o chyby programu — je to popis toho, co bylo ve vstupu
špatně a jak se to spravilo.

| hlášení | co znamená |
|---|---|
| `degenerate faces removed` | trojúhelníky s nulovou plochou nebo opakovaným vrcholem |
| `duplicate faces removed` | dva trojúhelníky nad stejnou trojicí vrcholů |
| `non-manifold edges` | hrana sdílená 3+ trojúhelníky — viz `--nonmanifold` |
| `non-orientable edges` | plocha typu Möbius; nelze na ní zavést spojité normály, rozřízne se |
| `faces reoriented` | nekonzistentní navinutí, spraveno |
| `outward-flipped` | uzavřená komponenta byla navinutá dovnitř, otočena ven |
| `bow-tie vertices split` | vrchol, kde se dotýkají dvě jinak oddělené záplaty; rozdělen na jeden vrchol na vějíř |
| `isolated vertices` | vrchol, na který se neodkazuje žádný trojúhelník |

### Řádek `curvature`

```
curvature  239 ms  radius 8.182; planar 89 (0.31%), umbilic 2202 (7.76%), unusable 0 (0.00%)
```

- **planar** — plochá záplata, křivosti ani směry nenesou informaci
- **umbilic** — kulová záplata; hodnoty křivostí platí, ale hlavní **směr** neexistuje
- **unusable** — proložení vůbec neprošlo (málo sousedů, špatná podmíněnost, izolovaný vrchol)

Na reálných skenech je normální mít 8–26 % umbilických vrcholů. Tam se neseeduje a směr se odtud
nečte.

### Souhrn

```
lines               48 from 50 seed(s)
samples             3875 total, 80.7 per line
step / mean length  1.023 / 80.94
degenerate samples  206 (bridged by parallel transport)
non-finite samples  0
line ends           LengthReached=44, Boundary=42, Degenerate=10
```

- `degenerate samples` — vzorky, které padly do ploché nebo kulové oblasti; čára jimi prošla
  paralelním přenosem (geodetikou) místo sledování hlavního směru
- `non-finite samples` — **musí být 0**
- `line ends` — proč čáry skončily. Počítají se oba konce, takže součet je zhruba dvojnásobek
  počtu čar

| konec | význam |
|---|---|
| `LengthReached` | vyčerpán délkový rozpočet — normální |
| `Boundary` | narazila na okraj sítě nebo na rozříznutou nemanifoldní hranu |
| `SelfIntersection` | vrátila se na sebe (uzavřená křivka) |
| `Degenerate` | příliš dlouhý úsek bez definovaného směru |
| `Stuck` | pojistka; **kdyby se objevovalo, dejte vědět** |

---

## 4. Výstupní soubory

Pro vstup `kac1.obj` vznikne v `--out`:

| soubor | k čemu |
|---|---|
| `kac1_lines_tube.obj` + `.mtl` | **otevřete tenhle** — čáry jako trubičky z trojúhelníků, barva na čáru, vidět v jakémkoliv režimu stínování |
| `kac1_lines.obj` | tytéž čáry jako přesné polyčáry (`v` + `l`); malý soubor, vhodný jako vstup jinam. V MeshLabu je vidět jen v drátovém režimu |
| `kac1_curvature.obj` | vstupní síť obarvená podle `--color-by` |
| `kac1_samples.csv` | data pro další fázi, jeden řádek na vzorek |
| `kac1_report.json` | strojově čitelný report topologie i trasování |

### Prohlížení v MeshLabu

```bash
meshlab out/kac1_lines_tube.obj out/kac1_curvature.obj
```

Barvy vrcholů se u `_curvature.obj` zapínají ikonou **Vertex Color** v horní liště (nebo
*Render → Color → Per Vertex*).

Barevný klíč pro `--color-by flags`:

| barva | význam |
|---|---|
| zelená | použitelný hlavní směr |
| modrá | umbilická (kulová) oblast — směr neexistuje |
| šedá | rovina |
| oranžová | okraj sítě |
| červená | proložení selhalo |

Modré a šedé oblasti jsou přesně ta místa, kde původní verze vyráběla NaN. Tady v nich nezačíná
žádná čára.

### Sloupce v CSV

```
lineId, sampleIndex, arcLength, x, y, z, nx, ny, nz, kMin, kMax, kappaG, confidence, flags, triangle
```

`kMin`, `kMax`, `kappaG` je podpis, který bude párovat druhá fáze. Vzorky jsou ekvidistantní
v délce oblouku. `confidence` je v ⟨0,1⟩, `flags` je textový výčet oddělený `|`.

Znaménko: operátor tvaru je `dN`, takže konvexní plocha s vnějšími normálami má křivost kladnou.
Koule o poloměru R má `kMin = kMax = 1/R`.

---

## 5. Nejčastější úpravy

Všechny délkové parametry jsou **bezrozměrné** — násobky průměrné délky hrany nebo podíly
úhlopříčky. Stejné hodnoty proto fungují na modelu velkém 0.14 i 256.

### Málo nebo krátké čáry

Typicky na roztříštěných modelech (hodně komponent, hodně okrajů — `geb1.obj`, `cha1.obj`):

```bash
dotnet run -c Release --project src/MeshRegistration.Cli -- trace data/geb1.obj --out out \
  --lines 100 --seed-spacing 0.02
```

### Čáry končí moc brzy na `Degenerate`

Model má hodně kulových nebo plochých oblastí. Buď povolte delší přemostění, nebo zpřísněte, co
se pokládá za umbilické:

```bash
--max-degenerate-run 12          # projde delší kulovou záplatou
--umbilic-threshold 0.03         # méně bodů se označí za umbilické
```

Pozor: snížení `--umbilic-threshold` pod ~0.02 začne pouštět směry, které jsou jen šum.

### Hrubší nebo jemnější podpis

```bash
--step 2.0     # řidší vzorkování, méně šumu v kappaG, méně detailu
--nbhood 4     # menší okolí pro proložení: ostřejší detail, citlivější na šum
--length 1.0   # delší čáry (podíl úhlopříčky; každá půlka dostane polovinu)
```

`kappaG` je druhá diference poloh, takže je to nejšumnější kanál. Pod `--step 1.0` nemá smysl jít.

### Síť se rozpadá na tisíce komponent

Soubor nejspíš ukládá vlastní kopii vrcholů pro každý trojúhelník. Zapněte svařování:

```bash
--weld
```

`inspect` to potvrdí: bez `--weld` uvidíte počet komponent zhruba rovný počtu trojúhelníků.

### Nemanifoldní hrany

```bash
--nonmanifold cut         # výchozí: rozřízne, plocha se rozpadne na manifoldní záplaty
--nonmanifold pair-best   # ponechá nejplošší pokračování, zbytek rozřízne
--nonmanifold strict      # tvrdá chyba (chování původní verze), vypíše konkrétní hrany
```

### Jiné obarvení

```bash
--color-by kmax           # síť podle maximální křivosti (divergentní modrá-bílá-červená)
--color-by aniso          # anizotropie: kde je směr dobře určený
--tube-color-by kappa-g   # trubičky podle geodetické křivosti
--tube-color-by flags     # ukáže, kudy čára prošla degenerovanou oblastí
```

Hodnoty pro `--color-by` a `--tube-color-by`: `flags`, `aniso`, `kmin`, `kmax`, `mean`, `gauss`,
`confidence`, `kappa-g`, `line`.

U všech výčtových voleb se nerozlišují velká písmena a pomlčky ani podtržítka nevadí — projde
`pair-best`, `pair_best` i `PairBestContinuation`. Při překlepu program vypíše seznam platných
hodnot.

### Levotočivá data

```bash
--flip-z
```

Otočí Z při načtení. Pozor, mění to i orientaci soustavy, tedy efektivní navinutí a **znaménko
všech křivostí**. Původní verze to dělala vždy a mlčky; teď je to vypnuté a musí se vyžádat.

Úplný seznam voleb: `... -- trace --help`

---

## 6. Kontrola, že je vše v pořádku

```bash
dotnet test
```

72 testů. Klíčové jsou ty na analytických plochách s přesně známou křivostí (rovina, koule, válec,
torus, sedlo) — rovina a koule jsou přímé regresní testy na NaN.

Rychlá kontrola nad reálnými daty:

```bash
grep -ciE "nan|infinity" out/*_samples.csv
```

Všude musí být `0`.

Determinismus (dva běhy musí dát bajtově shodný výstup):

```bash
dotnet run -c Release --project src/MeshRegistration.Cli -- trace data/kac1.obj --out /tmp/a
dotnet run -c Release --project src/MeshRegistration.Cli -- trace data/kac1.obj --out /tmp/b
diff -r /tmp/a /tmp/b && echo OK
```

---

## 7. Co ještě není hotové

Druhá fáze pipeline: krájení podpisů na okna, párování Smith–Watermanem mezi sítěmi, Kabsch nad
každou shodou, shlukování transformací podle hustoty a doladění ICP. Rozhraní pro ni je hotové —
`TracedLine` v kódu a `_samples.csv` na disku.
