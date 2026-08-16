# Registrace trojúhelníkových sítí podle křivkových podpisů

*Souvislý výklad úlohy, matematiky za ní a dvou míst, kde se to v praxi láme.*

---

## 1. Úloha

Trojrozměrný skener nasnímá objekt vždy jen zčásti — z jedné strany, z jednoho úhlu. Aby vznikl
celý model, je potřeba pořídit několik snímků z různých stran a pak je **složit dohromady**. Tomu
složení se říká **registrace**.

Formálně: máme dvě trojúhelníkové sítě `P` a `Q`, které zachycují částečně se překrývající části
téhož objektu, v neznámých vzájemných polohách. Hledáme tuhou transformaci `T ∈ SE(3)` — rotaci
a posunutí — takovou, že `T(Q)` sedí na `P`.

### Proč to není triviální

Nejznámější metoda je **ICP** (Iterative Closest Point): opakovaně se pro každý bod `Q` najde
nejbližší bod v `P`, z těch dvojic se spočítá transformace, ta se aplikuje a celé se to opakuje.
ICP je jednoduchý a přesný, ale má zásadní omezení — **potřebuje dobrý počáteční odhad**. Když jsou
sítě vůči sobě otočené o víc než pár desítek stupňů, ICP zkonverguje do lokálního minima a vrátí
nesmysl.

Úloha, kterou řeší tento nástroj, je proto **globální registrace**: najít transformaci **bez
počátečního odhadu**, jen z geometrie obou sítí. ICP pak přijde na řadu až na konci jako
zpřesnění.

### Nápad

Kdyby na povrchu existovaly nějaké **význačné křivky**, které jsou vlastností plochy samotné
a nezávisí na tom, jak je objekt v prostoru natočený, pak by tytéž křivky musely být na obou
snímcích (v překryvu) stejné. Kdyby se navíc podél nich dalo měřit něco charakteristického, šlo by
dvě takové křivky porovnat jako dvě posloupnosti čísel — a z toho, jak si odpovídají, dopočítat
hledanou transformaci.

Takové křivky existují: jsou to **křivky hlavních směrů křivosti**.

---

## 2. Matematický základ

### Křivost plochy

Postavme se do bodu `p` na hladké ploše a mějme tam jednotkovou normálu `n`. Zajímá nás, jak se
plocha v okolí zakřivuje. Když se z `p` vydáme v nějakém tečném směru, plocha se pod námi ohýbá —
někde víc, někde míň, a v různých směrech různě.

Toto chování popisuje **Weingartenův operátor** (též operátor tvaru) `S`: lineární zobrazení tečné
roviny do sebe, které tečnému posunu přiřadí odpovídající změnu normály. Je to symetrická matice
2×2, takže má:

- dvě reálná **vlastní čísla** `kMin ≤ kMax` — **hlavní křivosti**,
- dva na sebe kolmé **vlastní vektory** — **hlavní směry**.

Hlavní směry jsou ty, ve kterých se plocha ohýbá nejvíc a nejmíň. Odvozené veličiny: střední
křivost `H = (kMin + kMax)/2` a Gaussova křivost `K = kMin · kMax`.

Příklady:

| plocha | `kMin` | `kMax` | hlavní směry |
|---|---|---|---|
| rovina | 0 | 0 | **neexistují** |
| koule `R` | `1/R` | `1/R` | **neexistují** |
| válec `R` | 0 | `1/R` | podél osy / kolem tubusu |
| sedlo | záporná | kladná | dvě kolmé osy |

Ty dva prázdné řádky nejsou opomenutí. Vrátíme se k nim v části 4 — jsou jádrem celého problému.

### Křivky hlavních směrů

Hlavní směry tvoří na ploše **směrové pole**. Integrální křivky tohoto pole — křivky, jejichž tečna
je v každém bodě hlavním směrem — se nazývají **křivky křivosti** (lines of curvature). Tvoří na
ploše ortogonální síť.

Na válci jsou to kružnice kolem tubusu a přímky podél osy. Na toru dvě rodiny kružnic. Na rotačně
symetrické ploše soustředné kružnice a radiální paprsky.

**Podstatné je, že jsou vlastností plochy, ne jejího umístění.** Otočíme-li objektem, otočí se
s ním, ale samy se nezmění. Přesně to potřebujeme.

### Podpis

Podél takové křivky vzorkujeme v **konstantním kroku délky oblouku** trojici:

```
(kMin, kMax, κ_g)
```

`kMin` a `kMax` jsou hlavní křivosti v daném bodě. `κ_g` je **geodetická křivost** samotné křivky —
ta část jejího ohybu, která leží *uvnitř* plochy.

Rozklad křivosti křivky na ploše:

```
κ² = κ_n² + κ_g²
```

`κ_n` (normálová) je vnucená plochou samotnou a o křivce neříká nic. `κ_g` popisuje, jak křivka
zatáčí *po povrchu*, a je tedy nositelem informace. Na geodetice je nulová.

Všechny tři veličiny jsou invariantní vůči tuhé transformaci. Konstantní krok v délce oblouku
zajistí, že jsou dvě takové posloupnosti porovnatelné.

### Zbytek pipeline

Zbývající kroky jsou už standardní:

1. Podpisy se nakrájejí na okna a **zarovnají jako sekvence** — Smithův–Watermanův algoritmus,
   tentýž, který se používá pro lokální zarovnání DNA. Výsledkem jsou dvojice odpovídajících si
   bodů.
2. Z každé dvojice se **Kabschovým algoritmem** (SVD křížové kovarianční matice) spočítá tuhá
   transformace.
3. Kandidátů jsou tisíce, většinou špatných. Správná odpověď se pozná tím, že se na ní shodne
   hodně kandidátů — hledá se tedy **nejhustší shluk** v prostoru transformací.
4. Vítěz se doladí ICP.

Kroky 1–4 zatím nejsou implementované; návrh je v [09](09-dalsi-faze.md).

---

## 3. První úskalí: reálná data nejsou hezká

Matematika výše mlčky předpokládá **hladkou, orientovatelnou, manifoldní plochu**. Výstup skeneru
takový není.

### Co je manifoldní síť

Trojúhelníková síť je manifoldní, když se v každém bodě lokálně chová jako kus roviny. Prakticky:

- každá hrana patří **nejvýše dvěma** trojúhelníkům,
- okolí každého vrcholu tvoří **jediný souvislý vějíř** trojúhelníků.

Odhad křivosti i trasování na tom stojí. Odhad potřebuje kolem bodu okolí, ve kterém proloží
operátor tvaru; tracer potřebuje přes hranu přejít do jednoznačně určeného souseda.

### Co v datech reálně je

Na 24 souborech přiloženého datasetu:

| závada | výskyt |
|---|---|
| **motýlkové vrcholy** | v 15 z 24 souborů, 1 až 499 na soubor |
| **izolované vrcholy** | v 8 souborech, 1 až 71 |
| **nemanifoldní hrany** | ve 2 souborech, 32 a 70 |
| **nekonzistentní navinutí** | ve 2 souborech |
| **degenerované trojúhelníky** | přes 4 000 v jednom souboru |

Původní implementace při duplicitní orientované hraně vyhodila výjimku
`"Non-manifold mesh at the input."` — dva soubory se tedy vůbec nenačetly.

Horší ale byly závady, které procházely **tiše**.

### Motýlkový vrchol

Dvě jinak oddělené záplaty plochy se dotýkají v jediném bodě:

```
        ╱|              |╲
       ╱ |              | ╲
      ╱__|              |__╲
          ╲            ╱
            ╲        ╱
              ╲    ╱
                ●          ← tady
              ╱    ╲
            ╱        ╲
```

Každá *hrana* je přitom dokonale manifoldní, takže kontroly na úrovni hran ho vůbec nevidí. Vrchol
má ale **dva disjunktní vějíře**, a obchůzka okolí může projít jen jeden z nich. Původní kód
ukládal jeden libovolný přilehlý roh na vrchol, takže tiše vracel neúplné okolí — a z něj špatnou
křivost.

### Řešení: opravovat, ne odmítat

Načítání proto neodmítá nic. Postup má devět kroků; nejdůležitější tři:

**Hrany se seskupí do kbelíků** podle neorientované hrany. Klíč je zabalený do jediného `ulong`
(`min << 32 | max`), pole se seřadí a kbelíky vzniknou skenem. Zásadní je, že se tím **odhalí
kbelíky libovolné velikosti** — kód nemusí po dvojicích rozhodovat, jestli je hrana legální.

Podle velikosti kbelíku: 1 = okraj, 2 = normální hrana, ≥3 = nemanifoldní. Ty se řeší
přepínatelnou politikou; výchozí **rozřízne** hranu pro všechny přilehlé rohy, čímž se plocha
rozpadne na manifoldní záplaty. Nic se nezahazuje — jen sousednost přes singularitu. Trasování se
tam zastaví přesně jako na skutečné hranici, takže zbytek pipeline nepotřebuje žádný zvláštní
případ.

**Motýlkové vrcholy se rozštěpí.** Union-find nad rohy: dva rohy se spojí, když manifoldní hrana
dovolí vějíři přejít z jednoho na druhý. Protože obchůzka vějíře nikdy neopustí vrchol, každá
vzniklá množina je právě jeden vějíř. Vrchol s více vějíři se **duplikuje** — jednou na vějíř.
Pozice se nemění, kopie leží na sobě; mění se jen konektivita. Geometrie, kterou vidí prohlížeč,
je identická, ale každý dotaz na okolí se stane dobře definovaným.

Tím je topologie **manifoldní z konstrukce** a zbytek kódu se na to smí spoléhat.

**Vše se hlásí.** Report obsahuje počty všech nalezených a opravených závad. Tiché závady jsou
horší než hlasité; report je nedělá tichými.

### Vedlejší efekt: nesvařené sítě

Existuje ještě jedna, na první pohled neviditelná varianta. Některé exportéry ukládají pro každý
trojúhelník **vlastní kopii jeho vrcholů**. Souřadnice jsou bit po bitu identické, ale indexy
různé — a program pozná sousedství podle **sdílených indexů**, ne podle souřadnic.

Taková síť je topologicky rozpadlá na samostatné trojúhelníky. Ověřeno na reálném souboru
(210 984 vrcholů na 70 328 stěn, přesně trojnásobek):

| | bez svaření | se svařením |
|---|---|---|
| komponenty | **70 323** | 12 |
| manifoldní hrany | **0** | ~105 000 |
| nepoužitelné vrcholy | **100 %** | 0.17 % |
| trasované čáry | **0** | 47 |

Svaření (`--weld`) sloučí vrcholy ležící na stejném místě a přeindexuje trojúhelníky. Souřadnicemi
nehýbe.

---

## 4. Druhé úskalí: místa, kde hlavní směr neexistuje

Tohle je zajímavější problém, protože není o špinavých datech. Je o matematice.

### Umbilické body

Vraťme se k těm dvěma prázdným řádkům z tabulky v části 2. Na **rovině** je operátor tvaru nulový.
Na **kouli** je násobkem identity. V obou případech platí `kMin = kMax`, a tedy:

> **Každý** tečný směr je hlavní. Žádný není vyznačený.

Takovému bodu se říká **umbilický**. Není to numerický problém, který by šlo obejít lepším
algoritmem — hlavní směr tam prostě jako matematický objekt **neexistuje**. Na kouli není žádný
směr „zakřivenější" než jiný.

### Jak to selhalo

Vlastní vektor symetrické matice `[[A,B],[B,C]]` se klasicky počítá volbou podle toho, který
jmenovatel je větší:

```csharp
if (Math.Abs(b) < Math.Abs(a - e1))  v1 = -b / (a - e1);
else                                 v2 = (e1 - a) / b;
```

Pro umbilický bod je `A == C` a `B == 0`, tedy `e1 == A`. Podmínka je `0 < 0` → nepravda → jde se
do druhé větve: `(e1 − A) / B` = **`0 / 0`** = **NaN**.

Přepočet větvení:

| vstup | výsledek `(e1, e2, v1, v2)` |
|---|---|
| rovina, `S = 0` | `(0, 0, 1, NaN)` |
| koule `R = 10` | `(0.1, 0.1, 1, NaN)` |
| válec `R = 10` | `(0, 0.1, 0, 1)` ✓ |
| skoro-umbilický, `B = 1e-12` | směr skočí z `(1,0)` na `(−1,1)` |

Doprovodný test singularity to nezachytil, protože zkoumal *momentovou matici* proložení, ne
výsledný operátor — a na rovině je momentová matice dokonale podmíněná. NaN pak propagoval do
traceru, jehož vlastní pojistka `moveVector.X == double.NaN` **nikdy nemohla zareagovat**, protože
NaN se nerovná sám sobě.

### Jak často to nastává

Ne okrajově. Podíl vrcholů bez definovaného hlavního směru:

| model | rovinné | umbilické | celkem |
|---|---|---|---|
| `dra1` (drak) | 0.01 % | 2.4 % | 2.4 % |
| `kac1` (kachna) | 0.31 % | 7.8 % | 8.1 % |
| `hip1` | 0.00 % | 29.1 % | 29.1 % |
| `eie1` (architektura) | **43.7 %** | 29.7 % | **73.4 %** |

U modelu s rovnými panely nemají tři čtvrtiny vrcholů hlavní směr.

### Řešení, část první: zůstat konečný

Klíčové pozorování: úhel vlastního vektoru symetrické matice 2×2 splňuje

```
tan(2θ) = 2B / (A − C)
```

odkud

```
θ = ½ · atan2(2B, A − C)
```

`atan2` je **totální funkce**: `Atan2(0, 0)` je definováno a vrací `0`. Dělení ze vzorce zmizelo
úplně. Degenerovaný vstup tedy dá libovolný, ale **konečný a deterministický** směr místo NaN.

### Řešení, část druhá: vědět, že odpověď nic neznamená

Konečné číslo ale **není použitelný směr**. Ten směr je pořád libovolný — jen už není NaN. Kdyby se
na tom skončilo, tracer by ho poslušně následoval a šel by kamkoliv.

Bod se proto musí **klasifikovat**. Křivost má rozměr `1/délka`, takže samotný práh na křivost
skrytě předpokládá velikost modelu. Vynásobením poloměrem okolí `r` vzniknou čistá čísla:

```
aniso = (kMax − kMin)/2 · r      < 0.05  →  Umbilic
curv  = max(|kMax|,|kMin|) · r   < 0.02  →  Planar (a zároveň Umbilic)
```

Čtou se jako „o kolik radiánů se plocha přes okolí stočí". Rovina dostane oba příznaky, protože
rovina *je* umbilická.

Volající pak nesmí testovat, jestli je vektor nenulový — musí se ptát na příznak. To je natolik
snadné splést, že to nese samostatný název: `CurvatureSample.HasUsableDirection`.

### Řešení, část třetí: co s tím udělá tracer

Uvnitř takové oblasti není co sledovat. Tracer proto **přestane číst směrové pole** a pokračuje
paralelním přenosem předchozího směru — tedy trasuje geodetiku.

Není to zvláštní kód. Za normálních okolností se směr přepíše z křivosti; při degeneraci se ta
větev jen přeskočí, takže v proměnné zůstane hodnota, kterou už paralelně přenesl pochod po ploše.

Krátké oblasti se tím **přemostí**: čára zůstane celistvá, zachová parametrizaci délkou oblouku
(což potřebuje fáze 2) a podpis v tom úseku poctivě zaznamená „rovina" nebo „koule", což je samo
o sobě použitelná informace. Po `MaxDegenerateRun` po sobě jdoucích degenerovaných vzorcích se
čára **ukončí** — přes dlouhou trať už přenesený směr nemá s plochou nic společného.

Naměřeno na jednom modelu: ze 91 degenerovaných úseků se 79 přemostilo a 12 ukončilo čáru. Že jde
opravdu o geodetiku, je vidět na datech — `κ_g` v takovém úseku padá na 0.0000.

A seedy se v takových oblastech nikdy nezakládají.

---

## 5. Co z toho plyne pro implementaci

### Bezrozměrnost

Přiložený dataset má úhlopříčky bounding boxu od **0.13 do 399.6** — poměr 2 900×. U průměrné
délky hrany je to 4 500×.

Jakákoliv absolutní konstanta v kódu je proto na části dat nesmyslná. Původní implementace měla
`length = 30` pro délku trasované čáry: na jednom modelu to znamenalo čáru o 11 krocích, na jiném
pokus o 43 000 kroků přes model 214× menší, než byla požadovaná délka.

Každý práh je proto vyjádřen relativně: délky jako násobky průměrné hrany nebo podíly úhlopříčky,
prahy degenerace jako křivost krát poloměr okolí. Průběžná kontrola: průměrná délka trasované čáry
jako podíl úhlopříčky se drží v pásmu **0.012–0.427** napříč celým datasetem.

### Přímkové pole, ne vektorové

Hlavní směry mají dvě nepříjemné vlastnosti. Každý je určen jen **až na znaménko**, a označení
„minimální" a „maximální" se **prohazuje** všude, kde se obě křivosti protnou — tedy právě podél
umbilických křivek.

Sledovat `DirMax` podle jména proto znamená skákat mezi různými integrálními křivkami. Tracer místo
toho vybírá ze všech čtyř kandidátů ten, který nejlépe pokračuje v přeneseném předchozím směru.

Není to jen estetika. Poloha výměny označení je citlivá na šum, takže dva skeny téhož objektu by
při sledování označení odbočily jinde a vytrasovaly **různé křivky** — přesně to, co by fázi 2
rozbilo. Spojitost dá tutéž křivku bez ohledu na označení, a podpis `(kMin, kMax, κ_g)` je na
označení nezávislý, protože je seřazený.

Naměřeno: s výchozím seedováním na maximum skončí 52–93 % vzorků na maximálním poli. Míchání je
tedy reálné a měří se, nepředpokládá.

### Jak vůbec ověřit, že čára jde správně

Na reálném skenu není s čím výsledek porovnat. Proto nástroj umí místo souboru vygenerovat plochu,
u které je odpověď známá: na válci musí být čára maximální křivosti **kružnice kolmá k ose**, na
rotačně symetrické ploše **soustředná kružnice nebo radiální paprsek**, v parabolickém žlabu
**dokonale přímý ruling**.

Dvě jemnosti, které se ukázaly až při použití:

- **Mřížka nesmí být polární.** Kdyby se rotačně symetrická plocha síťovala polárně, hrany sítě by
  ležely na očekávaných křivkách a tracer, který by jen kopíroval hrany, by vypadal správně.
  Kartézská mřížka to zarovnání odstraní.
- **Obrys musí respektovat symetrii plochy.** Na čtvercovém výřezu rotačně symetrické plochy
  nepadlo 8 ze 42 čar ani do jedné rodiny — a nešlo o chybu trasování, ty čáry měly 100 % vzorků
  u hranice čtverce. Po ořezu na kruh: 38 kružnic, 12 paprsků, 0 nejasných.

---

## 6. Výsledky

Nad celým datasetem (24 souborů, 4.9 M trojúhelníků):

| kontrola | výsledek |
|---|---|
| běh skončil bez chyby | **25 / 25** (včetně dodaného souboru navíc) |
| `NaN` nebo `Infinity` ve výstupu | **0 výskytů** |
| dva běhy bajtově shodné | **ano** |
| délka čáry / úhlopříčka | 0.012–0.427 napříč měřítky lišícími se 2 900× |
| celý dataset | **~16 s** |

Největší model (1.48 M trojúhelníků, 114 MB) projde celou pipeline za 5.6 s, z toho načtení 0.7 s.

Testů je 97. Správnost se prokazuje proti plochám se známou křivostí, ne proti reálným skenům —
u koule se dá porovnat s `1/R`, u reálného skenu není s čím.

Tři skutečné chyby odhalily testy až během implementace:

1. **Geodetická křivost se počítala v prostoru, ne v ploše.** Test na válci: hlavní kružnice válce
   jsou geodetiky, takže `κ_g` má být 0 — naměřeno −1.05 ≈ −1/R. Chyběla projekce tětiv do tečné
   roviny. Tutéž chybu měla i původní implementace.
2. **Po překlopení trojúhelníka se permutují rohy**, takže indexy rohů v edge bucketech přestanou
   odpovídat hranám, pro které byly postavené. Odhalil test orientace tetraedru.
3. **Popis znaménka u parabolického válce byl obrácený.** S vnější normálou se žlab odklání od
   normály, takže parabolická křivost je záporná a nula (přímé rulings) je **maximum**, ne minimum.

---

## 7. Co zbývá

Implementované je načtení, oprava topologie, odhad křivosti a trasování čar. Chybí druhá polovina:
krájení podpisů, Smithovo–Watermanovo zarovnání, Kabsch, shlukování transformací podle hustoty
a ICP.

Rozhraní mezi fázemi je hotové — `TracedLine` v kódu a `<název>_samples.csv` na disku.

Dvě věci jsou tam podle měření předem známé jako problematické:

- **Skórování v zarovnání se musí standardizovat.** Původní implementace porovnávala euklidovskou
  vzdálenost nad syrovým `(k1, k2, κ_g)` s pevným prahem `0.055`. Ten je absolutní, míchá kanály
  s různým rozsahem, a hlavně: dlouhý rovný nebo kulový úsek má skoro konstantní podpis, takže se
  zarovná stejně dobře **kdekoli**. To je klasický zdroj falešných shod. Kanály je potřeba
  standardizovat robustně (medián/MAD) a vzorkům s nízkou anizotropií dát váhu → 0.
- **Kandidátní transformace potřebují test podmíněnosti.** Segmenty ležící na kouli nebo v rovině
  určují transformaci jen do **jednoparametrické rodiny** (rotace kolem středu, posun v rovině).
  Kabsch v takovém případě vrátí *nějakou* odpověď, ale ta je podél nulového směru libovolná.
  Pozná se to podle nejmenšího singulárního čísla křížové kovarianční matice.

---

## Odkazy do dokumentace

| téma | dokument |
|---|---|
| architektura, invarianty | [01](01-architektura.md) |
| oprava topologie krok za krokem | [02](02-nacitani-a-topologie.md) |
| odvození odhadu křivosti | [03](03-krivosti.md) |
| pochod po ploše, volba směru | [04](04-trasovani.md) |
| naměřené hodnoty | [08](08-vysledky.md) |
| návrh fáze 2 | [09](09-dalsi-faze.md) |
| analytické tvary | [12](12-analyticke-tvary.md) |
