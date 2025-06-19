#include "../gen/union_perf_b.h"
#include "../gen/bebop.h"
#include <assert.h>
#include <stdio.h>
#include <stdlib.h>
#include <time.h>

int main()
{
    // Create a context for memory management
    bebop_context_t* context = bebop_context_create();
    assert(context != NULL);

    // Create GUID
    bebop_guid_t myGuid = bebop_guid_from_string("81c6987b-48b7-495f-ad01-ec20cc5f5be1");

    const int count = 1000000;

    // Seed random number generator
    srand((unsigned int)time(NULL));

    // Start timing
    clock_t start_time = clock();

    int32_t sum = 0;

    for (int i = 0; i < count; i++) {
        // Create inner B14 struct
        b14_t inner = { .i14 = rand() % 2,
            .u = 11111,
            .b = true,
            .f = 3.14,
            .g = myGuid,
            .s = bebop_string_view_from_cstr("yeah") };

        // Encode B14 to get encoded data
        bebop_writer_t inner_writer;
        assert(bebop_context_get_writer(context, &inner_writer) == BEBOP_OK);
        assert(b14_encode_into(&inner, &inner_writer) == BEBOP_OK);

        // Get the encoded data as a byte array view
        uint8_t* inner_buffer;
        size_t inner_buffer_length;
        assert(bebop_writer_get_buffer(&inner_writer, &inner_buffer,
                   &inner_buffer_length)
            == BEBOP_OK);

        // Create the byte array view for encoded_data
        bebop_byte_array_view_t encoded_data = { .data = inner_buffer,
            .length = inner_buffer_length };

        // Create UnionPerfB with the encoded data
        union_perf_b_t b = { .protocol_version = 456,
            .incoming_opcode = 789,
            .encoded_data = encoded_data };

        // Encode UnionPerfB
        bebop_writer_t outer_writer;
        assert(bebop_context_get_writer(context, &outer_writer) == BEBOP_OK);
        assert(union_perf_b_encode_into(&b, &outer_writer) == BEBOP_OK);

        // Get buffer for outer encoding
        uint8_t* outer_buffer;
        size_t outer_buffer_length;
        assert(bebop_writer_get_buffer(&outer_writer, &outer_buffer,
                   &outer_buffer_length)
            == BEBOP_OK);

        // Decode UnionPerfB
        bebop_reader_t outer_reader;
        assert(bebop_context_get_reader(context, outer_buffer, outer_buffer_length,
                   &outer_reader)
            == BEBOP_OK);

        union_perf_b_t b2;
        assert(union_perf_b_decode_into(&outer_reader, &b2) == BEBOP_OK);

        // Decode the inner B14 from the encoded_data
        bebop_reader_t inner_reader;
        assert(bebop_context_get_reader(context, b2.encoded_data.data,
                   b2.encoded_data.length,
                   &inner_reader)
            == BEBOP_OK);

        b14_t inner2;
        assert(b14_decode_into(&inner_reader, &inner2) == BEBOP_OK);

        // Add to sum
        sum += inner2.i14;

        // Reset context for next iteration (clears buffers)
        bebop_context_reset(context);
    }

    // End timing
    clock_t end_time = clock();
    double elapsed_ms = ((double)(end_time - start_time) / CLOCKS_PER_SEC) * 1000.0;

    printf("Computed sum=%d in %.0fms\n", sum, elapsed_ms);

    bebop_context_destroy(context);
    return 0;
}