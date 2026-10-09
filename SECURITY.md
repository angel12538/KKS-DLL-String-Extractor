# Security policy

The extractor operates on untrusted third-party DLL files. Its intent is to
inspect metadata/IL and embedded resources without executing those DLLs.
Do not use this tool as a malware scanner or assume arbitrary files are safe.
Run it with normal user privileges, preferably on a copy of a plugin folder.

Please report security concerns privately to the repository maintainer using
the GitHub security advisory feature, if enabled. Do not include proprietary
or private DLL contents in public reports.

Security fixes are not guaranteed for unmaintained releases. Update Mono.Cecil
and the .NET runtime as upstream patches become available.
