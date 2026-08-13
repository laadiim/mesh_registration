# Technická dokumentace — fáze 1

Podrobný popis současného stavu řešení: matematika, algoritmy, datové struktury a naměřené
výsledky.

## Rozcestník

| dokument | obsah |
|---|---|
| [01 — Architektura](01-architektura.md) | projekty, tok dat, klíčové typy, invarianty řešení |
| [02 — Načítání a topologie](02-nacitani-a-topologie.md) | OBJ čtečka, corner table, oprava nemanifoldních sítí |
| [03 — Odhad křivosti](03-krivosti.md) | metoda nejmenších čtverců, vlastní čísla 2×2, detekce degenerace |
| [04 — Trasování čar](04-trasovani.md) | pochod po ploše, volba směru, geodetická křivost |
| [05 — Výstupy](05-vystupy.md) | formáty souborů, barevné mapy |
| [06 — Parametry](06-parametry.md) | referenční tabulka všech parametrů |
| [07 — Testování](07-testovani.md) | analytické plochy, topologické testy, co který test hlídá |
| [08 — Naměřené výsledky](08-vysledky.md) | statistiky nad 24 reálnými sítěmi, výkon |
| [09 — Návrh fáze 2](09-dalsi-faze.md) | co zbývá a na co si dát pozor |

Ostatní dokumenty v kořeni repozitáře:

- [README.md](../README.md) — stručné zdůvodnění návrhu (anglicky)
- [MANUAL.md](../MANUAL.md) — návod k obsluze
- [CLAUDE.md](../CLAUDE.md) — pokyny pro Claude Code (anglicky)

## Co je hotové

```
načtení OBJ  →  oprava topologie  →  odhad křivosti  →  výběr seedů  →  trasování  →  export
    ✓                 ✓                    ✓                ✓              ✓            ✓
```

## Co zbývá

```
→  krájení podpisů  →  Smith–Waterman  →  Kabsch  →  shlukování hustotou  →  ICP
        ✗                   ✗               ✗              ✗                  ✗
```

Rozhraní mezi fázemi je `TracedLine` v kódu a `<název>_samples.csv` na disku. Podrobněji
v [09 — Návrh fáze 2](09-dalsi-faze.md).

## Úloha jednou větou

Mějme dvě trojúhelníkové sítě `P` a `Q`, které zachycují částečně se překrývající části téhož
objektu, v libovolných vzájemných polohách. Hledáme tuhou transformaci `T ∈ SE(3)` takovou, že
`T(Q)` sedí na `P` — **bez počátečního odhadu**.

Metoda staví na tom, že hlavní směry křivosti jsou vlastností plochy samotné, ne jejího umístění
v prostoru. Integrální křivky tohoto směrového pole jsou tedy na obou sítích tytéž křivky
(v překryvu), a podpis vzorkovaný podél nich je invariantní vůči tuhé transformaci. Zarovnání
dvou takových podpisů jako sekvencí dá odpovídající si body, a z nich Kabschovým algoritmem
kandidátní transformaci.
