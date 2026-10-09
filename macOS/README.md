# macOS build

The macOS desktop frontend uses .NET 10 and Avalonia. It embeds the same dog,
bed, feeder and toy artwork as the Windows application. Download the Apple
silicon (`arm64`) or Intel (`x64`) ZIP from the project's GitHub release.
The ZIP contains `Kitsu.app` and launch instructions in English and Russian.

To build locally, install .NET SDK 10 and Xcode Command Line Tools on macOS:

```bash
bash macOS/build-macos.sh
```

This produces both architecture packages under `artifacts/macOS`. To build
one architecture or choose another output directory:

```bash
bash macOS/build-macos.sh osx-arm64 ./artifacts/macOS
bash macOS/build-macos.sh osx-x64 ./artifacts/macOS
```

The script publishes a self-contained application, creates its `.app` bundle,
signs its native code and bundle with an ad-hoc signature, verifies the
signature, and runs the artwork `--self-test` and GUI `--smoke-test` when the
target matches the host CPU.
`ditto` creates the ZIP with the executable permissions preserved. No .NET
installation is needed on the receiving Mac. The minimum macOS version is 14.

The community release has no Apple Developer ID notarization. Its first-launch
instructions follow [Apple's per-app exception procedure](https://support.apple.com/en-us/102445).
Application data and error logs are stored outside the signed bundle in
`~/Library/Application Support/Kitsu`. Windows desktop-icon interaction is
available in the Windows application.

The [release workflow](../.github/workflows/release.yml) builds and launches the
application on separate native Apple silicon and Intel GitHub runners. It also
runs all six Windows checks and launches the standalone Windows executable.
Only after every platform succeeds does it verify the three archives and
produce a combined `kitsu-downloads` artifact and `SHA256SUMS.txt`.

Publishing a GitHub release triggers those checks and attaches the verified
archives to that release. To retry an existing release, run the workflow
manually with its `release_tag` (for example, `v7.0.0`). Leave the field empty
to build and check without attaching release assets. The tag must match the
version in `Kitsu.csproj`.
