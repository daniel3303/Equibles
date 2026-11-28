# Retained ESEF retrieval fixtures

- `glaston-2025-legacy-envelope.xhtml.gz` is the complete, unchanged retained extraction envelope from the [2025 annual report](https://www.oam.fi/cns-web/oam/viewAttachment.action?messageAttachmentId=340873), read on 2026-10-06.
- The earlier capture omitted stylesheet metadata and embedded media; the fixture is those exact stored bytes, including all remaining Inline XBRL facts and prose continuations.
- Gzip: 799,786 bytes, SHA-256 `4cf6823ed339e46502210ec80a7a452d464d332fa623c4be2af837bd6f0469f8`.
- Decompressed: 7,899,597 bytes, SHA-256 `4919e93a364d9c5ccabdcfc7eaaaea22981907d6c4805d3819f2e85692bb12cf`.
- The earlier normalized body was readable but split the issuer sentence from the facility amounts; replay joins the source-defined continuation without changing the retained envelope.
- Expected complete derived text: 584,638 bytes, SHA-256 `3adbf26bc784e81e71bf82adab8dc21544219ef480c613e9ca141d6189bff2f2`.
