# Barcode encoder notice

Phase 7.3 uses **ZXing.Net 0.16.11**, by the ZXing.Net contributors, under the Apache License 2.0. Upstream: [ZXing.Net](https://github.com/micjahn/ZXing.Net); pinned package: [NuGet 0.16.11](https://www.nuget.org/packages/ZXing.Net/0.16.11). The full license is [ZXing-Apache-2.0.txt](ZXing-Apache-2.0.txt) and is copied to `ThirdPartyNotices` in build output. No upstream source was modified.

Only the package's `lib/net48/zxing.dll` (531,968 bytes) is referenced and deployed. Its .NET Framework 4.8 asset has no package dependencies. The package's optional presentation/WPF assembly is excluded; it is not a project dependency. Production uses the Code128/EAN13 writers' boolean module output and the existing System.Drawing native print pipeline. Tests also use its decoder for raster round trips; this proves software encoding consistency, not physical scanner acceptance or independent barcode certification.

The library uses managed encoders; it adds no modern Windows print API, online service, font or browser dependency. Windows 7 SP1 still requires .NET 4.8, the legacy Chromium 109 WebView2 distribution, suitable fonts and an actual supported printer driver. That target has not been tested here.
