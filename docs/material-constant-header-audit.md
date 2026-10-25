# Black constant-color materials

The PC single/multi-texture serialized material header starts with the GRO
type at byte 0, ambient at 4, diffuse at 8, packed specular/constant color at
12, and the floating-point specular exponent at 16. Runtime structure offsets
must not be substituted for these serialized offsets.

`ResolvedMaterial.Specular` previously read byte 16. A zero exponent therefore
turned valid constant-color materials black in both the viewport and GLB
exports. Reading byte 12 fixes the common format reader; no object-name or
scene-key exceptions are involved. Serialized black constants remain black.

Source evidence:

- `external/jaded/src/libraries/GraphicDK/Sources/MATerial/MATmultitexture.c`,
  `MAT_pst_CreateMultiTextureFromBuffer`, reads ambient, diffuse, specular,
  exponent and opacity in that order after the GRO type.
- `external/jaded/src/libraries/GraphicDK/Sources/MATerial/MATDraw.c`, the
  `MAT_Cc_ColorOp_Disable` branch, selects specular RGB as the constant.
- `external/irisEng/graphicsobjects/geomaterial.c` reads `solidVertexColor`
  before `unkFloat1` in the same sequence.
- IrisTool's `GAOInnerParserMaterialMultiTexture.parse` reads `vertexColor`
  as the third material word; `OpenGLFunctions.java` selects it for color mode 2.

Archive verification against the supplied `sally_clean.bf`:

- `FF007685`, `01_07_entrepot_debarras_caisses`, `PNJ_Rat_01.gao`: member
  material `A90018CA`, constant `00B0B0B0`, exponent bits `00000000`, resolved
  texture `A9002455`. Exported RGB factors are approximately `(0.6902, 0.6902, 0.6902)`.
- `FF0045A2`, `Cine_SFX_Faisceau_Trou.gao`: material `3500BFE8`, constant
  `0047F201`, exponent bits `00000000`, resolved texture `62002871`.
  Exported RGB factors are approximately `(0.0039, 0.9490, 0.2784)`; local
  alpha remains independently decoded from the material layer.

The earlier synthetic specular fixtures repeated the erroneous offset and
could not catch the bug. Corrected fixtures separate diffuse, packed constant
and zero exponent, including a GPU pixel check and GLB/baked-layer checks.
The generic object audit now accepts a world name as its container selector
and reports actor-member materials and resolved texture provenance.
