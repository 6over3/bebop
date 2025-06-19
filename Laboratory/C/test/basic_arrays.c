#include "../gen/basic_arrays.h"
#include "../gen/bebop.h"
#include <assert.h>
#include <stdio.h>
#include <stdlib.h>

int main(int argc, char** argv)
{
    bebop_context_t* context = bebop_context_create();
    assert(context != NULL);

    // Create array with argc elements, all set to 12345
    test_int32array_t original;

    // Allocate array data first
    int32_t* data = (int32_t*)bebop_context_alloc(context, argc * sizeof(int32_t));
    assert(data != NULL);

    // Fill the data
    for (int i = 0; i < argc; i++) {
        data[i] = 12345;
    }

    // Set up the original array to point to our data
    original.a.length = argc;
    original.a.data = data;

    // Encode
    bebop_writer_t writer;
    assert(bebop_context_get_writer(context, &writer) == BEBOP_OK);
    assert(test_int32array_encode(&original, &writer) == BEBOP_OK);

    // Get buffer and check size
    uint8_t* buffer;
    size_t buffer_length;
    assert(bebop_writer_get_buffer(&writer, &buffer, &buffer_length) == BEBOP_OK);
    assert(buffer_length == test_int32array_encoded_size(&original));

    printf("✅ Encoding successful: %zu bytes\n", buffer_length);

    // Now decode and verify the data matches
    bebop_reader_t reader;
    assert(bebop_context_get_reader(context, buffer, buffer_length, &reader) == BEBOP_OK);

    test_int32array_t decoded;
    assert(test_int32array_decode(&reader, &decoded) == BEBOP_OK);

    // Verify the decoded array matches the original
    assert(decoded.a.length == original.a.length);
    printf("✅ Array length matches: %zu elements\n", decoded.a.length);

    // Check each element
    for (size_t i = 0; i < decoded.a.length; i++) {
        assert(decoded.a.data[i] == original.a.data[i]);
        assert(decoded.a.data[i] == 12345); // Also verify against expected value
    }
    printf("✅ All %zu array elements match (value: 12345)\n", decoded.a.length);

    // Additional verification: check that we've read the entire buffer
    assert(bebop_reader_bytes_read(&reader) == buffer_length);
    printf("✅ Entire buffer was consumed during decode\n");

    bebop_context_destroy(context);

    printf("\n✅ All basic_arrays round-trip tests passed!\n");
    return 0;
}