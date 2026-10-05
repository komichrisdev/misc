# libwebp provenance

Version: 1.6.0, official Windows x64 precompiled distribution.

Documentation: https://developers.google.com/speed/webp/docs/precompiled
Archive: https://storage.googleapis.com/downloads.webmproject.org/releases/webp/libwebp-1.6.0-windows-x64.zip
Archive SHA-256: `48886f506b21f62e4661f0f4cbfca19800897c385128e8902542d29a950c93f1`

Included binary: `bin/dwebp.exe`, 512512 bytes.
Binary SHA-256: `17c1488bf84b7834e9aa908bb40afd0be2e55d57567d210e96336c56bb6ae993`
`dwebp.exe.gz` is a deterministic gzip of that exact binary.

License, patent grant and author notices were obtained from the upstream v1.6.0 tag at https://chromium.googlesource.com/webm/libwebp/ .

The test WebP files in `tests/fixtures/` were generated from Beholder's own fixture artwork using the same upstream distribution's `cwebp.exe` and `webpmux.exe`. Those encoders are test-generation tools, not runtime dependencies.
