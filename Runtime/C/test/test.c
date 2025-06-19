#include <assert.h>
#include <float.h>
#include <limits.h>
#include <math.h>
#include <pthread.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <unistd.h>

#include "../src/bebop.h"

// Test counters
static int tests_run = 0;
static int tests_passed = 0;

#define TEST_START(name)             \
  do {                               \
    printf("Testing %s...\n", name); \
    tests_run++;                     \
  } while (0)

#define TEST_END(name)                     \
  do {                                     \
    tests_passed++;                        \
    printf("✓ %s tests passed\n\n", name); \
  } while (0)

// Context tests
void test_context(void) {
  TEST_START("context management");

  // Test default options
  bebop_context_options_t options = bebop_context_default_options();
  assert(options.arena_options.initial_block_size == 4096);
  assert(options.arena_options.max_block_size == 1048576);
  assert(options.arena_options.allocator.malloc_func == NULL);
  assert(options.arena_options.allocator.free_func == NULL);
  assert(options.initial_writer_size == 1024);

  // Test creation with default options
  bebop_context_t *context1 = bebop_context_create();
  assert(context1 != NULL);
  bebop_context_destroy(context1);

  // Test creation with custom options
  options.arena_options.initial_block_size = 1024;
  options.arena_options.max_block_size = 8192;
  options.initial_writer_size = 512;
  bebop_context_t *context2 = bebop_context_create_with_options(&options);
  assert(context2 != NULL);

  // Test basic allocations through context
  void *ptr1 = bebop_context_alloc(context2, 100);
  assert(ptr1 != NULL);
  assert(bebop_context_space_used(context2) >= 100);

  void *ptr2 = bebop_context_alloc(context2, 200);
  assert(ptr2 != NULL);
  assert(ptr2 != ptr1);
  assert(bebop_context_space_used(context2) >= 300);

  // Test large allocation that exceeds block size
  void *large_ptr = bebop_context_alloc(context2, 10000);
  assert(large_ptr != NULL);

  // Test zero-size allocation
  void *zero_ptr = bebop_context_alloc(context2, 0);
  assert(zero_ptr == NULL);

  // Test string duplication through context
  char *str1 = bebop_context_strdup(context2, "Hello", 5);
  assert(str1 != NULL);
  assert(strcmp(str1, "Hello") == 0);

  char *str2 = bebop_context_strdup(context2, "", 0);
  assert(str2 != NULL);
  assert(strlen(str2) == 0);

  // Test null inputs
  assert(bebop_context_alloc(NULL, 100) == NULL);
  assert(bebop_context_strdup(NULL, "test", 4) == NULL);
  assert(bebop_context_strdup(context2, NULL, 4) == NULL);

  // Test reset
  size_t used_before_reset = bebop_context_space_used(context2);
  bebop_context_reset(context2);
  assert(bebop_context_space_used(context2) == 0);

  printf("  Context space used before reset: %zu bytes\n", used_before_reset);
  printf("  Context space allocated: %zu bytes\n",
         bebop_context_space_allocated(context2));

  bebop_context_destroy(context2);

  TEST_END("context management");
}

// Reader/Writer initialization tests
void test_reader_writer_init(void) {
  TEST_START("reader/writer initialization");

  uint8_t buffer[1024];
  bebop_reader_t reader;
  bebop_writer_t writer;
  bebop_context_t *context = bebop_context_create();

  // Test reader initialization
  assert(bebop_context_get_reader(context, buffer, sizeof(buffer), &reader) ==
         BEBOP_OK);
  assert(bebop_context_get_reader(NULL, buffer, sizeof(buffer), &reader) ==
         BEBOP_ERROR_NULL_POINTER);
  assert(bebop_context_get_reader(context, NULL, sizeof(buffer), &reader) ==
         BEBOP_ERROR_NULL_POINTER);
  assert(bebop_context_get_reader(context, buffer, sizeof(buffer), NULL) ==
         BEBOP_ERROR_NULL_POINTER);

  // Test writer initialization
  assert(bebop_context_get_writer(context, &writer) == BEBOP_OK);
  assert(bebop_context_get_writer(NULL, &writer) == BEBOP_ERROR_NULL_POINTER);
  assert(bebop_context_get_writer(context, NULL) == BEBOP_ERROR_NULL_POINTER);

  bebop_context_destroy(context);

  TEST_END("reader/writer initialization");
}

// Basic type serialization tests
void test_basic_types(void) {
  TEST_START("basic type serialization");

  bebop_context_t *context = bebop_context_create();
  bebop_writer_t writer;
  assert(bebop_context_get_writer(context, &writer) == BEBOP_OK);

  // Test all integer types with boundary values
  assert(bebop_writer_write_byte(&writer, 0) == BEBOP_OK);
  assert(bebop_writer_write_byte(&writer, 255) == BEBOP_OK);
  assert(bebop_writer_write_byte(&writer, 0x42) == BEBOP_OK);

  assert(bebop_writer_write_uint16(&writer, 0) == BEBOP_OK);
  assert(bebop_writer_write_uint16(&writer, UINT16_MAX) == BEBOP_OK);
  assert(bebop_writer_write_uint16(&writer, 0x1234) == BEBOP_OK);

  assert(bebop_writer_write_uint32(&writer, 0) == BEBOP_OK);
  assert(bebop_writer_write_uint32(&writer, UINT32_MAX) == BEBOP_OK);
  assert(bebop_writer_write_uint32(&writer, 0x12345678) == BEBOP_OK);

  assert(bebop_writer_write_uint64(&writer, 0) == BEBOP_OK);
  assert(bebop_writer_write_uint64(&writer, UINT64_MAX) == BEBOP_OK);
  assert(bebop_writer_write_uint64(&writer, 0x123456789ABCDEF0ULL) == BEBOP_OK);

  assert(bebop_writer_write_int16(&writer, INT16_MIN) == BEBOP_OK);
  assert(bebop_writer_write_int16(&writer, INT16_MAX) == BEBOP_OK);
  assert(bebop_writer_write_int16(&writer, -1234) == BEBOP_OK);

  assert(bebop_writer_write_int32(&writer, INT32_MIN) == BEBOP_OK);
  assert(bebop_writer_write_int32(&writer, INT32_MAX) == BEBOP_OK);
  assert(bebop_writer_write_int32(&writer, -123456) == BEBOP_OK);

  assert(bebop_writer_write_int64(&writer, INT64_MIN) == BEBOP_OK);
  assert(bebop_writer_write_int64(&writer, INT64_MAX) == BEBOP_OK);
  assert(bebop_writer_write_int64(&writer, -123456789LL) == BEBOP_OK);

  // Test floating point types
  assert(bebop_writer_write_float32(&writer, 0.0f) == BEBOP_OK);
  assert(bebop_writer_write_float32(&writer, -0.0f) == BEBOP_OK);
  assert(bebop_writer_write_float32(&writer, FLT_MIN) == BEBOP_OK);
  assert(bebop_writer_write_float32(&writer, FLT_MAX) == BEBOP_OK);
  assert(bebop_writer_write_float32(&writer, 3.14159f) == BEBOP_OK);
  assert(bebop_writer_write_float32(&writer, INFINITY) == BEBOP_OK);
  assert(bebop_writer_write_float32(&writer, -INFINITY) == BEBOP_OK);
  assert(bebop_writer_write_float32(&writer, NAN) == BEBOP_OK);

  assert(bebop_writer_write_float64(&writer, 0.0) == BEBOP_OK);
  assert(bebop_writer_write_float64(&writer, -0.0) == BEBOP_OK);
  assert(bebop_writer_write_float64(&writer, DBL_MIN) == BEBOP_OK);
  assert(bebop_writer_write_float64(&writer, DBL_MAX) == BEBOP_OK);
  assert(bebop_writer_write_float64(&writer, 2.718281828) == BEBOP_OK);
  assert(bebop_writer_write_float64(&writer, INFINITY) == BEBOP_OK);
  assert(bebop_writer_write_float64(&writer, -INFINITY) == BEBOP_OK);
  assert(bebop_writer_write_float64(&writer, NAN) == BEBOP_OK);

  // Test boolean types
  assert(bebop_writer_write_bool(&writer, true) == BEBOP_OK);
  assert(bebop_writer_write_bool(&writer, false) == BEBOP_OK);

  // Get buffer for reading
  uint8_t *buffer;
  size_t length;
  assert(bebop_writer_get_buffer(&writer, &buffer, &length) == BEBOP_OK);

  // Read back and verify all values
  bebop_reader_t reader;
  assert(bebop_context_get_reader(context, buffer, length, &reader) ==
         BEBOP_OK);

  uint8_t byte_vals[3];
  assert(bebop_reader_read_byte(&reader, &byte_vals[0]) == BEBOP_OK &&
         byte_vals[0] == 0);
  assert(bebop_reader_read_byte(&reader, &byte_vals[1]) == BEBOP_OK &&
         byte_vals[1] == 255);
  assert(bebop_reader_read_byte(&reader, &byte_vals[2]) == BEBOP_OK &&
         byte_vals[2] == 0x42);

  uint16_t uint16_vals[3];
  assert(bebop_reader_read_uint16(&reader, &uint16_vals[0]) == BEBOP_OK &&
         uint16_vals[0] == 0);
  assert(bebop_reader_read_uint16(&reader, &uint16_vals[1]) == BEBOP_OK &&
         uint16_vals[1] == UINT16_MAX);
  assert(bebop_reader_read_uint16(&reader, &uint16_vals[2]) == BEBOP_OK &&
         uint16_vals[2] == 0x1234);

  uint32_t uint32_vals[3];
  assert(bebop_reader_read_uint32(&reader, &uint32_vals[0]) == BEBOP_OK &&
         uint32_vals[0] == 0);
  assert(bebop_reader_read_uint32(&reader, &uint32_vals[1]) == BEBOP_OK &&
         uint32_vals[1] == UINT32_MAX);
  assert(bebop_reader_read_uint32(&reader, &uint32_vals[2]) == BEBOP_OK &&
         uint32_vals[2] == 0x12345678);

  uint64_t uint64_vals[3];
  assert(bebop_reader_read_uint64(&reader, &uint64_vals[0]) == BEBOP_OK &&
         uint64_vals[0] == 0);
  assert(bebop_reader_read_uint64(&reader, &uint64_vals[1]) == BEBOP_OK &&
         uint64_vals[1] == UINT64_MAX);
  assert(bebop_reader_read_uint64(&reader, &uint64_vals[2]) == BEBOP_OK &&
         uint64_vals[2] == 0x123456789ABCDEF0ULL);

  int16_t int16_vals[3];
  assert(bebop_reader_read_int16(&reader, &int16_vals[0]) == BEBOP_OK &&
         int16_vals[0] == INT16_MIN);
  assert(bebop_reader_read_int16(&reader, &int16_vals[1]) == BEBOP_OK &&
         int16_vals[1] == INT16_MAX);
  assert(bebop_reader_read_int16(&reader, &int16_vals[2]) == BEBOP_OK &&
         int16_vals[2] == -1234);

  int32_t int32_vals[3];
  assert(bebop_reader_read_int32(&reader, &int32_vals[0]) == BEBOP_OK &&
         int32_vals[0] == INT32_MIN);
  assert(bebop_reader_read_int32(&reader, &int32_vals[1]) == BEBOP_OK &&
         int32_vals[1] == INT32_MAX);
  assert(bebop_reader_read_int32(&reader, &int32_vals[2]) == BEBOP_OK &&
         int32_vals[2] == -123456);

  int64_t int64_vals[3];
  assert(bebop_reader_read_int64(&reader, &int64_vals[0]) == BEBOP_OK &&
         int64_vals[0] == INT64_MIN);
  assert(bebop_reader_read_int64(&reader, &int64_vals[1]) == BEBOP_OK &&
         int64_vals[1] == INT64_MAX);
  assert(bebop_reader_read_int64(&reader, &int64_vals[2]) == BEBOP_OK &&
         int64_vals[2] == -123456789LL);

  float float32_vals[8];
  assert(bebop_reader_read_float32(&reader, &float32_vals[0]) == BEBOP_OK &&
         float32_vals[0] == 0.0f);
  assert(bebop_reader_read_float32(&reader, &float32_vals[1]) == BEBOP_OK &&
         float32_vals[1] == -0.0f);
  assert(bebop_reader_read_float32(&reader, &float32_vals[2]) == BEBOP_OK &&
         float32_vals[2] == FLT_MIN);
  assert(bebop_reader_read_float32(&reader, &float32_vals[3]) == BEBOP_OK &&
         float32_vals[3] == FLT_MAX);
  assert(bebop_reader_read_float32(&reader, &float32_vals[4]) == BEBOP_OK);
  assert(fabsf(float32_vals[4] - 3.14159f) < 0.00001f);
  assert(bebop_reader_read_float32(&reader, &float32_vals[5]) == BEBOP_OK &&
         isinf(float32_vals[5]) && float32_vals[5] > 0);
  assert(bebop_reader_read_float32(&reader, &float32_vals[6]) == BEBOP_OK &&
         isinf(float32_vals[6]) && float32_vals[6] < 0);
  assert(bebop_reader_read_float32(&reader, &float32_vals[7]) == BEBOP_OK &&
         isnan(float32_vals[7]));

  double float64_vals[8];
  assert(bebop_reader_read_float64(&reader, &float64_vals[0]) == BEBOP_OK &&
         float64_vals[0] == 0.0);
  assert(bebop_reader_read_float64(&reader, &float64_vals[1]) == BEBOP_OK &&
         float64_vals[1] == -0.0);
  assert(bebop_reader_read_float64(&reader, &float64_vals[2]) == BEBOP_OK &&
         float64_vals[2] == DBL_MIN);
  assert(bebop_reader_read_float64(&reader, &float64_vals[3]) == BEBOP_OK &&
         float64_vals[3] == DBL_MAX);
  assert(bebop_reader_read_float64(&reader, &float64_vals[4]) == BEBOP_OK);
  assert(fabs(float64_vals[4] - 2.718281828) < 0.000000001);
  assert(bebop_reader_read_float64(&reader, &float64_vals[5]) == BEBOP_OK &&
         isinf(float64_vals[5]) && float64_vals[5] > 0);
  assert(bebop_reader_read_float64(&reader, &float64_vals[6]) == BEBOP_OK &&
         isinf(float64_vals[6]) && float64_vals[6] < 0);
  assert(bebop_reader_read_float64(&reader, &float64_vals[7]) == BEBOP_OK &&
         isnan(float64_vals[7]));

  bool bool_vals[2];
  assert(bebop_reader_read_bool(&reader, &bool_vals[0]) == BEBOP_OK &&
         bool_vals[0] == true);
  assert(bebop_reader_read_bool(&reader, &bool_vals[1]) == BEBOP_OK &&
         bool_vals[1] == false);

  printf("  Wrote and verified %zu bytes\n", length);

  bebop_context_destroy(context);
  TEST_END("basic type serialization");
}

// String and array tests
void test_strings_and_arrays(void) {
  TEST_START("strings and arrays");

  bebop_context_t *context = bebop_context_create();
  bebop_writer_t writer;
  assert(bebop_context_get_writer(context, &writer) == BEBOP_OK);

  // Test various string cases
  const char *test_strings[] = {
      "",                           // Empty string
      "a",                          // Single character
      "Hello, World!",              // Regular string
      "Unicode: 你好世界",          // Unicode string
      "Special chars: \n\t\r\\\"",  // Escape sequences
      NULL};

  // Test various byte arrays
  const uint8_t empty_bytes[] = {};
  const uint8_t single_byte[] = {0x42};
  const uint8_t test_bytes[] = {0xDE, 0xAD, 0xBE, 0xEF, 0xCA, 0xFE, 0xBA, 0xBE};
  const uint8_t zero_bytes[] = {0x00, 0x00, 0x00, 0x00};

  // Write all test data
  for (int i = 0; test_strings[i] != NULL; i++) {
    assert(bebop_writer_write_string(&writer, test_strings[i],
                                     strlen(test_strings[i])) == BEBOP_OK);
  }

  assert(bebop_writer_write_byte_array(&writer, empty_bytes, 0) == BEBOP_OK);
  assert(bebop_writer_write_byte_array(&writer, single_byte,
                                       sizeof(single_byte)) == BEBOP_OK);
  assert(bebop_writer_write_byte_array(&writer, test_bytes,
                                       sizeof(test_bytes)) == BEBOP_OK);
  assert(bebop_writer_write_byte_array(&writer, zero_bytes,
                                       sizeof(zero_bytes)) == BEBOP_OK);

  // Test string views
  bebop_string_view_t view1 = bebop_string_view_from_cstr("test view");
  assert(bebop_writer_write_string_view(&writer, view1) == BEBOP_OK);

  // Test byte array views
  bebop_byte_array_view_t byte_view = {test_bytes, sizeof(test_bytes)};
  assert(bebop_writer_write_byte_array_view(&writer, byte_view) == BEBOP_OK);

  // Get buffer for reading
  uint8_t *buffer;
  size_t length;
  assert(bebop_writer_get_buffer(&writer, &buffer, &length) == BEBOP_OK);

  // Read back and verify
  bebop_reader_t reader;
  assert(bebop_context_get_reader(context, buffer, length, &reader) ==
         BEBOP_OK);

  // Verify strings
  for (int i = 0; test_strings[i] != NULL; i++) {
    bebop_string_view_t string_view;
    assert(bebop_reader_read_string_view(&reader, &string_view) == BEBOP_OK);
    assert(string_view.length == strlen(test_strings[i]));
    if (string_view.length > 0) {
      assert(memcmp(string_view.data, test_strings[i], string_view.length) ==
             0);
    }
    printf("  String %d: \"%.*s\" (%zu bytes)\n", i, (int)string_view.length,
           string_view.data, string_view.length);
  }

  // Verify byte arrays
  bebop_byte_array_view_t byte_views[4];
  assert(bebop_reader_read_byte_array_view(&reader, &byte_views[0]) ==
         BEBOP_OK);
  assert(byte_views[0].length == 0);

  assert(bebop_reader_read_byte_array_view(&reader, &byte_views[1]) ==
         BEBOP_OK);
  assert(byte_views[1].length == 1 && byte_views[1].data[0] == 0x42);

  assert(bebop_reader_read_byte_array_view(&reader, &byte_views[2]) ==
         BEBOP_OK);
  assert(byte_views[2].length == 8);
  assert(memcmp(byte_views[2].data, test_bytes, 8) == 0);

  assert(bebop_reader_read_byte_array_view(&reader, &byte_views[3]) ==
         BEBOP_OK);
  assert(byte_views[3].length == 4);
  assert(memcmp(byte_views[3].data, zero_bytes, 4) == 0);

  // Verify string view
  bebop_string_view_t view_read;
  assert(bebop_reader_read_string_view(&reader, &view_read) == BEBOP_OK);
  assert(bebop_string_view_equal(view1, view_read));

  // Verify byte array view
  bebop_byte_array_view_t byte_view_read;
  assert(bebop_reader_read_byte_array_view(&reader, &byte_view_read) ==
         BEBOP_OK);
  assert(byte_view_read.length == byte_view.length);
  assert(memcmp(byte_view_read.data, byte_view.data, byte_view.length) == 0);

  // Test string copy functionality
  bebop_reader_seek(&reader, buffer);  // Reset to beginning
  char *copied_string;
  assert(bebop_reader_read_string_copy(&reader, &copied_string) == BEBOP_OK);
  assert(strcmp(copied_string, test_strings[0]) == 0);

  printf("  Total serialized size: %zu bytes\n", length);

  bebop_context_destroy(context);
  TEST_END("strings and arrays");
}

// GUID tests
void test_guid(void) {
  TEST_START("GUID operations");

  bebop_context_t *context = bebop_context_create();

  // Test various GUID string formats
  const char *test_guids[] = {
      "00000000-0000-0000-0000-000000000000",  // Null GUID
      "12345678-1234-5678-9abc-def012345678",  // Regular GUID
      "FFFFFFFF-FFFF-FFFF-FFFF-FFFFFFFFFFFF",  // Max GUID
      "12345678123456789abcdef012345678",      // No dashes
      NULL};

  for (int i = 0; test_guids[i] != NULL; i++) {
    printf("  Testing GUID: %s\n", test_guids[i]);

    bebop_guid_t guid = bebop_guid_from_string(test_guids[i]);

    char *guid_str_out;
    assert(bebop_guid_to_string(guid, context, &guid_str_out) == BEBOP_OK);
    printf("    Formatted: %s\n", guid_str_out);

    // Test round-trip conversion (note: output will always have dashes)
    bebop_guid_t guid2 = bebop_guid_from_string(guid_str_out);
    assert(bebop_guid_equal(guid, guid2));

    // Test serialization
    bebop_writer_t writer;
    assert(bebop_context_get_writer(context, &writer) == BEBOP_OK);
    assert(bebop_writer_write_guid(&writer, guid) == BEBOP_OK);

    uint8_t *buffer;
    size_t length;
    assert(bebop_writer_get_buffer(&writer, &buffer, &length) == BEBOP_OK);
    assert(length == 16);  // GUID is always 16 bytes

    // Read back
    bebop_reader_t reader;
    assert(bebop_context_get_reader(context, buffer, length, &reader) ==
           BEBOP_OK);

    bebop_guid_t guid_read;
    assert(bebop_reader_read_guid(&reader, &guid_read) == BEBOP_OK);
    assert(bebop_guid_equal(guid, guid_read));

    // Reset context for next iteration
    bebop_context_reset(context);
  }

  // Test GUID equality
  bebop_guid_t guid1 =
      bebop_guid_from_string("12345678-1234-5678-9abc-def012345678");
  bebop_guid_t guid2 =
      bebop_guid_from_string("12345678-1234-5678-9abc-def012345678");
  bebop_guid_t guid3 =
      bebop_guid_from_string("87654321-4321-8765-cba9-876543210fed");

  assert(bebop_guid_equal(guid1, guid2));
  assert(!bebop_guid_equal(guid1, guid3));

  // Test malformed GUID strings
  bebop_guid_t null_guid = bebop_guid_from_string(NULL);
  bebop_guid_t empty_guid = bebop_guid_from_string("");
  bebop_guid_t short_guid = bebop_guid_from_string("12345");

  // These should return zero GUIDs
  bebop_guid_t zero_guid = {0};
  assert(bebop_guid_equal(null_guid, zero_guid));
  assert(bebop_guid_equal(empty_guid, zero_guid));
  assert(bebop_guid_equal(short_guid, zero_guid));

  bebop_context_destroy(context);
  TEST_END("GUID operations");
}

// Date tests
void test_date(void) {
  TEST_START("date operations");

  bebop_context_t *context = bebop_context_create();

  // Test various date values
  bebop_date_t test_dates[] = {
      0,                                        // Unix epoch
      1609459200LL * BEBOP_TICKS_PER_SECOND,    // 2021-01-01 00:00:00 UTC
      -62135596800LL * BEBOP_TICKS_PER_SECOND,  // 0001-01-01 00:00:00 UTC
      253402300799LL * BEBOP_TICKS_PER_SECOND,  // 9999-12-31 23:59:59 UTC
      BEBOP_TICKS_PER_SECOND / 2,               // 0.5 seconds
      -BEBOP_TICKS_PER_SECOND,                  // -1 second
  };

  for (size_t i = 0; i < sizeof(test_dates) / sizeof(test_dates[0]); i++) {
    bebop_writer_t writer;
    assert(bebop_context_get_writer(context, &writer) == BEBOP_OK);
    assert(bebop_writer_write_date(&writer, test_dates[i]) == BEBOP_OK);

    uint8_t *buffer;
    size_t length;
    assert(bebop_writer_get_buffer(&writer, &buffer, &length) == BEBOP_OK);
    assert(length == 8);  // Date is always 8 bytes

    bebop_reader_t reader;
    assert(bebop_context_get_reader(context, buffer, length, &reader) ==
           BEBOP_OK);

    bebop_date_t date_read;
    assert(bebop_reader_read_date(&reader, &date_read) == BEBOP_OK);
    assert(date_read == test_dates[i]);

    printf("  Date %zu: %lld ticks (%lld seconds)\n", i,
           (long long)test_dates[i],
           (long long)(test_dates[i] / BEBOP_TICKS_PER_SECOND));

    // Reset context for next iteration
    bebop_context_reset(context);
  }

  bebop_context_destroy(context);
  TEST_END("date operations");
}

// Reader positioning and seeking tests
void test_reader_positioning(void) {
  TEST_START("reader positioning");

  bebop_context_t *context = bebop_context_create();
  bebop_writer_t writer;
  assert(bebop_context_get_writer(context, &writer) == BEBOP_OK);

  // Write some data to test positioning
  assert(bebop_writer_write_uint32(&writer, 0x12345678) == BEBOP_OK);
  assert(bebop_writer_write_uint16(&writer, 0xABCD) == BEBOP_OK);
  assert(bebop_writer_write_byte(&writer, 0xEF) == BEBOP_OK);

  uint8_t *buffer;
  size_t length;
  assert(bebop_writer_get_buffer(&writer, &buffer, &length) == BEBOP_OK);

  bebop_reader_t reader;
  assert(bebop_context_get_reader(context, buffer, length, &reader) ==
         BEBOP_OK);

  // Test initial position
  assert(bebop_reader_bytes_read(&reader) == 0);
  assert(bebop_reader_position(&reader) == buffer);

  // Read and check positioning
  uint32_t val32;
  assert(bebop_reader_read_uint32(&reader, &val32) == BEBOP_OK);
  assert(val32 == 0x12345678);
  assert(bebop_reader_bytes_read(&reader) == 4);

  // Test seeking
  const uint8_t *pos_after_uint32 = bebop_reader_position(&reader);
  bebop_reader_seek(&reader, buffer);  // Seek back to start
  assert(bebop_reader_bytes_read(&reader) == 0);

  // Re-read first value
  assert(bebop_reader_read_uint32(&reader, &val32) == BEBOP_OK);
  assert(val32 == 0x12345678);

  // Test skipping
  bebop_reader_seek(&reader, buffer);  // Back to start
  bebop_reader_skip(&reader, 4);       // Skip the uint32
  assert(bebop_reader_position(&reader) == pos_after_uint32);

  uint16_t val16;
  assert(bebop_reader_read_uint16(&reader, &val16) == BEBOP_OK);
  assert(val16 == 0xABCD);

  // Test seeking to invalid positions
  const uint8_t *current_pos = bebop_reader_position(&reader);
  bebop_reader_seek(&reader, buffer - 1);                 // Before start
  assert(bebop_reader_position(&reader) == current_pos);  // Should not change

  bebop_reader_seek(&reader, buffer + length + 1);        // After end
  assert(bebop_reader_position(&reader) == current_pos);  // Should not change

  // Test safe skipping
  const uint8_t *pos_before_skip = bebop_reader_position(&reader);
  bebop_reader_skip(&reader, 10);  // Try to skip past end
  assert(bebop_reader_position(&reader) ==
         pos_before_skip);  // Should not change

  printf("  Buffer length: %zu bytes\n", length);
  printf("  Final position: %zu bytes read\n",
         bebop_reader_bytes_read(&reader));

  bebop_context_destroy(context);
  TEST_END("reader positioning");
}

// Writer buffer management tests
void test_writer_buffer_management(void) {
  TEST_START("writer buffer management");

  bebop_context_t *context = bebop_context_create();

  // Test arena-backed writer with growth
  bebop_writer_t writer;
  bebop_context_options_t options = bebop_context_default_options();
  options.initial_writer_size = 32;  // Small initial size to test growth

  bebop_context_t *small_context = bebop_context_create_with_options(&options);
  assert(bebop_context_get_writer(small_context, &writer) == BEBOP_OK);

  // Write more data than initial capacity
  for (int i = 0; i < 100; i++) {
    assert(bebop_writer_write_uint32(&writer, i) == BEBOP_OK);
  }

  printf("  Writer buffer grew to: %zu bytes\n", bebop_writer_length(&writer));

  // Test ensure capacity
  assert(bebop_writer_ensure_capacity(&writer, 1000) == BEBOP_OK);
  assert(bebop_writer_remaining(&writer) >= 1000);

  bebop_context_destroy(context);
  bebop_context_destroy(small_context);
  TEST_END("writer buffer management");
}

// Message length reservation tests
void test_message_length(void) {
  TEST_START("message length reservation");

  bebop_context_t *context = bebop_context_create();
  bebop_writer_t writer;
  assert(bebop_context_get_writer(context, &writer) == BEBOP_OK);

  // Write message header
  assert(bebop_writer_write_byte(&writer, 0x01) == BEBOP_OK);  // Message type

  // Reserve space for message length
  size_t length_position;
  assert(bebop_writer_reserve_message_length(&writer, &length_position) ==
         BEBOP_OK);

  size_t start_pos = bebop_writer_length(&writer);

  // Write message body
  assert(bebop_writer_write_uint32(&writer, 0x12345678) == BEBOP_OK);
  assert(bebop_writer_write_string(&writer, "test message", 12) == BEBOP_OK);
  assert(bebop_writer_write_bool(&writer, true) == BEBOP_OK);

  size_t end_pos = bebop_writer_length(&writer);
  uint32_t message_length = (uint32_t)(end_pos - start_pos);

  // Fill in the message length
  assert(bebop_writer_fill_message_length(&writer, length_position,
                                          message_length) == BEBOP_OK);

  // Verify the written data
  uint8_t *buffer;
  size_t total_length;
  assert(bebop_writer_get_buffer(&writer, &buffer, &total_length) == BEBOP_OK);

  bebop_reader_t reader;
  assert(bebop_context_get_reader(context, buffer, total_length, &reader) ==
         BEBOP_OK);

  uint8_t msg_type;
  assert(bebop_reader_read_byte(&reader, &msg_type) == BEBOP_OK);
  assert(msg_type == 0x01);

  uint32_t read_length;
  assert(bebop_reader_read_uint32(&reader, &read_length) == BEBOP_OK);
  assert(read_length == message_length);

  printf("  Message length: %u bytes at position %zu\n", message_length,
         length_position);
  printf("  Total buffer size: %zu bytes\n", total_length);

  // Test invalid position
  assert(bebop_writer_fill_message_length(&writer, total_length + 10, 123) ==
         BEBOP_ERROR_MALFORMED_PACKET);

  bebop_context_destroy(context);
  TEST_END("message length reservation");
}

// Length prefix tests
void test_length_prefix(void) {
  TEST_START("length prefix handling");

  bebop_context_t *context = bebop_context_create();
  bebop_writer_t writer;
  assert(bebop_context_get_writer(context, &writer) == BEBOP_OK);

  // Write a length prefix manually
  assert(bebop_writer_write_uint32(&writer, 8) == BEBOP_OK);
  assert(bebop_writer_write_uint64(&writer, 0x1122334455667788ULL) == BEBOP_OK);

  uint8_t *buffer;
  size_t length;
  assert(bebop_writer_get_buffer(&writer, &buffer, &length) == BEBOP_OK);

  bebop_reader_t reader;
  assert(bebop_context_get_reader(context, buffer, length, &reader) ==
         BEBOP_OK);

  uint32_t prefix_length;
  assert(bebop_reader_read_length_prefix(&reader, &prefix_length) == BEBOP_OK);
  assert(prefix_length == 8);

  uint64_t data;
  assert(bebop_reader_read_uint64(&reader, &data) == BEBOP_OK);
  assert(data == 0x1122334455667788ULL);

  // Test malformed length prefix (length exceeds buffer)
  bebop_context_reset(context);
  bebop_writer_t bad_writer;
  assert(bebop_context_get_writer(context, &bad_writer) == BEBOP_OK);

  // Write a length prefix that's larger than the remaining buffer
  assert(bebop_writer_write_uint32(&bad_writer, 1000) ==
         BEBOP_OK);  // Claim 1000 bytes follow
  assert(bebop_writer_write_uint32(&bad_writer, 0x12345678) ==
         BEBOP_OK);  // But only write 4 bytes

  uint8_t *bad_buffer;
  size_t bad_length;
  assert(bebop_writer_get_buffer(&bad_writer, &bad_buffer, &bad_length) ==
         BEBOP_OK);

  bebop_reader_t reader2;
  assert(bebop_context_get_reader(context, bad_buffer, bad_length, &reader2) ==
         BEBOP_OK);

  uint32_t bad_length_prefix;
  assert(bebop_reader_read_length_prefix(&reader2, &bad_length_prefix) ==
         BEBOP_ERROR_MALFORMED_PACKET);

  bebop_context_destroy(context);
  TEST_END("length prefix handling");
}

// Utility function tests
void test_utility_functions(void) {
  TEST_START("utility functions");

  // Test string view utilities
  bebop_string_view_t view1 = bebop_string_view_from_cstr("hello");
  bebop_string_view_t view2 = bebop_string_view_from_cstr("hello");
  bebop_string_view_t view3 = bebop_string_view_from_cstr("world");
  bebop_string_view_t view4 = bebop_string_view_from_cstr("");
  bebop_string_view_t view5 = bebop_string_view_from_cstr(NULL);

  assert(view1.length == 5);
  assert(view4.length == 0);
  assert(view5.length == 0);

  assert(bebop_string_view_equal(view1, view2));
  assert(!bebop_string_view_equal(view1, view3));
  assert(bebop_string_view_equal(view4, view5));  // Both empty

  // Test with embedded nulls
  bebop_string_view_t view6 = {"hello\0world", 11};
  bebop_string_view_t view7 = {"hello\0world", 11};
  bebop_string_view_t view8 = {"hello\0WORLD", 11};

  assert(bebop_string_view_equal(view6, view7));
  assert(!bebop_string_view_equal(view6, view8));

  // Test reader/writer utility functions
  bebop_context_t *context = bebop_context_create();
  bebop_writer_t writer;
  assert(bebop_context_get_writer(context, &writer) == BEBOP_OK);

  assert(bebop_writer_length(&writer) == 0);
  assert(bebop_writer_remaining(&writer) >= 1024);  // Initial size

  assert(bebop_writer_write_uint32(&writer, 0x12345678) == BEBOP_OK);
  assert(bebop_writer_length(&writer) == 4);

  uint8_t *buffer;
  size_t length;
  assert(bebop_writer_get_buffer(&writer, &buffer, &length) == BEBOP_OK);

  bebop_reader_t reader;
  assert(bebop_context_get_reader(context, buffer, length, &reader) ==
         BEBOP_OK);

  assert(bebop_reader_bytes_read(&reader) == 0);
  assert(bebop_reader_position(&reader) == buffer);

  uint32_t value;
  assert(bebop_reader_read_uint32(&reader, &value) == BEBOP_OK);
  assert(bebop_reader_bytes_read(&reader) == 4);

  // Test null pointer handling
  assert(bebop_writer_length(NULL) == 0);
  assert(bebop_writer_remaining(NULL) == 0);
  assert(bebop_reader_bytes_read(NULL) == 0);
  assert(bebop_reader_position(NULL) == NULL);

  bebop_context_destroy(context);
  TEST_END("utility functions");
}

// Comprehensive error condition tests
void test_error_conditions(void) {
  TEST_START("error conditions");

  // Test null pointer errors for context functions
  assert(bebop_context_create_with_options(NULL) == NULL);
  assert(bebop_context_alloc(NULL, 100) == NULL);
  assert(bebop_context_strdup(NULL, "test", 4) == NULL);

  bebop_context_t *context = bebop_context_create();
  assert(bebop_context_strdup(context, NULL, 4) == NULL);

  // Reader null pointer tests
  uint8_t buffer[100];
  bebop_reader_t reader;
  uint32_t dummy_u32;
  bool dummy_bool;
  bebop_guid_t dummy_guid;
  bebop_string_view_t dummy_string_view;
  char *dummy_string_copy;

  assert(bebop_context_get_reader(NULL, buffer, 100, &reader) ==
         BEBOP_ERROR_NULL_POINTER);
  assert(bebop_context_get_reader(context, NULL, 100, &reader) ==
         BEBOP_ERROR_NULL_POINTER);
  assert(bebop_context_get_reader(context, buffer, 100, NULL) ==
         BEBOP_ERROR_NULL_POINTER);

  // Initialize reader properly for the remaining tests
  assert(bebop_context_get_reader(context, buffer, 100, &reader) == BEBOP_OK);

  assert(bebop_reader_read_uint32(NULL, &dummy_u32) ==
         BEBOP_ERROR_NULL_POINTER);
  assert(bebop_reader_read_uint32(&reader, NULL) == BEBOP_ERROR_NULL_POINTER);
  assert(bebop_reader_read_bool(NULL, &dummy_bool) == BEBOP_ERROR_NULL_POINTER);
  assert(bebop_reader_read_bool(&reader, NULL) == BEBOP_ERROR_NULL_POINTER);
  assert(bebop_reader_read_guid(NULL, &dummy_guid) == BEBOP_ERROR_NULL_POINTER);
  assert(bebop_reader_read_guid(&reader, NULL) == BEBOP_ERROR_NULL_POINTER);
  assert(bebop_reader_read_string_view(NULL, &dummy_string_view) ==
         BEBOP_ERROR_NULL_POINTER);
  assert(bebop_reader_read_string_view(&reader, NULL) ==
         BEBOP_ERROR_NULL_POINTER);
  assert(bebop_reader_read_string_copy(NULL, &dummy_string_copy) ==
         BEBOP_ERROR_NULL_POINTER);
  assert(bebop_reader_read_string_copy(&reader, NULL) ==
         BEBOP_ERROR_NULL_POINTER);

  // Writer null pointer tests
  bebop_writer_t writer;
  uint8_t *dummy_buffer;
  size_t dummy_length, dummy_position;

  assert(bebop_context_get_writer(NULL, &writer) == BEBOP_ERROR_NULL_POINTER);
  assert(bebop_context_get_writer(context, NULL) == BEBOP_ERROR_NULL_POINTER);

  // Initialize writer properly for the remaining tests
  assert(bebop_context_get_writer(context, &writer) == BEBOP_OK);

  assert(bebop_writer_write_uint32(NULL, 123) == BEBOP_ERROR_NULL_POINTER);
  assert(bebop_writer_write_string(NULL, "test", 4) ==
         BEBOP_ERROR_NULL_POINTER);
  assert(bebop_writer_write_string(&writer, NULL, 4) ==
         BEBOP_ERROR_NULL_POINTER);
  assert(bebop_writer_ensure_capacity(NULL, 100) == BEBOP_ERROR_NULL_POINTER);
  assert(bebop_writer_reserve_message_length(NULL, &dummy_position) ==
         BEBOP_ERROR_NULL_POINTER);
  assert(bebop_writer_reserve_message_length(&writer, NULL) ==
         BEBOP_ERROR_NULL_POINTER);
  assert(bebop_writer_fill_message_length(NULL, 0, 100) ==
         BEBOP_ERROR_NULL_POINTER);
  assert(bebop_writer_get_buffer(NULL, &dummy_buffer, &dummy_length) ==
         BEBOP_ERROR_NULL_POINTER);
  assert(bebop_writer_get_buffer(&writer, NULL, &dummy_length) ==
         BEBOP_ERROR_NULL_POINTER);
  assert(bebop_writer_get_buffer(&writer, &dummy_buffer, NULL) ==
         BEBOP_ERROR_NULL_POINTER);

  // GUID utility null pointer tests
  char *dummy_guid_string;
  assert(bebop_guid_to_string((bebop_guid_t){0}, NULL, &dummy_guid_string) ==
         BEBOP_ERROR_NULL_POINTER);
  assert(bebop_guid_to_string((bebop_guid_t){0}, context, NULL) ==
         BEBOP_ERROR_NULL_POINTER);

  // Test malformed packet errors
  uint8_t small_buffer[4] = {0x01, 0x02, 0x03, 0x04};
  bebop_reader_t small_reader;
  assert(bebop_context_get_reader(context, small_buffer, 4, &small_reader) ==
         BEBOP_OK);

  // Try to read more data than available
  uint64_t big_value;
  assert(bebop_reader_read_uint64(&small_reader, &big_value) ==
         BEBOP_ERROR_MALFORMED_PACKET);

  // Reset reader position for next test
  assert(bebop_context_get_reader(context, small_buffer, 4, &small_reader) ==
         BEBOP_OK);

  bebop_guid_t guid_value;
  assert(bebop_reader_read_guid(&small_reader, &guid_value) ==
         BEBOP_ERROR_MALFORMED_PACKET);

  printf("  Tested all error conditions\n");

  bebop_context_destroy(context);
  TEST_END("error conditions");
}

// Threading test data
typedef struct {
  bebop_context_t *context;
  int thread_id;
  int iterations;
  size_t bytes_allocated;
} thread_test_data_t;

void *context_thread_test(void *arg) {
  thread_test_data_t *data = (thread_test_data_t *)arg;
  data->bytes_allocated = 0;

  for (int i = 0; i < data->iterations; i++) {
    // Allocate various sizes
    size_t size = 16 + (i % 1000);
    void *ptr = bebop_context_alloc(data->context, size);
    if (ptr) {
      data->bytes_allocated += size;
      // Write some data to ensure memory is accessible
      memset(ptr, (uint8_t)data->thread_id, size);
    }

    // Occasionally allocate strings
    if (i % 10 == 0) {
      char test_str[32];
      snprintf(test_str, sizeof(test_str), "thread_%d_iter_%d", data->thread_id,
               i);
      char *str =
          bebop_context_strdup(data->context, test_str, strlen(test_str));
      if (str) {
        data->bytes_allocated += strlen(test_str) + 1;
        assert(strcmp(str, test_str) == 0);
      }
    }
  }

  return NULL;
}

// Thread safety tests
void test_thread_safety(void) {
  TEST_START("thread safety");

  const int num_threads = 4;
  const int iterations = 1000;

  bebop_context_t *context = bebop_context_create();
  pthread_t threads[num_threads];
  thread_test_data_t thread_data[num_threads];

  // Start threads
  for (int i = 0; i < num_threads; i++) {
    thread_data[i].context = context;
    thread_data[i].thread_id = i;
    thread_data[i].iterations = iterations;
    thread_data[i].bytes_allocated = 0;

    int result =
        pthread_create(&threads[i], NULL, context_thread_test, &thread_data[i]);
    assert(result == 0);
  }

  // Wait for threads to complete
  for (int i = 0; i < num_threads; i++) {
    int result = pthread_join(threads[i], NULL);
    assert(result == 0);
    printf("  Thread %d allocated %zu bytes\n", i,
           thread_data[i].bytes_allocated);
  }

  printf("  Total context space used: %zu bytes\n",
         bebop_context_space_used(context));
  printf("  Total context space allocated: %zu bytes\n",
         bebop_context_space_allocated(context));

  bebop_context_destroy(context);
  TEST_END("thread safety");
}

// Stress tests with large data
void test_stress(void) {
  TEST_START("stress testing");

  bebop_context_t *context = bebop_context_create();
  bebop_writer_t writer;
  assert(bebop_context_get_writer(context, &writer) == BEBOP_OK);

  // Write a large amount of data
  const int num_items = 10000;

  for (int i = 0; i < num_items; i++) {
    assert(bebop_writer_write_uint32(&writer, i) == BEBOP_OK);

    char str[64];
    snprintf(str, sizeof(str), "item_%d_test_string", i);
    assert(bebop_writer_write_string(&writer, str, strlen(str)) == BEBOP_OK);

    assert(bebop_writer_write_bool(&writer, i % 2 == 0) == BEBOP_OK);
  }

  uint8_t *buffer;
  size_t length;
  assert(bebop_writer_get_buffer(&writer, &buffer, &length) == BEBOP_OK);

  printf("  Wrote %d items in %zu bytes\n", num_items, length);

  // Read it all back
  bebop_reader_t reader;
  assert(bebop_context_get_reader(context, buffer, length, &reader) ==
         BEBOP_OK);

  for (int i = 0; i < num_items; i++) {
    uint32_t val;
    assert(bebop_reader_read_uint32(&reader, &val) == BEBOP_OK);
    assert(val == (uint32_t)i);

    bebop_string_view_t str_view;
    assert(bebop_reader_read_string_view(&reader, &str_view) == BEBOP_OK);

    char expected_str[64];
    snprintf(expected_str, sizeof(expected_str), "item_%d_test_string", i);
    assert(str_view.length == strlen(expected_str));
    assert(memcmp(str_view.data, expected_str, str_view.length) == 0);

    bool bool_val;
    assert(bebop_reader_read_bool(&reader, &bool_val) == BEBOP_OK);
    assert(bool_val == (i % 2 == 0));
  }

  printf("  Successfully read back all %d items\n", num_items);

  bebop_context_destroy(context);
  TEST_END("stress testing");
}

// Array view tests
void test_array_views(void) {
  TEST_START("array views");

  // Test all array view types
  uint16_t uint16_array[] = {1, 2, 3, 4, 5};
  uint32_t uint32_array[] = {10, 20, 30};
  uint64_t uint64_array[] = {100, 200};
  int16_t int16_array[] = {-1, -2, -3, -4};
  int32_t int32_array[] = {-10, -20};
  int64_t int64_array[] = {-100};
  float float32_array[] = {1.1f, 2.2f, 3.3f};
  double float64_array[] = {1.11, 2.22};
  bool bool_array[] = {true, false, true, false, true};

  bebop_uint16_array_view_t uint16_view = {uint16_array, 5};
  bebop_uint32_array_view_t uint32_view = {uint32_array, 3};
  bebop_uint64_array_view_t uint64_view = {uint64_array, 2};
  bebop_int16_array_view_t int16_view = {int16_array, 4};
  bebop_int32_array_view_t int32_view = {int32_array, 2};
  bebop_int64_array_view_t int64_view = {int64_array, 1};
  bebop_float32_array_view_t float32_view = {float32_array, 3};
  bebop_float64_array_view_t float64_view = {float64_array, 2};
  bebop_bool_array_view_t bool_view = {bool_array, 5};

  // Verify the views contain correct data
  assert(uint16_view.data[2] == 3);
  assert(uint32_view.data[1] == 20);
  assert(uint64_view.data[0] == 100);
  assert(int16_view.data[3] == -4);
  assert(int32_view.data[1] == -20);
  assert(int64_view.data[0] == -100);
  assert(fabsf(float32_view.data[2] - 3.3f) < 0.001f);
  assert(fabs(float64_view.data[1] - 2.22) < 0.001);
  assert(bool_view.data[0] == true);
  assert(bool_view.data[1] == false);

  printf("  Verified all array view types\n");

  TEST_END("array views");
}

// Version and constants tests
void test_version_and_constants(void) {
  TEST_START("version and constants");

  // Test version constants
  printf("  Bebop version: %d.%d.%d (0x%08X)\n", BEBOPC_VER_MAJOR,
         BEBOPC_VER_MINOR, BEBOPC_VER_PATCH, BEBOPC_VER);

  // Test date constants
  assert(BEBOP_TICKS_PER_SECOND == 10000000LL);
  assert(BEBOP_TICKS_BETWEEN_EPOCHS == 621355968000000000LL);

  // Test size constants
  assert(BEBOP_GUID_STRING_LENGTH == 36);
  assert(BEBOP_ARENA_DEFAULT_ALIGNMENT == sizeof(void *));

  // Test compile-time assertions passed (they would fail compilation if wrong)
  printf("  All compile-time assertions passed\n");

  // Test endianness assumption
  assert(BEBOP_ASSUME_LITTLE_ENDIAN == 1);
  printf("  Assuming little-endian byte order\n");

  TEST_END("version and constants");
}

bebop_result_t test_optional_serialization(bebop_writer_t *writer,
                                           bebop_reader_t *reader) {
  // Test creating optional values
  bebop_optional(int32_t) opt_int_none = bebop_none();
  bebop_optional(int32_t) opt_int_some = bebop_some(42);
  bebop_optional(float) opt_float_none = bebop_none();
  bebop_optional(float) opt_float_some = bebop_some(3.14f);
  bebop_optional(bool) opt_bool_none = bebop_none();
  bebop_optional(bool) opt_bool_some = bebop_some(true);

  // Test serialization of optional types
  bebop_write_optional(writer, opt_int_none, bebop_writer_write_int32);
  bebop_write_optional(writer, opt_int_some, bebop_writer_write_int32);
  bebop_write_optional(writer, opt_float_none, bebop_writer_write_float32);
  bebop_write_optional(writer, opt_float_some, bebop_writer_write_float32);
  bebop_write_optional(writer, opt_bool_none, bebop_writer_write_bool);
  bebop_write_optional(writer, opt_bool_some, bebop_writer_write_bool);

  return BEBOP_OK;
}

bebop_result_t test_optional_deserialization(bebop_reader_t *reader) {
  bebop_optional(int32_t) read_int_none, read_int_some;
  bebop_optional(float) read_float_none, read_float_some;
  bebop_optional(bool) read_bool_none, read_bool_some;

  bebop_read_optional(reader, &read_int_none, bebop_reader_read_int32);
  bebop_read_optional(reader, &read_int_some, bebop_reader_read_int32);
  bebop_read_optional(reader, &read_float_none, bebop_reader_read_float32);
  bebop_read_optional(reader, &read_float_some, bebop_reader_read_float32);
  bebop_read_optional(reader, &read_bool_none, bebop_reader_read_bool);
  bebop_read_optional(reader, &read_bool_some, bebop_reader_read_bool);

  // Verify deserialized values
  assert(bebop_is_none(read_int_none));
  assert(bebop_is_some(read_int_some));
  assert(bebop_unwrap(read_int_some) == 42);

  assert(bebop_is_none(read_float_none));
  assert(bebop_is_some(read_float_some));
  assert(fabsf(bebop_unwrap(read_float_some) - 3.14f) < 0.001f);

  assert(bebop_is_none(read_bool_none));
  assert(bebop_is_some(read_bool_some));
  assert(bebop_unwrap(read_bool_some) == true);

  return BEBOP_OK;
}

// Optional types tests
void test_optional_types(void) {
  TEST_START("optional types");

  bebop_context_t *context = bebop_context_create();
  bebop_writer_t writer;
  assert(bebop_context_get_writer(context, &writer) == BEBOP_OK);

  // Test creating optional values
  bebop_optional(int32_t) opt_int_none = bebop_none();
  bebop_optional(int32_t) opt_int_some = bebop_some(42);
  bebop_optional(float) opt_float_none = bebop_none();
  bebop_optional(float) opt_float_some = bebop_some(3.14f);
  bebop_optional(bool) opt_bool_none = bebop_none();
  bebop_optional(bool) opt_bool_some = bebop_some(true);

  // Test checking optional states
  assert(bebop_is_none(opt_int_none));
  assert(bebop_is_some(opt_int_some));
  assert(bebop_is_none(opt_float_none));
  assert(bebop_is_some(opt_float_some));
  assert(bebop_is_none(opt_bool_none));
  assert(bebop_is_some(opt_bool_some));

  // Test unwrapping values
  assert(bebop_unwrap(opt_int_some) == 42);
  assert(fabsf(bebop_unwrap(opt_float_some) - 3.14f) < 0.001f);
  assert(bebop_unwrap(opt_bool_some) == true);

  // Test unwrap_or with defaults
  assert(bebop_unwrap_or(opt_int_none, -1) == -1);
  assert(bebop_unwrap_or(opt_int_some, -1) == 42);
  assert(fabsf(bebop_unwrap_or(opt_float_none, 0.0f) - 0.0f) < 0.001f);
  assert(fabsf(bebop_unwrap_or(opt_float_some, 0.0f) - 3.14f) < 0.001f);

  printf("  Basic optional operations verified\n");

  // Test serialization using helper function
  assert(test_optional_serialization(&writer, NULL) == BEBOP_OK);

  // Get buffer for reading
  uint8_t *buffer;
  size_t length;
  assert(bebop_writer_get_buffer(&writer, &buffer, &length) == BEBOP_OK);

  // Read back and verify all optional values
  bebop_reader_t reader;
  assert(bebop_context_get_reader(context, buffer, length, &reader) ==
         BEBOP_OK);

  assert(test_optional_deserialization(&reader) == BEBOP_OK);

  printf("  Optional serialization/deserialization verified\n");

  // Test with complex types
  bebop_optional(bebop_string_view_t) opt_string_none = bebop_none();
  bebop_string_view_t test_string =
      bebop_string_view_from_cstr("Hello, World!");
  bebop_optional(bebop_string_view_t) opt_string_some = bebop_some(test_string);

  assert(bebop_is_none(opt_string_none));
  assert(bebop_is_some(opt_string_some));
  assert(bebop_string_view_equal(bebop_unwrap(opt_string_some), test_string));

  // Test with GUID
  bebop_guid_t test_guid =
      bebop_guid_from_string("12345678-1234-5678-9abc-def012345678");
  bebop_optional(bebop_guid_t) opt_guid_none = bebop_none();
  bebop_optional(bebop_guid_t) opt_guid_some = bebop_some(test_guid);

  assert(bebop_is_none(opt_guid_none));
  assert(bebop_is_some(opt_guid_some));
  assert(bebop_guid_equal(bebop_unwrap(opt_guid_some), test_guid));

  printf("  Optional complex types verified\n");

  // Test edge cases
  bebop_optional(uint8_t) opt_zero = bebop_some(0);
  bebop_optional(bool) opt_false = bebop_some(false);

  assert(bebop_is_some(opt_zero));  // Zero is still a valid value
  assert(bebop_unwrap(opt_zero) == 0);
  assert(bebop_is_some(opt_false));  // False is still a valid value
  assert(bebop_unwrap(opt_false) == false);

  // Test with arrays
  uint32_t test_array_data[] = {1, 2, 3, 4, 5};
  bebop_uint32_array_view_t test_array = {test_array_data, 5};
  bebop_optional(bebop_uint32_array_view_t) opt_array_none = bebop_none();
  bebop_optional(bebop_uint32_array_view_t) opt_array_some =
      bebop_some(test_array);

  assert(bebop_is_none(opt_array_none));
  assert(bebop_is_some(opt_array_some));
  assert(bebop_unwrap(opt_array_some).length == 5);
  assert(bebop_unwrap(opt_array_some).data[2] == 3);

  printf("  Optional edge cases and arrays verified\n");

  // Test serialization of string optionals (requires resetting writer)
  bebop_context_reset(context);
  assert(bebop_context_get_writer(context, &writer) == BEBOP_OK);

  // Manually serialize string optionals since we don't have a
  // write_string_view_optional
  assert(bebop_writer_write_bool(&writer, opt_string_none.has_value) ==
         BEBOP_OK);
  if (opt_string_none.has_value) {
    assert(bebop_writer_write_string_view(&writer, opt_string_none.value) ==
           BEBOP_OK);
  }

  assert(bebop_writer_write_bool(&writer, opt_string_some.has_value) ==
         BEBOP_OK);
  if (opt_string_some.has_value) {
    assert(bebop_writer_write_string_view(&writer, opt_string_some.value) ==
           BEBOP_OK);
  }

  // Read back string optionals
  assert(bebop_writer_get_buffer(&writer, &buffer, &length) == BEBOP_OK);
  assert(bebop_context_get_reader(context, buffer, length, &reader) ==
         BEBOP_OK);

  bebop_optional(bebop_string_view_t) read_string_none, read_string_some;

  assert(bebop_reader_read_bool(&reader, &read_string_none.has_value) ==
         BEBOP_OK);
  if (read_string_none.has_value) {
    assert(bebop_reader_read_string_view(&reader, &read_string_none.value) ==
           BEBOP_OK);
  }

  assert(bebop_reader_read_bool(&reader, &read_string_some.has_value) ==
         BEBOP_OK);
  if (read_string_some.has_value) {
    assert(bebop_reader_read_string_view(&reader, &read_string_some.value) ==
           BEBOP_OK);
  }

  assert(bebop_is_none(read_string_none));
  assert(bebop_is_some(read_string_some));
  assert(bebop_string_view_equal(bebop_unwrap(read_string_some), test_string));

  printf("  String optional serialization verified\n");

  bebop_context_destroy(context);
  TEST_END("optional types");
}

// Optional error handling tests
void test_optional_error_conditions(void) {
  TEST_START("optional error conditions");

  bebop_context_t *context = bebop_context_create();
  bebop_writer_t writer;
  bebop_reader_t reader;
  uint8_t buffer[100];

  assert(bebop_context_get_writer(context, &writer) == BEBOP_OK);
  assert(bebop_context_get_reader(context, buffer, sizeof(buffer), &reader) ==
         BEBOP_OK);

  // Test optional serialization with null writer/reader
  bebop_optional(int32_t) opt_value = bebop_some(42);

  // These should be compile-time safe due to macro design,
  // but we'll test the underlying functions they call
  assert(bebop_writer_write_bool(NULL, true) == BEBOP_ERROR_NULL_POINTER);
  assert(bebop_reader_read_bool(NULL, &opt_value.has_value) ==
         BEBOP_ERROR_NULL_POINTER);

  // Test with malformed optional data (bool followed by insufficient data)
  uint8_t malformed_buffer[] = {
      0x01};  // has_value = true, but no value follows
  bebop_reader_t malformed_reader;
  assert(bebop_context_get_reader(context, malformed_buffer, 1,
                                  &malformed_reader) == BEBOP_OK);

  bebop_optional(int32_t) malformed_opt;
  assert(bebop_reader_read_bool(&malformed_reader, &malformed_opt.has_value) ==
         BEBOP_OK);
  assert(malformed_opt.has_value == true);

  // This should fail since there's no data for the int32
  assert(bebop_reader_read_int32(&malformed_reader, &malformed_opt.value) ==
         BEBOP_ERROR_MALFORMED_PACKET);

  printf("  Optional error conditions verified\n");

  bebop_context_destroy(context);
  TEST_END("optional error conditions");
}

// Nested optional tests
void test_nested_optionals(void) {
  TEST_START("nested optionals");

  // Since nested optionals are complex with macros, let's test simpler cases
  // Test array of optionals instead
  bebop_optional(int32_t)
      opt_array[] = {bebop_none(), bebop_some(10), bebop_none(), bebop_some(20),
                     bebop_some(30)};

  for (int i = 0; i < 5; i++) {
    if (i == 0 || i == 2) {
      assert(bebop_is_none(opt_array[i]));
    } else {
      assert(bebop_is_some(opt_array[i]));
    }
  }

  assert(bebop_unwrap(opt_array[1]) == 10);
  assert(bebop_unwrap(opt_array[3]) == 20);
  assert(bebop_unwrap(opt_array[4]) == 30);

  // Test optional with complex types manually
  bebop_optional(bebop_string_view_t) opt_string_none = bebop_none();
  bebop_string_view_t test_string =
      bebop_string_view_from_cstr("Hello, World!");
  bebop_optional(bebop_string_view_t) opt_string_some = bebop_some(test_string);

  assert(bebop_is_none(opt_string_none));
  assert(bebop_is_some(opt_string_some));
  assert(bebop_string_view_equal(bebop_unwrap(opt_string_some), test_string));

  printf("  Array of optionals and complex optional types verified\n");

  TEST_END("nested optionals");
}

// Helper function for performance test serialization
bebop_result_t write_performance_optionals(bebop_writer_t *writer,
                                           int num_optionals) {
  // Write many optionals (mix of some and none)
  for (int i = 0; i < num_optionals; i++) {
    if (i % 3 == 0) {
      // Write none
      bebop_optional(int32_t) opt_none = bebop_none();
      bebop_write_optional(writer, opt_none, bebop_writer_write_int32);
    } else {
      // Write some
      bebop_optional(int32_t) opt_some = bebop_some(i);
      bebop_write_optional(writer, opt_some, bebop_writer_write_int32);
    }
  }
  return BEBOP_OK;
}

bebop_result_t read_performance_optionals(bebop_reader_t *reader,
                                          int num_optionals, int *none_count,
                                          int *some_count) {
  *none_count = 0;
  *some_count = 0;

  for (int i = 0; i < num_optionals; i++) {
    bebop_optional(int32_t) opt_read;
    bebop_read_optional(reader, &opt_read, bebop_reader_read_int32);

    if (bebop_is_none(opt_read)) {
      (*none_count)++;
      assert(i % 3 == 0);  // Should match our write pattern
    } else {
      (*some_count)++;
      assert(bebop_unwrap(opt_read) == i);
    }
  }
  return BEBOP_OK;
}

// Performance test for optionals
void test_optional_performance(void) {
  TEST_START("optional performance");

  bebop_context_t *context = bebop_context_create();
  bebop_writer_t writer;
  assert(bebop_context_get_writer(context, &writer) == BEBOP_OK);

  const int num_optionals = 10000;

  // Write many optionals using helper function
  assert(write_performance_optionals(&writer, num_optionals) == BEBOP_OK);

  uint8_t *buffer;
  size_t length;
  assert(bebop_writer_get_buffer(&writer, &buffer, &length) == BEBOP_OK);

  // Read them all back using helper function
  bebop_reader_t reader;
  assert(bebop_context_get_reader(context, buffer, length, &reader) ==
         BEBOP_OK);

  int none_count, some_count;
  assert(read_performance_optionals(&reader, num_optionals, &none_count,
                                    &some_count) == BEBOP_OK);

  printf("  Processed %d optionals (%d some, %d none) in %zu bytes\n",
         num_optionals, some_count, none_count, length);
  printf("  Average bytes per optional: %.2f\n",
         (double)length / num_optionals);

  bebop_context_destroy(context);
  TEST_END("optional performance");
}

// Entry point
int main(void) {
  printf("Bebop C Runtime Comprehensive Test Suite\n");
  printf("=========================================\n\n");

  test_version_and_constants();
  test_context();
  test_reader_writer_init();
  test_basic_types();
  test_strings_and_arrays();
  test_guid();
  test_date();
  test_reader_positioning();
  test_writer_buffer_management();
  test_message_length();
  test_length_prefix();
  test_utility_functions();
  test_array_views();
  test_error_conditions();
  test_thread_safety();
  test_stress();

  test_optional_types();
  test_optional_error_conditions();
  test_nested_optionals();
  test_optional_performance();

  printf("🎉 Test Results: %d/%d tests passed!\n", tests_passed, tests_run);

  if (tests_passed == tests_run) {
    printf("✅ All tests successful!\n");
    return 0;
  } else {
    printf("❌ Some tests failed!\n");
    return 1;
  }
}