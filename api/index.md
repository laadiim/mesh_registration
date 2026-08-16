# API reference

Referenční dokumentace veřejného rozhraní, generovaná z XML komentářů v kódu.

Zdroj jsou **zkompilované assembly a jejich XML soubory**, ne zdrojové texty — viz poznámka
v `docfx.json`.

## Jmenné prostory

### `MeshRegistration.Core`

| jmenný prostor | obsah |
|---|---|
| <xref:MeshRegistration.Core.Geometry> | <xref:MeshRegistration.Core.Geometry.Vec3>, <xref:MeshRegistration.Core.Geometry.BoundingBox>, <xref:MeshRegistration.Core.Geometry.TangentFrame> |
| <xref:MeshRegistration.Core.Numerics> | <xref:MeshRegistration.Core.Numerics.Sym2x2> (operátor tvaru a jeho vlastní čísla), <xref:MeshRegistration.Core.Numerics.Sym3x3Solver> (LDL řešič) |
| <xref:MeshRegistration.Core.Mesh> | <xref:MeshRegistration.Core.Mesh.TriangleMesh>, <xref:MeshRegistration.Core.Mesh.MeshTopology>, <xref:MeshRegistration.Core.Mesh.MeshBuilder> (oprava topologie), <xref:MeshRegistration.Core.Mesh.SurfacePoint> |

### `MeshRegistration.Algorithms`

| jmenný prostor | obsah |
|---|---|
| <xref:MeshRegistration.Algorithms.Curvature> | <xref:MeshRegistration.Algorithms.Curvature.ShapeOperatorField>, <xref:MeshRegistration.Algorithms.Curvature.CurvatureSample>, <xref:MeshRegistration.Algorithms.Curvature.CurvatureFlags> |
| <xref:MeshRegistration.Algorithms.Tracing> | <xref:MeshRegistration.Algorithms.Tracing.SurfaceWalker>, <xref:MeshRegistration.Algorithms.Tracing.LineTracer>, <xref:MeshRegistration.Algorithms.Tracing.SeedSelector>, <xref:MeshRegistration.Algorithms.Tracing.TracedLine> |

### `MeshRegistration.IO`

| jmenný prostor | obsah |
|---|---|
| <xref:MeshRegistration.IO> | <xref:MeshRegistration.IO.ObjReader> |
| <xref:MeshRegistration.IO.Export> | exportéry pro MeshLab, CSV a JSON |

## Kde začít

Typický průchod pipeline:

1. <xref:MeshRegistration.IO.ObjReader.Read*> — načte pozice a trojúhelníky
2. <xref:MeshRegistration.Core.Mesh.MeshBuilder.Build*> — opraví topologii, vrátí
   <xref:MeshRegistration.Core.Mesh.MeshBuildResult>
3. <xref:MeshRegistration.Algorithms.Curvature.ShapeOperatorField.Compute*> — proloží operátor
   tvaru na každém vrcholu
4. <xref:MeshRegistration.Algorithms.Tracing.SeedSelector.Select*> — vybere výchozí body
5. <xref:MeshRegistration.Algorithms.Tracing.LineTracer.TraceAll*> — vytrasuje čáry

## Dva typy, které je potřeba znát

<xref:MeshRegistration.Core.Numerics.Sym2x2.Eigen> — rozklad operátoru tvaru. Je **totální**:
pro rovinu i kouli vrací konečné hodnoty tam, kde klasická formulace počítá `0/0`.

<xref:MeshRegistration.Algorithms.Curvature.CurvatureSample.HasUsableDirection> — **tohle musí
volající kontrolovat**, ne jen to, jestli je směrový vektor nenulový. Eigensolver vždy vrátí
konečné číslo, takže nenulovost není důkaz, že hlavní směr existuje.
