# MeshRegistration

Nástroj pro **globální registraci dvojic trojúhelníkových sítí** — zarovnání dvou částečně se
překrývajících skenů bez počátečního odhadu. Metoda trasuje integrální křivky pole hlavních směrů
křivosti, vzorkuje podél nich podpis `(kMin, kMax, κ_g)` v konstantním kroku délky oblouku,
zarovnává tyto podpisy jako sekvence, z každého zarovnání odvodí tuhou transformaci a vybere
nejhustší shluk kandidátů.

.NET 10, bez knihoven na lineární algebru, cross-platform.

> [!NOTE]
> Implementovaná je **fáze 1**: načtení, oprava topologie, odhad křivosti, trasování čar a exporty
> pro MeshLab. Párování, shlukování transformací a ICP zatím ne — návrh je
> v [kapitole 9](docs/09-dalsi-faze.md).

## Kudy dál

| chci… | jít na |
|---|---|
| spustit nástroj a rozumět výstupu | [Manuál](MANUAL.md) |
| pochopit matematiku a algoritmy | [Technická dokumentace](docs/README.md) |
| najít konkrétní typ nebo metodu | [API reference](api/index.md) |
| vědět, proč přepis vznikl | [README](README.md) |

## Dvě opravené chyby

**Načítání nemanifoldních sítí.** Původní implementace vyhodila výjimku, jakmile se zopakovala
orientovaná hrana — v testovacích datech to spouští `cha1.obj` (32 nemanifoldních hran)
a `cha2m.obj` (70). Horší byly ale závady, které procházely tiše: motýlkové vrcholy (v 15 z 24
souborů, až 499 na soubor) a izolované vrcholy, kde obchůzka okolí vracela nesmysl bez jediného
varování. Nyní se síť opravuje a o opravě se vydá report — viz
[kapitola 2](docs/02-nacitani-a-topologie.md).

**Křivost na rovné a kulové ploše.** Na rovině je operátor tvaru nulový, na kouli násobkem
identity; klasická větev pro vlastní vektor pro obojí počítá `0/0` a vrátí NaN, který pak tiše
propaguje do traceru. Na reálných datech je takových vrcholů **1.4 % až 75 %**.
<xref:MeshRegistration.Core.Numerics.Sym2x2.Eigen> se dělení vyhne úplně a
<xref:MeshRegistration.Algorithms.Curvature.CurvatureFlags> říká, kdy výsledek nic neznamená —
viz [kapitola 3](docs/03-krivosti.md).

## Rychlý start

```bash
dotnet build -c Release
mkdir -p data && unzip -o ../vasa-projekt/Data.zip -d data
dotnet run -c Release --project src/MeshRegistration.Cli -- trace data/kac1.obj --out out --color-by flags
```

Otevřít `out/kac1_lines_tube.obj` (trasované čáry) a `out/kac1_curvature.obj` (síť obarvená podle
degenerace) v MeshLabu.
