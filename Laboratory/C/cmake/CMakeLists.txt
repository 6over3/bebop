cmake_minimum_required(VERSION 3.20)
set(RUNTIME_SOURCE_DIR "${CMAKE_CURRENT_SOURCE_DIR}/../../../Runtime/C/src")
set(GEN_SOURCE_DIR "${CMAKE_CURRENT_SOURCE_DIR}/../gen")
project(sandbox LANGUAGES C)

add_custom_command(
    WORKING_DIRECTORY "${CMAKE_CURRENT_SOURCE_DIR}/.."
    COMMAND dotnet run --project ../../Compiler --trace --include "../Schemas/Valid/jazz.bop" build --generator "c:gen/jazz.c"
    DEPENDS "${CMAKE_CURRENT_SOURCE_DIR}/../../Schemas/Valid/jazz.bop"
    OUTPUT "${GEN_SOURCE_DIR}/bebop.h" "${GEN_SOURCE_DIR}/bebop.c" "${GEN_SOURCE_DIR}/jazz.h" "${GEN_SOURCE_DIR}/jazz.c"
    COMMENT "Compiling jazz.bop"
)
add_custom_target(bebopc_jazz DEPENDS "${GEN_SOURCE_DIR}/bebop.h" "${GEN_SOURCE_DIR}/bebop.c" "${GEN_SOURCE_DIR}/jazz.h" "${GEN_SOURCE_DIR}/jazz.c")

set(CMAKE_C_STANDARD 11)
set(CMAKE_C_STANDARD_REQUIRED ON)

# NOTE: We could have checked for __STDC_NO_ATOMICS__, and then also this might be not required if we were to build with BEBOP_SINGLE_THREADED
set(CMAKE_C_FLAGS "${CMAKE_C_FLAGS} /experimental:c11atomics")

add_executable(sandbox
    "${GEN_SOURCE_DIR}/bebop.h"
    "${GEN_SOURCE_DIR}/bebop.c"
    "${GEN_SOURCE_DIR}/jazz.h"
    "${GEN_SOURCE_DIR}/jazz.c"
    #sandbox.c
    "${GEN_SOURCE_DIR}/../test/jazz_bench.c"
)
target_include_directories(sandbox PRIVATE ${RUNTIME_SOURCE_DIR})
set_property(DIRECTORY ${CMAKE_CURRENT_SOURCE_DIR} PROPERTY VS_STARTUP_PROJECT sandbox)
add_dependencies(sandbox bebopc_jazz)
