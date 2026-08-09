WAA bundled LMDB runtime
========================

Source: https://github.com/LMDB/lmdb
Release: LMDB_1.0.1
Commit: 6f0a32496a5aadee15a5e5103c479bd3355ae273
License: OpenLDAP Public License 2.8 (see LICENSE.txt)

Windows x64 build:
  x86_64-w64-mingw32-gcc -O2 -shared -static-libgcc \
    -Wl,--export-all-symbols -o lmdb.dll mdb.c midl.c -ladvapi32

Windows SHA-256:
  337b749a297eb2f52c54c4ecb2b384c9cb0124d58a2f18cddcc035497c1107ba  lmdb.dll

The DLL imports only ADVAPI32.dll, KERNEL32.dll, and msvcrt.dll. It requires
no installation or administrator access. liblmdb.so is built from the same
source solely to execute the repository's validation suite on Linux.

Linux validation SHA-256:
  bcc657f0d81982b51afa92b0024222cbc3d3869d9884814ffd3b6eadbf8bd7a2  liblmdb.so
