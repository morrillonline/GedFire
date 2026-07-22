# GEDCOM test fixtures

`Example-7.0.ged` is an unmodified copy of `maximal70-tree1.ged` from the
FamilySearch GEDCOM.io repository at commit
`26c332316ddbe5cfcdf6c1ef51917bb5b2caa9d2`:

https://raw.githubusercontent.com/FamilySearch/GEDCOM.io/26c332316ddbe5cfcdf6c1ef51917bb5b2caa9d2/testfiles/gedcom70/maximal70-tree1.ged

The upstream file states that it contains no meaningful historical or
genealogical data. The upstream repository did not publish a repository-level
license file at the pinned revision. This copy therefore retains its source
identity and must not be represented as independently licensed by GedFire.

`Example-5.5.ged` is generated from `Example-7.0.ged` by this repository's
`gedfire downgrade` command. It is a derived test artifact and must be
regenerated, not edited by hand:

```text
.\GedFire\bin\Debug\net10.0\GedFire.exe downgrade --input GedCore.Tests/TestData/Example-7.0.ged --output GedCore.Tests/TestData/Example-5.5.ged
```

`Extensions-7.0.ged` is a hand-authored fixture (not derived from an
upstream file) used by `ExtensionTagPreservationTests`. Its `HEAD.SCHMA`
declaration syntax and its use of an extension tag as a direct
substructure (e.g. `1 _MILT`) follow the pattern shown in the official
FamilySearch GEDCOM 7 extension-tag example file:

https://github.com/FamilySearch/GEDCOM.io/blob/main/testfiles/gedcom70/extensions.ged

The specific tags (`_MILT`, `_NICKSRC`, `_ANNIV`) are invented for this
fixture only and carry no special meaning to GedFire.