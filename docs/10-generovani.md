# 10 — Generování dokumentace

Z XML komentářů v kódu se generuje **API reference**, která se spolu s touto koncepční
dokumentací skládá do jednoho prohledávatelného webu.

```bash
./scripts/build-docs.sh            # vygeneruje do _site/
./scripts/build-docs.sh --serve    # a spustí server na http://localhost:8080
```

Výstup (`_site/`, ~32 MB, 458 stránek) je v `.gitignore`.

---

## Nástroj: DocFX

Pro .NET je obdobou Doxygenu **DocFX** — čte XML dokumentační soubory, které vyrábí překladač
(`GenerateDocumentationFile` je zapnuté v `Directory.Build.props`), a generuje statický web.

### Instalace

```bash
dotnet tool install --global docfx --version 2.77.0
```

Verze je **záměrně přišpendlená**. DocFX 2.78+ potřebuje běhové prostředí ASP.NET Core 10, které
samotné .NET SDK neinstaluje; 2.77.0 má build pro net8.0 a rozběhne se na běžně přítomném
ASP.NET Core 8.

### Proč ne Doxygen

Doxygen C# formálně podporuje, ale parsuje zdrojový text vlastním parserem a na moderní syntaxi
selhává. Konkrétně na `Sym2x2.Eigen`, která vrací pojmenovanou n-tici:

```csharp
public (double EigenMax, double EigenMin, double AngleMax) Eigen()
```

Doxygen z toho udělá `double double double AngleMax Eigen()`, vymyslí si dva neexistující veřejné
atributy `EigenMax` a `EigenMin` — a **zahodí celý blok `<remarks>`**, tedy právě to vysvětlení
opravy NaN, které je nejcennější dokumentací v projektu.

DocFX čte XML vygenerované překladačem, takže žádný vlastní parser nemá a tento problém nastat
nemůže.

### Proč se čtou assembly, ne zdrojáky

`docfx.json` míří na `bin/Release/net10.0/*.dll`, ne na `.csproj`. Důvod: DocFX si zdrojáky
překládá vlastním přibaleným Roslynem, který **nespouští source generatory**. Kontext pro
serializaci JSON v `MeshRegistration.IO` je generovaný, takže překlad ze zdrojáků hlásí

```
error CS0534: 'ReportJsonContext' does not implement inherited abstract member
              'JsonSerializerContext.GetTypeInfo(Type)'
```

Čtení Release výstupu použije přesně to, co vyrobil skutečný překladač. Proto musí `build-docs.sh`
nejdřív přeložit řešení.

---

## Struktura

```
docfx.json          konfigurace: metadata (API) + build (web)
toc.yml             hlavní navigace
index.md            úvodní stránka webu
api/
  index.md          ručně psaný rozcestník API  ← ve verzování
  *.yml             generované, .gitignore
docs/
  toc.yml           navigace koncepční dokumentace
  *.md              tato dokumentace
_site/              výstup, .gitignore
```

`api/index.md` je ručně psaný a `docfx metadata` ho nemaže — ověřeno.

## Co se do webu dostane

| zdroj | výsledek |
|---|---|
| XML komentáře v kódu | API reference, jedna stránka na typ i na člen |
| `docs/*.md` | koncepční kapitoly |
| `README.md`, `MANUAL.md`, `CLAUDE.md` | samostatné stránky |
| `index.md` | úvodní rozcestník |

Odkazy `<xref:Plný.Název.Typu>` v markdownu se překládají na odkazy do API reference; odkazy na
typy .NET (např. `Math.Atan2`) míří do dokumentace Microsoftu. Fulltextové vyhledávání je zapnuté.

## Pokrytí komentáři

```
Členů v XML:  1 044
Se <summary>: 1 034  (99 %)
```

Zbývajících 10 jsou členy generované JSON source generatorem (`ReportJsonContext`), které nejsou
v tomto zdrojovém kódu.

## Kontrola při překladu

`build-docs.sh` spouští `docfx build --warningsAsErrors`, takže rozbitý odkaz mezi dokumenty
nebo neplatný křížový odkaz shodí generování. To je stejná politika jako `TreatWarningsAsErrors`
u kódu.

Časté příčiny selhání:

| varování | příčina |
|---|---|
| `InvalidFileLink` | odkaz na soubor, který není v `content` globu v `docfx.json` |
| `InvalidBookmark` | anchor v `toc.yml` neodpovídá tomu, jak DocFX generuje ID nadpisů (u českých nadpisů s diakritikou se liší od GitHubu) |
| `No .NET API detected` | chybí Release build, nebo se změnila cesta k assembly |

## Konvence pro psaní komentářů

- `<summary>` je jedna věta, co ta věc **je**.
- `<remarks>` je na to, **proč** je to tak, a hlavně proč se to liší od původní implementace.
  Zmínit konkrétní závadu, ne „vylepšeno" — ten kontext je hlavní důvod, proč to čtenář nezruší.
- Dokumentace parametrů je **všechno nebo nic**: popsat jeden `<param>` znamená popsat všechny
  (CS1573, a `TreatWarningsAsErrors` je zapnuté). Text, který se týká jen některých parametrů,
  patří do `<remarks>`.
- `<exception>` u všeho, co vyhazuje.
