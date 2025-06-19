#!/usr/bin/env bash
set -e
>&2 echo "Timing bebopc:"

if [ -e /proc/version ] && grep -q Microsoft /proc/version; then
  # Windows: Visual Studio + WSL to run this script
  bebopc="../../bin/compiler/Windows-Debug/bebopc.exe"
else
  # Linux or Mac
  bebopc="dotnet run --project ../../Compiler"
fi
$bebopc --trace --include "../Schemas/Valid/$1.bop" build --generator "c:gen/$1.c"
rm ./a.out || true
>&2 echo "Timing C++ compiler:"
time clang -std=c11 test/$1.c gen/$1.c gen/bebop.c -Wall
./a.out
