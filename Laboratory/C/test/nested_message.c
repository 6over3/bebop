#include "../gen/nested_message.h"
#include "../gen/bebop.h"
#include <assert.h>
#include <stdio.h>

int main()
{
    // Create a context for memory management
    bebop_context_t* context = bebop_context_create();
    assert(context != NULL);

    // Test 1: OuterM (message) containing optional InnerM and InnerS
    {
        printf("=== Testing OuterM (message with optionals) ===\n");

        // Create inner_m with x = 3
        inner_m_t inner_m_val = { 0 }; // Initialize optionals to none
        bebop_assign_some(inner_m_val.x, (int32_t)3);

        // Create inner_s with y = true
        inner_s_t inner_s_val = { .y = true };

        // Create outer_m with both inner values
        outer_m_t o = { 0 }; // Initialize optionals to none
        bebop_assign_some(o.inner_m, inner_m_val);
        bebop_assign_some(o.inner_s, inner_s_val);

        // Encode
        bebop_writer_t writer;
        assert(bebop_context_get_writer(context, &writer) == BEBOP_OK);
        assert(outer_m_encode_into(&o, &writer) == BEBOP_OK);

        // Get buffer
        uint8_t* buffer;
        size_t buffer_length;
        assert(bebop_writer_get_buffer(&writer, &buffer, &buffer_length) == BEBOP_OK);

        printf("Encoded %zu bytes\n", buffer_length);

        // Decode
        bebop_reader_t reader;
        assert(bebop_context_get_reader(context, buffer, buffer_length, &reader) == BEBOP_OK);

        outer_m_t o2;
        assert(outer_m_decode_into(&reader, &o2) == BEBOP_OK);

        // Verify decoded values
        assert(bebop_is_some(o2.inner_m));
        inner_m_t decoded_inner_m = bebop_unwrap(o2.inner_m);
        assert(bebop_is_some(decoded_inner_m.x));
        int32_t decoded_x = bebop_unwrap(decoded_inner_m.x);
        printf("o2.innerM.x = %d\n", decoded_x);
        assert(decoded_x == 3);

        assert(bebop_is_some(o2.inner_s));
        inner_s_t decoded_inner_s = bebop_unwrap(o2.inner_s);
        printf("o2.innerS.y = %s\n", decoded_inner_s.y ? "true" : "false");
        assert(decoded_inner_s.y == true);

        printf("✅ OuterM test passed!\n\n");
    }

    // Test 2: OuterS (struct) containing non-optional InnerM and InnerS
    {
        printf("=== Testing OuterS (struct with non-optionals) ===\n");

        // Create inner_m with x = 4
        inner_m_t inner_m_val = { 0 }; // Initialize optionals to none
        bebop_assign_some(inner_m_val.x, (int32_t)4);

        // Create inner_s with y = false
        inner_s_t inner_s_val = { .y = false };

        // Create outer_s with both inner values
        outer_s_t o = { .inner_m = inner_m_val, .inner_s = inner_s_val };

        // Encode
        bebop_writer_t writer;
        assert(bebop_context_get_writer(context, &writer) == BEBOP_OK);
        assert(outer_s_encode_into(&o, &writer) == BEBOP_OK);

        // Get buffer
        uint8_t* buffer;
        size_t buffer_length;
        assert(bebop_writer_get_buffer(&writer, &buffer, &buffer_length) == BEBOP_OK);

        printf("Encoded %zu bytes\n", buffer_length);

        // Decode
        bebop_reader_t reader;
        assert(bebop_context_get_reader(context, buffer, buffer_length, &reader) == BEBOP_OK);

        outer_s_t o2;
        assert(outer_s_decode_into(&reader, &o2) == BEBOP_OK);

        // Verify decoded values
        // Note: For OuterS, inner_m and inner_s are not optional, so we access them
        // directly
        assert(bebop_is_some(o2.inner_m.x));
        int32_t decoded_x = bebop_unwrap(o2.inner_m.x);
        printf("o2.innerM.x = %d\n", decoded_x);
        assert(decoded_x == 4);

        printf("o2.innerS.y = %s\n", o2.inner_s.y ? "true" : "false");
        assert(o2.inner_s.y == false);

        printf("✅ OuterS test passed!\n\n");
    }

    printf("✅ All nested message tests passed!\n");

    bebop_context_destroy(context);
    return 0;
}