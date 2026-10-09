# Third-party dependencies

This project depends on [Mono.Cecil 0.11.5](https://github.com/jbevain/cecil/tree/0.11.5)
via NuGet. Copyright (c) 2008 - 2015 Jb Evain; Copyright (c) 2008 - 2011 Novell, Inc.
Its full MIT/X11 license is included in [licenses/Mono.Cecil-LICENSE.txt](licenses/Mono.Cecil-LICENSE.txt).
The project LICENSE, this notice and the Mono.Cecil license are copied to all
build and publish outputs as external files, including single-file builds.

Self-contained EXE packages also contain the .NET runtime. The publish target
copies `licenses/dotnet-LICENSE.txt` and `licenses/dotnet-THIRD-PARTY-NOTICES.txt`
from the actual resolved Microsoft.NETCore.App runtime package. Publishing fails
if those files cannot be found. Keep the complete publish directory together
when redistributing the EXE; do not distribute only the executable.

KKS, Koikatsu Sunshine, BepInEx and XUnity.AutoTranslator are third-party
projects/products. This project is not an official release of or affiliated
with their respective maintainers. Game assets and third-party DLLs are not
bundled with this repository.
