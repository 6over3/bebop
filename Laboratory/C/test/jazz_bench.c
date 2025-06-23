
#include "../gen/jazz.h"
#include "../gen/bebop.h"
#include <time.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <assert.h>

typedef struct
{
    double encode_time_ms;
    double decode_time_ms;
    size_t encoded_size_bytes;
    size_t iterations;
    double throughput_mb_per_sec;
} benchmark_result_t;

// High-resolution timer function
static double get_time_ms(void)
{
    struct timespec ts;
    clock_gettime(CLOCK_MONOTONIC, &ts);
    return ts.tv_sec * 1000.0 + ts.tv_nsec / 1000000.0;
}

// Create a library with test data
static library_t make_library(bebop_context_t *context)
{
    library_t lib = {0};

    // Create library with multiple songs
    lib.songs.length = 4;
    lib.songs.entries = (bebop_guid_song_map_entry_t *)bebop_context_alloc(
        context, sizeof(bebop_guid_song_map_entry_t) * 4);
    assert(lib.songs.entries != NULL);

    // Song 1: Giant Steps
    {
        bebop_guid_t guid1 = bebop_guid_from_string("ff990458-a276-4b71-b2e3-57d49470b949");
        lib.songs.entries[0].key = guid1;

        song_t *song = &lib.songs.entries[0].value;
        *song = (song_t){0};

        bebop_assign_some(song->title, bebop_string_view_from_cstr("Giant Steps"));
        bebop_assign_some(song->year, (uint16_t)1959);

        // Create performers
        bebop_musician_array_t performers;
        performers.length = 1;
        performers.data = (musician_t *)bebop_context_alloc(context, sizeof(musician_t));
        assert(performers.data != NULL);

        musician_t m = {
            .name = bebop_string_view_from_cstr("John Coltrane"),
            .plays = INSTRUMENT_SAX};
        memcpy(&performers.data[0], &m, sizeof(musician_t));
        bebop_assign_some(song->performers, performers);
    }

    // Song 2: A Night in Tunisia
    {
        bebop_guid_t guid2 = bebop_guid_from_string("84f4b320-0f1e-463e-982c-78772fabd74d");
        lib.songs.entries[1].key = guid2;

        song_t *song = &lib.songs.entries[1].value;
        *song = (song_t){0};

        bebop_assign_some(song->title, bebop_string_view_from_cstr("A Night in Tunisia"));
        bebop_assign_some(song->year, (uint16_t)1942);

        // Create performers
        bebop_musician_array_t performers;
        performers.length = 2;
        performers.data = (musician_t *)bebop_context_alloc(context, sizeof(musician_t) * 2);
        assert(performers.data != NULL);

        musician_t m1 = {
            .name = bebop_string_view_from_cstr("Dizzy Gillespie"),
            .plays = INSTRUMENT_TRUMPET};
        musician_t m2 = {
            .name = bebop_string_view_from_cstr("Count Basie"),
            .plays = INSTRUMENT_CLARINET};
        memcpy(&performers.data[0], &m1, sizeof(musician_t));
        memcpy(&performers.data[1], &m2, sizeof(musician_t));
        bebop_assign_some(song->performers, performers);
    }

    // Song 3: Groovin' High (minimal data)
    {
        bebop_guid_t guid3 = bebop_guid_from_string("b28d54d6-a3f7-48bf-a07a-117c15cf33ef");
        lib.songs.entries[2].key = guid3;

        song_t *song = &lib.songs.entries[2].value;
        *song = (song_t){0};

        bebop_assign_some(song->title, bebop_string_view_from_cstr("Groovin' High"));
        // year and performers remain unset (none)
    }

    // Song 4: Donna Lee (complex data)
    {
        bebop_guid_t guid4 = bebop_guid_from_string("81c6987b-48b7-495f-ad01-ec20cc5f5be1");
        lib.songs.entries[3].key = guid4;

        song_t *song = &lib.songs.entries[3].value;
        *song = (song_t){0};

        bebop_assign_some(song->title, bebop_string_view_from_cstr("Donna Lee"));
        bebop_assign_some(song->year, (uint16_t)1974);

        // Create performers with multiple instruments
        bebop_musician_array_t performers;
        performers.length = 3;
        performers.data = (musician_t *)bebop_context_alloc(context, sizeof(musician_t) * 3);
        assert(performers.data != NULL);

        musician_t m1 = {
            .name = bebop_string_view_from_cstr("Charlie Parker"),
            .plays = INSTRUMENT_SAX};
        musician_t m2 = {
            .name = bebop_string_view_from_cstr("Miles Davis"),
            .plays = INSTRUMENT_TRUMPET};
        musician_t m3 = {
            .name = bebop_string_view_from_cstr("Tommy Potter"),
            .plays = INSTRUMENT_CLARINET};
        memcpy(&performers.data[0], &m1, sizeof(musician_t));
        memcpy(&performers.data[1], &m2, sizeof(musician_t));
        memcpy(&performers.data[2], &m3, sizeof(musician_t));
        bebop_assign_some(song->performers, performers);
    }

    return lib;
}

// Validation function to ensure data integrity
static void is_valid(const library_t *lib)
{
    assert(lib->songs.length == 4);

    // Validate Giant Steps
    {
        bebop_guid_t target = bebop_guid_from_string("ff990458-a276-4b71-b2e3-57d49470b949");
        song_t *song = NULL;
        for (size_t i = 0; i < lib->songs.length; i++)
        {
            if (bebop_guid_equal(lib->songs.entries[i].key, target))
            {
                song = &lib->songs.entries[i].value;
                break;
            }
        }
        assert(song != NULL);
        assert(bebop_is_some(song->title));
        assert(bebop_string_view_equal(bebop_unwrap(song->title),
                                       bebop_string_view_from_cstr("Giant Steps")));
        assert(bebop_is_some(song->year));
        assert(bebop_unwrap(song->year) == 1959);
        assert(bebop_is_some(song->performers));
        assert(bebop_unwrap(song->performers).length == 1);
    }

    // Validate Groovin' High (minimal data)
    {
        bebop_guid_t target = bebop_guid_from_string("b28d54d6-a3f7-48bf-a07a-117c15cf33ef");
        song_t *song = NULL;
        for (size_t i = 0; i < lib->songs.length; i++)
        {
            if (bebop_guid_equal(lib->songs.entries[i].key, target))
            {
                song = &lib->songs.entries[i].value;
                break;
            }
        }
        assert(song != NULL);
        assert(bebop_is_some(song->title));
        assert(!bebop_is_some(song->year));
        assert(!bebop_is_some(song->performers));
    }
}

// Bebop encoding/decoding functions
static size_t encode_library(const library_t *lib, uint8_t **output, bebop_context_t *context)
{
    bebop_writer_t writer;
    if (bebop_context_get_writer(context, &writer) != BEBOP_OK)
    {
        return 0;
    }

    if (library_encode_into(lib, &writer) != BEBOP_OK)
    {
        return 0;
    }

    size_t buffer_length;
    if (bebop_writer_get_buffer(&writer, output, &buffer_length) != BEBOP_OK)
    {
        return 0;
    }

    return buffer_length;
}

static int decode_library(const uint8_t *input, size_t input_size, library_t *lib, bebop_context_t *context)
{
    bebop_reader_t reader;
    if (bebop_context_get_reader(context, input, input_size, &reader) != BEBOP_OK)
    {
        return 0;
    }

    bebop_result_t result = library_decode_into(&reader, lib);
    return result == BEBOP_OK ? 1 : 0;
}

// Warmup function
static void warmup_benchmark(bebop_context_t *context)
{
    printf("Warming up...\n");
    for (int i = 0; i < 100; i++)
    {
        library_t lib = make_library(context);
        uint8_t *encoded = NULL;
        size_t size = encode_library(&lib, &encoded, context);
        if (encoded)
        {
            library_t decoded_lib;
            decode_library(encoded, size, &decoded_lib, context);
        }
        bebop_context_reset(context);
    }
    printf("Warmup complete.\n\n");
}

// Encoding benchmark
static benchmark_result_t benchmark_encoding(bebop_context_t *context, size_t iterations)
{
    benchmark_result_t result = {0};
    result.iterations = iterations;

    double start_time = get_time_ms();
    size_t total_bytes = 0;

    for (size_t i = 0; i < iterations; i++)
    {
        library_t lib = make_library(context);

        size_t predicted_size = library_encoded_size(&lib);

        uint8_t *encoded = NULL;
        size_t encoded_size = encode_library(&lib, &encoded, context);

        if (encoded)
        {
            total_bytes += encoded_size;
            if (i == 0)
            {
                result.encoded_size_bytes = encoded_size;
                printf("Predicted size: %zu bytes, Actual size: %zu bytes\n",
                       predicted_size, encoded_size);
            }
        }

        bebop_context_reset(context);
    }

    double end_time = get_time_ms();

    result.encode_time_ms = end_time - start_time;
    result.throughput_mb_per_sec = (total_bytes / 1024.0 / 1024.0) / (result.encode_time_ms / 1000.0);

    return result;
}

// Decoding benchmark
static benchmark_result_t benchmark_decoding(bebop_context_t *context, size_t iterations)
{
    benchmark_result_t result = {0};
    result.iterations = iterations;

    // Pre-encode data for decoding benchmark
    library_t template_lib = make_library(context);
    uint8_t *template_encoded = NULL;
    size_t template_size = encode_library(&template_lib, &template_encoded, context);

    if (!template_encoded)
    {
        printf("Failed to create template for decoding benchmark\n");
        return result;
    }

    uint8_t* local_template_encoded = malloc(template_size);
    assert(local_template_encoded);
    memcpy(local_template_encoded, template_encoded, template_size);

    result.encoded_size_bytes = template_size;
    bebop_context_reset(context); // invalidates template_encoded data

    double start_time = get_time_ms();
    size_t total_bytes = 0;

    for (size_t i = 0; i < iterations; i++)
    {
        library_t decoded_lib;
        if (decode_library(local_template_encoded, template_size, &decoded_lib, context))
        {
            total_bytes += template_size;
            is_valid(&decoded_lib);
        }
        bebop_context_reset(context);
    }

    double end_time = get_time_ms();

    free(local_template_encoded);

    result.decode_time_ms = end_time - start_time;
    result.throughput_mb_per_sec = (total_bytes / 1024.0 / 1024.0) / (result.decode_time_ms / 1000.0);

    return result;
}

// Roundtrip benchmark
static benchmark_result_t benchmark_roundtrip(bebop_context_t *context, size_t iterations)
{
    benchmark_result_t result = {0};
    result.iterations = iterations;

    double start_time = get_time_ms();
    size_t total_bytes = 0;

    for (size_t i = 0; i < iterations; i++)
    {
        library_t lib = make_library(context);

        uint8_t *encoded = NULL;
        size_t encoded_size = encode_library(&lib, &encoded, context);

        if (encoded)
        {
            if (i == 0)
            {
                result.encoded_size_bytes = encoded_size;
            }

            library_t decoded_lib;
            if (decode_library(encoded, encoded_size, &decoded_lib, context))
            {
                is_valid(&decoded_lib);
                total_bytes += encoded_size * 2;
            }
        }

        bebop_context_reset(context);
    }

    double end_time = get_time_ms();

    double total_time = end_time - start_time;
    result.encode_time_ms = total_time / 2;
    result.decode_time_ms = total_time / 2;
    result.throughput_mb_per_sec = (total_bytes / 1024.0 / 1024.0) / (total_time / 1000.0);

    return result;
}

// Print benchmark results
static void print_results(const char *test_name, const benchmark_result_t *result)
{
    printf("=== %s ===\n", test_name);
    printf("Iterations:           %zu\n", result->iterations);
    printf("Encoded size:         %zu bytes\n", result->encoded_size_bytes);
    printf("Encode time:          %.3f ms (%.3f µs per op)\n",
           result->encode_time_ms,
           result->encode_time_ms * 1000.0 / result->iterations);
    printf("Decode time:          %.3f ms (%.3f µs per op)\n",
           result->decode_time_ms,
           result->decode_time_ms * 1000.0 / result->iterations);
    printf("Throughput:           %.2f MB/s\n", result->throughput_mb_per_sec);
    printf("Operations per second: %.0f\n",
           result->iterations / ((result->encode_time_ms + result->decode_time_ms) / 1000.0));
    printf("\n");
}

// Error handling benchmark
static void benchmark_error_handling(bebop_context_t *context, size_t iterations)
{
    printf("=== Error Handling Benchmark ===\n");

    uint8_t malformed_buffer[] = {123, 123, 123, 123, 123};

    double start_time = get_time_ms();

    size_t error_count = 0;
    for (size_t i = 0; i < iterations; i++)
    {
        bebop_reader_t reader;
        if (bebop_context_get_reader(context, malformed_buffer, sizeof(malformed_buffer), &reader) == BEBOP_OK)
        {
            library_t lib;
            bebop_result_t result = library_decode_into(&reader, &lib);
            if (result == BEBOP_ERROR_MALFORMED_PACKET)
            {
                error_count++;
            }
        }
        bebop_context_reset(context);
    }

    double end_time = get_time_ms();
    double total_time = end_time - start_time;

    printf("Error handling rate:  %.0f errors/sec\n", error_count / (total_time / 1000.0));
    printf("Correctly handled:    %zu/%zu malformed packets\n", error_count, iterations);
    printf("Average time per error: %.3f µs\n\n", total_time * 1000.0 / iterations);
}

int main(int argc, char *argv[])
{
    size_t iterations = 10000;
    if (argc > 1)
    {
        iterations = atoi(argv[1]);
        if (iterations == 0)
            iterations = 10000;
    }

    printf("Bebop Jazz Library Performance Benchmark\n");
    printf("========================================\n");
    printf("Iterations per test: %zu\n\n", iterations);

    bebop_context_t *context = bebop_context_create();
    if (!context)
    {
        fprintf(stderr, "Failed to create bebop context\n");
        return 1;
    }

    // Warmup
    warmup_benchmark(context);

    // Run benchmarks
    benchmark_result_t encode_result = benchmark_encoding(context, iterations);
    print_results("Encoding Benchmark", &encode_result);

    benchmark_result_t decode_result = benchmark_decoding(context, iterations);
    print_results("Decoding Benchmark", &decode_result);

    benchmark_result_t roundtrip_result = benchmark_roundtrip(context, iterations);
    print_results("Roundtrip Benchmark", &roundtrip_result);

    // Error handling benchmark
    benchmark_error_handling(context, iterations / 10);

    // Performance summary
    printf("=== Performance Summary ===\n");
    printf("Encode rate:     %8.0f ops/sec (%.2f MB/s)\n",
           iterations / (encode_result.encode_time_ms / 1000.0),
           encode_result.throughput_mb_per_sec);
    printf("Decode rate:     %8.0f ops/sec (%.2f MB/s)\n",
           iterations / (decode_result.decode_time_ms / 1000.0),
           decode_result.throughput_mb_per_sec);
    printf("Roundtrip rate:  %8.0f ops/sec (%.2f MB/s)\n",
           iterations / ((roundtrip_result.encode_time_ms + roundtrip_result.decode_time_ms) / 1000.0),
           roundtrip_result.throughput_mb_per_sec);
    printf("Data size:       %zu bytes per library\n", encode_result.encoded_size_bytes);

    // Cleanup
    bebop_context_destroy(context);

    return 0;
}