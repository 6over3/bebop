#pragma once

#include "schema.h"
#include <assert.h>
#include <stdio.h>
#include <string.h>

/**
 * @brief Creates a library with test data matching the C++ reference
 * @param context The bebop context for memory allocation
 * @return A library_t structure with test data
 */
library_t make_library(bebop_context_t *context);

/**
 * @brief Validates that a library contains the expected test data
 * @param lib Pointer to the library to validate
 */
void is_valid(const library_t *lib);