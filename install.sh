#!/bin/bash
#
# Bebop: an extremely simple, fast, efficient, cross-platform serialization format
# this script is responsible for installing /updating the Bebop compiler
# https://github.com/6over3/bebop
#
# The Apache License 2.0 License

set -u

abort() {
    printf "%s\n" "$@"
    exit 1
}

# Fail fast with a concise message when not using bash
# Single brackets are needed here for POSIX compatibility
if [ -z "${BASH_VERSION:-}" ]; then
    abort "Bash is required to use this script."
fi

### Constants
readonly BEBOPC_VERSION="${1:-3.1.3}"
readonly BEBOP_RELEASE_URL="https://api.github.com/repos/6over3/bebop/releases/tags/v${BEBOPC_VERSION}"

### string formatters

# listen, emojis are a way of life
COLLISION_UTF8=$'\360\237\222\245'
NOGO_UTF8=$'\360\237\231\205'
LOOKING_UTF8=$'\360\237\247\220'
GLASS_UTF8=$'\360\237\224\215'
ROCKET_UTF8=$'\360\237\232\200'
ABORT_UTF8=$'\360\237\232\253'
CONSTRUCTION_UTF8=$'\360\237\232\247'
SUCCESS_UTF8=$'\342\234\205'
ERROR_UTF8=$'\342\235\214'
HAPPY_UTF8=$'\360\237\230\200'
UNICORN_UTF8=$'\360\237\246\204'
FIRE_UTF8=$'\xF0\x9F\x94\xA5'
LOCK_UTF8=$'\360\237\224\222'
EYES_UTF8=$'\360\237\221\200'

if [[ -t 1 ]]; then
    tty_escape() { printf "\033[%sm" "$1"; }
else
    tty_escape() { :; }
fi
tty_mkbold() { tty_escape "1;$1"; }
tty_underline="$(tty_escape "4;39")"
tty_blue="$(tty_mkbold 34)"
tty_white="$(tty_mkbold 37)"
tty_red="$(tty_mkbold 31)"
tty_bold="$(tty_mkbold 39)"
tty_reset="$(tty_escape 0)"

shell_join() {
    local arg
    printf "%s" "$1"
    shift
    for arg in "$@"; do
        printf " "
        printf "%s" "${arg// /\ }"
    done
}

chomp() {
    local spaces="${2:-0}"
    printf "%*s%s" "$spaces" '' "${1/"$'\n'"/}"
}

point() {
    printf "${tty_blue}==>${tty_bold} %s${tty_reset}\n" "$(shell_join "$@")"
}

warn() {
    printf "${tty_red}Warning${tty_reset}: %s\n" "$(chomp "$1")"
}

### interactivity checks

# Check if script is run with force-interactive mode in CI
if [[ -n "${CI-}" && -n "${INTERACTIVE-}" ]]; then
    abort "${ABORT_UTF8} Cannot run force-interactive mode in CI."
fi

# Check if both `INTERACTIVE` and `NONINTERACTIVE` are set
# Always use single-quoted strings with `exp` expressions
# shellcheck disable=SC2016
if [[ -n "${INTERACTIVE-}" && -n "${NONINTERACTIVE-}" ]]; then
    abort 'Both `$INTERACTIVE` and `$NONINTERACTIVE` are set. Please unset at least one variable and try again.'
fi

# Check if script is run non-interactively (e.g. CI)
# If it is run non-interactively we should not prompt for passwords.
# Always use single-quoted strings with `exp` expressions
# shellcheck disable=SC2016
if [[ -z "${NONINTERACTIVE-}" ]]; then
    if [[ -n "${CI-}" ]]; then
        warn 'Running in non-interactive mode because `$CI` is set.'
        NONINTERACTIVE=1
    elif [[ ! -t 0 ]]; then
        if [[ -z "${INTERACTIVE-}" ]]; then
            warn 'Running in non-interactive mode because `stdin` is not a TTY.'
            NONINTERACTIVE=1
        else
            warn 'Running in interactive mode despite `stdin` not being a TTY because `$INTERACTIVE` is set.'
        fi
    fi
else
    point 'Running in non-interactive mode because `$NONINTERACTIVE` is set.'
fi

### fetch OS info

# don't throw on no case matches (so we can handle it)
shopt -s nocasematch

# Cache the output of uname so we don't have to spawn it multiple times.
IFS=" " read -ra uname <<<"$(uname -srm)"

# get OS name
kernel_name="${uname[0]}"
case $kernel_name in
Darwin) os=macos ;;
Linux | GNU*)
    os=linux
    ;;
*)
    abort "$COLLISION_UTF8} Unsupported OS detected: '${tty_underline}${tty_white}$kernel_name${tty_reset}', aborting..."
    ;;
esac
unset kernel_name
readonly os

# get OS version
os_version="${uname[1]}"
if [[ "$os" == "mac" ]]; then
    # OS/X tries really hard to hand back programs the fake "10.16" version number rather than the true "11.0.X" one.
    export SYSTEM_VERSION_COMPAT=0
    IFS=$'\n' read -d "" -ra sw_vers <<<"$(awk -F'<|>' '/key|string/ {print $3}' \
        "/System/Library/CoreServices/SystemVersion.plist")"
    for ((i = 0; i < ${#sw_vers[@]}; i += 2)); do
        case ${sw_vers[i]} in
        ProductVersion) os_version=${sw_vers[i + 1]} ;;
        esac
    done
fi
readonly os_version

# get OS arch
kernel_arch="${uname[2]}"
case $kernel_arch in
x86_64 | amd64)
    os_arch=x64
    ;;
arm64 | aarch64)
    os_arch=arm64
    ;;
*)
    abort "$COLLISION_UTF8} Unsupported OS architecture detected: '${tty_underline}${tty_white}$kernel_arch${tty_reset}', aborting..."
    ;;
esac
unset kernel_arch
readonly os_arch

readonly system_info="${tty_underline}${tty_white}$os $os_version ($os_arch)${tty_reset}"

# OS support checks

unset HAVE_SUDO_ACCESS # unset this from the environment

have_sudo_access() {
    if [[ ! -x "/usr/bin/sudo" ]]; then
        return 1
    fi

    local -a SUDO=("/usr/bin/sudo")
    if [[ -n "${SUDO_ASKPASS-}" ]]; then
        SUDO+=("-A")
    elif [[ -n "${NONINTERACTIVE-}" ]]; then
        SUDO+=("-n")
    fi

    if [[ -z "${HAVE_SUDO_ACCESS-}" ]]; then
        if [[ -n "${NONINTERACTIVE-}" ]]; then
            "${SUDO[@]}" -l mkdir &>/dev/null
        else
            "${SUDO[@]}" -v && "${SUDO[@]}" -l mkdir &>/dev/null
        fi
        HAVE_SUDO_ACCESS="$?"
    fi

    if [[ $os != "linux" ]] && [[ "${HAVE_SUDO_ACCESS}" -ne 0 ]]; then
        abort "${FIRE_UTF8} Need sudo access on macOS (e.g. the user ${USER} needs to be an Administrator)!"
    fi

    return "${HAVE_SUDO_ACCESS}"
}

### Helper functions

major_minor() {
    echo "${1%%.*}.$(
        x="${1#*.}"
        echo "${x%%.*}"
    )"
}
version_gt() {
    [[ "${1%.*}" -gt "${2%.*}" ]] || [[ "${1%.*}" -eq "${2%.*}" && "${1#*.}" -gt "${2#*.}" ]]
}
version_ge() {
    [[ "${1%.*}" -gt "${2%.*}" ]] || [[ "${1%.*}" -eq "${2%.*}" && "${1#*.}" -ge "${2#*.}" ]]
}
version_lt() {
    [[ "${1%.*}" -lt "${2%.*}" ]] || [[ "${1%.*}" -eq "${2%.*}" && "${1#*.}" -lt "${2#*.}" ]]
}

uuid() {
    local N B C='89ab'

    for ((N = 0; N < 16; ++N)); do
        B=$((RANDOM % 256))

        case $N in
        6)
            printf '4%x' $((B % 16))
            ;;
        8)
            printf '%c%x' ${C:$RANDOM%${#C}:1} $((B % 16))
            ;;
        3 | 5 | 7 | 9)
            printf '%02x' $B
            ;;
        *)
            printf '%02x' $B
            ;;
        esac
    done

    echo
}

ring_bell() {
    # Use the shell's audible bell.
    if [[ -t 1 ]]; then
        printf "\a"
    fi
}

download() {
    local url=$1
    local filename=$2
    if ! result=$(
        if [[ -x "$(which wget)" ]]; then
            wget -O "${filename}" "${url}" 2>&1
        elif [[ -x "$(which curl)" ]]; then
            curl -fLO "${filename}" "${url}" 2>&1
        fi
    ); then
        local error_message
        error_message=$(echo "$result" | grep -o -i "failed.*\|error.*\|command.*")

        abort "$(
            cat <<EOABORT
${ERROR_UTF8} 
   - ${tty_red}Unable to download '${url}'${tty_reset} (exit code 1):
     - ${error_message}
EOABORT
        )"
    fi
}

fetch_release_urls() {
    local target_os_name="$1"
    local target_os_arch="$2"
    readonly bebopc_target="bebopc-${target_os_name}-${target_os_arch}.zip"
    readonly chordc_target="chordc-${target_os_name}-${target_os_arch}.zip"
    local exit_code=1
    if result=$(
        if [[ -x "$(which wget)" ]]; then
            wget -O - "${BEBOP_RELEASE_URL}" 2>&1
        elif [[ -x "$(which curl)" ]]; then
            curl -fL "${BEBOP_RELEASE_URL}" 2>&1
        fi
    ); then
        local bebopc_url
        local chordc_url
        bebopc_url="$(echo "$result" | grep -E 'browser_download_url' | grep "${bebopc_target}" | cut -d '"' -f 4)"
        chordc_url="$(echo "$result" | grep -E 'browser_download_url' | grep "${chordc_target}" | cut -d '"' -f 4)"
        if [[ -n "$bebopc_url" && -n "$chordc_url" ]]; then
            echo "$bebopc_url $chordc_url"
            exit 0
        fi
    else
        exit_code=$?
    fi

    local error_message
    error_message=$(echo "$result" | grep -o -i "failed.*\|error.*\|command.*")

    abort "$(
        cat <<EOABORT
${ERROR_UTF8} 
   - ${tty_red}Unable to locate release artifacts for bebopc and chordc${tty_reset} ${tty_underline}${tty_white}$BEBOPC_VERSION${tty_reset} (exit code ${exit_code}):
     - $error_message
EOABORT
    )"
}

extract() {
    local filename=$1
    local destination=$2
    if ! result=$(sudo unzip -X -qq -o -j "${filename}" -d "${destination}" 2>&1); then
        abort "$(
            cat <<EOABORT
${ERROR_UTF8} 
 - ${tty_red}Failed to install to ${BEBOPC_PREFIX}/bin${tty_reset}:
   - ${result}
EOABORT
        )"
    else
        # cleanup the archive after successful extraction
        rm -f "$filename" &>/dev/null
    fi
}

# USER isn't always set so provide a fall back for the installer and subprocesses.
if [[ -z "${USER-}" ]]; then
    USER="$(chomp "$(id -un)")"
    export USER
fi

# Invalidate sudo timestamp before exiting (if it wasn't active before).
if [[ -x /usr/bin/sudo ]] && ! /usr/bin/sudo -n -v 2>/dev/null; then
    trap '/usr/bin/sudo -k' EXIT
fi

# Things can fail later if `pwd` doesn't exist.
# Also sudo prints a warning message for no good reason
cd "/usr" || exit 1

####################################################################### Install Script

point "${GLASS_UTF8} Checking for dependencies..."
if ! command -v curl >/dev/null && ! command -v wget >/dev/null; then
    abort "$COLLISION_UTF8} You must install either ${tty_underline}${tty_white}cURL${tty_reset} or ${tty_underline}${tty_white}wget${tty_reset} to use this script."
fi

if ! command -v unzip >/dev/null; then
    abort "$COLLISION_UTF8} You must install ${tty_underline}${tty_white}unzip${tty_reset} to use this script."
fi

BEBOPC_PREFIX="/usr/local"

# shellcheck disable=SC2016
point "${LOCK_UTF8} Checking for \`sudo\` access (which may request your password)..."
if [[ $os != "linux" ]]; then
    have_sudo_access
elif [[ -n "${NONINTERACTIVE-}" ]] && ! have_sudo_access; then
    abort "${NOGO_UTF8} Insufficient permissions to install bebopc to \"${BEBOPC_PREFIX}\"."
fi

# Lets enforce good hygeine / practices and abort if the user has executed this script with sudo
# Especially since this script is most likely being piped in over the network
if [[ "${EUID:-${UID}}" == "0" ]]; then
    # Allow Azure Pipelines/GitHub Actions/Docker/Concourse/Kubernetes to do everything as root (as it's normal there)
    if ! [[ -f /proc/1/cgroup ]] ||
        ! grep -E "azpl_job|actions_job|docker|garden|kubepods" -q /proc/1/cgroup; then
        abort "${NOGO_UTF8} ${tty_red}Don't run this as root!${tty_reset}"
    fi
fi

if [[ -d "${BEBOPC_PREFIX}" && ! -x "${BEBOPC_PREFIX}" ]]; then
    abort "$(
        cat <<EOABORT
${LOOKING_UTF8} The bebopc prefix ${tty_underline}${BEBOPC_PREFIX}${tty_reset} exists but is not searchable.
If this is not intentional, please restore the default permissions and
try running the installer again:
    ${tty_underline}${tty_white}sudo chmod 775 ${BEBOPC_PREFIX}${tty_reset}
EOABORT
    )"
fi

# check if bebopc is already installed
if [[ -x "$(which bebopc)" ]]; then
    installed_bebopc_version="$(bebopc --version 2>/dev/null)"
    readonly installed_bebopc_version
    # exit when the remote version of bebopc is the same as the currently installed version
    if [[ "${installed_bebopc_version}" == "${BEBOPC_VERSION}" ]]; then
        point "${ROCKET_UTF8} ${tty_underline}${tty_white}bebopc $installed_bebopc_version${tty_reset} is already installed and up-to-date"
        exit 0
    fi
    # abort when the remote version is less than the installed version.
    # downgrading an install could change code generation
    if version_lt "$(major_minor "${BEBOPC_VERSION}")" "$(major_minor "${installed_bebopc_version}")"; then
        abort "$(
            cat <<EOABORT
$COLLISION_UTF8} ${tty_underline}${tty_white}bebopc $installed_bebopc_version${tty_reset} is already installed,
and a higher version than targeted ${tty_underline}${tty_white}${BEBOPC_VERSION}${tty_reset}.

Please uninstall bebopc and try again.

EOABORT
        )"
    fi

    # warn and proceed if we're updating rather than installing
    # this is a warning because version changes may introduce code generator changes
    # patches do not produce a warning
    if version_lt "$(major_minor "${installed_bebopc_version}")" "$(major_minor "${BEBOPC_VERSION}")"; then
        warn "${tty_underline}${tty_white}bebopc $installed_bebopc_version${tty_reset} will be upgraded to ${tty_underline}${tty_white}${BEBOPC_VERSION}${tty_reset}"
    fi

else
    point "${EYES_UTF8} This script will install:"
    echo "- ${BEBOPC_PREFIX}/bin/bebopc"
fi

point "${UNICORN_UTF8} Downloading and installing Bebop (${BEBOPC_VERSION})..."
(
    echo -ne "- ${tty_bold}Locating releases${tty_reset} "

    if ! release_urls=$(fetch_release_urls "$os" "$os_arch"); then
        # should be the error if fetch_release_urls failed
        abort "$release_urls"
    fi

    read -r bebopc_url chordc_url <<<"$release_urls"

    echo -e "${SUCCESS_UTF8}"

    echo -ne "- ${tty_bold}Creating temp files${tty_reset} "

    bebopc_temp_file=$(mktemp)
    chordc_temp_file=$(mktemp)
    readonly bebopc_temp_file chordc_temp_file
    # Bail out if the temp files weren't created successfully.
    if [ ! -e "$bebopc_temp_file" ] || [ ! -e "$chordc_temp_file" ]; then
        abort "$(
            cat <<EOABORT
${ERROR_UTF8} 
    ${tty_red}Failed to create temporary files${tty_reset}
EOABORT
        )"
    fi

    echo -e "${SUCCESS_UTF8}"
    echo -ne "- ${tty_bold}Downloading bebopc${tty_reset} "

    download "${bebopc_url}" "${bebopc_temp_file}"

    echo -e "${SUCCESS_UTF8}"
    echo -ne "- ${tty_bold}Downloading chordc${tty_reset} "

    download "${chordc_url}" "${chordc_temp_file}"

    echo -e "${SUCCESS_UTF8}"
    echo -ne "- ${tty_bold}Installing bebopc and chordc "

    extract "${bebopc_temp_file}" "${BEBOPC_PREFIX}/bin"
    extract "${chordc_temp_file}" "${BEBOPC_PREFIX}/bin"

    BEBOPC_PATH="${BEBOPC_PREFIX}/bin/bebopc"
    CHORDC_PATH="${BEBOPC_PREFIX}/bin/chordc"
    if [ ! -f "${BEBOPC_PATH}" ] || [ ! -f "${CHORDC_PATH}" ]; then
        abort "$(
            cat <<EOABORT
${ERROR_UTF8} 
    ${tty_red}Could not locate \`${BEBOPC_PATH}\` or \`${CHORDC_PATH}\` after installation${tty_reset}
EOABORT
        )"
    fi

    echo -e "${SUCCESS_UTF8}"

    if [[ ":${PATH}:" != *":${BEBOPC_PREFIX}/bin:"* ]]; then
        warn "${tty_underline}${tty_white}${BEBOPC_PREFIX}/bin${tty_reset} is not in your ${tty_underline}${tty_white}PATH${tty_reset}.
  Instructions on how to configure your shell for bebopc and chordc
  can be found in the 'Next steps' section below."
    fi

    point "${ROCKET_UTF8} Installation successful!"

    echo
    ring_bell

    point "Tempo, an RPC framework built on top of Bebop is in public preview. Check it out:"
    echo "$(
        cat <<EOS
  ${tty_underline}${tty_white}https://tempo.im${tty_reset}
EOS
    )
"
    case "${SHELL}" in
    */bash*)
        if [[ -r "${HOME}/.bash_profile" ]]; then
            shell_profile="${HOME}/.bash_profile"
        else
            shell_profile="${HOME}/.profile"
        fi
        ;;
    */zsh*)
        shell_profile="${HOME}/.zprofile"
        ;;
    *)
        shell_profile="${HOME}/.profile"
        ;;
    esac

    point "${HAPPY_UTF8} Next steps:"
    readonly additional_shellenv_commands="export PATH=\$PATH:$BEBOPC_PREFIX/bin"
    echo "- Run this command in your terminal to add bebopc and chordc to your ${tty_underline}${tty_white}PATH${tty_reset}:"
    printf "${tty_bold}${tty_white}    echo '%s' >> ${shell_profile}${tty_reset}\n" "${additional_shellenv_commands[@]}"
    printf "${tty_bold}${tty_white}    %s${tty_reset}\n" "${additional_shellenv_commands[@]}"
    if [[ ! -x "$BEBOPC_PATH" ]] || [[ ! -x "$CHORDC_PATH" ]]; then
        printf "${tty_bold}${tty_white}    %s${tty_reset}\n" "sudo chmod +x ${BEBOPC_PATH} ${CHORDC_PATH}"
    fi

    cat <<EOS
- Run ${tty_bold}bebopc --help${tty_reset} to get started
- Further documentation:
    ${tty_underline}${tty_white}https://docs.bebop.sh${tty_reset}
EOS

) ||
    exit 1
