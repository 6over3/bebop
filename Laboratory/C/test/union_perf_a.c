#include "../gen/union_perf_a.h"
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
        // Create inner A14 struct
        a14_t inner = { .i14 = rand() % 2,
            .u = 11111,
            .b = true,
            .f = 3.14,
            .g = myGuid,
            .s = bebop_string_view_from_cstr("yeah") };

        // Create UnionPerfU with A14 variant (tag = 14)
        union_perf_u_t u = { .tag = 14, // A14 discriminator
            .as_a14 = inner };

        // Create UnionPerfA
        union_perf_a_t a = {
            .container_opcode = 123, .protocol_version = 456, .u = u
        };

        // Encode
        bebop_writer_t writer;
        assert(bebop_context_get_writer(context, &writer) == BEBOP_OK);
        assert(union_perf_a_encode_into(&a, &writer) == BEBOP_OK);

        // Get buffer
        uint8_t* buffer;
        size_t buffer_length;
        assert(bebop_writer_get_buffer(&writer, &buffer, &buffer_length) == BEBOP_OK);

        // Decode
        bebop_reader_t reader;
        assert(bebop_context_get_reader(context, buffer, buffer_length, &reader) == BEBOP_OK);

        union_perf_a_t a2;
        assert(union_perf_a_decode_into(&reader, &a2) == BEBOP_OK);

        // Extract value based on union tag
        if (a2.u.tag == 14) { // A14 discriminator
            sum += a2.u.as_a14.i14;
        }

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