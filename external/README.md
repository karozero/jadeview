# External source references

These are local, shallow Git checkouts requested for comparative research. They
are separate upstream repositories, not JadeView code or submodules. Their
contents are not included in JadeView builds.

| Repository | Upstream revision checked out | Purpose | License notes |
|---|---|---|---|
| [irisEng](https://github.com/ka87/irisEng) | `45ef8bb74977288567259ca9da26a35578b9bcc1` | Reconstructed/reversed-engineered Jade/BGE engine and BGE AI-script sources | No root license or repository README was present in this checkout; do not redistribute or copy code based on this checkout alone. |
| [iristool](https://github.com/ka87/iristool) | `40a7643ca2405c4f9f5ca7cff9006fcbbefeedd6` | Java BF viewer and animation preview | Root `LICENSE` is GNU GPL v3. |
| [jaded](https://github.com/hogsy/jaded) | `8d55704c80adfcec4f3bcaeb2445dc9f68ef960e` | Community-maintained later Jade Engine source | No root-level project license was found; individual bundled dependencies may have their own licenses. |

The checked-out commit IDs make the reviewed snapshots identifiable. Refreshing
the repositories changes the evidence and requires rechecking this note. Keep
each upstream's license and attribution with its source; do not copy code into
JadeView without first verifying the applicable license.

See [the facial-event comparison](../docs/face-morph-pc-dialogue-chain.md#external-source-comparison)
for findings relevant to the PC 2003 cutscene investigation and the version
limits on those findings.
