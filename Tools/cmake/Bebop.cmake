set(BEBOP_RELEASES_URL https://github.com/6over3/bebop/releases/download
    CACHE STRING "Public location of Bebop binary releases" FORCE)

set(BEBOP_LANGUAGES cpp cs ts dart rust py c)

include(FetchContent)

function(Bebop_Generate target_name)
    set(_options)
    set(_unaryargs VERSION LANGUAGE OUTPUT NAMESPACE OPTIONS)
    set(_varargs BOPS)

    cmake_parse_arguments(PARSE_ARGV 1 Bebop_Generate "${_options}" "${_unaryargs}" "${_varargs}")

    if(NOT Bebop_Generate_BOPS)
        message(SEND_ERROR "Error: Bebop_Generate was not given any BOPS as input")
    endif()

    if(NOT Bebop_Generate_VERSION)
        message(SEND_ERROR "Error: Bebop_Generate must be pinned to a VERSION")
    endif()
    set(_bebopc_prefix "bebopc_${Bebop_Generate_VERSION}")

    if(NOT Bebop_Generate_LANGUAGE)
        set(Bebop_Generate_LANGUAGE cpp)
    endif()
    string(TOLOWER "${Bebop_Generate_LANGUAGE}" Bebop_Generate_LANGUAGE)
    list(FIND BEBOP_LANGUAGES ${Bebop_Generate_LANGUAGE} _i)
    if(_i EQUAL -1)
        message(SEND_ERROR "Error: Bebop_Generate was given an unknown LANGUAGE \"${Bebop_Generate_LANGUAGE}\"")
    endif()

    if(NOT Bebop_Generate_OUTPUT)
        message(SEND_ERROR "Error: Bebop_Generate not given an OUTPUT path")
    endif()

    set(_bebopc_executable_name "bebopc")

    string(TOLOWER "${CMAKE_HOST_SYSTEM_PROCESSOR}" _system_processor)
    
    if(_system_processor STREQUAL "amd64")
        set(_system_processor "x64")
    elseif(_system_processor STREQUAL "arm64")
        set(_system_processor "arm64")
    endif()

    FetchContent_GetProperties(${_bebopc_prefix})
    if(NOT ${_bebopc_prefix}_POPULATED)
        if(CMAKE_HOST_WIN32)
            string(APPEND _bebopc_executable_name ".exe")
            set(_bebopc_zip "bebopc-windows-${_system_processor}.zip")
        elseif(CMAKE_HOST_APPLE)
            set(_bebopc_zip "bebopc-macos-${_system_processor}.zip")
        else()
            set(_bebopc_zip "bebopc-linux-${_system_processor}.zip")
        endif()
        
        set(_bebopc_zip_url "${BEBOP_RELEASES_URL}/${Bebop_Generate_VERSION}/${_bebopc_zip}")

        FetchContent_Declare(${_bebopc_prefix}
            URL "${_bebopc_zip_url}"
        )
        FetchContent_Populate(${_bebopc_prefix})
    endif()
    set(_bebopc "${${_bebopc_prefix}_SOURCE_DIR}/${_bebopc_executable_name}")

    set(_includeArgs --include)
    foreach(_bop IN LISTS Bebop_Generate_BOPS)
        list(APPEND _includeArgs ${_bop})
    endforeach()

    set(_generatorArgs "--generator" "${Bebop_Generate_LANGUAGE}:${Bebop_Generate_OUTPUT}")
    if(Bebop_Generate_OPTIONS)
        string(APPEND _generatorArgs ",${Bebop_Generate_OPTIONS}")
    endif()

    add_custom_command(
        OUTPUT ${Bebop_Generate_OUTPUT}
        COMMAND "${_bebopc}"
        ${_includeArgs} build
        ${_generatorArgs}
        DEPENDS ${BEBOP_COMPILER} ${Bebop_Generate_BOPS}
    )

    add_custom_target(${target_name} DEPENDS ${Bebop_Generate_OUTPUT})
endfunction()
