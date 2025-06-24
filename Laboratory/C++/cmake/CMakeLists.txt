cmake_minimum_required(VERSION 3.20)
set(RUNTIME_SOURCE_DIR "${CMAKE_CURRENT_SOURCE_DIR}/../../../Runtime/C++/src")
set(GEN_SOURCE_DIR "${CMAKE_CURRENT_SOURCE_DIR}/../gen")
project(sandbox LANGUAGES CXX)

add_custom_command(
    WORKING_DIRECTORY "${CMAKE_CURRENT_SOURCE_DIR}/.."
    COMMAND dotnet run --project ../../Compiler --trace --include "../Schemas/Valid/jazz.bop" build --generator "cpp:gen/jazz.hpp"
    DEPENDS "${CMAKE_CURRENT_SOURCE_DIR}/../../Schemas/Valid/jazz.bop"
    OUTPUT "${GEN_SOURCE_DIR}/bebop.hpp" "${GEN_SOURCE_DIR}/jazz.hpp"
    COMMENT "Compiling jazz.bop"
)
add_custom_target(bebopc_jazz DEPENDS
    "${GEN_SOURCE_DIR}/bebop.hpp"
    "${GEN_SOURCE_DIR}/jazz.hpp"
)

set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

add_executable(sandbox
    "${GEN_SOURCE_DIR}/bebop.hpp"
    "${GEN_SOURCE_DIR}/jazz.hpp"
    "${GEN_SOURCE_DIR}/../test/jazz.cpp"
)
target_include_directories(sandbox PRIVATE ${RUNTIME_SOURCE_DIR})
set_property(DIRECTORY ${CMAKE_CURRENT_SOURCE_DIR} PROPERTY VS_STARTUP_PROJECT sandbox)
add_dependencies(sandbox bebopc_jazz)
