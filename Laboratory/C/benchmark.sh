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
rm "$1"_bench.out || true
>&2 echo "Timing C++ compiler:"
time clang \
  -std=c11 \
  -O3 \
  -ffast-math \
  -march=native \
  -flto \
  -DNDEBUG \
  -Wall \
  -o "$1"_bench.out \
  test/"$1"_bench.c gen/"$1".c gen/bebop.c

./"$1"_bench.out
