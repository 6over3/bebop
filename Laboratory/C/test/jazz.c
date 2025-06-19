#include "../gen/jazz.h"
#include "../gen/bebop.h"
#include <assert.h>
#include <stdio.h>
#include <string.h>
#include <math.h>

int main()
{
    // Create a context for memory management
    bebop_context_t *context = bebop_context_create();
    assert(context != NULL);

    // Test float array encoding/decoding first
    printf("=== Testing float array encoding/decoding ===\n");

    // Create sample data inline
    float sample_values[] = {1.5f, -2.75f, 3.14159f, 0.0f, -1000.5f};
    const size_t num_samples = sizeof(sample_values) / sizeof(sample_values[0]);

    // Create test data with inline initialization
    audio_data_t test_data = {
        .samples = {
            .length = num_samples,
            .data = sample_values}};

    printf("Original samples (%zu items): ", num_samples);
    for (size_t i = 0; i < num_samples; i++)
    {
        printf("%.6f ", test_data.samples.data[i]);
    }
    printf("\n");

    // Encode the test data
    bebop_writer_t test_writer;
    assert(bebop_context_get_writer(context, &test_writer) == BEBOP_OK);

    printf("Computed byte count before encoding: %zu\n", audio_data_encoded_size(&test_data));

    assert(audio_data_encode_into(&test_data, &test_writer) == BEBOP_OK);

    uint8_t *test_buffer;
    size_t test_buffer_length;
    assert(bebop_writer_get_buffer(&test_writer, &test_buffer, &test_buffer_length) == BEBOP_OK);

    printf("Bytes written to buffer: %zu\n", test_buffer_length);
    printf("Buffer contents:");
    for (size_t i = 0; i < test_buffer_length; i++)
    {
        printf(" %02x", test_buffer[i]);
    }
    printf("\n");

    // Decode the test data
    bebop_reader_t test_reader;
    assert(bebop_context_get_reader(context, test_buffer, test_buffer_length, &test_reader) == BEBOP_OK);

    audio_data_t decoded_test;
    assert(audio_data_decode_into(&test_reader, &decoded_test) == BEBOP_OK);

    printf("Decoded samples (%zu items): ", decoded_test.samples.length);
    for (size_t i = 0; i < decoded_test.samples.length; i++)
    {
        printf("%.6f ", decoded_test.samples.data[i]);
    }
    printf("\n");

    // Verify the decoded data matches
    assert(decoded_test.samples.length == num_samples);
    for (size_t i = 0; i < num_samples; i++)
    {
        // Use approximate comparison for floats
        float diff = fabsf(decoded_test.samples.data[i] - sample_values[i]);
        assert(diff < 1e-6f); // Very small tolerance for float comparison
        printf("Sample %zu: original=%.6f, decoded=%.6f ✓\n",
               i, sample_values[i], decoded_test.samples.data[i]);
    }

    printf("✅ Float array test passed!\n\n");

    // Original jazz library tests continue below...
    printf("=== Testing jazz library ===\n");

    // Create and parse GUID
    bebop_guid_t g = bebop_guid_from_string("81c6987b-48b7-495f-ad01-ec20cc5f5be1");

    char *guid_str;
    assert(bebop_guid_to_string(g, context, &guid_str) == BEBOP_OK);
    printf("%s\n", guid_str);

    // Create a song with optional fields
    song_t s = {0}; // Initialize all optionals to none

    // Set title
    const bebop_string_view_t title_view = bebop_string_view_from_cstr("Donna Lee");
    bebop_assign_some(s.title, title_view);

    // Set year
    bebop_assign_some(s.year, (uint16_t)1974);

    // Create musician using compound literal initialization
    // This works with const fields because we're initializing, not assigning
    musician_t m = {.name = bebop_string_view_from_cstr("Charlie Parker"),
                    .plays = INSTRUMENT_SAX};

    // Create performers array (allocate in arena)
    bebop_musician_array_t performers;
    performers.length = 1;
    performers.data = (musician_t *)bebop_context_alloc(context, sizeof(musician_t));
    assert(performers.data != NULL);

    // Initialize the allocated musician using memcpy since we can't assign to
    // const fields
    memcpy(&performers.data[0], &m, sizeof(musician_t));

    bebop_assign_some(s.performers, performers);

    // Create library with map
    library_t l;
    l.songs.length = 1;
    l.songs.entries = (bebop_guid_song_map_entry_t *)bebop_context_alloc(
        context, sizeof(bebop_guid_song_map_entry_t));
    assert(l.songs.entries != NULL);
    l.songs.entries[0].key = g;
    l.songs.entries[0].value = s;

    // Get writer and encode
    bebop_writer_t writer;
    assert(bebop_context_get_writer(context, &writer) == BEBOP_OK);

    printf("Computed byte count before encoding: %zu\n",
           library_encoded_size(&l));

    assert(library_encode_into(&l, &writer) == BEBOP_OK);

    uint8_t *buffer;
    size_t buffer_length;
    assert(bebop_writer_get_buffer(&writer, &buffer, &buffer_length) == BEBOP_OK);

    printf("Bytes written to buffer: %zu\n", buffer_length);
    printf("Byte count of encoded buffer: %zu\n", buffer_length);

    printf("Buffer contents:");
    for (size_t i = 0; i < buffer_length; i++)
    {
        printf(" %02x", buffer[i]);
    }
    printf("\n");

    // Decode the library
    bebop_reader_t reader;
    assert(bebop_context_get_reader(context, buffer, buffer_length, &reader) == BEBOP_OK);

    library_t l2;
    assert(library_decode_into(&reader, &l2) == BEBOP_OK);

    printf("Decoded library with %zu songs\n", l2.songs.length);

    // Find our song by GUID
    song_t *decoded_song = NULL;
    for (size_t i = 0; i < l2.songs.length; i++)
    {
        if (bebop_guid_equal(l2.songs.entries[i].key, g))
        {
            decoded_song = &l2.songs.entries[i].value;
            break;
        }
    }

    assert(decoded_song != NULL);

    // Verify decoded values
    assert(bebop_is_some(decoded_song->title));
    bebop_string_view_t decoded_title = bebop_unwrap(decoded_song->title);
    printf("Song title: %.*s\n", (int)decoded_title.length, decoded_title.data);

    assert(bebop_is_some(decoded_song->year));
    uint16_t decoded_year = bebop_unwrap(decoded_song->year);
    printf("Song year: %d\n", decoded_year);

    assert(bebop_is_some(decoded_song->performers));
    bebop_musician_array_t decoded_performers = bebop_unwrap(decoded_song->performers);
    assert(decoded_performers.length == 1);
    printf("Musician: %.*s\n", (int)decoded_performers.data[0].name.length,
           decoded_performers.data[0].name.data);
    printf("Instrument: %d (Sax=%d)\n", decoded_performers.data[0].plays,
           INSTRUMENT_SAX);

    // Test malformed packet handling
    printf("\nTesting malformed packet handling...\n");
    uint8_t malformed_buffer[] = {123, 123, 123, 123, 123};
    bebop_reader_t malformed_reader;
    assert(bebop_context_get_reader(context, malformed_buffer,
                                    sizeof(malformed_buffer),
                                    &malformed_reader) == BEBOP_OK);

    library_t l3;
    bebop_result_t result = library_decode_into(&malformed_reader, &l3);
    if (result == BEBOP_ERROR_MALFORMED_PACKET)
    {
        printf("Decoding a malformed packet correctly returned "
               "BEBOP_ERROR_MALFORMED_PACKET\n");
    }
    else
    {
        printf("Expected BEBOP_ERROR_MALFORMED_PACKET, got %d\n", result);
    }

    // Verify the values match what we encoded
    assert(bebop_string_view_equal(decoded_title, title_view));
    assert(decoded_year == 1974);
    assert(decoded_performers.data[0].plays == INSTRUMENT_SAX);
    assert(bebop_string_view_equal(decoded_performers.data[0].name, m.name));

    printf("\n✅ All jazz library tests passed!\n");
    printf("✅ All tests completed successfully!\n");

    bebop_context_destroy(context);
    return 0;
}