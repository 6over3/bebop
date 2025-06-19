#include "../gen/const.h" // Generated constants header
#include "../gen/bebop.h"
#include <assert.h>
#include <math.h>
#include <stdio.h>
#include <string.h>

int main()
{
    printf("=== Testing Bebop Constants ===\n");

    // Test byte constant
    printf("PCASE = 0x%02X (expected 0x12)\n", PCASE);
    assert(PCASE == 0x12);

    // Test int32 constant
    printf("EXAMPLE_CONST_INT32 = %d (expected -123)\n", EXAMPLE_CONST_INT32);
    assert(EXAMPLE_CONST_INT32 == -123);

    // Test uint64 constant
    printf("EXAMPLE_CONST_UINT64 = 0x%llX (expected 0x123FFFFFFFF)\n",
        (unsigned long long)EXAMPLE_CONST_UINT64);
    assert(EXAMPLE_CONST_UINT64 == 0x123ffffffffULL);

    // Test float64 constant
    printf("EXAMPLE_CONST_FLOAT64 = %.9e (expected 1.234567800e+11)\n",
        EXAMPLE_CONST_FLOAT64);
    assert(fabs(EXAMPLE_CONST_FLOAT64 - 123.45678e9) < 1e6); // Allow small floating point error

    // Test infinity constants
    printf("EXAMPLE_CONST_INF = %f (expected inf)\n", EXAMPLE_CONST_INF);
    assert(isinf(EXAMPLE_CONST_INF) && EXAMPLE_CONST_INF > 0);

    printf("EXAMPLE_CONST_NEG_INF = %f (expected -inf)\n", EXAMPLE_CONST_NEG_INF);
    assert(isinf(EXAMPLE_CONST_NEG_INF) && EXAMPLE_CONST_NEG_INF < 0);

    // Test NaN constant
    printf("EXAMPLE_CONST_NAN = %f (expected nan)\n", EXAMPLE_CONST_NAN);
    assert(isnan(EXAMPLE_CONST_NAN));

    // Test boolean constants
    printf("EXAMPLE_CONST_FALSE = %s (expected false)\n",
        EXAMPLE_CONST_FALSE ? "true" : "false");
    assert(EXAMPLE_CONST_FALSE == false);

    printf("EXAMPLE_CONST_TRUE = %s (expected true)\n",
        EXAMPLE_CONST_TRUE ? "true" : "false");
    assert(EXAMPLE_CONST_TRUE == true);

    // Test string constant (both #define and string view)
    printf("EXAMPLE_CONST_STRING = %s\n", EXAMPLE_CONST_STRING);
    printf("EXAMPLE_CONST_STRING_VIEW.length = %zu\n",
        EXAMPLE_CONST_STRING_VIEW.length);
    printf("EXAMPLE_CONST_STRING_VIEW.data = \"%.*s\"\n",
        (int)EXAMPLE_CONST_STRING_VIEW.length, EXAMPLE_CONST_STRING_VIEW.data);

    // Verify string content matches expected value
    const char* expected_string = "hello \"world\"\nwith newlines";
    assert(strcmp(EXAMPLE_CONST_STRING, expected_string) == 0);
    assert(EXAMPLE_CONST_STRING_VIEW.length == 27); // Expected length from generated code
    assert(strncmp(EXAMPLE_CONST_STRING_VIEW.data, expected_string,
               EXAMPLE_CONST_STRING_VIEW.length)
        == 0);

    // Test GUID constant
    printf("Testing EXAMPLE_CONST_GUID...\n");

    // Convert GUID to string for verification
    bebop_context_t* context = bebop_context_create();
    assert(context != NULL);

    char* guid_str;
    bebop_result_t result = bebop_guid_to_string(EXAMPLE_CONST_GUID, context, &guid_str);
    assert(result == BEBOP_OK);

    printf("EXAMPLE_CONST_GUID = %s (expected "
           "e215a946-b26f-4567-a276-13136f0a1708)\n",
        guid_str);
    assert(strcmp(guid_str, "e215a946-b26f-4567-a276-13136f0a1708") == 0);

    bebop_context_destroy(context);

    // Test that we can create a GUID from the expected string and it matches
    bebop_guid_t expected_guid = bebop_guid_from_string("e215a946-b26f-4567-a276-13136f0a1708");
    assert(bebop_guid_equal(EXAMPLE_CONST_GUID, expected_guid));

    printf("\n✅ All constant tests passed!\n");

    // Print summary of what we tested
    printf("\nTested constants:\n");
    printf("- Byte constant PCASE (0x12)\n");
    printf("- Int32 constant (-123)\n");
    printf("- UInt64 constant (0x123ffffffffULL)\n");
    printf("- Float64 constant (123.45678e9)\n");
    printf("- Infinity constants (+inf, -inf)\n");
    printf("- NaN constant\n");
    printf("- Boolean constants (true, false)\n");
    printf("- String constant with quotes and newlines (length=27)\n");
    printf("- GUID constant\n");

    return 0;
}