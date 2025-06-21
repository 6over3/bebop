cmake_minimum_required (VERSION 3.20)
set(RUNTIME_SOURCE_DIR "${CMAKE_CURRENT_SOURCE_DIR}/../../../Runtime/C/src")
project(sandbox LANGUAGES C)
set(CMAKE_C_STANDARD 11)
set(CMAKE_C_STANDARD_REQUIRED ON)
set(CMAKE_C_FLAGS "${CMAKE_C_FLAGS} /experimental:c11atomics")
add_executable(sandbox 
    sandbox.c
    ${RUNTIME_SOURCE_DIR}/bebop.h
    ${RUNTIME_SOURCE_DIR}/bebop.c
)
target_include_directories(sandbox PRIVATE ${RUNTIME_SOURCE_DIR})
set_property(DIRECTORY ${CMAKE_CURRENT_SOURCE_DIR} PROPERTY VS_STARTUP_PROJECT sandbox)
